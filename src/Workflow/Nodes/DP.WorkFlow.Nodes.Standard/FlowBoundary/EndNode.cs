namespace DP.WorkFlow;

/// <summary>
/// 表示工作流当前路径的标准结束节点。
/// </summary>
[WorkflowNode("End", DisplayName = "结束节点", Category = "1.Base/流程边界")]
public sealed class EndNodeModel : WorkflowNodeModel
{
    /// <inheritdoc />
    public override string NodeType => "End";
}

/// <summary>执行标准结束节点。</summary>
public sealed class EndNodeHandler : WorkflowNodeHandler<EndNodeModel>
{
    /// <inheritdoc />
    protected override ValueTask<NodeExecutionResult> ExecuteAsync(
        EndNodeModel node,
        IWorkflowNodeExecutionContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        context.Trace("End", "当前执行路径结束。");
        return ValueTask.FromResult(NodeExecutionResult.Complete());
    }
}
