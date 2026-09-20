namespace DP.WorkFlow;

/// <summary>按当前批次创建产品并放入指定工站。</summary>
[WorkflowNode("ProductCreate", DisplayName = "产品创建节点", Category = "8.Process/ProductFlow")]
public sealed class ProductCreateNodeModel : WorkflowNodeModel
{
    /// <inheritdoc />
    public override string NodeType => "ProductCreate";
    /// <summary>获取或设置工站来源。</summary>
    public E_ProductFlowValueSource StationIdSource { get; set; }
    /// <summary>获取或设置固定工站 ID。</summary>
    public string StationId { get; set; } = string.Empty;
    /// <summary>获取或设置绑定工站 ID。</summary>
    public WorkflowInput<string> StationIdBinding { get; set; } = WorkflowInput<string>.FromLiteral(string.Empty);
    /// <summary>获取或设置槽位来源。</summary>
    public E_ProductFlowValueSource SlotIdSource { get; set; }
    /// <summary>获取或设置固定槽位 ID。</summary>
    public string SlotId { get; set; } = string.Empty;
    /// <summary>获取或设置绑定槽位 ID。</summary>
    public WorkflowInput<string> SlotIdBinding { get; set; } = WorkflowInput<string>.FromLiteral(string.Empty);
    /// <summary>获取或设置产品 ID 来源。</summary>
    public E_ProductFlowValueSource ProductIdSource { get; set; }
    /// <summary>获取或设置固定产品 ID。</summary>
    public string ProductId { get; set; } = string.Empty;
    /// <summary>获取或设置绑定产品 ID。</summary>
    public WorkflowInput<string> ProductIdBinding { get; set; } = WorkflowInput<string>.FromLiteral(string.Empty);
    /// <summary>获取或设置产品 ID 为空时是否自动生成。</summary>
    public bool AutoGenerateProductId { get; set; } = true;
    /// <summary>获取或设置配方来源。</summary>
    public E_ProductFlowValueSource RecipeSource { get; set; }
    /// <summary>获取或设置固定配方。</summary>
    public string Recipe { get; set; } = string.Empty;
    /// <summary>获取或设置绑定配方。</summary>
    public WorkflowInput<string> RecipeBinding { get; set; } = WorkflowInput<string>.FromLiteral(string.Empty);
    /// <summary>获取或设置原因来源。</summary>
    public E_ProductFlowValueSource ReasonSource { get; set; }
    /// <summary>获取或设置固定原因。</summary>
    public string Reason { get; set; } = "WorkflowCreateProduct";
    /// <summary>获取或设置绑定原因。</summary>
    public WorkflowInput<string> ReasonBinding { get; set; } = WorkflowInput<string>.FromLiteral(string.Empty);
    /// <summary>获取或设置结果变量键。</summary>
    public string ResultVarKey { get; set; } = "ProductCreateResult";
}

/// <summary>执行产品创建节点。</summary>
public sealed class ProductCreateNodeHandler : WorkflowNodeHandler<ProductCreateNodeModel>
{
    /// <inheritdoc />
    protected override async ValueTask<NodeExecutionResult> ExecuteAsync(ProductCreateNodeModel node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
    {
        static string Resolve(E_ProductFlowValueSource source, string literal, WorkflowInput<string> binding, IWorkflowNodeExecutionContext ctx) =>
            source == E_ProductFlowValueSource.Binding ? ctx.ResolveInput(binding) ?? string.Empty : literal?.Trim() ?? string.Empty;
        var stationId = Resolve(node.StationIdSource, node.StationId, node.StationIdBinding, context);
        if (string.IsNullOrWhiteSpace(stationId)) throw new InvalidOperationException("StationId 为空。");
        var productId = Resolve(node.ProductIdSource, node.ProductId, node.ProductIdBinding, context);
        if (string.IsNullOrWhiteSpace(productId) && !node.AutoGenerateProductId) throw new InvalidOperationException("ProductId 为空，且未启用自动生成。");
        var request = new ProductCreateRequest(
            string.IsNullOrWhiteSpace(productId) ? null : productId,
            node.AutoGenerateProductId,
            Resolve(node.RecipeSource, node.Recipe, node.RecipeBinding, context),
            Resolve(node.ReasonSource, node.Reason, node.ReasonBinding, context),
            stationId,
            Resolve(node.SlotIdSource, node.SlotId, node.SlotIdBinding, context));
        var service = context.GetRequiredCapability<IWorkflowProcessService>();
        var output = await service.CreateProductAsync(request, cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(node.ResultVarKey)) context.SetVariable(node.ResultVarKey.Trim(), output);
        return NodeExecutionResult.Continue(output: output);
    }
}
