namespace DP.WorkFlow;

/// <summary>
/// 表示工作流的标准启动节点。
/// </summary>
[WorkflowNode("Start", DisplayName = "启动节点", Category = "1.Base/流程边界")]
public sealed class StartNodeModel : WorkflowNodeModel
{
    /// <inheritdoc />
    public override string NodeType => "Start";
}

/// <summary>执行标准启动节点。</summary>
public sealed class StartNodeHandler : WorkflowNodeHandler<StartNodeModel>
{
    /// <inheritdoc />
    protected override ValueTask<NodeExecutionResult> ExecuteAsync(
        StartNodeModel node,
        IWorkflowNodeExecutionContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        context.Trace("Start", "流程由启动节点进入。");
        return ValueTask.FromResult(NodeExecutionResult.Continue());
    }
}
