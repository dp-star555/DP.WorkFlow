namespace DP.WorkFlow;

/// <summary>标记指定工站槽位当前产品处理完成。</summary>
[WorkflowNode("StationFinished", DisplayName = "工站完成节点", Category = "8.Process/ProductFlow")]
public sealed class StationFinishedNodeModel : ProductFlowStationSlotNodeModel
{
    /// <inheritdoc />
    public override string NodeType => "StationFinished";
    /// <summary>结果变量键。</summary>
    public string ResultVarKey { get; set; } = "StationFinishedResult";
}

/// <summary>执行工站完成标记。</summary>
public sealed class StationFinishedNodeHandler : WorkflowNodeHandler<StationFinishedNodeModel>
{
    /// <inheritdoc />
    protected override async ValueTask<NodeExecutionResult> ExecuteAsync(StationFinishedNodeModel node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
    {
        var address = ProductFlowNodeRuntime.ResolveAddress(node, context);
        await ProductFlowNodeRuntime.GetService(context).MarkStationFinishedAsync(address.StationId, address.SlotId, cancellationToken).ConfigureAwait(false);
        var output = new StationFinishedNodeResult(address.StationId, address.SlotId, true);
        if (!string.IsNullOrWhiteSpace(node.ResultVarKey)) context.SetVariable(node.ResultVarKey.Trim(), output);
        return NodeExecutionResult.Continue(output: output);
    }
}
