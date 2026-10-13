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

/// <summary>
/// 汇总结构、并行和绑定编译诊断，并支持导航到问题节点。
/// 子画布内部的问题按复合节点各汇总成一条，定位到该复合节点；详情在节点窗口子画布页的诊断中查看。
/// </summary>
public sealed class WorkflowDiagnosticsModel : IDisposable
{
    /// <summary>子画布问题汇总项的诊断代码。</summary>
    public const string SubDocumentSummaryCode = "WFSUB";

    private readonly WorkflowDesignerSession _session;
    private readonly bool _followsDocumentEntry;
    private IReadOnlyList<WorkflowDiagnosticItem> _items = Array.Empty<WorkflowDiagnosticItem>();
    private bool _refreshing;
    private readonly IWorkflowDiagnosticProvider? _provider;
    private readonly WorkflowDesignerNavigator? _navigator;
    private readonly int _navigationDepth;

    /// <summary>初始化诊断模型并订阅设计会话的文档变更。</summary>
    /// <param name="session">设计器会话。</param>
    /// <param name="startNodeId">工作流开始节点标识。</param>
    public WorkflowDiagnosticsModel(WorkflowDesignerSession session, string startNodeId) : this(session, startNodeId, null, null) { }

    /// <summary>
    /// 创建检查会话当前文档的诊断模型，入口始终跟随文档入口；用于节点窗口中正在编辑的子画布，
    /// 子画布刚创建时可能还没有启动节点，放入后自动按新入口检查。
    /// </summary>
    /// <param name="session">子画布的设计会话。</param>
    public WorkflowDiagnosticsModel(WorkflowDesignerSession session)
        : this(session, session?.Document.EntryNodeId ?? throw new ArgumentNullException(nameof(session)), null, null, followsDocumentEntry: true) { }

    /// <summary>组合领域检查并支持子流程定位；保持原构造入口兼容。</summary>
    public WorkflowDiagnosticsModel(WorkflowDesignerSession session, string startNodeId, IWorkflowDiagnosticProvider? provider, WorkflowDesignerNavigator? navigator)
        : this(session, startNodeId, provider, navigator, followsDocumentEntry: false) { }

    private WorkflowDiagnosticsModel(WorkflowDesignerSession session, string startNodeId, IWorkflowDiagnosticProvider? provider,
        WorkflowDesignerNavigator? navigator, bool followsDocumentEntry)
    {
        _provider = provider; _navigator = navigator; _navigationDepth = navigator?.Depth ?? 0;
        _followsDocumentEntry = followsDocumentEntry;
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
            if (_followsDocumentEntry)
                EntryNodeId = _session.Document.EntryNodeId;
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
            _items = SummarizeSubDocuments(diagnostics
                    .Select(item => new WorkflowDiagnosticItem(item.Code, item.Severity, item.Message, item.NodeId) { PlanPath = item.PlanPath })
                    .Concat(external))
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

    /// <summary>
    /// 当前画布内的问题原样保留；位于子画布内的问题按所在的最外层复合节点各汇总成一条，
    /// 定位到当前画布上的该复合节点。
    /// </summary>
    private IEnumerable<WorkflowDiagnosticItem> SummarizeSubDocuments(IEnumerable<WorkflowDiagnosticItem> items)
    {
        var nested = new Dictionary<string, List<WorkflowDiagnosticItem>>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            var compositeId = item.FromRoot && _navigationDepth != 0 ? null : FirstSegment(item.PlanPath);
            if (compositeId is null)
            {
                yield return item;
                continue;
            }
            if (!nested.TryGetValue(compositeId, out var list))
                nested[compositeId] = list = new List<WorkflowDiagnosticItem>();
            list.Add(item);
        }
        foreach (var (compositeId, list) in nested)
        {
            var errors = list.Count(item => item.Severity == WorkflowValidationSeverity.Error);
            var warnings = list.Count - errors;
            var title = _session.Document.Graph.Nodes.FirstOrDefault(node => node.Id == compositeId)?.Title;
            var name = string.IsNullOrWhiteSpace(title) ? compositeId : title;
            var counts = string.Join("、", new[]
            {
                errors > 0 ? $"{errors} 个错误" : null,
                warnings > 0 ? $"{warnings} 个警告" : null
            }.Where(text => text is not null));
            yield return new WorkflowDiagnosticItem(
                SubDocumentSummaryCode,
                errors > 0 ? WorkflowValidationSeverity.Error : WorkflowValidationSeverity.Warning,
                $"{name} 的子画布有 {counts}，请打开该节点在子画布诊断中查看。",
                compositeId)
            {
                BlocksRun = list.Any(item => item.Severity == WorkflowValidationSeverity.Error && item.BlocksRun),
                Detail = string.Join(Environment.NewLine, list.Select(item =>
                    $"{(item.Severity == WorkflowValidationSeverity.Error ? "错误" : "警告")} {item.NodeId}：{item.Message}"))
            };
        }
    }

    /// <summary>子画布路径的第一段复合节点 ID；当前画布（<c>$</c>）返回空。</summary>
    private static string? FirstSegment(string planPath)
    {
        var segments = planPath.Split('/');
        return segments.Length > 1 && segments[1].Length > 0 ? Uri.UnescapeDataString(segments[1]) : null;
    }

    /// <summary>选择诊断关联节点；节点位于子流程内部时选择包含它的最外层子流程节点。</summary>
    /// <param name="item">目标数据项。</param>
    public bool NavigateTo(WorkflowDiagnosticItem item)
    {
        Guard.NotNull(item);
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
