namespace DP.WorkFlow.UI;

/// <summary>画布工具栏上的运行命令：运行、暂停、继续、停止；主画布和节点窗口的子画布各自提供实现。</summary>
public interface IWorkflowCanvasRunCommands
{
    /// <summary>当前运行状态。</summary>
    E_WorkflowExecutionState State { get; }

    /// <summary>现在能否开始运行。</summary>
    bool CanRun { get; }

    /// <summary>运行按钮的文字，例如“运行”“运行当前子流程”“运行子画布”。</summary>
    string RunText { get; }

    /// <summary>运行按钮的提示；不能运行时说明原因。</summary>
    string RunToolTip { get; }

    /// <summary>开始运行。</summary>
    Task<WorkflowRunResult> RunAsync();

    /// <summary>暂停当前运行。</summary>
    void Pause();

    /// <summary>继续当前运行。</summary>
    void Resume();

    /// <summary>停止当前运行。</summary>
    void Cancel();

    /// <summary>状态或可用性变化时发生，可能来自非界面线程。</summary>
    event EventHandler? Changed;
}

/// <summary>工作台主画布的运行命令：运行整个流程（导航到子画布时按运行目标运行当前子流程）。</summary>
public sealed class WorkflowStudioRunCommands : IWorkflowCanvasRunCommands, IDisposable
{
    private readonly WorkflowStudioRuntimeBinding _binding;
    private readonly Func<string> _runToolTip;

    /// <summary>创建主画布运行命令。</summary>
    /// <param name="binding">运行绑定。</param>
    /// <param name="runToolTip">运行按钮提示，例如导航到子画布时为“运行当前子流程”。</param>
    public WorkflowStudioRunCommands(WorkflowStudioRuntimeBinding binding, Func<string>? runToolTip = null)
    {
        _binding = binding ?? throw new ArgumentNullException(nameof(binding));
        _runToolTip = runToolTip ?? (() => "运行");
        _binding.StateChanged += OnStateChanged;
    }

    /// <inheritdoc />
    public E_WorkflowExecutionState State => _binding.State;

    /// <inheritdoc />
    public bool CanRun => State is not (E_WorkflowExecutionState.Running or E_WorkflowExecutionState.Paused);

    /// <inheritdoc />
    public string RunText => _runToolTip();

    /// <inheritdoc />
    public string RunToolTip => _runToolTip();

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <inheritdoc />
    public Task<WorkflowRunResult> RunAsync() => _binding.RunAsync();

    /// <inheritdoc />
    public void Pause() => _binding.Pause();

    /// <inheritdoc />
    public void Resume() => _binding.Resume();

    /// <inheritdoc />
    public void Cancel() => _binding.Cancel();

    /// <summary>通知界面重新读取提示和可用性，例如导航层级变化后。</summary>
    public void Refresh() => Changed?.Invoke(this, EventArgs.Empty);

    /// <inheritdoc />
    public void Dispose() => _binding.StateChanged -= OnStateChanged;

    private void OnStateChanged(object? sender, EventArgs e) => Changed?.Invoke(this, EventArgs.Empty);
}
