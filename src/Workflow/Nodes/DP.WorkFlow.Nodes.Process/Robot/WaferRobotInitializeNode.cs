namespace DP.WorkFlow;

/// <summary>执行晶圆机器人初始化或复位。</summary>
[WorkflowNode("WaferRobotInitialize", DisplayName = "晶圆机器人初始化节点", Category = "8.Process/WaferRobot", OutputType = typeof(WaferRobotCommandNodeResult))]
[WorkflowOutputPorts(WorkflowPorts.Success, WorkflowPorts.Failed)]
public sealed class WaferRobotInitializeNodeModel : WaferRobotCommandNodeModel { /// <inheritdoc />
    public override string NodeType => "WaferRobotInitialize"; }

/// <summary>执行机器人初始化。</summary>
public sealed class WaferRobotInitializeNodeHandler : WorkflowNodeHandler<WaferRobotInitializeNodeModel>, IWorkflowInPlaceRecoveryNodeHandler
{
    /// <inheritdoc />
    protected override IEnumerable<WorkflowRuntimeCapabilityRequirement> GetRequiredCapabilities(WaferRobotInitializeNodeModel node) =>
        new[] { ProcessCapabilities.WaferRobotCapability };

    /// <inheritdoc />
    protected override ValueTask<NodeExecutionResult> ExecuteAsync(WaferRobotInitializeNodeModel node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken) =>
        WaferRobotNodeRuntime.ExecuteCommandAsync(node, "Initialize", null, context, static (service, key, ct) => service.InitializeAsync(key, ct), false, cancellationToken);

    /// <summary>就地恢复：目标是绝对位置 / 状态，补发同一命令即可补完（机器人到位时立即完成）。</summary>
    public ValueTask<NodeExecutionResult> RecoverInPlaceAsync(IWorkflowNodeModel node, IWorkflowNodeExecutionContext context,
        WorkflowNodeInterruption interruption, CancellationToken cancellationToken) =>
        ExecuteAsync((WaferRobotInitializeNodeModel)node, context, cancellationToken);
}
