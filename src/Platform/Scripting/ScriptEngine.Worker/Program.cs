using System.Globalization;
using System.IO.Pipes;
using System.Text;
using Microsoft.CodeAnalysis;

namespace ScriptEngine.Worker;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var pipeName = GetArgument(args, "--pipe");
        if (pipeName is not null)
            return await RunNamedPipeServerAsync(pipeName).ConfigureAwait(false);
        if (args.Any(item => string.Equals(item, "--stdio", StringComparison.OrdinalIgnoreCase)))
            return await RunCommandServerAsync(Console.In, Console.Out, redirectConsole: true).ConfigureAwait(false);
        var requestDirectory = GetArgument(args, "--request");
        return requestDirectory is null ? 2 : await ProcessRequestAsync(requestDirectory).ConfigureAwait(false);
    }

    private static async Task<int> ProcessRequestAsync(string requestDirectory)
    {
        var responsePath = Path.Combine(requestDirectory, "response.txt");
        try
        {
            var request = ReadRequest(requestDirectory);
            using var output = new BoundedTextWriter(request.MaximumOutputCharacters);
            var environment = new RoslynScriptEnvironment
            {
                Imports = request.Imports,
                ReferencePaths = request.ReferencePaths,
                ResolveReferenceDependencies = request.ResolveReferenceDependencies,
                ValidateReferenceArchitecture = request.ValidateReferenceArchitecture,
                SecurityPolicy = new RoslynScriptSecurityPolicy
                {
                    DeniedNamespacePrefixes = request.DeniedNamespaces,
                    DeniedSymbolPrefixes = request.DeniedSymbols
                },
                Output = output
            };
            using var service = new RoslynScriptService();
            var result = await service.ExecuteProjectAsync(
                "isolated-worker",
                new RoslynScriptProject { SourceFiles = request.SourceFiles },
                environment: environment,
                cancellationToken: CancellationToken.None).ConfigureAwait(false);
            WriteResponse(
                responsePath,
                result.Diagnostics.All(item => item.Severity != DiagnosticSeverity.Error),
                result.ReturnValue,
                output.ToString(),
                result.Diagnostics,
                null);
            return 0;
        }
        catch (Exception exception)
        {
            WriteResponse(
                responsePath,
                false,
                null,
                string.Empty,
                Array.Empty<RoslynScriptDiagnostic>(),
                exception.ToString());
            return 1;
        }
    }

    private static async Task<int> RunNamedPipeServerAsync(string pipeName)
    {
        using var pipe = new NamedPipeClientStream(
            ".",
            pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);
        await pipe.ConnectAsync(15000).ConfigureAwait(false);
        using var input = new StreamReader(pipe, new UTF8Encoding(false), false, 4096, leaveOpen: true);
        using var output = new StreamWriter(pipe, new UTF8Encoding(false), 4096, leaveOpen: true) { AutoFlush = true };
        return await RunCommandServerAsync(input, output, redirectConsole: false).ConfigureAwait(false);
    }

    private static async Task<int> RunCommandServerAsync(
        TextReader protocolInput,
        TextWriter protocolOutput,
        bool redirectConsole)
    {
        using var currentProcess = System.Diagnostics.Process.GetCurrentProcess();
        await protocolOutput.WriteLineAsync($"READY|1|{currentProcess.Id}").ConfigureAwait(false);
        await protocolOutput.FlushAsync().ConfigureAwait(false);
        string? command;
        while ((command = await protocolInput.ReadLineAsync().ConfigureAwait(false)) is not null)
        {
            if (string.Equals(command, "QUIT", StringComparison.Ordinal)) return 0;
            var parts = command.Split('|');
            if (parts.Length == 2 && string.Equals(parts[0], "PING", StringComparison.Ordinal))
            {
                await protocolOutput.WriteLineAsync($"PONG|{parts[1]}|1").ConfigureAwait(false);
                await protocolOutput.FlushAsync().ConfigureAwait(false);
                continue;
            }
            if (parts.Length != 3 || !string.Equals(parts[0], "RUN", StringComparison.Ordinal)) continue;
            var nonce = parts[1];
            var exitCode = 1;
            var originalInput = Console.In;
            var originalOutput = Console.Out;
            try
            {
                var directory = Decode(parts[2]);
                if (redirectConsole)
                {
                    // 标准输入输出作为兼容控制通道时，禁止用户脚本干扰控制协议。
                    Console.SetIn(TextReader.Null);
                    Console.SetOut(TextWriter.Null);
                }
                exitCode = await ProcessRequestAsync(directory).ConfigureAwait(false);
            }
            catch
            {
                exitCode = 1;
            }
            finally
            {
                if (redirectConsole)
                {
                    Console.SetIn(originalInput);
                    Console.SetOut(originalOutput);
                }
            }
            await protocolOutput.WriteLineAsync($"DONE|{nonce}|{exitCode}").ConfigureAwait(false);
            await protocolOutput.FlushAsync().ConfigureAwait(false);
        }
        return 0;
    }

    private static WorkerRequest ReadRequest(string directory)
    {
        var imports = new List<string>();
        var references = new List<string>();
        var sourceFileNames = new List<string>();
        var deniedNamespaces = new List<string>();
        var deniedSymbols = new List<string>();
        var maximumOutputCharacters = 256 * 1024;
        var resolveDependencies = true;
        var validateArchitecture = true;
        foreach (var line in File.ReadAllLines(Path.Combine(directory, "request.txt"), Encoding.UTF8))
        {
            var separator = line.IndexOf('|');
            if (separator < 0) continue;
            var kind = line.Substring(0, separator);
            var value = line.Substring(separator + 1);
            switch (kind)
            {
                case "M": maximumOutputCharacters = int.Parse(value, CultureInfo.InvariantCulture); break;
                case "I": imports.Add(Decode(value)); break;
                case "R": references.Add(Decode(value)); break;
                case "F": sourceFileNames.Add(Decode(value)); break;
                case "N": deniedNamespaces.Add(Decode(value)); break;
                case "S": deniedSymbols.Add(Decode(value)); break;
                case "D": resolveDependencies = value == "1"; break;
                case "A": validateArchitecture = value == "1"; break;
            }
        }
        var sourceDirectory = Path.Combine(directory, "sources");
        var sourceFiles = sourceFileNames.Count == 0
            ? new[]
            {
                new RoslynScriptSourceFile(
                    "Program.cs",
                    File.ReadAllText(Path.Combine(directory, "Program.cs"), Encoding.UTF8))
            }
            : sourceFileNames.Select(name =>
            {
                if (Path.IsPathRooted(name) || name.Replace('\\', '/').Split('/').Any(part => part == ".."))
                    throw new InvalidDataException($"无效的 Worker 源文件名：{name}");
                return new RoslynScriptSourceFile(
                    name,
                    File.ReadAllText(
                        Path.Combine(sourceDirectory, name.Replace('/', Path.DirectorySeparatorChar)),
                        Encoding.UTF8));
            }).ToArray();
        return new WorkerRequest(
            sourceFiles,
            imports.Count == 0 ? RoslynScriptEnvironment.StandardImports : imports,
            references.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            deniedNamespaces,
            deniedSymbols,
            maximumOutputCharacters,
            resolveDependencies,
            validateArchitecture);
    }

    private static void WriteResponse(
        string path,
        bool success,
        object? returnValue,
        string output,
        IEnumerable<RoslynScriptDiagnostic> diagnostics,
        string? error)
    {
        var lines = new List<string>
        {
            "V|1",
            "OK|" + (success ? "1" : "0"),
            "RV|" + Encode(returnValue?.ToString()),
            "RT|" + Encode(returnValue?.GetType().AssemblyQualifiedName),
            "O|" + Encode(output),
            "E|" + Encode(error)
        };
        lines.AddRange(diagnostics.Select(item => string.Join("|", new[]
        {
            "G",
            Encode(item.Id),
            ((int)item.Severity).ToString(CultureInfo.InvariantCulture),
            Encode(item.Message),
            item.Start.ToString(CultureInfo.InvariantCulture),
            item.Length.ToString(CultureInfo.InvariantCulture),
            item.Line.ToString(CultureInfo.InvariantCulture),
            item.Column.ToString(CultureInfo.InvariantCulture),
            Encode(item.SourceName),
            ((int)item.Origin).ToString(CultureInfo.InvariantCulture)
        })));
        var temporaryPath = path + ".tmp";
        File.WriteAllLines(temporaryPath, lines, new UTF8Encoding(false));
        File.Move(temporaryPath, path);
    }

    private static string? GetArgument(IReadOnlyList<string> args, string name)
    {
        for (var index = 0; index + 1 < args.Count; index++)
            if (string.Equals(args[index], name, StringComparison.OrdinalIgnoreCase))
                return string.Equals(name, "--request", StringComparison.OrdinalIgnoreCase)
                    ? Path.GetFullPath(args[index + 1])
                    : args[index + 1];
        return null;
    }

    private static string Encode(string? value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(value ?? string.Empty));
    private static string Decode(string value) => Encoding.UTF8.GetString(Convert.FromBase64String(value));

    private sealed record WorkerRequest(
        IReadOnlyList<RoslynScriptSourceFile> SourceFiles,
        IReadOnlyList<string> Imports,
        IReadOnlyList<string> ReferencePaths,
        IReadOnlyList<string> DeniedNamespaces,
        IReadOnlyList<string> DeniedSymbols,
        int MaximumOutputCharacters,
        bool ResolveReferenceDependencies,
        bool ValidateReferenceArchitecture);

    private sealed class BoundedTextWriter : StringWriter
    {
        private readonly int _maximumCharacters;
        private int _written;

        public BoundedTextWriter(int maximumCharacters) => _maximumCharacters = maximumCharacters;

        public override void Write(char value)
        {
            if (_written >= _maximumCharacters) return;
            base.Write(value);
            _written++;
        }

        public override void Write(string? value)
        {
            if (string.IsNullOrEmpty(value) || _written >= _maximumCharacters) return;
            var available = Math.Min(value!.Length, _maximumCharacters - _written);
            base.Write(value.Substring(0, available));
            _written += available;
        }

        public override void WriteLine(string? value)
        {
            Write(value);
            Write(NewLine);
        }
    }
}
