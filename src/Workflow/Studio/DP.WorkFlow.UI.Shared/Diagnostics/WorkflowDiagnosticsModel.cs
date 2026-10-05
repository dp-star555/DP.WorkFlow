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
    string? NodeId)
{
    /// <summary>从根或当前文档出发的 URI 编码子流程路径。</summary>
    public string PlanPath { get; init; } = "$";
    /// <summary>是否从导航器根文档定位。</summary>
    public bool FromRoot { get; init; }
    /// <summary>历史运行失败不锁住重试按钮。</summary>
    public bool BlocksRun { get; init; } = true;
    /// <summary>完整技术原因。</summary>
    public string? Detail { get; init; }
    /// <summary>发现、检查或准备阶段。</summary>
    public string? Phase { get; init; }
}

/// <summary>汇总结构、并行和绑定编译诊断，并支持导航到问题节点。</summary>
public sealed class WorkflowDiagnosticsModel : IDisposable
{
    private readonly WorkflowDesignerSession _session;
    private IReadOnlyList<WorkflowDiagnosticItem> _items = Array.Empty<WorkflowDiagnosticItem>();
    private bool _refreshing;
    private readonly IWorkflowDiagnosticProvider? _provider;
    private readonly WorkflowDesignerNavigator? _navigator;
    private readonly int _navigationDepth;

    /// <summary>初始化诊断模型并订阅设计会话的文档变更。</summary>
    /// <param name="session">设计器会话。</param>
    /// <param name="startNodeId">工作流开始节点标识。</param>
    public WorkflowDiagnosticsModel(WorkflowDesignerSession session, string startNodeId) : this(session, startNodeId, null, null) { }

    /// <summary>组合领域检查并支持子流程定位；保持原构造入口兼容。</summary>
    public WorkflowDiagnosticsModel(WorkflowDesignerSession session, string startNodeId, IWorkflowDiagnosticProvider? provider, WorkflowDesignerNavigator? navigator)
    {
        _provider = provider; _navigator = navigator; _navigationDepth = navigator?.Depth ?? 0;
        _session = session ?? throw new ArgumentNullException(nameof(session));
        EntryNodeId = startNodeId ?? throw new ArgumentNullException(nameof(startNodeId));
        _session.Changed += OnSessionChanged;
        Refresh();
    }

    /// <summary>获取或设置当前工作流的开始节点标识。</summary>
    public string EntryNodeId { get; set; }

    public IReadOnlyList<WorkflowDiagnosticItem> Items => _items;

    /// <summary>获取当前诊断是否不包含阻止运行的错误。</summary>
    public bool CanRun => _items.All(item => item.Severity != WorkflowValidationSeverity.Error || !item.BlocksRun);

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
            var external = Array.Empty<WorkflowDiagnosticItem>();
            try { external = _provider?.Analyze(_session.Document).ToArray() ?? external; }
            catch (Exception error) { external = new[] { new WorkflowDiagnosticItem("UI_DIAGNOSTICS_FAILED", WorkflowValidationSeverity.Error, error.Message, null) { Detail = error.ToString() } }; }
            _items = diagnostics
                .Select(item => new WorkflowDiagnosticItem(item.Code, item.Severity, item.Message, item.NodeId))
                .Concat(external)
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

    /// <summary>选择诊断关联节点；节点位于子流程内部时选择包含它的最外层子流程节点。</summary>
    /// <param name="item">目标数据项。</param>
    public bool NavigateTo(WorkflowDiagnosticItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (_navigator != null && (item.FromRoot || item.PlanPath != "$"))
        {
            var depth = item.FromRoot ? 0 : _navigationDepth;
            var document = item.FromRoot ? _navigator.RootDocument : _session.Document;
            var parents = item.PlanPath.Split('/').Skip(1).Select(Uri.UnescapeDataString).ToArray();
            foreach (var parent in parents)
            {
                var child = document.Graph.Nodes.FirstOrDefault(n => n.Id == parent) as IWorkflowSubDocumentNode;
                if (child == null) return false;
                document = child.SubDocument;
            }
            if (!document.Graph.Nodes.Any(n => n.Id == item.NodeId)) return false;
            if (_navigator.Depth != depth) _navigator.NavigateToDepth(depth);
            // 子画布以节点窗口弹出编辑，主画布不再进入子画布：嵌套诊断定位到所在画布上的最外层子流程节点。
            _navigator.CurrentSession.SelectedNodeId = parents.Length > 0 ? parents[0] : item.NodeId; return true;
        }
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
        {
            _refreshing = true;
            try { _provider?.Invalidate(); }
            finally { _refreshing = false; }
            Refresh();
        }
    }
}
