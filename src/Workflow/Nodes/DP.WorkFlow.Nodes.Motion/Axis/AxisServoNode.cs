namespace DP.WorkFlow;

/// <summary>设置轴伺服使能状态。</summary>
[WorkflowNode("AxisServo", DisplayName = "伺服使能", Category = "4.Motion/Axis")]
public sealed class AxisServoNodeModel : WorkflowAxisNodeModel, IWorkflowRetrySafetyNode
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
    /// <summary>获取或设置结果变量键。</summary>
    public string ResultVarKey { get; set; } = "AxisServoResult";
    /// <inheritdoc />
    public WorkflowRetrySafety RetrySafety => WorkflowRetrySafety.Idempotent;
}

/// <summary>执行轴伺服节点。</summary>
public sealed class AxisServoNodeHandler : WorkflowNodeHandler<AxisServoNodeModel>, IWorkflowNodeOperationFactory
{
    /// <inheritdoc />
    protected override async ValueTask<NodeExecutionResult> ExecuteAsync(AxisServoNodeModel node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
    {
        await using var operation = await CreateOperationAsync(Guid.NewGuid(), node, context, cancellationToken).ConfigureAwait(false);
        return await operation.ExecuteAsync(context, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public ValueTask<IWorkflowNodeOperation> CreateOperationAsync(Guid operationId, IWorkflowNodeModel node,
        IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (node is not AxisServoNodeModel servo) throw new ArgumentException("需要伺服节点配置。", nameof(node));
        ValidateWait(servo.TimeoutMs, servo.PollIntervalMs);
        return ValueTask.FromResult<IWorkflowNodeOperation>(new ServoOperation(GetService(context), servo.ResolveAxis(context),
            servo.Enabled, servo.WaitForCompleted, servo.TimeoutMs, servo.PollIntervalMs,
            servo.ResultVarKey, servo.Interrupt?.AlarmCode ?? 0));
    }

    private sealed class ServoOperation(IWorkflowAxisService service, WorkflowAxisAddress axis, bool enabled,
        bool wait, int timeoutMs, int pollIntervalMs, string resultKey, int alarmCode) : IWorkflowNodeOperation
    {
        public async ValueTask<NodeExecutionResult> ExecuteAsync(IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
        {
            // SetServo是既有目标状态契约，不把Toggle/Pulse或任意AxisStep视为可重复动作。
            var output = await service.SetServoAsync(axis, enabled, wait, timeoutMs, pollIntervalMs, cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(resultKey)) context.SetVariable(resultKey.Trim(), output);
            if (!output.Success) return NodeExecutionResult.Fail(
                $"Axis servo wait timeout: target={enabled}, axis={axis.DeviceId}[{axis.AxisId}]",
                alarmCode > 0 ? WorkflowFaultDisposition.RequestRecovery : WorkflowFaultDisposition.HandleAtScope, alarmCode);
            return NodeExecutionResult.Continue(output: output);
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    internal static IWorkflowAxisService GetService(IWorkflowNodeExecutionContext context) =>
        context.GetRequiredCapability<IWorkflowAxisService>();

    internal static void ValidateWait(int timeoutMs, int pollIntervalMs)
    {
        if (timeoutMs < 0) throw new InvalidOperationException("TimeoutMs 不能小于 0。");
        if (pollIntervalMs < 5) throw new InvalidOperationException("PollIntervalMs 不能小于 5。");
    }
}
