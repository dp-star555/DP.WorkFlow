namespace DP.WorkFlow;

/// <summary>执行晶圆机器人回原或安全初始位动作。</summary>
[WorkflowNode("WaferRobotHome", DisplayName = "晶圆机器人回原节点", Category = "8.Process/WaferRobot", OutputType = typeof(WaferRobotCommandNodeResult))]
[WorkflowOutputPorts(WorkflowPorts.Success, WorkflowPorts.Failed)]
public sealed class WaferRobotHomeNodeModel : WaferRobotCommandNodeModel { /// <inheritdoc />
    public override string NodeType => "WaferRobotHome"; }

/// <summary>执行机器人回原。</summary>
public sealed class WaferRobotHomeNodeHandler : WorkflowNodeHandler<WaferRobotHomeNodeModel>
{
    /// <inheritdoc />
    protected override IEnumerable<WorkflowRuntimeCapabilityRequirement> GetRequiredCapabilities(WaferRobotHomeNodeModel node) =>
        new[] { ProcessCapabilities.WaferRobotCapability };

    /// <inheritdoc />
    protected override ValueTask<NodeExecutionResult> ExecuteAsync(WaferRobotHomeNodeModel node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken) =>
        WaferRobotNodeRuntime.ExecuteCommandAsync(node, "Home", null, context, static (service, key, ct) => service.HomeAsync(key, ct), true, cancellationToken);
}
