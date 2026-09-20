using System.Diagnostics;
using System.Text;
using Microsoft.CodeAnalysis;

namespace ScriptEngine;

/// <summary>独立脚本 Worker 的进程资源和超时配置。</summary>
public sealed class RoslynScriptWorkerOptions
{
    /// <summary>Worker 可执行文件路径；也可以指向由 dotnet 启动的 .dll。</summary>
    public string WorkerPath { get; init; } = string.Empty;

    /// <summary>单次编译和执行的硬超时。</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>宿主轮询检测的 Worker 工作集上限；小于等于零表示不限制。</summary>
    public long MaximumWorkingSetBytes { get; init; } = 256L * 1024 * 1024;

    /// <summary>Windows Job Object 强制执行的进程提交内存上限；小于等于零表示不限制。</summary>
    public long MaximumCommittedMemoryBytes { get; init; } = 512L * 1024 * 1024;

    /// <summary>脚本标准输出最多保留的字符数。</summary>
    public int MaximumOutputCharacters { get; init; } = 256 * 1024;

    /// <summary>Worker 进程工作目录；为空时使用 Worker 所在目录。</summary>
    public string? WorkingDirectory { get; init; }

    /// <summary>在 Windows 上将 Worker 放入关闭时终止整个进程树的 Job Object。</summary>
    public bool UseWindowsJobObject { get; init; } = true;

    /// <summary>Job Object 创建或分配失败时是否拒绝执行；非 Windows 平台启用此项也会拒绝执行。</summary>
    public bool RequireWindowsJobObject { get; init; }

    /// <summary>Job Object 允许的最大活动进程数；小于等于零表示不限制。默认仅允许 Worker 本身。</summary>
    public int MaximumProcessCount { get; init; } = 1;
}

/// <summary>独立进程脚本执行结果；返回值跨进程转换为文本和类型名称。</summary>
public sealed record RoslynScriptWorkerResult(
    bool Success,
    bool TimedOut,
    bool ResourceLimitExceeded,
    string? ReturnValueText,
    string? ReturnValueType,
    string Output,
    IReadOnlyList<RoslynScriptDiagnostic> Diagnostics,
    string? WorkerError = null,
    int? ExitCode = null,
    bool OperatingSystemIsolationApplied = false);

/// <summary>
/// 在独立 Worker 进程中执行不可信或可能失控的脚本。该接口提供硬超时和进程崩溃隔离，
/// 不传递宿主对象和 IServiceProvider；操作系统账号隔离仍由宿主部署负责。
/// </summary>
public static class RoslynScriptWorkerClient
{
    /// <summary>启动 Worker、执行一次单文件脚本并在取消、超时或超出工作集时终止进程。</summary>
    public static Task<RoslynScriptWorkerResult> ExecuteAsync(
        string source,
        RoslynScriptWorkerOptions options,
        RoslynScriptEnvironment? environment = null,
        CancellationToken cancellationToken = default)
    {
        if (source is null) throw new ArgumentNullException(nameof(source));
        return ExecuteFilesAsync(
            new[] { new RoslynScriptSourceFile("Program.cs", source) },
            options,
            environment,
            cancellationToken);
    }

    /// <summary>在独立 Worker 中编译并执行多文件脚本项目。</summary>
    public static Task<RoslynScriptWorkerResult> ExecuteProjectAsync(
        RoslynScriptProject project,
        RoslynScriptWorkerOptions options,
        RoslynScriptEnvironment? environment = null,
        CancellationToken cancellationToken = default)
    {
        if (project is null) throw new ArgumentNullException(nameof(project));
        return ExecuteFilesAsync(project.GetValidatedFiles(), options, environment, cancellationToken);
    }

    private static async Task<RoslynScriptWorkerResult> ExecuteFilesAsync(
        IReadOnlyList<RoslynScriptSourceFile> files,
        RoslynScriptWorkerOptions options,
        RoslynScriptEnvironment? environment,
        CancellationToken cancellationToken)
    {
        if (options is null) throw new ArgumentNullException(nameof(options));
        if (string.IsNullOrWhiteSpace(options.WorkerPath))
            throw new ArgumentException("Worker 路径不能为空。", nameof(options));
        if (options.Timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options), "Worker 超时必须大于零。");
        if (options.MaximumOutputCharacters < 1)
            throw new ArgumentOutOfRangeException(nameof(options), "Worker 输出上限必须大于零。");

