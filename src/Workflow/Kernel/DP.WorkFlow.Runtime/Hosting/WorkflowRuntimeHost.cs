namespace DP.WorkFlow;

/// <summary>
/// 管理画布编译、引擎创建、运行复用、取消及事件转发。
/// UI 只依赖该宿主，不需要直接管理 WorkflowEngine 生命周期。
/// </summary>
public sealed class WorkflowRuntimeHost : IWorkflowRuntimeHost, IDisposable
{
    private readonly object _syncRoot = new();
    private readonly WorkflowNodeCatalog _nodeCatalog;
    private readonly WorkflowNodeHandlerCatalog _handlerCatalog;
    private readonly WorkflowExecutionOptions _executionOptions;
    private WorkflowBoundExecutionPlan? _boundPlan;
    private WorkflowContext? _context;
    private WorkflowEngine? _engine;
    private Task<WorkflowRunResult>? _runTask;
    private CancellationTokenSource? _runCancellation;
    private WorkflowRunResult? _lastRunResult;
    private readonly HashSet<string> _externalHoldReasons = new(StringComparer.Ordinal);
    private bool _manualPauseRequested;
    private bool _disposed;

    /// <summary>初始化负责定义编译和引擎生命周期的运行宿主。</summary>
    /// <param name="nodeCatalog">编译期使用的节点类型和端口目录。</param>
    /// <param name="handlerCatalog">运行期用于解析节点处理器的目录。</param>
    /// <param name="executionOptions">每次创建引擎时复用的执行安全上限。</param>
    public WorkflowRuntimeHost(
        WorkflowNodeCatalog nodeCatalog,
        WorkflowNodeHandlerCatalog handlerCatalog,
        WorkflowExecutionOptions? executionOptions = null)
    {
        _nodeCatalog = nodeCatalog ?? throw new ArgumentNullException(nameof(nodeCatalog));
        _handlerCatalog = handlerCatalog ?? throw new ArgumentNullException(nameof(handlerCatalog));
        _executionOptions = executionOptions ?? new WorkflowExecutionOptions();
    }

    /// <inheritdoc />
    public WorkflowEngine? Engine
    {
        get
        {
            lock (_syncRoot)
                return _engine;
        }
    }

    /// <inheritdoc />
    public E_WorkflowExecutionState State => Engine?.State ?? E_WorkflowExecutionState.Idle;

    /// <inheritdoc />
    public WorkflowRunResult? LastRunResult
    {
        get
        {
            lock (_syncRoot)
                return _lastRunResult;
        }
    }

    /// <summary>引擎实例变化时发生。</summary>
    public event Action<WorkflowEngine>? EngineChanged;

    /// <summary>运行快照变化时发生。</summary>
    public event Action<WorkflowRuntimeSnapshot>? SnapshotChanged;

