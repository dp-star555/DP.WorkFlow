namespace DP.WorkFlow;

/// <summary>驱动晶圆机器人向指定站位放片。</summary>
[WorkflowNode("WaferRobotPlace", DisplayName = "晶圆机器人放片节点", Category = "8.Process/WaferRobot")]
public sealed class WaferRobotPlaceNodeModel : WaferRobotTargetCommandNodeModel { /// <inheritdoc />
    public override string NodeType => "WaferRobotPlace"; }

/// <summary>执行机器人放片。</summary>
public sealed class WaferRobotPlaceNodeHandler : WorkflowNodeHandler<WaferRobotPlaceNodeModel>
{
    /// <inheritdoc />
    protected override ValueTask<NodeExecutionResult> ExecuteAsync(WaferRobotPlaceNodeModel node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
    {
        var target = WaferRobotNodeRuntime.ResolveTarget(node, context);
        return WaferRobotNodeRuntime.ExecuteCommandAsync(node, "Place", target, context, (service, key, ct) => service.PlaceAsync(key, target, ct), true, cancellationToken);
    }
}
