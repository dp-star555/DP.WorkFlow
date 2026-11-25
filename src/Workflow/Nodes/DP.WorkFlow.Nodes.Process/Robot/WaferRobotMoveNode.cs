namespace DP.WorkFlow;

/// <summary>驱动晶圆机器人移动到指定站位。</summary>
[WorkflowNode("WaferRobotMove", DisplayName = "晶圆机器人移动节点", Category = "8.Process/WaferRobot", OutputType = typeof(WaferRobotCommandNodeResult))]
[WorkflowOutputPorts(WorkflowPorts.Success, WorkflowPorts.Failed)]
public sealed class WaferRobotMoveNodeModel : WaferRobotTargetCommandNodeModel { /// <inheritdoc />
    public override string NodeType => "WaferRobotMove"; }

/// <summary>执行机器人移动。</summary>
public sealed class WaferRobotMoveNodeHandler : WorkflowNodeHandler<WaferRobotMoveNodeModel>, IWorkflowInPlaceRecoveryNodeHandler
{
    /// <inheritdoc />
    protected override IEnumerable<WorkflowRuntimeCapabilityRequirement> GetRequiredCapabilities(WaferRobotMoveNodeModel node) =>
        new[] { ProcessCapabilities.WaferRobotCapability };

    /// <inheritdoc />
    protected override ValueTask<NodeExecutionResult> ExecuteAsync(WaferRobotMoveNodeModel node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
    {
        var target = WaferRobotNodeRuntime.ResolveTarget(node, context);
        return WaferRobotNodeRuntime.ExecuteCommandAsync(node, "MoveTo", target, context, (service, key, ct) => service.MoveAsync(key, target, ct), true, cancellationToken);
    }

    /// <summary>就地恢复：目标是绝对位置 / 状态，补发同一命令即可补完（机器人到位时立即完成）。</summary>
    public ValueTask<NodeExecutionResult> RecoverInPlaceAsync(IWorkflowNodeModel node, IWorkflowNodeExecutionContext context,
        WorkflowNodeInterruption interruption, CancellationToken cancellationToken) =>
        ExecuteAsync((WaferRobotMoveNodeModel)node, context, cancellationToken);
}
