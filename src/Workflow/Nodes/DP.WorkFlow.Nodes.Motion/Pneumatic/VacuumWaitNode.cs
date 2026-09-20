namespace DP.WorkFlow;

/// <summary>等待真空达到或离开吸附状态。</summary>
[WorkflowNode("VacuumWait", DisplayName = "真空等待节点", Category = "4.Motion/Pneumatic")]
public sealed class VacuumWaitNodeModel : WorkflowNodeModel, IRecoverableWorkflowNode
{
    /// <inheritdoc />
    public RecoverableInterruptOption Interrupt { get; set; } = new();
    /// <inheritdoc />
    public override string NodeType => "VacuumWait";
    /// <summary>获取或设置真空注册名称。</summary>
    public string VacuumName { get; set; } = string.Empty;
    /// <summary>获取或设置目标状态。</summary>
    public WorkflowVacuumTargetState TargetState { get; set; } = WorkflowVacuumTargetState.VacuumOk;
    /// <summary>获取或设置超时毫秒数。</summary>
    public int TimeoutMs { get; set; } = 3000;
    /// <summary>获取或设置轮询间隔毫秒数。</summary>
    public int PollIntervalMs { get; set; } = 50;
    /// <summary>获取或设置超时是否走 Timeout 分支。</summary>
    public bool TimeoutAsFalseBranch { get; set; } = true;
}

/// <summary>执行真空等待节点。</summary>
public sealed class VacuumWaitNodeHandler : WorkflowNodeHandler<VacuumWaitNodeModel>
{
    /// <inheritdoc />
    protected override async ValueTask<NodeExecutionResult> ExecuteAsync(VacuumWaitNodeModel node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
    {
        if (node.PollIntervalMs < 5) throw new InvalidOperationException("PollIntervalMs 不能小于 5。");
        var output = await CylinderControlNodeHandler.GetService(context, node.VacuumName).WaitVacuumAsync(node.VacuumName.Trim(), node.TargetState, node.TimeoutMs, node.PollIntervalMs, cancellationToken).ConfigureAwait(false);
        if (!output.Success && !node.TimeoutAsFalseBranch)
            return WorkflowRecoverableNodeFailure.Create(node, output.Message ?? "Vacuum wait timeout.");
        return NodeExecutionResult.Continue(output.Success ? WorkflowPorts.Success : WorkflowPorts.Timeout, output);
    }
}
