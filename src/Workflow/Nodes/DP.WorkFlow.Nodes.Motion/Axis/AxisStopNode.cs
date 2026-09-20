namespace DP.WorkFlow;

/// <summary>停止指定轴并可等待停止完成。</summary>
[WorkflowNode("AxisStop", DisplayName = "轴停止", Category = "4.Motion/Axis")]
public sealed class AxisStopNodeModel : WorkflowAxisNodeModel
{
    /// <inheritdoc />
    public override string NodeType => "AxisStop";
    /// <summary>获取或设置是否等待停止完成。</summary>
    public bool WaitForCompleted { get; set; } = true;
    /// <summary>获取或设置超时毫秒数。</summary>
    public int TimeoutMs { get; set; } = 5000;
    /// <summary>获取或设置轮询间隔毫秒数。</summary>
    public int PollIntervalMs { get; set; } = 20;
    /// <summary>获取或设置结果变量键。</summary>
    public string ResultVarKey { get; set; } = "AxisStopResult";
}

/// <summary>执行轴停止节点。</summary>
public sealed class AxisStopNodeHandler : WorkflowNodeHandler<AxisStopNodeModel>
{
    /// <inheritdoc />
    protected override async ValueTask<NodeExecutionResult> ExecuteAsync(AxisStopNodeModel node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
    {
        AxisServoNodeHandler.ValidateWait(node.TimeoutMs, node.PollIntervalMs);
        var axis = node.ResolveAxis(context);
        var output = await AxisServoNodeHandler.GetService(context).StopAsync(
            axis, node.WaitForCompleted, node.TimeoutMs, node.PollIntervalMs, cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(node.ResultVarKey)) context.SetVariable(node.ResultVarKey.Trim(), output);
        if (!output.Success) return WorkflowRecoverableNodeFailure.Create(node, $"Axis stop timeout: axis={axis.DeviceId}[{axis.AxisId}], InPos=false");
        return NodeExecutionResult.Continue(output: output);
    }
}
