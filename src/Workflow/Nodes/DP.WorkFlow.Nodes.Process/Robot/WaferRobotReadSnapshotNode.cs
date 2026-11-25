namespace DP.WorkFlow;

/// <summary>读取晶圆机器人当前快照。</summary>
[WorkflowNode("WaferRobotReadSnapshot", DisplayName = "读取机器人快照节点", Category = "8.Process/WaferRobot", OutputType = typeof(WaferRobotSnapshotResult))]
[WorkflowOutputPorts(WorkflowPorts.Success, WorkflowPorts.Failed)]
public sealed class WaferRobotReadSnapshotNodeModel : WorkflowNodeModel
{
    /// <inheritdoc />
    public override string NodeType => "WaferRobotReadSnapshot";
    /// <summary>机器人注册键。</summary>
    public string RobotKey { get; set; } = string.Empty;
}

/// <summary>执行机器人快照读取。</summary>
public sealed class WaferRobotReadSnapshotNodeHandler : WorkflowSideEffectFreeNodeHandler<WaferRobotReadSnapshotNodeModel>
{
    /// <inheritdoc />
    protected override IEnumerable<WorkflowRuntimeCapabilityRequirement> GetRequiredCapabilities(WaferRobotReadSnapshotNodeModel node) =>
        new[] { ProcessCapabilities.WaferRobotCapability };

    /// <inheritdoc />
    protected override async ValueTask<NodeExecutionResult> ExecuteAsync(WaferRobotReadSnapshotNodeModel node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(node.RobotKey)) throw new InvalidOperationException("RobotKey 为空。");
        var output = await WaferRobotNodeRuntime.GetService(context).ReadSnapshotAsync(node.RobotKey.Trim(), cancellationToken).ConfigureAwait(false);
        return NodeExecutionResult.Continue(output: output);
    }
}
