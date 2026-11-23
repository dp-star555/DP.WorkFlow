namespace DP.WorkFlow.UI;

/// <summary>指定工作室运行根工作流还是当前导航到的子工作流。</summary>
public enum WorkflowStudioRunTarget
{
    /// <summary>始终编译并运行最外层根工作流。</summary>
    RootWorkflow,
    /// <summary>运行导航器当前显示的画布；进入子流程时仅运行该子流程。</summary>
    CurrentCanvas
}

/// <summary>将 RuntimeHost 状态和快照连接到设计器导航器。</summary>
/// <remarks>
/// R1-04：快照由这里按帧拉取，不由引擎推送。帧计时器每隔 <see cref="FrameIntervalMilliseconds"/>
/// 比较一次 <see cref="IWorkflowRuntimeHost.ChangeVersion"/>，变了才取一份快照并在界面线程应用；
/// 暂停、完成、失败、取消等状态变化另外立即刷新一帧。界面每秒最多处理固定次数的快照，
/// 与流程跑得多快无关，执行线程也不再为界面构造快照。
/// </remarks>
public sealed class WorkflowStudioRuntimeBinding : IWorkflowDesignerRuntime, IWorkflowRuntimeMonitorSource, IDisposable
{
    private readonly IWorkflowRuntimeHost _host;
    private WorkflowDesignerNavigator _navigator;
    private readonly SynchronizationContext? _dispatchContext;
    private readonly object _snapshotSync = new();
    private readonly Timer _frameTimer;
    private int _frameIntervalMilliseconds = WorkflowUiRefresh.FrameIntervalMilliseconds;
    // 没有单独设置间隔时跟随全局刷新节拍（WorkflowUiRefresh）。
    private bool _followsUiRefresh = true;
    // 已排队但尚未在界面线程执行的一帧；同一时刻最多一帧，界面线程忙时不会越积越多。
    private bool _frameQueued;
    private long _lastPulledVersion = long.MinValue;
    /// <summary>默认刷新间隔（毫秒），约每秒 10 帧；实际默认值跟随 <see cref="WorkflowUiRefresh.FramesPerSecond"/>。</summary>
    public const int DefaultFrameIntervalMilliseconds = 1000 / WorkflowUiRefresh.DefaultFramesPerSecond;
    private Guid _lastAppliedRunId;
    private long _lastAppliedSequence = -1;
    private bool _disposed;

