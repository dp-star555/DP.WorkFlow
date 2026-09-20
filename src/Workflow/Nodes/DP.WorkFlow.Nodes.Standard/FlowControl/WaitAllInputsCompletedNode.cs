namespace DP.WorkFlow;

/// <summary>表示并行分支的显式汇聚点。该节点只会在全部分支到达后执行一次。</summary>
[WorkflowNode("WaitAllInputsCompleted", DisplayName = "并行汇聚节点", Category = "3.Control/流程控制")]
public sealed class WaitAllInputsCompletedNodeModel : WorkflowNodeModel, IWorkflowParallelJoinNode
{
    /// <inheritdoc />
    public override string NodeType => "WaitAllInputsCompleted";
}

/// <summary>执行并行汇聚节点。</summary>
public sealed class WaitAllInputsCompletedNodeHandler : WorkflowNodeHandler<WaitAllInputsCompletedNodeModel>
{
    /// <inheritdoc />
    protected override ValueTask<NodeExecutionResult> ExecuteAsync(
        WaitAllInputsCompletedNodeModel node,
        IWorkflowNodeExecutionContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        context.Trace("ParallelMerged", "全部并行分支已经到达汇聚点。");
        return ValueTask.FromResult(NodeExecutionResult.Continue());
    }
}
