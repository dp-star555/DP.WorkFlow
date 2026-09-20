namespace DP.WorkFlow;

/// <summary>循环等待工站允许进料。</summary>
[WorkflowNode("StationWaitCanReceive", DisplayName = "等待可进料节点", Category = "8.Process/ProductFlow")]
public sealed class StationWaitCanReceiveNodeModel : ProductFlowStationSlotNodeModel, IRecoverableWorkflowNode
{
    /// <inheritdoc />
    public RecoverableInterruptOption Interrupt { get; set; } = new();
    /// <inheritdoc />
    public override string NodeType => "StationWaitCanReceive";
    /// <summary>超时毫秒数；0 表示不超时。</summary>
    public int TimeoutMs { get; set; } = 5000;
    /// <summary>轮询间隔毫秒数。</summary>
    public int PollIntervalMs { get; set; } = 100;
    /// <summary>超时是否走 Timeout 分支。</summary>
    public bool TimeoutAsFalseBranch { get; set; } = true;
}

/// <summary>执行等待可进料节点。</summary>
public sealed class StationWaitCanReceiveNodeHandler : WorkflowNodeHandler<StationWaitCanReceiveNodeModel>
{
    /// <inheritdoc />
    protected override async ValueTask<NodeExecutionResult> ExecuteAsync(StationWaitCanReceiveNodeModel node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
    {
        var address = ProductFlowNodeRuntime.ResolveAddress(node, context);
        var service = ProductFlowNodeRuntime.GetService(context);
        var matched = await ProductFlowNodeRuntime.WaitAsync(ct => service.CanReceiveAsync(address.StationId, address.SlotId, ct), node.TimeoutMs, node.PollIntervalMs, cancellationToken).ConfigureAwait(false);
        var output = new StationWaitNodeResult(address.StationId, address.SlotId, matched);
        if (!matched && !node.TimeoutAsFalseBranch)
            return WorkflowRecoverableNodeFailure.Create(node, $"等待工站可进料超时: {address.StationId}");
        return NodeExecutionResult.Continue(matched ? WorkflowPorts.Success : WorkflowPorts.Timeout, output);
    }
}
