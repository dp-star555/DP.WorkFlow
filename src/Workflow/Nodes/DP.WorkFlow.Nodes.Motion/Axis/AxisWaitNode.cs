namespace DP.WorkFlow;

/// <summary>等待指定轴满足旧版六种状态条件之一。</summary>
[WorkflowNode("AxisWait", DisplayName = "轴等待", Category = "4.Motion/Axis")]
public sealed class AxisWaitNodeModel : WorkflowAxisNodeModel
{
    /// <inheritdoc />
    public override string NodeType => "AxisWait";
    /// <summary>获取或设置等待条件。</summary>
    public AxisWaitCondition Condition { get; set; } = AxisWaitCondition.InPosOff;
    /// <summary>获取或设置目标位置来源。</summary>
    public E_AxisValueSource TargetPositionSource { get; set; }
    /// <summary>获取或设置固定目标位置。</summary>
    public double TargetPosition { get; set; }
    /// <summary>获取或设置绑定目标位置。</summary>
    public WorkflowInput<double> TargetPositionBinding { get; set; } = WorkflowInput<double>.FromLiteral(0);
    /// <summary>获取或设置位置容差。</summary>
    public double PositionTolerance { get; set; } = 0.01;
    /// <summary>获取或设置超时毫秒数。</summary>
    public int TimeoutMs { get; set; } = 5000;
    /// <summary>获取或设置轮询间隔毫秒数。</summary>
    public int PollIntervalMs { get; set; } = 20;
    /// <summary>获取或设置超时是否走 Timeout 分支。</summary>
    public bool TimeoutAsFalseBranch { get; set; } = true;
    /// <summary>获取或设置结果变量键。</summary>
    public string ResultVarKey { get; set; } = "AxisWaitResult";
}

/// <summary>执行轴等待节点。</summary>
public sealed class AxisWaitNodeHandler : WorkflowNodeHandler<AxisWaitNodeModel>
{
    /// <inheritdoc />
    protected override async ValueTask<NodeExecutionResult> ExecuteAsync(AxisWaitNodeModel node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
    {
        AxisServoNodeHandler.ValidateWait(node.TimeoutMs, node.PollIntervalMs);
        if (node.PositionTolerance < 0) throw new InvalidOperationException("PositionTolerance 不能小于 0。");
        var needsPosition = node.Condition is AxisWaitCondition.PositionReached or AxisWaitCondition.PositionPassed;
        double? target = needsPosition
            ? node.TargetPositionSource == E_AxisValueSource.Binding ? context.ResolveInput(node.TargetPositionBinding) : node.TargetPosition
            : null;
        var request = new WorkflowAxisWaitRequest(
            node.ResolveAxis(context), node.Condition, target, node.PositionTolerance, node.TimeoutMs, node.PollIntervalMs);
        var output = await AxisServoNodeHandler.GetService(context).WaitAsync(request, cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(node.ResultVarKey)) context.SetVariable(node.ResultVarKey.Trim(), output);
        if (!output.Success && !node.TimeoutAsFalseBranch)
            return WorkflowRecoverableNodeFailure.Create(node, $"Axis wait timeout: {output.DeviceId}[{output.AxisId}] {output.Condition}.");
        return NodeExecutionResult.Continue(output.Success ? WorkflowPorts.Success : WorkflowPorts.Timeout, output);
    }
}