        var workerPath = Path.GetFullPath(options.WorkerPath);
        if (!File.Exists(workerPath)) throw new FileNotFoundException("找不到脚本 Worker。", workerPath);
        var requestDirectory = Path.Combine(Path.GetTempPath(), "ScriptEngineWorker", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(requestDirectory);
        Process? process = null;
        WindowsProcessJob? processJob = null;
        try
        {
            WriteRequest(requestDirectory, files, environment ?? new RoslynScriptEnvironment(), options.MaximumOutputCharacters);
            var startInfo = CreateStartInfo(workerPath, requestDirectory, options.WorkingDirectory);
            process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            var exited = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            process.Exited += (_, _) => exited.TrySetResult(true);
            if (!process.Start()) throw new InvalidOperationException("无法启动脚本 Worker。 ");
            processJob = WindowsProcessJob.TryAssign(process, options);
            if (process.HasExited) exited.TrySetResult(true);

            var started = Stopwatch.StartNew();
            var timedOut = false;
            var resourceExceeded = false;
            while (!exited.Task.IsCompleted)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (started.Elapsed >= options.Timeout)
                {
                    timedOut = true;
                    Kill(process);
                    break;
                }
                if (options.MaximumWorkingSetBytes > 0)
                {
                    try
                    {
                        process.Refresh();
                        if (!process.HasExited && process.WorkingSet64 > options.MaximumWorkingSetBytes)
                        {
                            resourceExceeded = true;
                            Kill(process);
                            break;
                        }
                    }
                    catch (InvalidOperationException)
                    {
                        // 进程恰好退出时由 exited 任务完成。
                    }
                }
                await Task.WhenAny(exited.Task, Task.Delay(50, cancellationToken)).ConfigureAwait(false);
            }
            if (timedOut || resourceExceeded)
            {
                await WaitForExitAfterKillAsync(process).ConfigureAwait(false);
                return new RoslynScriptWorkerResult(
                    false,
                    timedOut,
                    resourceExceeded,
                    null,
                    null,
                    string.Empty,
                    Array.Empty<RoslynScriptDiagnostic>(),
                    timedOut ? "脚本执行超过硬超时。" : "脚本 Worker 超出工作集上限。",
                    TryGetExitCode(process),
                    processJob is not null);
            }

            await exited.Task.ConfigureAwait(false);
            var responsePath = Path.Combine(requestDirectory, "response.txt");
            if (!File.Exists(responsePath))
            {
                return new RoslynScriptWorkerResult(
                    false, false, false, null, null, string.Empty,
                    Array.Empty<RoslynScriptDiagnostic>(),
                    "脚本 Worker 未返回有效响应。",
                    TryGetExitCode(process),
                    processJob is not null);
            }
            return ReadResponse(responsePath, TryGetExitCode(process)) with
            {
                OperatingSystemIsolationApplied = processJob is not null
            };
        }
        finally
        {
            processJob?.Dispose();
            if (process is not null)
            {
                Kill(process);
                process.Dispose();
            }
            TryDeleteDirectory(requestDirectory);
        }
    }

    private static ProcessStartInfo CreateStartInfo(string workerPath, string requestDirectory, string? workingDirectory)
    {
        var isDll = string.Equals(Path.GetExtension(workerPath), ".dll", StringComparison.OrdinalIgnoreCase);
        return new ProcessStartInfo
        {
            FileName = isDll ? "dotnet" : workerPath,
            Arguments = isDll
                ? Quote(workerPath) + " --request " + Quote(requestDirectory)
                : "--request " + Quote(requestDirectory),
            WorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory)
                ? Path.GetDirectoryName(workerPath) ?? Environment.CurrentDirectory
                : Path.GetFullPath(workingDirectory),
            UseShellExecute = false,
            CreateNoWindow = true
        };
    }

    internal static void WriteRequest(
        string directory,
        IReadOnlyList<RoslynScriptSourceFile> files,
        RoslynScriptEnvironment environment,
        int maximumOutputCharacters)
    {
        var sourceDirectory = Path.Combine(directory, "sources");
        Directory.CreateDirectory(sourceDirectory);
        var lines = new List<string> { "V|2", "M|" + maximumOutputCharacters };
        foreach (var file in files)
        {
            var path = Path.Combine(sourceDirectory, file.FileName.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, file.Source, new UTF8Encoding(false));
            lines.Add("F|" + Encode(file.FileName));
        }
        lines.AddRange(environment.Imports.Select(item => "I|" + Encode(item)));
        foreach (var assembly in environment.References.Where(item => !item.IsDynamic))
        {
            var location = GetAssemblyLocation(assembly);
            if (string.IsNullOrWhiteSpace(location))
                throw new NotSupportedException(
                    $"独立 Worker 无法按路径传输单文件宿主中的嵌入程序集引用：{assembly.FullName}。请改用 ReferencePaths 部署外部 DLL。");
            lines.Add("R|" + Encode(Path.GetFullPath(location)));
        }
        lines.AddRange(environment.ReferencePaths.Select(item => "R|" + Encode(Path.GetFullPath(item))));
        lines.AddRange(environment.SecurityPolicy.DeniedNamespacePrefixes.Select(item => "N|" + Encode(item)));
        lines.AddRange(environment.SecurityPolicy.DeniedSymbolPrefixes.Select(item => "S|" + Encode(item)));
        lines.Add("D|" + (environment.ResolveReferenceDependencies ? "1" : "0"));
        lines.Add("A|" + (environment.ValidateReferenceArchitecture ? "1" : "0"));
        File.WriteAllLines(Path.Combine(directory, "request.txt"), lines, new UTF8Encoding(false));
    }

