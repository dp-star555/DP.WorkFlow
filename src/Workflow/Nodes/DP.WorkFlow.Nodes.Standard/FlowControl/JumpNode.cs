namespace DP.WorkFlow;

/// <summary>
/// 表示显式跳转节点。新版跳转目标由 Success 端口连接表达，不再在节点配置中保存目标 ID。
/// </summary>
[WorkflowNode("Jump", DisplayName = "跳转节点", Category = "3.Control/流程控制")]
public sealed class JumpNodeModel : WorkflowNodeModel
{
    /// <inheritdoc />
    public override string NodeType => "Jump";
}

/// <summary>执行显式跳转节点。</summary>
public sealed class JumpNodeHandler : WorkflowNodeHandler<JumpNodeModel>
{
    /// <inheritdoc />
    protected override ValueTask<NodeExecutionResult> ExecuteAsync(
        JumpNodeModel node,
        IWorkflowNodeExecutionContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        context.Trace("Jump", "通过 Success 端口跳转到连接目标。");
        return ValueTask.FromResult(NodeExecutionResult.Continue());
    }
}
