namespace DP.WorkFlow.UI;

/// <summary>
/// 节点窗口子画布的运行命令。“运行”在最近一次运行的基础上把这个复合节点再正常执行一次：
/// 输入取自该次运行的结果，结果照常写入并取代该节点上一次的结果；执行的是窗口里当前的内容（含尚未确认的修改）。
/// 流程正在运行或暂停时不能单独运行，暂停、继续、停止作用于整个流程。
/// </summary>
public sealed class WorkflowSubWorkflowRunCommands : IWorkflowCanvasRunCommands, IDisposable
{
    private readonly WorkflowSubWorkflowEditorPageModel _page;
    private IWorkflowDesignerRuntime? _runtime;
    private bool _disposed;

    /// <summary>为子画布页创建运行命令。</summary>
    /// <param name="page">节点窗口的子画布页。</param>
    public WorkflowSubWorkflowRunCommands(WorkflowSubWorkflowEditorPageModel page)
    {
        _page = page ?? throw new ArgumentNullException(nameof(page));
        _page.Session.Changed += OnSessionChanged;
        AttachRuntime();
    }

    /// <inheritdoc />
    public E_WorkflowExecutionState State => _page.Session.Runtime?.State ?? E_WorkflowExecutionState.Idle;

    /// <inheritdoc />
    public bool CanRun => BlockReason is null;

    /// <inheritdoc />
    public string RunText => "运行子画布";

    /// <inheritdoc />
    public string RunToolTip => BlockReason ?? "单独运行本子画布（使用上次运行的输入，结果写入流程）";

    /// <summary>不能单独运行的原因；可以运行时为空。</summary>
    public string? BlockReason
    {
        get
        {
            var runtime = _page.Session.Runtime;
            if (runtime is null) return "未接入运行时，不能运行。";
            if (runtime.State is E_WorkflowExecutionState.Running or E_WorkflowExecutionState.Paused)
                return "流程正在运行；暂停、继续、停止作用于整个流程。";
            if (!runtime.HasRun) return "还没有运行过流程：先运行一次主流程，子画布才能使用上游节点的结果。";
            // 继续运行在根运行中执行该节点：只有直接放在运行画布上的复合节点可以单独运行。
            if (_page.RuntimeSession?.RuntimePath.Count is not 0)
                return "只能单独运行主画布上的子流程、循环块；嵌套在其他子画布中的请从外层运行。";
            return null;
        }
    }

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <inheritdoc />
    public Task<WorkflowRunResult> RunAsync()
    {
        if (BlockReason is { } reason)
            throw new InvalidOperationException(reason);
        // 子画布启动节点的输入成员来自复合节点的接口；编译前按当前画布刷新。
        WorkflowSubflowInterfaces.Refresh(_page.ParentSession.Document, _page.ParentSession.Catalog);
        return _page.Session.Runtime!.RunNodeAsync(_page.ParentSession.Document, _page.Node.Id);
    }

    /// <inheritdoc />
    public void Pause() => _page.Session.Runtime?.Pause();

    /// <inheritdoc />
    public void Resume() => _page.Session.Runtime?.Resume();

    /// <inheritdoc />
    public void Cancel() => _page.Session.Runtime?.Cancel();

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _page.Session.Changed -= OnSessionChanged;
        if (_runtime is not null) _runtime.StateChanged -= OnRuntimeChanged;
        _runtime = null;
    }

    private void OnSessionChanged(object? sender, WorkflowDesignerChangedEventArgs e)
    {
        if (e.Kind != WorkflowDesignerChangeKind.Runtime) return;
        AttachRuntime();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void OnRuntimeChanged(object? sender, EventArgs e) => Changed?.Invoke(this, EventArgs.Empty);

    private void AttachRuntime()
    {
        if (_disposed || ReferenceEquals(_runtime, _page.Session.Runtime)) return;
        if (_runtime is not null) _runtime.StateChanged -= OnRuntimeChanged;
        _runtime = _page.Session.Runtime;
        if (_runtime is not null) _runtime.StateChanged += OnRuntimeChanged;
    }
}