#if !NETFRAMEWORK
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(
        "SingleFile",
        "IL3000:Avoid accessing Assembly file path when publishing as a single file",
        Justification = "File-backed references are forwarded to the Worker; embedded host assemblies cannot be transferred by path.")]
#endif
    private static string? GetAssemblyLocation(System.Reflection.Assembly assembly)
    {
        var location = assembly.Location;
        return string.IsNullOrWhiteSpace(location) ? null : location;
    }

    internal static RoslynScriptWorkerResult ReadResponse(string path, int? exitCode)
    {
        var success = false;
        string? returnValue = null;
        string? returnType = null;
        var output = string.Empty;
        string? error = null;
        var diagnostics = new List<RoslynScriptDiagnostic>();
        foreach (var line in File.ReadAllLines(path, Encoding.UTF8))
        {
            var parts = line.Split('|');
            if (parts.Length < 2) continue;
            switch (parts[0])
            {
                case "OK": success = parts[1] == "1"; break;
                case "RV": returnValue = Decode(parts[1]); break;
                case "RT": returnType = Decode(parts[1]); break;
                case "O": output = Decode(parts[1]); break;
                case "E": error = Decode(parts[1]); break;
                case "G" when parts.Length >= 10:
                    diagnostics.Add(new RoslynScriptDiagnostic(
                        Decode(parts[1]),
                        (DiagnosticSeverity)int.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture),
                        Decode(parts[3]),
                        int.Parse(parts[4], System.Globalization.CultureInfo.InvariantCulture),
                        int.Parse(parts[5], System.Globalization.CultureInfo.InvariantCulture),
                        int.Parse(parts[6], System.Globalization.CultureInfo.InvariantCulture),
                        int.Parse(parts[7], System.Globalization.CultureInfo.InvariantCulture),
                        Decode(parts[8]),
                        (RoslynScriptDiagnosticOrigin)int.Parse(parts[9], System.Globalization.CultureInfo.InvariantCulture)));
                    break;
            }
        }
        return new RoslynScriptWorkerResult(
            success,
            false,
            false,
            returnValue,
            returnType,
            output,
            diagnostics,
            error,
            exitCode);
    }

    private static async Task WaitForExitAfterKillAsync(Process process)
    {
        var timeout = DateTime.UtcNow.AddSeconds(5);
        while (!process.HasExited && DateTime.UtcNow < timeout)
            await Task.Delay(20).ConfigureAwait(false);
    }

    private static void Kill(Process process)
    {
        try
        {
#if NETFRAMEWORK
            process.Kill();
#else
            process.Kill(entireProcessTree: true);
#endif
        }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }
    }

    private static int? TryGetExitCode(Process process)
    {
        try { return process.HasExited ? process.ExitCode : null; }
        catch (InvalidOperationException) { return null; }
    }

    internal static string Quote(string value) => "\"" + value.Replace("\"", "\\\"") + "\"";
    private static string Encode(string? value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(value ?? string.Empty));
    private static string Decode(string value) => Encoding.UTF8.GetString(Convert.FromBase64String(value));

    internal static void TryDeleteDirectory(string path)
    {
        try { Directory.Delete(path, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
