namespace DP.WorkFlow;

/// <summary>等待气缸伸出或缩回到位。</summary>
[WorkflowNode("CylinderWait", DisplayName = "气缸等待节点", Category = "4.Motion/Pneumatic")]
public sealed class CylinderWaitNodeModel : WorkflowNodeModel, IRecoverableWorkflowNode
{
    /// <inheritdoc />
    public RecoverableInterruptOption Interrupt { get; set; } = new();
    /// <inheritdoc />
    public override string NodeType => "CylinderWait";
    /// <summary>获取或设置气缸注册名称。</summary>
    public string CylinderName { get; set; } = string.Empty;
    /// <summary>获取或设置目标状态。</summary>
    public WorkflowCylinderTargetState TargetState { get; set; } = WorkflowCylinderTargetState.Extended;
    /// <summary>获取或设置超时毫秒数。</summary>
    public int TimeoutMs { get; set; } = 3000;
    /// <summary>获取或设置超时是否走 Timeout 分支。</summary>
    public bool TimeoutAsFalseBranch { get; set; } = true;
}

/// <summary>执行气缸等待节点。</summary>
public sealed class CylinderWaitNodeHandler : WorkflowNodeHandler<CylinderWaitNodeModel>
{
    /// <inheritdoc />
    protected override async ValueTask<NodeExecutionResult> ExecuteAsync(CylinderWaitNodeModel node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
    {
        var output = await CylinderControlNodeHandler.GetService(context, node.CylinderName).WaitCylinderAsync(node.CylinderName.Trim(), node.TargetState, node.TimeoutMs, cancellationToken).ConfigureAwait(false);
        if (!output.Success && !node.TimeoutAsFalseBranch)
            return WorkflowRecoverableNodeFailure.Create(node, output.Message ?? "Cylinder wait timeout.");
        return NodeExecutionResult.Continue(output.Success ? WorkflowPorts.Success : WorkflowPorts.Timeout, output);
    }
}
