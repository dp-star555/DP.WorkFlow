namespace DP.WorkFlow;

/// <summary>发起正常或虚拟创建型产品流转会话。</summary>
[WorkflowNode("ProductMoveStart", DisplayName = "开始流转节点", Category = "8.Process/ProductFlow")]
public sealed class ProductMoveStartNodeModel : WorkflowNodeModel
{
    /// <inheritdoc />
    public override string NodeType => "ProductMoveStart";
    /// <summary>流转模式。</summary>
    public E_ProductMoveStartMode MoveMode { get; set; }
    /// <summary>来源工站来源。</summary>
    public E_ProductFlowValueSource FromStationIdSource { get; set; }
    /// <summary>固定来源工站。</summary>
    public string FromStationId { get; set; } = string.Empty;
    /// <summary>绑定来源工站。</summary>
    public WorkflowInput<string> FromStationIdBinding { get; set; } = WorkflowInput<string>.FromLiteral(string.Empty);
    /// <summary>来源槽位来源。</summary>
    public E_ProductFlowValueSource FromSlotIdSource { get; set; }
    /// <summary>固定来源槽位。</summary>
    public string FromSlotId { get; set; } = string.Empty;
    /// <summary>绑定来源槽位。</summary>
    public WorkflowInput<string> FromSlotIdBinding { get; set; } = WorkflowInput<string>.FromLiteral(string.Empty);
    /// <summary>目标工站来源。</summary>
    public E_ProductFlowValueSource ToStationIdSource { get; set; }
    /// <summary>固定目标工站。</summary>
    public string ToStationId { get; set; } = string.Empty;
    /// <summary>绑定目标工站。</summary>
    public WorkflowInput<string> ToStationIdBinding { get; set; } = WorkflowInput<string>.FromLiteral(string.Empty);
    /// <summary>目标槽位来源。</summary>
    public E_ProductFlowValueSource ToSlotIdSource { get; set; }
    /// <summary>固定目标槽位。</summary>
    public string ToSlotId { get; set; } = string.Empty;
    /// <summary>绑定目标槽位。</summary>
    public WorkflowInput<string> ToSlotIdBinding { get; set; } = WorkflowInput<string>.FromLiteral(string.Empty);
    /// <summary>原因来源。</summary>
    public E_ProductFlowValueSource ReasonSource { get; set; }
    /// <summary>固定原因。</summary>
    public string Reason { get; set; } = "WorkflowStartMoving";
    /// <summary>绑定原因。</summary>
    public WorkflowInput<string> ReasonBinding { get; set; } = WorkflowInput<string>.FromLiteral(string.Empty);
    /// <summary>是否自动生成虚拟产品 ID。</summary>
    public bool AutoGenerateProductId { get; set; } = true;
    /// <summary>产品 ID 来源。</summary>
    public E_ProductFlowValueSource ProductIdSource { get; set; }
    /// <summary>固定产品 ID。</summary>
    public string ProductId { get; set; } = string.Empty;
    /// <summary>绑定产品 ID。</summary>
    public WorkflowInput<string> ProductIdBinding { get; set; } = WorkflowInput<string>.FromLiteral(string.Empty);
    /// <summary>配方来源。</summary>
    public E_ProductFlowValueSource RecipeSource { get; set; }
    /// <summary>固定配方。</summary>
    public string Recipe { get; set; } = string.Empty;
    /// <summary>绑定配方。</summary>
    public WorkflowInput<string> RecipeBinding { get; set; } = WorkflowInput<string>.FromLiteral(string.Empty);
    /// <summary>结果变量键。</summary>
    public string ResultVarKey { get; set; } = "ProductMoveStartResult";
}

/// <summary>执行开始产品流转节点。</summary>
public sealed class ProductMoveStartNodeHandler : WorkflowNodeHandler<ProductMoveStartNodeModel>
{
    /// <inheritdoc />
    protected override async ValueTask<NodeExecutionResult> ExecuteAsync(ProductMoveStartNodeModel node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
    {
        var toStation = ProductFlowNodeRuntime.ResolveRequired(node.ToStationIdSource, node.ToStationId, node.ToStationIdBinding, context, "ToStationId");
        var fromStation = node.MoveMode == E_ProductMoveStartMode.Normal ? ProductFlowNodeRuntime.ResolveRequired(node.FromStationIdSource, node.FromStationId, node.FromStationIdBinding, context, "FromStationId") : null;
        var productId = ProductFlowNodeRuntime.ResolveOptional(node.ProductIdSource, node.ProductId, node.ProductIdBinding, context);
        if (node.MoveMode == E_ProductMoveStartMode.VirtualCreate && productId is null && !node.AutoGenerateProductId) throw new InvalidOperationException("ProductId 为空，且未启用自动生成。");
        var request = new ProductMoveStartRequest(node.MoveMode, fromStation, ProductFlowNodeRuntime.ResolveOptional(node.FromSlotIdSource, node.FromSlotId, node.FromSlotIdBinding, context), toStation, ProductFlowNodeRuntime.ResolveOptional(node.ToSlotIdSource, node.ToSlotId, node.ToSlotIdBinding, context), ProductFlowNodeRuntime.ResolveOptional(node.ReasonSource, node.Reason, node.ReasonBinding, context), productId, node.AutoGenerateProductId, ProductFlowNodeRuntime.ResolveOptional(node.RecipeSource, node.Recipe, node.RecipeBinding, context) ?? string.Empty);
        var output = await ProductFlowNodeRuntime.GetService(context).StartMoveAsync(request, cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(node.ResultVarKey)) context.SetVariable(node.ResultVarKey.Trim(), output);
        return NodeExecutionResult.Continue(output: output);
    }
}