    /// <summary>初始化运行宿主与设计器导航器之间的状态绑定。</summary>
    /// <param name="host">工作流运行宿主。</param>
    /// <param name="navigator">要绑定的设计器导航器。</param>
    public WorkflowStudioRuntimeBinding(
        IWorkflowRuntimeHost host,
        WorkflowDesignerNavigator navigator)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _navigator = navigator ?? throw new ArgumentNullException(nameof(navigator));
        _dispatchContext = SynchronizationContext.Current;
        _host.StateChanged += OnHostStateChanged;
        BindOutputs();
        _lastPulledVersion = _host.ChangeVersion;
        _navigator.SetRuntimeSnapshot(_host.GetSnapshot());
        _frameTimer = new Timer(static state => ((WorkflowStudioRuntimeBinding)state!).OnFrameTick(), this,
            _frameIntervalMilliseconds, _frameIntervalMilliseconds);
        WorkflowUiRefresh.Changed += OnUiRefreshChanged;
    }

    private void OnUiRefreshChanged(object? sender, EventArgs e)
    {
        if (_followsUiRefresh) SetFrameInterval(WorkflowUiRefresh.FrameIntervalMilliseconds);
    }

    /// <summary>
    /// 获取或设置检查运行状态变化的间隔（毫秒，至少 16）。不设置时跟随 <see cref="WorkflowUiRefresh.FramesPerSecond"/>；
    /// 设置后本绑定固定用这个间隔。
    /// </summary>
    public int FrameIntervalMilliseconds
    {
        get => _frameIntervalMilliseconds;
        set
        {
            _followsUiRefresh = false;
            SetFrameInterval(value);
        }
    }

    private void SetFrameInterval(int value)
    {
        var interval = Math.Max(16, value);
        lock (_snapshotSync)
        {
            _frameIntervalMilliseconds = interval;
            if (!_disposed)
                _frameTimer.Change(interval, interval);
        }
    }

    public IWorkflowRuntimeHost Host => _host;

    public E_WorkflowExecutionState State => _host.State;

    /// <summary>获取或设置运行根流程还是当前打开的子画布。</summary>
    public WorkflowStudioRunTarget RunTarget { get; set; }

    /// <summary>启用后，每次运行前自动用目标画布和 Context 调用 Host.Configure。</summary>
    public bool AutoConfigureBeforeRun { get; set; }

    /// <summary>自动配置运行时使用的 Context；为空时创建新 Context。</summary>
    public WorkflowContext? RunContext { get; set; }

    /// <summary>在运行状态或运行时监视数据变化时发生。</summary>
    public event EventHandler? StateChanged;
    /// <summary>新运行配置前的目标文档路径；领域诊断据此将当前子画布运行映射回根文档。</summary>
    public event Action<string>? RunConfiguring;

    /// <summary>切换文档后更新接收运行快照的设计导航器。</summary>
    /// <param name="navigator">要绑定的设计器导航器。</param>
    public void SetNavigator(WorkflowDesignerNavigator navigator)
    {
        ThrowIfDisposed();
        _navigator = navigator ?? throw new ArgumentNullException(nameof(navigator));
        BindOutputs();
        _navigator.SetRuntimeSnapshot(_host.GetSnapshot());
    }

    /// <summary>
    /// 获取或设置开始新运行前执行的配置回调。宿主可在此用最新根画布调用 Configure；
    /// 当前已在运行时不会重复调用。
    /// </summary>
    public Action? ConfigureBeforeRun { get; set; }

    /// <summary>启动运行；运行中重复调用返回同一 Host 任务。</summary>
    /// <param name="cancellationToken">用于取消异步运行的令牌。</param>
    /// <returns>返回本次工作流运行结果。</returns>
    public async Task<WorkflowRunResult> RunAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (_host.State is not (E_WorkflowExecutionState.Running or E_WorkflowExecutionState.Paused))
        {
            var diagnosticPath = RunTarget == WorkflowStudioRunTarget.CurrentCanvas
                ? "$" + string.Concat(_navigator.Breadcrumbs.Skip(1).Select(b => "/" + Uri.EscapeDataString(b.ParentNodeId!))) : "$";
            RunConfiguring?.Invoke(diagnosticPath);
            if (AutoConfigureBeforeRun)
            {
                var session = RunTarget == WorkflowStudioRunTarget.CurrentCanvas
                    ? _navigator.CurrentSession
                    : _navigator.RootSession;
                var startNodeId = RunTarget == WorkflowStudioRunTarget.CurrentCanvas
                    ? _navigator.CurrentEntryNodeId
                    : _navigator.RootEntryNodeId;
                if (!string.Equals(session.Document.EntryNodeId, startNodeId, StringComparison.Ordinal))
                    throw new InvalidOperationException("运行目标文档的 EntryNodeId 与导航入口不一致，不能通过 Canvas 兼容入口启动。");
                _host.Configure(session.Document, RunContext);
            }
            else
                ConfigureBeforeRun?.Invoke();
        }
        var result = await _host.RunAsync(cancellationToken);
        ApplySnapshot(_host.GetSnapshot());
        StateChanged?.Invoke(this, EventArgs.Empty);
        return result;
    }

    /// <inheritdoc />
    public bool HasRun => _host.Engine is { } engine && engine.RunState.RunId != Guid.Empty;

    /// <inheritdoc />
    public WorkflowTraceBatch? GetTraceBatch(WorkflowTraceCursor? since = null) =>
        _disposed ? null : _host.Engine?.GetTraceBatch(includeChildRuns: true, since);

    /// <inheritdoc />
    public WorkflowRuntimeSnapshot? GetSnapshot() => _disposed ? null : _host.GetSnapshot();

    /// <inheritdoc />
    event EventHandler? IWorkflowRuntimeMonitorSource.Changed
    {
        add => StateChanged += value;
        remove => StateChanged -= value;
    }

    /// <inheritdoc />
    public async Task<WorkflowRunResult> RunNodeAsync(WorkflowDocument document, string nodeId, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var running = _host.RunNodeAsync(document, nodeId, cancellationToken);
        StateChanged?.Invoke(this, EventArgs.Empty);
        var result = await running;
        ApplySnapshot(_host.GetSnapshot());
        StateChanged?.Invoke(this, EventArgs.Empty);
        return result;
    }

    /// <summary>暂停当前工作流运行。</summary>
    public void Pause()
    {
        ThrowIfDisposed();
        _host.Pause();
    }

    /// <summary>恢复已暂停的工作流运行。</summary>
    public void Resume()
    {
        ThrowIfDisposed();
        _host.Resume();
    }

    /// <summary>请求取消当前工作流运行。</summary>
    public void Cancel()
    {
        ThrowIfDisposed();
        _host.Cancel();
    }

    /// <summary>
    /// 让根画布和当前画布可以查询引擎最近输出；会话按自身快照的 RunId 过滤，
    /// 因此子画布或旧运行不会显示其他运行的输出。
    /// </summary>
    private void BindOutputs()
    {
        _navigator.RootSession.NodeOutputProvider = FindLatestOutput;
        _navigator.CurrentSession.NodeOutputProvider = FindLatestOutput;
        // 子画布的快照属于子运行：按快照的运行标识向引擎查询，循环块、子流程内部节点的结果因此可见。
        _navigator.RootSession.RunNodeOutputProvider = FindLatestRunOutput;
        _navigator.CurrentSession.RunNodeOutputProvider = FindLatestRunOutput;
        _navigator.RootSession.Runtime = this;
        _navigator.CurrentSession.Runtime = this;
        _navigator.RootSession.RuntimePath = Array.Empty<string>();
        // 运行当前子画布时它就是运行的根画布；否则它位于根运行的子运行中。
        if (_navigator.Depth > 0)
            _navigator.CurrentSession.RuntimePath = RunTarget == WorkflowStudioRunTarget.CurrentCanvas
                ? Array.Empty<string>()
                : _navigator.Breadcrumbs.Skip(1).Select(item => item.ParentNodeId!).ToArray();
    }

    private WorkflowNodeOutput? FindLatestRunOutput(Guid runId, string nodeId) =>
        _disposed ? null : _host.Engine?.FindLatestNodeOutput(runId, nodeId);

    private WorkflowNodeOutput? FindLatestOutput(string nodeId)
    {
        if (_disposed || _host.Engine?.RunState is not { } runState) return null;
        var invalidated = runState.InvalidatedOutputSequences;
        return runState.NodeOutputs.LastOrDefault(output =>
            string.Equals(output.NodeId, nodeId, StringComparison.Ordinal)
            && !invalidated.Contains(output.ExecutionSequence));
    }

    /// <summary>解除事件订阅并释放当前模型持有的资源。</summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        lock (_snapshotSync)
        {
            _disposed = true;
            _frameTimer.Dispose();
        }
        _host.StateChanged -= OnHostStateChanged;
        WorkflowUiRefresh.Changed -= OnUiRefreshChanged;
    }

    /// <summary>帧计时器：状态有变化才排队一帧，没变化什么都不做。</summary>
    private void OnFrameTick()
    {
        if (_disposed || _host.ChangeVersion == Volatile.Read(ref _lastPulledVersion))
            return;
        QueueFrame();
    }

    /// <summary>停止类状态（暂停、完成、失败、取消）立即刷新一帧，不等下一次帧检查。</summary>
    private void OnHostStateChanged(E_WorkflowExecutionState state)
    {
        if (state is E_WorkflowExecutionState.Paused or E_WorkflowExecutionState.Completed
            or E_WorkflowExecutionState.Faulted or E_WorkflowExecutionState.Canceled)
            QueueFrame();
    }

    /// <summary>把一帧投递到界面同步上下文（没有时用线程池）；已有一帧在排队时不重复投递。</summary>
    private void QueueFrame()
    {
        lock (_snapshotSync)
        {
            if (_disposed || _frameQueued)
                return;
            _frameQueued = true;
        }
        if (_dispatchContext is not null)
            _dispatchContext.Post(static state => ((WorkflowStudioRuntimeBinding)state!).ApplyFrame(), this);
        else
            ThreadPool.QueueUserWorkItem(static state => ((WorkflowStudioRuntimeBinding)state!).ApplyFrame(), this);
    }

    /// <summary>在界面线程取一份最新快照并应用。先记下变化计数再取快照，期间的变化留给下一帧。</summary>
    private void ApplyFrame()
    {
        lock (_snapshotSync)
        {
            _frameQueued = false;
            if (_disposed)
                return;
        }
        Volatile.Write(ref _lastPulledVersion, _host.ChangeVersion);
        ApplySnapshot(_host.GetSnapshot());
    }

    /// <summary>将运行时快照更新到导航器和监视模型。</summary>
    /// <param name="snapshot">运行时快照。</param>
    private void ApplySnapshot(WorkflowRuntimeSnapshot snapshot)
    {
        lock (_snapshotSync)
        {
            if (snapshot.RunId == _lastAppliedRunId && snapshot.Sequence < _lastAppliedSequence) return;
            _lastAppliedRunId = snapshot.RunId;
            _lastAppliedSequence = snapshot.Sequence;
        }
        BindOutputs();
        if (RunTarget == WorkflowStudioRunTarget.CurrentCanvas && _navigator.Depth > 0)
            _navigator.CurrentSession.SetRuntimeSnapshot(snapshot);
        else
            _navigator.SetRuntimeSnapshot(snapshot);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>对象已经释放时抛出异常。</summary>
    private void ThrowIfDisposed() => Guard.NotDisposed(_disposed, this);
}
