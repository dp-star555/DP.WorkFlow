namespace DP.WorkFlow;

/// <summary>驱动晶圆机器人移动到指定站位。</summary>
[WorkflowNode("WaferRobotMove", DisplayName = "晶圆机器人移动节点", Category = "8.Process/WaferRobot")]
public sealed class WaferRobotMoveNodeModel : WaferRobotTargetCommandNodeModel { /// <inheritdoc />
    public override string NodeType => "WaferRobotMove"; }

/// <summary>执行机器人移动。</summary>
public sealed class WaferRobotMoveNodeHandler : WorkflowNodeHandler<WaferRobotMoveNodeModel>
{
    /// <inheritdoc />
    protected override ValueTask<NodeExecutionResult> ExecuteAsync(WaferRobotMoveNodeModel node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
    {
        var target = WaferRobotNodeRuntime.ResolveTarget(node, context);
        return WaferRobotNodeRuntime.ExecuteCommandAsync(node, "MoveTo", target, context, (service, key, ct) => service.MoveAsync(key, target, ct), true, cancellationToken);
    }
}
