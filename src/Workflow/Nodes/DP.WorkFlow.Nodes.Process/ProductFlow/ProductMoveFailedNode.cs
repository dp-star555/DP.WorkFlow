namespace DP.WorkFlow;

/// <summary>提交流转失败并回滚目标工站预接料状态。</summary>
[WorkflowNode("ProductMoveFailed", DisplayName = "流转失败节点", Category = "8.Process/ProductFlow")]
public sealed class ProductMoveFailedNodeModel : WorkflowNodeModel
{
    /// <inheritdoc />
    public override string NodeType => "ProductMoveFailed";
    /// <summary>会话 ID 来源。</summary>
    public E_ProductFlowValueSource SessionIdSource { get; set; } = E_ProductFlowValueSource.Binding;
    /// <summary>固定会话 ID。</summary>
    public string SessionId { get; set; } = string.Empty;
    /// <summary>绑定会话 ID。</summary>
    public WorkflowInput<string> SessionIdBinding { get; set; } = WorkflowInput<string>.FromLiteral(string.Empty);
    /// <summary>失败原因。</summary>
    public string FailReason { get; set; } = "WorkflowMovingFailed";
    /// <summary>结果变量键。</summary>
    public string ResultVarKey { get; set; } = "ProductMoveFailedResult";
}

/// <summary>执行流转失败节点。</summary>
public sealed class ProductMoveFailedNodeHandler : WorkflowNodeHandler<ProductMoveFailedNodeModel>
{
    /// <inheritdoc />
    protected override async ValueTask<NodeExecutionResult> ExecuteAsync(ProductMoveFailedNodeModel node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
    {
        var sessionId = ProductFlowNodeRuntime.ResolveRequired(node.SessionIdSource, node.SessionId, node.SessionIdBinding, context, "SessionId");
        var reason = string.IsNullOrWhiteSpace(node.FailReason) ? null : node.FailReason.Trim();
        var output = await ProductFlowNodeRuntime.GetService(context).FailMoveAsync(sessionId, reason, cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(node.ResultVarKey)) context.SetVariable(node.ResultVarKey.Trim(), output);
        return NodeExecutionResult.Continue(output: output);
    }
}
