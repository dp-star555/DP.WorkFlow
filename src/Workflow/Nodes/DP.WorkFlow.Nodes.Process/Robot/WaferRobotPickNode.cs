namespace DP.WorkFlow;

/// <summary>驱动晶圆机器人从指定站位取片。</summary>
[WorkflowNode("WaferRobotPick", DisplayName = "晶圆机器人取片节点", Category = "8.Process/WaferRobot")]
public sealed class WaferRobotPickNodeModel : WaferRobotTargetCommandNodeModel { /// <inheritdoc />
    public override string NodeType => "WaferRobotPick"; }

/// <summary>执行机器人取片。</summary>
public sealed class WaferRobotPickNodeHandler : WorkflowNodeHandler<WaferRobotPickNodeModel>
{
    /// <inheritdoc />
    protected override ValueTask<NodeExecutionResult> ExecuteAsync(WaferRobotPickNodeModel node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
    {
        var target = WaferRobotNodeRuntime.ResolveTarget(node, context);
        return WaferRobotNodeRuntime.ExecuteCommandAsync(node, "Pick", target, context, (service, key, ct) => service.PickAsync(key, target, ct), true, cancellationToken);
    }
}
