namespace DP.WorkFlow;

/// <summary>停止晶圆机器人当前动作。</summary>
[WorkflowNode("WaferRobotStop", DisplayName = "晶圆机器人停止节点", Category = "8.Process/WaferRobot")]
public sealed class WaferRobotStopNodeModel : WaferRobotCommandNodeModel
{
    /// <inheritdoc />
    public override string NodeType => "WaferRobotStop";
    /// <summary>停止模式。</summary>
    public E_StopMode StopMode { get; set; }
}

/// <summary>执行机器人停止。</summary>
public sealed class WaferRobotStopNodeHandler : WorkflowNodeHandler<WaferRobotStopNodeModel>
{
    /// <inheritdoc />
    protected override ValueTask<NodeExecutionResult> ExecuteAsync(WaferRobotStopNodeModel node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken) =>
        WaferRobotNodeRuntime.ExecuteCommandAsync(node, "Stop", null, context, (service, key, ct) => service.StopAsync(key, node.StopMode, ct), false, cancellationToken);
}
