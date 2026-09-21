namespace DP.WorkFlow.UI;

/// <summary>表示子画布导航路径中的一个面包屑。</summary>
/// <param name="Depth">导航层级深度。</param>
/// <param name="Title">界面标题。</param>
/// <param name="ParentNodeId">父级复合节点标识；根层级为空。</param>
public sealed record WorkflowDesignerBreadcrumb(int Depth, string Title, string? ParentNodeId);

/// <summary>管理根画布和 Block 子画布的设计会话栈。</summary>
public sealed class WorkflowDesignerNavigator
{
    private readonly WorkflowNodeCatalog _catalog;
    private readonly List<NavigationFrame> _frames = new();

    /// <summary>初始化正式根文档的导航器并创建根设计会话。</summary>
    public WorkflowDesignerNavigator(WorkflowDocument rootDocument, WorkflowNodeCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(rootDocument);
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        if (string.IsNullOrWhiteSpace(rootDocument.EntryNodeId))
            throw new ArgumentException("根文档入口不能为空。", nameof(rootDocument));
        _frames.Add(new NavigationFrame(
            new WorkflowDesignerSession(rootDocument, catalog),
            rootDocument.EntryNodeId,
            string.IsNullOrWhiteSpace(rootDocument.Name) ? "Root" : rootDocument.Name,
            null));
    }

    public WorkflowDesignerSession RootSession => _frames[0].Session;

    /// <summary>获取根工作流文档。</summary>
    public WorkflowDocument RootDocument => RootSession.Document;

    public string RootEntryNodeId => _frames[0].EntryNodeId;

    public WorkflowDesignerSession CurrentSession => _frames[^1].Session;

    public string CurrentEntryNodeId => _frames[^1].EntryNodeId;

    public int Depth => _frames.Count - 1;

    public IReadOnlyList<WorkflowDesignerBreadcrumb> Breadcrumbs => _frames
        .Select((frame, index) => new WorkflowDesignerBreadcrumb(index, frame.Title, frame.ParentNodeId))
        .ToArray();

    /// <summary>在当前导航画布或层级变化时发生。</summary>
    public event EventHandler? CurrentChanged;

    /// <summary>进入指定复合节点的子画布。</summary>
    /// <param name="nodeId">节点标识。</param>
    /// <returns>返回操作结果；具体含义参见方法说明。</returns>
    public bool EnterSubCanvas(string nodeId)
    {
        var parentSession = CurrentSession;
        var item = parentSession.Canvas.Nodes.FirstOrDefault(node => node.Node.Id == nodeId);
        if (item?.Node is not IWorkflowSubDocumentNode composite)
            return false;
        var startNodeId = ResolveEntryNodeId(composite);
        if (string.IsNullOrWhiteSpace(startNodeId))
            return false;
        var title = string.IsNullOrWhiteSpace(item.Node.Title) ? item.Node.Id : item.Node.Title;
        _frames.Add(new NavigationFrame(
            new WorkflowDesignerSession(composite.SubDocument, _catalog, RootSession.PublicDataCatalog),
            startNodeId,
            title,
            item.Node.Id));
        SetRuntimeSnapshot(_frames[0].Session.RuntimeSnapshot);
        CurrentChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>返回上一层子画布。</summary>
    /// <returns>返回操作结果；具体含义参见方法说明。</returns>
    public bool NavigateUp() => NavigateToDepth(Depth - 1);

    /// <summary>导航到指定面包屑深度。</summary>
    /// <param name="depth">目标导航深度。</param>
    /// <returns>返回操作结果；具体含义参见方法说明。</returns>
    public bool NavigateToDepth(int depth)
    {
        if (depth < 0 || depth >= _frames.Count || depth == Depth)
            return false;
        _frames.RemoveRange(depth + 1, _frames.Count - depth - 1);
        CurrentChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>将根运行快照映射到当前打开的嵌套子画布。</summary>
    /// <param name="rootSnapshot">根工作流运行时快照。</param>
    public void SetRuntimeSnapshot(WorkflowRuntimeSnapshot? rootSnapshot)
    {
        WorkflowRuntimeSnapshot? current = rootSnapshot;
        _frames[0].Session.SetRuntimeSnapshot(current);
        for (var index = 1; index < _frames.Count; index++)
        {
            var parentNodeId = _frames[index].ParentNodeId;
            current = current?.EnumerateChildWorkflows()
                .Where(info => info.ParentNodeId == parentNodeId)
                .OrderByDescending(info => info.Snapshot.Sequence)
                .Select(info => info.Snapshot)
                .FirstOrDefault();
            _frames[index].Session.SetRuntimeSnapshot(current);
        }
    }

    /// <summary>只读解析子文档入口；导航本身不得修复或修改文档。</summary>
    /// <param name="composite">复合节点。</param>
    /// <returns>返回操作结果；具体含义参见方法说明。</returns>
    private static string? ResolveEntryNodeId(IWorkflowSubDocumentNode composite)
    {
        if (!string.IsNullOrWhiteSpace(composite.SubDocument.EntryNodeId))
            return composite.SubDocument.EntryNodeId;
        var starts = composite.SubDocument.Graph.Nodes
            .Where(node => node.NodeType == "Start")
            .Select(node => node.Id)
            .ToArray();
        return starts.Length == 1 ? starts[0] : null;
    }

    /// <summary>保存导航层级对应的会话、复合节点和运行快照信息。</summary>
    /// <param name="Session">关联的设计器会话。</param>
    /// <param name="EntryNodeId">工作流开始节点标识。</param>
    /// <param name="Title">界面标题。</param>
    /// <param name="ParentNodeId">父级复合节点标识；根层级为空。</param>
    private sealed record NavigationFrame(
        WorkflowDesignerSession Session,
        string EntryNodeId,
        string Title,
        string? ParentNodeId);
}
