namespace DP.WorkFlow;

/// <summary>设置轴伺服使能状态。</summary>
[WorkflowRecovery(WorkflowRecoveryCategory.Redoable)]
[WorkflowNode("AxisServo", DisplayName = "伺服使能", Category = "4.Motion/Axis", OutputType = typeof(AxisServoNodeResult))]
public sealed class AxisServoNodeModel : WorkflowAxisNodeModel
{
    /// <inheritdoc />
    public override string NodeType => "AxisServo";
    /// <summary>获取或设置目标使能状态。</summary>
    public bool Enabled { get; set; } = true;
    /// <summary>获取或设置是否等待实际状态匹配。</summary>
    public bool WaitForCompleted { get; set; } = true;
    /// <summary>获取或设置超时毫秒数。</summary>
    public int TimeoutMs { get; set; } = 5000;
    /// <summary>获取或设置轮询间隔毫秒数。</summary>
    public int PollIntervalMs { get; set; } = 20;
}

/// <summary>执行轴伺服节点。设置目标使能状态，重复执行没有额外影响，因此可直接重做。</summary>
public sealed class AxisServoNodeHandler : WorkflowNodeHandler<AxisServoNodeModel>
{
    /// <inheritdoc />
    protected override IEnumerable<WorkflowRuntimeCapabilityRequirement> GetRequiredCapabilities(AxisServoNodeModel node) =>
        new[] { MotionCapabilities.AxisCapability };

    /// <inheritdoc />
    protected override async ValueTask<NodeExecutionResult> ExecuteAsync(AxisServoNodeModel node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
    {
        ValidateWait(node.TimeoutMs, node.PollIntervalMs);
        var axis = node.ResolveAxis(context);
        var output = await GetService(context).SetServoAsync(axis, node.Enabled, node.WaitForCompleted, node.TimeoutMs,
            node.PollIntervalMs, cancellationToken).ConfigureAwait(false);
        if (!output.Success)
            return WorkflowRecoverableNodeFailure.Create(node,
                $"Axis servo wait timeout: target={node.Enabled}, axis={axis.DeviceId}[{axis.AxisId}]");
        return NodeExecutionResult.Continue(output: output);
    }

    internal static IWorkflowAxisService GetService(IWorkflowNodeExecutionContext context) =>
        context.GetRequiredCapability<IWorkflowAxisService>();

    internal static void ValidateWait(int timeoutMs, int pollIntervalMs)
    {
        if (timeoutMs < 0) throw new InvalidOperationException("TimeoutMs 不能小于 0。");
        if (pollIntervalMs < 5) throw new InvalidOperationException("PollIntervalMs 不能小于 5。");
    }
}
