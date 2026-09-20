namespace DP.WorkFlow;

/// <summary>判断指定工站是否允许出料。</summary>
[WorkflowNode("StationCanSend", DisplayName = "判断可出料节点", Category = "8.Process/ProductFlow")]
public sealed class StationCanSendNodeModel : ProductFlowStationSlotNodeModel { /// <inheritdoc />
    public override string NodeType => "StationCanSend"; }

/// <summary>执行工站可出料判断。</summary>
public sealed class StationCanSendNodeHandler : WorkflowNodeHandler<StationCanSendNodeModel>
{
    /// <inheritdoc />
    protected override async ValueTask<NodeExecutionResult> ExecuteAsync(StationCanSendNodeModel node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
    {
        var address = ProductFlowNodeRuntime.ResolveAddress(node, context);
        var value = await ProductFlowNodeRuntime.GetService(context).CanSendAsync(address.StationId, address.SlotId, cancellationToken).ConfigureAwait(false);
        var output = new StationBooleanNodeResult(address.StationId, address.SlotId, value);
        return NodeExecutionResult.Continue(value ? WorkflowPorts.True : WorkflowPorts.False, output);
    }
}
