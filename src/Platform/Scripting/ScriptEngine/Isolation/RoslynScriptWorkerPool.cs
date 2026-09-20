using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Pipes;
using System.Text;

namespace ScriptEngine;

/// <summary>
/// 复用常驻 Worker 进程的有界执行池。每个 Worker 串行执行请求，池内 Worker 可并行；
/// 超时、超出内存、协议故障、崩溃或达到回收次数后自动丢弃并在下一次请求时补充。
/// </summary>
public sealed class RoslynScriptWorkerPool : IDisposable
{
    private readonly RoslynScriptWorkerOptions _options;
    private readonly ConcurrentQueue<PooledWorker> _available = new();
    private readonly SemaphoreSlim _capacity;
    private readonly object _gate = new();
    private readonly HashSet<PooledWorker> _workers = new();
    private readonly int _maximumExecutionsPerWorker;
    private bool _disposed;

    /// <summary>创建通过命名管道通信的常驻 Worker 池。</summary>
    /// <param name="options">进程路径、操作系统隔离和每次请求的资源限制。</param>
    /// <param name="workerCount">最多常驻并行 Worker 数。</param>
    /// <param name="maximumExecutionsPerWorker">单进程执行多少次后主动回收，net48 可借此释放动态程序集。</param>
    public RoslynScriptWorkerPool(
        RoslynScriptWorkerOptions options,
        int workerCount = 2,
        int maximumExecutionsPerWorker = 50)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        if (workerCount is < 1 or > 32) throw new ArgumentOutOfRangeException(nameof(workerCount));
        if (maximumExecutionsPerWorker is < 1 or > 10000)
            throw new ArgumentOutOfRangeException(nameof(maximumExecutionsPerWorker));
        ValidateOptions(options);
        _capacity = new SemaphoreSlim(workerCount, workerCount);
        _maximumExecutionsPerWorker = maximumExecutionsPerWorker;
    }

    /// <summary>使用池中的常驻进程执行单文件脚本。</summary>
    public Task<RoslynScriptWorkerResult> ExecuteAsync(
        string source,
        RoslynScriptEnvironment? environment = null,
        CancellationToken cancellationToken = default)
    {
        if (source is null) throw new ArgumentNullException(nameof(source));
        return ExecuteFilesAsync(
            new[] { new RoslynScriptSourceFile("Program.cs", source) },
            environment,
            cancellationToken);
    }

    /// <summary>使用池中的常驻进程执行多文件项目。</summary>
    public Task<RoslynScriptWorkerResult> ExecuteProjectAsync(
        RoslynScriptProject project,
        RoslynScriptEnvironment? environment = null,
        CancellationToken cancellationToken = default)
    {
        if (project is null) throw new ArgumentNullException(nameof(project));
        return ExecuteFilesAsync(project.GetValidatedFiles(), environment, cancellationToken);
    }

    /// <summary>终止全部常驻 Worker 及其 Job Object 进程树。</summary>
    public void Dispose()
    {
        List<PooledWorker> workers;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            workers = _workers.ToList();
            _workers.Clear();
            while (_available.TryDequeue(out _)) { }
        }
        foreach (var worker in workers) worker.Dispose();
    }

    private async Task<RoslynScriptWorkerResult> ExecuteFilesAsync(
        IReadOnlyList<RoslynScriptSourceFile> files,
        RoslynScriptEnvironment? environment,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        await _capacity.WaitAsync(cancellationToken).ConfigureAwait(false);
        PooledWorker? worker = null;
        try
        {
            ThrowIfDisposed();
            if (!_available.TryDequeue(out worker))
            {
                worker = new PooledWorker(_options);
                lock (_gate)
                {
                    ThrowIfDisposed();
                    _workers.Add(worker);
                }
            }
            return await worker.ExecuteAsync(
                files,
                environment ?? new RoslynScriptEnvironment(),
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (worker is not null)
            {
                if (worker.IsHealthy && worker.ExecutionCount < _maximumExecutionsPerWorker && !_disposed)
                {
                    _available.Enqueue(worker);
                }
                else
                {
                    lock (_gate) _workers.Remove(worker);
                    worker.Dispose();
                }
            }
            _capacity.Release();
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(RoslynScriptWorkerPool));
    }

    private static void ValidateOptions(RoslynScriptWorkerOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.WorkerPath))
            throw new ArgumentException("Worker 路径不能为空。", nameof(options));
        if (!File.Exists(Path.GetFullPath(options.WorkerPath)))
            throw new FileNotFoundException("找不到脚本 Worker。", options.WorkerPath);
        if (options.Timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options), "Worker 超时必须大于零。");
        if (options.MaximumOutputCharacters < 1)
            throw new ArgumentOutOfRangeException(nameof(options), "Worker 输出上限必须大于零。");
    }

    private sealed class PooledWorker : IDisposable
    {
        private readonly RoslynScriptWorkerOptions _options;
        private readonly Process _process;
        private readonly WindowsProcessJob? _processJob;
        private readonly NamedPipeServerStream _pipe;
        private readonly StreamReader _reader;
        private readonly StreamWriter _writer;
        private readonly Task _initializationTask;
        private bool _disposed;

        public PooledWorker(RoslynScriptWorkerOptions options)
        {
            _options = options;
            var pipeName = "ScriptEngine-" + Guid.NewGuid().ToString("N");
            _pipe = new NamedPipeServerStream(
                pipeName,
                PipeDirection.InOut,
                1,
                PipeTransmissionMode.Byte,
#if NETFRAMEWORK
                PipeOptions.Asynchronous);
#else
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
#endif
            _reader = new StreamReader(_pipe, new UTF8Encoding(false), false, 4096, leaveOpen: true);
            _writer = new StreamWriter(_pipe, new UTF8Encoding(false), 4096, leaveOpen: true);

            var workerPath = Path.GetFullPath(options.WorkerPath);
            var isDll = string.Equals(Path.GetExtension(workerPath), ".dll", StringComparison.OrdinalIgnoreCase);
            var startInfo = new ProcessStartInfo
            {
                FileName = isDll ? "dotnet" : workerPath,
                Arguments = isDll
                    ? RoslynScriptWorkerClient.Quote(workerPath) + " --pipe " + RoslynScriptWorkerClient.Quote(pipeName)
                    : "--pipe " + RoslynScriptWorkerClient.Quote(pipeName),
                WorkingDirectory = string.IsNullOrWhiteSpace(options.WorkingDirectory)
                    ? Path.GetDirectoryName(workerPath) ?? Environment.CurrentDirectory
                    : Path.GetFullPath(options.WorkingDirectory),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            _process = new Process { StartInfo = startInfo };
            try
            {
                if (!_process.Start()) throw new InvalidOperationException("无法启动常驻脚本 Worker。");
                _processJob = WindowsProcessJob.TryAssign(_process, options);
                _ = _process.StandardOutput.ReadToEndAsync();
                _ = _process.StandardError.ReadToEndAsync();
                _initializationTask = InitializePipeAsync();
            }
            catch
            {
                Kill(_process);
                _process.Dispose();
                _writer.Dispose();
                _reader.Dispose();
                _pipe.Dispose();
                throw;
            }
        }

        public bool IsHealthy { get; private set; } = true;
        public int ExecutionCount { get; private set; }

        public async Task<RoslynScriptWorkerResult> ExecuteAsync(
            IReadOnlyList<RoslynScriptSourceFile> files,
            RoslynScriptEnvironment environment,
            CancellationToken cancellationToken)
        {
            if (_disposed || !IsHealthy) throw new ObjectDisposedException(nameof(PooledWorker));
            var requestDirectory = Path.Combine(Path.GetTempPath(), "ScriptEngineWorker", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(requestDirectory);
            var stopwatch = Stopwatch.StartNew();
            try
            {
                RoslynScriptWorkerClient.WriteRequest(
                    requestDirectory,
                    files,
                    environment,
                    _options.MaximumOutputCharacters);
                await AwaitWithLimitsAsync(_initializationTask, stopwatch, cancellationToken).ConfigureAwait(false);
                await PingAsync(stopwatch, cancellationToken).ConfigureAwait(false);

                var nonce = Guid.NewGuid().ToString("N");
                await SendAsync(
                    "RUN|" + nonce + "|" + Convert.ToBase64String(Encoding.UTF8.GetBytes(requestDirectory)),
                    stopwatch,
                    cancellationToken).ConfigureAwait(false);
                ExecutionCount++;
                var line = await ReadLineAsync(stopwatch, cancellationToken).ConfigureAwait(false);
                var expectedPrefix = "DONE|" + nonce + "|";
                if (line is null || !line.StartsWith(expectedPrefix, StringComparison.Ordinal))
                    throw new WorkerProtocolException("常驻脚本 Worker 返回了无效的请求响应。");

                var responsePath = Path.Combine(requestDirectory, "response.txt");
                if (!File.Exists(responsePath))
                    throw new WorkerProtocolException("常驻脚本 Worker 未返回有效响应文件。");
                var exitText = line.Substring(expectedPrefix.Length);
                var exitCode = int.TryParse(exitText, out var parsed) ? parsed : (int?)null;
                return RoslynScriptWorkerClient.ReadResponse(responsePath, exitCode) with
                {
                    OperatingSystemIsolationApplied = _processJob is not null
                };
            }
            catch (WorkerTimeoutException exception)
            {
                await InvalidateAsync().ConfigureAwait(false);
                return Failed(exception.Message, timedOut: true, resourceExceeded: false);
            }
            catch (WorkerResourceLimitException exception)
            {
                await InvalidateAsync().ConfigureAwait(false);
                return Failed(exception.Message, timedOut: false, resourceExceeded: true);
            }
            catch (WorkerProtocolException exception)
            {
                await InvalidateAsync().ConfigureAwait(false);
                return Failed(exception.Message, timedOut: false, resourceExceeded: false);
            }
            catch (OperationCanceledException)
            {
                await InvalidateAsync().ConfigureAwait(false);
                throw;
            }
            catch (Exception exception) when (exception is IOException or InvalidOperationException
                                               or ObjectDisposedException)
            {
                await InvalidateAsync().ConfigureAwait(false);
                return Failed("常驻脚本 Worker 通信失败：" + exception.Message, false, false);
            }
            finally
            {
                RoslynScriptWorkerClient.TryDeleteDirectory(requestDirectory);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try
            {
                if (!_process.HasExited && _pipe.IsConnected)
                {
                    _writer.WriteLine("QUIT");
                    _writer.Flush();
                    if (!_process.WaitForExit(1000)) Kill(_process);
                }
            }
            catch (InvalidOperationException) { }
            catch (IOException) { Kill(_process); }
            finally
            {
                _processJob?.Dispose();
                _writer.Dispose();
                _reader.Dispose();
                _pipe.Dispose();
                _process.Dispose();
            }
        }

        private async Task InitializePipeAsync()
        {
            await _pipe.WaitForConnectionAsync().ConfigureAwait(false);
            var ready = await _reader.ReadLineAsync().ConfigureAwait(false);
            var parts = ready?.Split('|');
            if (parts is not { Length: 3 }
                || !string.Equals(parts[0], "READY", StringComparison.Ordinal)
                || !string.Equals(parts[1], "1", StringComparison.Ordinal)
                || !int.TryParse(parts[2], out var processId)
                || processId != _process.Id)
                throw new WorkerProtocolException("脚本 Worker 命名管道握手失败、进程身份错误或协议版本不兼容。");
        }

        private async Task PingAsync(Stopwatch stopwatch, CancellationToken cancellationToken)
        {
            var nonce = Guid.NewGuid().ToString("N");
            await SendAsync("PING|" + nonce, stopwatch, cancellationToken).ConfigureAwait(false);
            var response = await ReadLineAsync(stopwatch, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(response, "PONG|" + nonce + "|1", StringComparison.Ordinal))
                throw new WorkerProtocolException("脚本 Worker 健康检查失败。");
        }

        private async Task SendAsync(string message, Stopwatch stopwatch, CancellationToken cancellationToken)
        {
            await AwaitWithLimitsAsync(_writer.WriteLineAsync(message), stopwatch, cancellationToken).ConfigureAwait(false);
            await AwaitWithLimitsAsync(_writer.FlushAsync(), stopwatch, cancellationToken).ConfigureAwait(false);
        }

        private Task<string?> ReadLineAsync(Stopwatch stopwatch, CancellationToken cancellationToken) =>
            AwaitWithLimitsAsync(_reader.ReadLineAsync(), stopwatch, cancellationToken);

        private async Task AwaitWithLimitsAsync(
            Task task,
            Stopwatch stopwatch,
            CancellationToken cancellationToken)
        {
            while (!task.IsCompleted)
            {
                CheckLimits(stopwatch, cancellationToken);
                await Task.WhenAny(task, Task.Delay(50, cancellationToken)).ConfigureAwait(false);
            }
            await task.ConfigureAwait(false);
        }

        private async Task<T> AwaitWithLimitsAsync<T>(
            Task<T> task,
            Stopwatch stopwatch,
            CancellationToken cancellationToken)
        {
            while (!task.IsCompleted)
            {
                CheckLimits(stopwatch, cancellationToken);
                await Task.WhenAny(task, Task.Delay(50, cancellationToken)).ConfigureAwait(false);
            }
            return await task.ConfigureAwait(false);
        }

        private void CheckLimits(Stopwatch stopwatch, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_disposed) throw new ObjectDisposedException(nameof(PooledWorker));
            if (_process.HasExited) throw new WorkerProtocolException("常驻脚本 Worker 意外退出。");
            if (stopwatch.Elapsed >= _options.Timeout)
                throw new WorkerTimeoutException("脚本执行超过硬超时。");
            if (_options.MaximumWorkingSetBytes <= 0) return;
            _process.Refresh();
            if (_process.WorkingSet64 > _options.MaximumWorkingSetBytes)
                throw new WorkerResourceLimitException("脚本 Worker 超出工作集上限。");
        }

        private async Task InvalidateAsync()
        {
            IsHealthy = false;
            Kill(_process);
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (DateTime.UtcNow < deadline)
            {
                try
                {
                    if (_process.HasExited) return;
                }
                catch (InvalidOperationException)
                {
                    return;
                }
                await Task.Delay(20).ConfigureAwait(false);
            }
        }

        private RoslynScriptWorkerResult Failed(string message, bool timedOut, bool resourceExceeded) => new(
            false,
            timedOut,
            resourceExceeded,
            null,
            null,
            string.Empty,
            Array.Empty<RoslynScriptDiagnostic>(),
            message,
            TryGetExitCode(_process),
            _processJob is not null);

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

        private sealed class WorkerTimeoutException(string message) : Exception(message);
        private sealed class WorkerResourceLimitException(string message) : Exception(message);
        private sealed class WorkerProtocolException(string message) : Exception(message);
    }
}
