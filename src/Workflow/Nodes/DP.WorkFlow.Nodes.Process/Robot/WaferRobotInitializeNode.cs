namespace DP.WorkFlow;

/// <summary>执行晶圆机器人初始化或复位。</summary>
[WorkflowNode("WaferRobotInitialize", DisplayName = "晶圆机器人初始化节点", Category = "8.Process/WaferRobot")]
public sealed class WaferRobotInitializeNodeModel : WaferRobotCommandNodeModel { /// <inheritdoc />
    public override string NodeType => "WaferRobotInitialize"; }

/// <summary>执行机器人初始化。</summary>
public sealed class WaferRobotInitializeNodeHandler : WorkflowNodeHandler<WaferRobotInitializeNodeModel>
{
    /// <inheritdoc />
    protected override ValueTask<NodeExecutionResult> ExecuteAsync(WaferRobotInitializeNodeModel node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken) =>
        WaferRobotNodeRuntime.ExecuteCommandAsync(node, "Initialize", null, context, static (service, key, ct) => service.InitializeAsync(key, ct), false, cancellationToken);
}
