namespace DP.WorkFlow.UI;

/// <summary>表示设计器诊断列表中的一项。</summary>
/// <param name="Code">诊断代码。</param>
/// <param name="Severity">诊断严重级别。</param>
/// <param name="Message">诊断或轨迹消息。</param>
/// <param name="NodeId">节点标识。</param>
public sealed record WorkflowDiagnosticItem(
    string Code,
    WorkflowValidationSeverity Severity,
    string Message,
    string? NodeId);

/// <summary>汇总结构、并行和绑定编译诊断，并支持导航到问题节点。</summary>
public sealed class WorkflowDiagnosticsModel : IDisposable
{
    private readonly WorkflowDesignerSession _session;
    private IReadOnlyList<WorkflowDiagnosticItem> _items = Array.Empty<WorkflowDiagnosticItem>();
    private bool _refreshing;

    /// <summary>初始化诊断模型并订阅设计会话的文档变更。</summary>
    /// <param name="session">设计器会话。</param>
    /// <param name="startNodeId">工作流开始节点标识。</param>
    public WorkflowDiagnosticsModel(WorkflowDesignerSession session, string startNodeId)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        EntryNodeId = startNodeId ?? throw new ArgumentNullException(nameof(startNodeId));
        _session.Changed += OnSessionChanged;
        Refresh();
    }

    /// <summary>获取或设置当前工作流的开始节点标识。</summary>
    public string EntryNodeId { get; set; }

    public IReadOnlyList<WorkflowDiagnosticItem> Items => _items;

    /// <summary>获取当前诊断是否不包含阻止运行的错误。</summary>
    public bool CanRun => _items.All(item => item.Severity != WorkflowValidationSeverity.Error);

    /// <summary>在模型内容发生变化、界面需要刷新时发生。</summary>
    public event EventHandler? Changed;

    /// <summary>重新编译当前文档并刷新诊断。</summary>
    public void Refresh()
    {
        if (_refreshing)
            return;
        _refreshing = true;
        try
        {
            IReadOnlyList<WorkflowValidationError> diagnostics;
            try
            {
                var compiler = new WorkflowCompiler(_session.Catalog);
                if (!string.Equals(_session.Document.EntryNodeId, EntryNodeId, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"诊断入口 {EntryNodeId} 与正式文档入口 {_session.Document.EntryNodeId} 不一致。");
                }
                diagnostics = compiler.Compile(_session.Document).Diagnostics;
            }
            catch (WorkflowCompilationException exception)
            {
                diagnostics = exception.Errors;
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
            {
                diagnostics = new[]
                {
                    new WorkflowValidationError("UI001", exception.Message)
                };
            }
            _items = diagnostics
                .Select(item => new WorkflowDiagnosticItem(item.Code, item.Severity, item.Message, item.NodeId))
                .OrderByDescending(item => item.Severity)
                .ThenBy(item => item.Code, StringComparer.Ordinal)
                .ThenBy(item => item.NodeId, StringComparer.Ordinal)
                .ToArray();
            Changed?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            _refreshing = false;
        }
    }

    /// <summary>选择诊断关联节点。</summary>
    /// <param name="item">目标数据项。</param>
    public bool NavigateTo(WorkflowDiagnosticItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (string.IsNullOrWhiteSpace(item.NodeId)
            || !_session.Canvas.Nodes.Any(node => node.Node.Id == item.NodeId))
        {
            return false;
        }
        _session.SelectedNodeId = item.NodeId;
        return true;
    }

    /// <summary>解除事件订阅并释放当前模型持有的资源。</summary>
    public void Dispose() => _session.Changed -= OnSessionChanged;

    /// <summary>处理设计会话变更，并同步刷新派生模型。</summary>
    private void OnSessionChanged(object? sender, WorkflowDesignerChangedEventArgs e)
    {
        if (e.Kind == WorkflowDesignerChangeKind.Document)
            Refresh();
    }
}
