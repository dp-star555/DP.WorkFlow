namespace DP.WorkFlow.UI;

public sealed partial class WorkflowDesignerSession
{
    /// <summary>
    /// 运行宿主的提示板（R1-01），由运行绑定设置；未接入运行时为空。
    /// 提示跨运行保留，节点右上角显示角标，使用者确认后清除。
    /// </summary>
    public WorkflowNoticeBoard? Notices { get; set; }

    /// <summary>本画布在提示板中的路径：根画布为 <c>$</c>，子画布为 <c>$/复合节点</c>（节点标识按 URI 转义）。</summary>
    public string NoticePlanPath => WorkflowNoticeLocation.PlanPath(RuntimePath);

    /// <summary>提示变化计数；界面比较它决定是否重画角标。未接入提示板时为 -1。</summary>
    public long NoticeVersion => Notices?.Version ?? -1;

    /// <summary>读取节点的提示，按出现先后排列。</summary>
    /// <param name="nodeId">节点标识。</param>
    public IReadOnlyList<WorkflowNotice> GetNodeNotices(string nodeId) =>
        Notices?.Get(NoticePlanPath, nodeId) ?? Array.Empty<WorkflowNotice>();

    /// <summary>读取本画布的画布级提示（无法归属到节点的，例如记录丢失）。</summary>
    public IReadOnlyList<WorkflowNotice> GetCanvasNotices() =>
        Notices?.Get(NoticePlanPath, null) ?? Array.Empty<WorkflowNotice>();

    /// <summary>确认并清除节点的提示。</summary>
    /// <param name="nodeId">节点标识。</param>
    /// <returns>清除的条数。</returns>
    public int AcknowledgeNodeNotices(string nodeId)
    {
        var count = Notices?.Acknowledge(NoticePlanPath, nodeId) ?? 0;
        if (count > 0) RaiseChanged(WorkflowDesignerChangeKind.Runtime);
        return count;
    }

    /// <summary>确认并清除本画布的画布级提示。</summary>
    /// <returns>清除的条数。</returns>
    public int AcknowledgeCanvasNotices()
    {
        var count = Notices?.Acknowledge(NoticePlanPath, null) ?? 0;
        if (count > 0) RaiseChanged(WorkflowDesignerChangeKind.Runtime);
        return count;
    }
}

/// <summary>提示的位置：画布路径与显示文字。</summary>
public static class WorkflowNoticeLocation
{
    /// <summary>由运行中的复合节点路径得到提示板使用的画布路径（与引擎的 PlanPath 同一写法）。</summary>
    /// <param name="runtimePath">从根画布起经过的复合节点标识。</param>
    public static string PlanPath(IEnumerable<string> runtimePath)
    {
        Guard.NotNull(runtimePath);
        return "$" + string.Concat(runtimePath.Select(id => "/" + Uri.EscapeDataString(id)));
    }

    /// <summary>把画布路径拆回复合节点标识（根画布为空）。</summary>
    /// <param name="planPath">画布路径。</param>
    public static IReadOnlyList<string> Split(string planPath)
    {
        if (string.IsNullOrEmpty(planPath) || planPath == "$") return Array.Empty<string>();
        var text = planPath.StartsWith("$/", StringComparison.Ordinal) ? planPath.Substring(2) : planPath.TrimStart('$', '/');
        return text.Split('/').Select(Uri.UnescapeDataString).ToArray();
    }

    /// <summary>位置的显示文字：根画布为“主流程”，子画布用“ / ”连接复合节点标识，再接节点或“画布”。</summary>
    /// <param name="notice">提示。</param>
    public static string Describe(WorkflowNotice notice)
    {
        Guard.NotNull(notice);
        var parts = Split(notice.PlanPath);
        var canvas = parts.Count == 0 ? "主流程" : string.Join(" / ", parts);
        return notice.NodeId is null ? canvas + " · 画布" : canvas + " · " + notice.NodeId;
    }
}
