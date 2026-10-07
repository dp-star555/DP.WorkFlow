namespace DP.WorkFlow.UI;

/// <summary>
/// 节点窗口中其它页面的模型可实现此接口，为同一窗口的“参数”页追加属性（例如“导入配方”“单字库”等操作按钮），
/// 让专用页面只保留图像等主体内容。条目在参数页每次重建时重新读取，可随页面状态出现或消失。
/// </summary>
public interface IWorkflowNodeEditorPropertyContributor
{
    /// <summary>为参数页追加的条目；当前不可用时返回空集合。</summary>
    /// <param name="editingNode">节点窗口的编辑副本。</param>
    IEnumerable<WorkflowPropertyEntry> CreateProperties(IWorkflowNodeModel editingNode);
}

/// <summary>参数页附加属性的组合。</summary>
public static class WorkflowNodeEditorPropertyContributors
{
    /// <summary>宿主附加属性在前，页面模型贡献的条目在后。</summary>
    /// <param name="host">宿主提供的附加属性；可为空。</param>
    /// <param name="pages">同一节点窗口的页面。</param>
    public static Func<IWorkflowNodeModel, IReadOnlyList<WorkflowPropertyEntry>>? Compose(
        Func<IWorkflowNodeModel, IReadOnlyList<WorkflowPropertyEntry>>? host, IEnumerable<WorkflowNodeEditorPageDescriptor> pages)
    {
        var contributors = pages.Select(page => page.Model).OfType<IWorkflowNodeEditorPropertyContributor>().ToArray();
        if (contributors.Length == 0) return host;
        return node => (host?.Invoke(node) ?? Array.Empty<WorkflowPropertyEntry>())
            .Concat(contributors.SelectMany(contributor => contributor.CreateProperties(node))).ToArray();
    }
}