    /// <inheritdoc />
    public void Configure(WorkflowDocument document, WorkflowContext? context = null)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(document);
        var plan = new WorkflowCompiler(_nodeCatalog).Compile(document);
        ConfigurePlan(plan, context);
    }

    /// <summary>直接配置已经编译并隔离的执行计划。</summary>
    public void ConfigurePlan(WorkflowExecutionPlan plan, WorkflowContext? context = null)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(plan);
        var boundPlan = new WorkflowRuntimeBinder(_handlerCatalog).Bind(plan);
        lock (_syncRoot)
        {
            EnsureNotRunningLocked();
            DetachEngineLocked();
            _boundPlan = boundPlan;
            _context = context ?? new WorkflowContext();
            _lastRunResult = null;
            _runTask = null;
        }
    }

    /// <inheritdoc />
    public Task<WorkflowRunResult> RunAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        WorkflowEngine? createdEngine = null;
        Task<WorkflowRunResult> task;
        lock (_syncRoot)
        {
            if (_runTask is { IsCompleted: false })
                return _runTask;
            if (_boundPlan is null || _context is null)
                throw new InvalidOperationException("运行宿主尚未 Configure。");

            _runCancellation?.Dispose();
            _runCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            createdEngine = new WorkflowEngine(_boundPlan, _context, _executionOptions);
            AttachEngineLocked(createdEngine);
            _engine = createdEngine;
            _lastRunResult = null;
            if (_manualPauseRequested)
                createdEngine.Pause();
            foreach (var holdReason in _externalHoldReasons)
                createdEngine.AddExternalHold(holdReason);
            // Host 是 UI 与运行引擎之间的异步边界。不能直接调用 RunCoreAsync：
            // async 方法会在遇到第一个真正未完成的 await 之前同步执行；如果节点返回已完成
            // ValueTask 并在其中执行 ONNX/图像处理等 CPU 工作，调用 RunAsync 的 UI 线程就会被阻塞。
            // 在此统一调度到线程池，使所有节点（包括“异步签名、同步计算”的节点）都不会占用 UI 线程。
            var engineToRun = createdEngine;
            var runContext = _context;
            var runPlan = _boundPlan;
            var runToken = _runCancellation.Token;
            _runTask = Task.Run(() => RunCoreAsync(engineToRun, runContext, runPlan, runToken));
            task = _runTask;
        }

        SafeInvoke(EngineChanged, createdEngine);
        return task;
    }

    /// <summary>请求取消并等待当前运行（包括运行准备）结束；宿主应先禁用新的Run请求，再释放设备及图像资源。</summary>
    /// <returns>当前任务完成；运行准备异常仍向调用者传播。</returns>
    public async Task StopAsync()
    {
        Task<WorkflowRunResult>? running;
        lock (_syncRoot) running = _runTask;
        Cancel();
        if (running is not null) await running.ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void Pause()
    {
        WorkflowEngine? engine;
        lock (_syncRoot)
        {
            _manualPauseRequested = true;
            engine = _engine;
        }
        engine?.Pause();
    }

    /// <inheritdoc />
    public void Resume()
    {
        WorkflowEngine? engine;
        lock (_syncRoot)
        {
            _manualPauseRequested = false;
            engine = _engine;
        }
        engine?.Resume();
    }

    /// <inheritdoc />
    public void AddExternalHold(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("外部 Hold 原因不能为空。", nameof(reason));
        var normalizedReason = reason.Trim();
        WorkflowEngine? engine;
        lock (_syncRoot)
        {
            _externalHoldReasons.Add(normalizedReason);
            engine = _engine;
        }
        engine?.AddExternalHold(normalizedReason);
    }

    /// <inheritdoc />
    public void RemoveExternalHold(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            return;
        var normalizedReason = reason.Trim();
        WorkflowEngine? engine;
        lock (_syncRoot)
        {
            _externalHoldReasons.Remove(normalizedReason);
            engine = _engine;
        }
        engine?.RemoveExternalHold(normalizedReason);
    }

    /// <inheritdoc />
    public void Cancel()
    {
        lock (_syncRoot)
            _runCancellation?.Cancel();
    }

    /// <inheritdoc />
    public WorkflowRuntimeSnapshot GetSnapshot()
    {
        var engine = Engine;
        if (engine is not null)
            return engine.GetRuntimeSnapshot();

        lock (_syncRoot)
        {
            return new WorkflowRuntimeSnapshot(
                Guid.Empty,
                0,
                DateTimeOffset.UtcNow,
                _boundPlan?.Plan.Name ?? string.Empty,
                E_WorkflowExecutionState.Idle,
                null,
                Array.Empty<string>(),
                new Dictionary<long, WorkflowActiveTokenInfo>(),
                TimeSpan.Zero,
                new Dictionary<string, WorkflowNodeRuntimeInfo>(),
                new Dictionary<long, WorkflowParallelScopeInfo>(),
                new Dictionary<string, WorkflowChildRuntimeInfo>(),
                Array.Empty<string>());
        }
    }

    /// <summary>取消当前运行并等待结束后清理引擎；编译计划、上下文、流程局部变量和公共数据仍会保留。</summary>
    /// <param name="cancellationToken">只控制等待清理的调用方；当前运行始终会收到宿主取消请求。</param>
    /// <returns>引擎事件解除订阅且最近结果清空后完成的任务。</returns>
    public async Task ResetAsync(CancellationToken cancellationToken = default)
    {
        Task<WorkflowRunResult>? runningTask;
        lock (_syncRoot)
        {
            _runCancellation?.Cancel();
            runningTask = _runTask;
        }

        if (runningTask is not null)
            await runningTask.WaitAsync(cancellationToken).ConfigureAwait(false);

        lock (_syncRoot)
        {
            DetachEngineLocked();
            _engine = null;
            _runTask = null;
            _lastRunResult = null;
            _runCancellation?.Dispose();
            _runCancellation = null;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        lock (_syncRoot)
        {
            _runCancellation?.Cancel();
            DetachEngineLocked();
            _runCancellation?.Dispose();
            _runCancellation = null;
        }
    }

    /// <summary>在线程池边界内执行可选运行准备服务，然后启动本次引擎。</summary>
    /// <param name="engine">本次运行新创建的引擎。</param>
    /// <param name="context">用于发现 <see cref="IWorkflowRunPreparationService"/> 的上下文。</param>
    /// <param name="plan">本次运行已经编译且与文档隔离的执行计划。</param>
    /// <param name="cancellationToken">宿主与调用方链接后的运行取消令牌。</param>
    /// <returns>引擎最终结果，同时写入 <see cref="LastRunResult"/>。</returns>
    private async Task<WorkflowRunResult> RunCoreAsync(
        WorkflowEngine engine,
        WorkflowContext context,
        WorkflowBoundExecutionPlan plan,
        CancellationToken cancellationToken)
    {
        WorkflowRuntimeCapabilityValidator.Validate(plan, context.Services);
        if (context.Services.GetService(typeof(IWorkflowRunPreparationService)) is IWorkflowRunPreparationService preparation)
            await preparation.PrepareAsync(
                new WorkflowRunPreparationContext(EnumerateNodes(plan.Plan).ToArray()),
                cancellationToken).ConfigureAwait(false);
        var result = await engine.RunAsync(cancellationToken).ConfigureAwait(false);
        lock (_syncRoot)
            _lastRunResult = result;
        return result;
    }

    private static IEnumerable<IWorkflowNodeModel> EnumerateNodes(WorkflowExecutionPlan plan)
    {
        foreach (var nodeId in plan.NodeIds)
            yield return plan.GetNodeOrThrow(nodeId);
        foreach (var child in plan.ChildPlans.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        foreach (var node in EnumerateNodes(child.Value))
            yield return node;
    }

    private void AttachEngineLocked(WorkflowEngine engine)
    {
        engine.SnapshotChanged += OnEngineSnapshotChanged;
    }

    private void DetachEngineLocked()
    {
        if (_engine is not null)
            _engine.SnapshotChanged -= OnEngineSnapshotChanged;
    }

    private void OnEngineSnapshotChanged(WorkflowRuntimeSnapshot snapshot) =>
        SafeInvoke(SnapshotChanged, snapshot);

    private void EnsureNotRunningLocked()
    {
        if (_runTask is { IsCompleted: false })
            throw new InvalidOperationException("流程运行期间不能重新 Configure。");
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    private static void SafeInvoke<T>(Action<T>? handlers, T argument)
    {
        if (handlers is null)
            return;
        foreach (Action<T> handler in handlers.GetInvocationList())
        {
            try
            {
                handler(argument);
            }
            catch
            {
                // UI 或观察者异常不能改变工作流运行结果。
            }
        }
    }
}
