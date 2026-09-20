namespace DP.WorkFlow;

/// <summary>提交流转成功，完成产品出站与入站。</summary>
[WorkflowNode("ProductMoveSuccess", DisplayName = "流转成功节点", Category = "8.Process/ProductFlow")]
public sealed class ProductMoveSuccessNodeModel : WorkflowNodeModel
{
    /// <inheritdoc />
    public override string NodeType => "ProductMoveSuccess";
    /// <summary>会话 ID 来源。</summary>
    public E_ProductFlowValueSource SessionIdSource { get; set; } = E_ProductFlowValueSource.Binding;
    /// <summary>固定会话 ID。</summary>
    public string SessionId { get; set; } = string.Empty;
    /// <summary>绑定会话 ID。</summary>
    public WorkflowInput<string> SessionIdBinding { get; set; } = WorkflowInput<string>.FromLiteral(string.Empty);
    /// <summary>结果变量键。</summary>
    public string ResultVarKey { get; set; } = "ProductMoveSuccessResult";
}

/// <summary>执行流转成功节点。</summary>
public sealed class ProductMoveSuccessNodeHandler : WorkflowNodeHandler<ProductMoveSuccessNodeModel>
{
    /// <inheritdoc />
    protected override async ValueTask<NodeExecutionResult> ExecuteAsync(ProductMoveSuccessNodeModel node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
    {
        var sessionId = ProductFlowNodeRuntime.ResolveRequired(node.SessionIdSource, node.SessionId, node.SessionIdBinding, context, "SessionId");
        var output = await ProductFlowNodeRuntime.GetService(context).CompleteMoveAsync(sessionId, cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(node.ResultVarKey)) context.SetVariable(node.ResultVarKey.Trim(), output);
        return NodeExecutionResult.Continue(output: output);
    }
}
