namespace DP.WorkFlow.UI;

/// <summary>
/// 设计会话可用的运行入口：读取合并轨迹、在最近一次运行中单独运行节点，以及控制当前运行。
/// 由运行绑定提供给根画布和当前画布；子画布从所在画布继承。
/// </summary>
public interface IWorkflowDesignerRuntime
{
    /// <summary>当前运行状态。</summary>
    E_WorkflowExecutionState State { get; }

    /// <summary>是否有可继续的最近一次运行（运行过且其结果仍保留）。</summary>
    bool HasRun { get; }

    /// <summary>运行状态或快照变化时发生。</summary>
    event EventHandler? StateChanged;

    /// <summary>读取包含子运行的合并轨迹；尚未运行时为空。</summary>
    /// <param name="since">上次读取的位置（<see cref="WorkflowTraceBatch.Cursor"/>）；为空时返回整个保留窗口，否则只返回此后的新条目。</param>
    WorkflowTraceBatch? GetTraceBatch(WorkflowTraceCursor? since = null);

    /// <summary>在最近一次运行中单独再执行一个节点，输入取自该次运行的结果。</summary>
    /// <param name="document">节点所在画布的文档（可含尚未确认的修改）。</param>
    /// <param name="nodeId">要执行的节点。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<WorkflowRunResult> RunNodeAsync(WorkflowDocument document, string nodeId, CancellationToken cancellationToken = default);

    /// <summary>暂停当前运行。</summary>
    void Pause();

    /// <summary>继续当前运行。</summary>
    void Resume();

    /// <summary>停止当前运行。</summary>
    void Cancel();
}

/// <summary>运行监视控件的数据来源：一个画布的运行快照和轨迹。</summary>
public interface IWorkflowRuntimeMonitorSource
{
    /// <summary>当前快照；尚未运行时可为空。</summary>
    WorkflowRuntimeSnapshot? GetSnapshot();

    /// <summary>当前轨迹；尚未运行时可为空。</summary>
    /// <param name="since">上次读取的位置（<see cref="WorkflowTraceBatch.Cursor"/>）；为空时返回整个保留窗口，否则只返回此后的新条目。</param>
    WorkflowTraceBatch? GetTraceBatch(WorkflowTraceCursor? since = null);

    /// <summary>快照或轨迹可能变化时发生，可能来自非界面线程。</summary>
    event EventHandler? Changed;
}

/// <summary>
/// 以设计会话为准的监视来源：快照取会话当前的运行快照（子画布为其子运行），
/// 轨迹取运行入口的合并轨迹中属于该画布（<see cref="WorkflowDesignerSession.RuntimePath"/>）的部分。
/// </summary>
public sealed class WorkflowSessionRuntimeMonitorSource : IWorkflowRuntimeMonitorSource, IDisposable
{
    private readonly WorkflowDesignerSession _session;
    private IWorkflowDesignerRuntime? _runtime;
    private bool _disposed;

    /// <summary>为会话创建监视来源。</summary>
    /// <param name="session">画布会话。</param>
    public WorkflowSessionRuntimeMonitorSource(WorkflowDesignerSession session)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _session.Changed += OnSessionChanged;
        AttachRuntime();
    }

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <inheritdoc />
    public WorkflowRuntimeSnapshot? GetSnapshot() => _session.RuntimeSnapshot;

    /// <inheritdoc />
    public WorkflowTraceBatch? GetTraceBatch(WorkflowTraceCursor? since = null) =>
        WorkflowTraceScope.ForPath(_session.Runtime?.GetTraceBatch(since), _session.RuntimePath);

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _session.Changed -= OnSessionChanged;
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

    // 运行入口由运行绑定或父画布在应用快照时设置，随之更新订阅。
    private void AttachRuntime()
    {
        if (_disposed || ReferenceEquals(_runtime, _session.Runtime)) return;
        if (_runtime is not null) _runtime.StateChanged -= OnRuntimeChanged;
        _runtime = _session.Runtime;
        if (_runtime is not null) _runtime.StateChanged += OnRuntimeChanged;
    }
}

/// <summary>按画布路径截取合并轨迹。</summary>
public static class WorkflowTraceScope
{
    /// <summary>
    /// 只保留属于路径所指子画布的条目，并去掉路径前缀；最后一层是循环块时保留圈序号，显示为“第3圈 › 找线1”。
    /// 路径为空时原样返回。
    /// </summary>
    /// <param name="batch">根运行的合并轨迹。</param>
    /// <param name="path">从根画布起经过的复合节点 ID。</param>
    public static WorkflowTraceBatch? ForPath(WorkflowTraceBatch? batch, IReadOnlyList<string> path)
    {
        Guard.NotNull(path);
        if (batch is null || path.Count == 0) return batch;
        var entries = new List<WorkflowTraceEntry>();
        foreach (var entry in batch.Entries)
        {
            if (entry.Parents is not { } parents || parents.Count < path.Count) continue;
            var matches = true;
            for (var index = 0; index < path.Count && matches; index++)
                matches = string.Equals(parents[index].NodeId, path[index], StringComparison.Ordinal);
            if (!matches) continue;
            var cycle = parents[path.Count - 1].Cycle;
            var remaining = parents.Skip(path.Count);
            if (cycle > 0) remaining = remaining.Prepend(new WorkflowTraceParent(string.Empty, cycle));
            var trimmed = remaining.ToArray();
            entries.Add(entry with { Parents = trimmed.Length == 0 ? null : Array.AsReadOnly(trimmed) });
        }
        return batch with { Entries = entries.AsReadOnly() };
    }
}
