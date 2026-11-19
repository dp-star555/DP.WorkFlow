namespace DP.WorkFlow;

/// <summary>
/// 带子流程接口的复合节点：输入从父画布传入子画布（子画布中读取“启动节点.名称”），
/// 输出从子画布传回父画布（父画布中读取“复合节点.名称”）。
/// </summary>
public interface IWorkflowSubflowNode : IWorkflowSubDocumentNode
{
    /// <summary>获取或设置子流程输入。</summary>
    IList<WorkflowSubflowInput> Inputs { get; set; }

    /// <summary>获取或设置子流程输出。</summary>
    IList<WorkflowSubflowOutput> Outputs { get; set; }

    /// <summary>
    /// 输出成员及其类型（类型跟随子画布中的来源），由 <see cref="WorkflowSubflowInterfaces.Refresh(WorkflowDocument, WorkflowNodeCatalog)"/> 注入，不保存。
    /// </summary>
    IReadOnlyList<WorkflowBindableMember> SubflowOutputs { get; set; }
}

/// <summary>
/// 每圈运行一次子画布的复合节点（循环块）：子画布启动节点除子流程输入外还带"当前圈数"
/// （<see cref="WorkflowSubflowInterfaces.CurrentIterationName"/>，Int32，从 1 开始），可被绑定。
/// </summary>
public interface IWorkflowLoopSubflowNode : IWorkflowSubflowNode
{
}
