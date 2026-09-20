namespace DP.WorkFlow;

/// <summary>判断指定工站是否允许进料。</summary>
[WorkflowNode("StationCanReceive", DisplayName = "判断可进料节点", Category = "8.Process/ProductFlow")]
public sealed class StationCanReceiveNodeModel : ProductFlowStationSlotNodeModel { /// <inheritdoc />
    public override string NodeType => "StationCanReceive"; }

/// <summary>执行工站可进料判断。</summary>
public sealed class StationCanReceiveNodeHandler : WorkflowNodeHandler<StationCanReceiveNodeModel>
{
    /// <inheritdoc />
    protected override async ValueTask<NodeExecutionResult> ExecuteAsync(StationCanReceiveNodeModel node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
    {
        var address = ProductFlowNodeRuntime.ResolveAddress(node, context);
        var value = await ProductFlowNodeRuntime.GetService(context).CanReceiveAsync(address.StationId, address.SlotId, cancellationToken).ConfigureAwait(false);
        var output = new StationBooleanNodeResult(address.StationId, address.SlotId, value);
        return NodeExecutionResult.Continue(value ? WorkflowPorts.True : WorkflowPorts.False, output);
    }
}
