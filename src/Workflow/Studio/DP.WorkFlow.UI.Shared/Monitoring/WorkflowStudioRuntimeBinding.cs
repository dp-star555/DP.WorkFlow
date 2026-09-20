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
public sealed class WorkflowStudioRuntimeBinding : IDisposable
{
    private readonly IWorkflowRuntimeHost _host;
    private WorkflowDesignerNavigator _navigator;
    private readonly SynchronizationContext? _dispatchContext;
    private readonly object _snapshotSync = new();
    private WorkflowRuntimeSnapshot? _pendingSnapshot;
    private bool _snapshotDispatchQueued;
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
        _host.SnapshotChanged += OnSnapshotChanged;
        _navigator.SetRuntimeSnapshot(_host.GetSnapshot());
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

    /// <summary>切换文档后更新接收运行快照的设计导航器。</summary>
    /// <param name="navigator">要绑定的设计器导航器。</param>
    public void SetNavigator(WorkflowDesignerNavigator navigator)
    {
        ThrowIfDisposed();
        _navigator = navigator ?? throw new ArgumentNullException(nameof(navigator));
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

    /// <summary>解除事件订阅并释放当前模型持有的资源。</summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        lock (_snapshotSync)
        {
            _disposed = true;
            _pendingSnapshot = null;
        }
        _host.SnapshotChanged -= OnSnapshotChanged;
    }

    /// <summary>接收运行时快照并合并待分发状态。</summary>
    /// <param name="snapshot">运行时快照。</param>
    private void OnSnapshotChanged(WorkflowRuntimeSnapshot snapshot)
    {
        lock (_snapshotSync)
        {
            _pendingSnapshot = snapshot;
            if (_snapshotDispatchQueued) return;
            _snapshotDispatchQueued = true;
        }
        QueueSnapshotDispatch();
    }

    /// <summary>保证快照分发任务只排队一次。</summary>
    private void QueueSnapshotDispatch()
    {
        if (_dispatchContext is not null)
            _dispatchContext.Post(static state => ((WorkflowStudioRuntimeBinding)state!).DispatchPendingSnapshot(), this);
        else
            ThreadPool.QueueUserWorkItem(static state => ((WorkflowStudioRuntimeBinding)state!).DispatchPendingSnapshot(), this);
    }

    /// <summary>在同步上下文中提取并应用最新待处理快照。</summary>
    private void DispatchPendingSnapshot()
    {
        WorkflowRuntimeSnapshot? snapshot;
        lock (_snapshotSync)
        {
            if (_disposed)
            {
                _pendingSnapshot = null;
                _snapshotDispatchQueued = false;
                return;
            }
            snapshot = _pendingSnapshot;
            _pendingSnapshot = null;
        }
        if (snapshot is not null) ApplySnapshot(snapshot);
        lock (_snapshotSync)
        {
            if (_pendingSnapshot is null)
            {
                _snapshotDispatchQueued = false;
                return;
            }
        }
        QueueSnapshotDispatch();
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
        if (RunTarget == WorkflowStudioRunTarget.CurrentCanvas && _navigator.Depth > 0)
            _navigator.CurrentSession.SetRuntimeSnapshot(snapshot);
        else
            _navigator.SetRuntimeSnapshot(snapshot);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>对象已经释放时抛出异常。</summary>
    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
