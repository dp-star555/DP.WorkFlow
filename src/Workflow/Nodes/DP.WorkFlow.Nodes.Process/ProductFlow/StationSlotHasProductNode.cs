namespace DP.WorkFlow;

/// <summary>判断指定工站槽位是否存在产品。</summary>
[WorkflowNode("StationSlotHasProduct", DisplayName = "判断槽位有产品", Category = "8.Process/ProductFlow")]
public sealed class StationSlotHasProductNodeModel : ProductFlowStationSlotNodeModel { /// <inheritdoc />
    public override string NodeType => "StationSlotHasProduct"; }

/// <summary>执行槽位产品判断。</summary>
public sealed class StationSlotHasProductNodeHandler : WorkflowNodeHandler<StationSlotHasProductNodeModel>
{
    /// <inheritdoc />
    protected override async ValueTask<NodeExecutionResult> ExecuteAsync(StationSlotHasProductNodeModel node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
    {
        var address = ProductFlowNodeRuntime.ResolveAddress(node, context, true);
        var value = await ProductFlowNodeRuntime.GetService(context).SlotHasProductAsync(address.StationId, address.SlotId!, cancellationToken).ConfigureAwait(false);
        var output = new StationBooleanNodeResult(address.StationId, address.SlotId, value);
        return NodeExecutionResult.Continue(value ? WorkflowPorts.True : WorkflowPorts.False, output);
    }
}
