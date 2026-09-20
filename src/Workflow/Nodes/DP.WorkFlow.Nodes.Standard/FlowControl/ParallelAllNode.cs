namespace DP.WorkFlow;

/// <summary>
/// 将 Branch 端口上的全部连接同时派发，并在配对的汇聚节点等待全部分支完成。
/// </summary>
[WorkflowNode("ParallelAll", DisplayName = "并行节点", Category = "3.Control/流程控制")]
public sealed class ParallelAllNodeModel : WorkflowNodeModel, IWorkflowParallelForkNode
{
    /// <inheritdoc />
    public override string NodeType => "ParallelAll";
}

/// <summary>执行并行派发节点。</summary>
public sealed class ParallelAllNodeHandler : WorkflowNodeHandler<ParallelAllNodeModel>
{
    /// <inheritdoc />
    protected override ValueTask<NodeExecutionResult> ExecuteAsync(
        ParallelAllNodeModel node,
        IWorkflowNodeExecutionContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        context.Trace("ParallelDispatch", "请求派发 Branch 端口上的全部并行分支。");
        return ValueTask.FromResult(NodeExecutionResult.Continue(WorkflowPorts.Branch));
    }
}

