namespace DP.WorkFlow;

/// <summary>由包含正式子工作流文档的复合节点实现。</summary>
public interface IWorkflowSubDocumentNode : IWorkflowNodeModel
{
    /// <summary>获取或设置具有独立入口、语义图和布局的子文档。</summary>
    WorkflowDocument SubDocument { get; set; }
}
