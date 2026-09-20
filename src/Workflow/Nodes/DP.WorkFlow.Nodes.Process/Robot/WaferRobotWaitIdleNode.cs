namespace DP.WorkFlow;

/// <summary>循环刷新机器人状态并等待进入空闲状态。</summary>
[WorkflowNode("WaferRobotWaitIdle", DisplayName = "等待机器人空闲节点", Category = "8.Process/WaferRobot")]
public sealed class WaferRobotWaitIdleNodeModel : WorkflowNodeModel, IRecoverableWorkflowNode
{
    /// <inheritdoc />
    public RecoverableInterruptOption Interrupt { get; set; } = new();
    /// <inheritdoc />
    public override string NodeType => "WaferRobotWaitIdle";
    /// <summary>机器人注册键。</summary>
    public string RobotKey { get; set; } = string.Empty;
    /// <summary>超时毫秒数；0 表示不超时。</summary>
    public int TimeoutMs { get; set; } = 30000;
    /// <summary>轮询间隔毫秒数。</summary>
    public int PollIntervalMs { get; set; } = 100;
    /// <summary>超时是否走 Timeout 分支。</summary>
    public bool TimeoutAsFalseBranch { get; set; } = true;
    /// <summary>结果变量键。</summary>
    public string ResultVarKey { get; set; } = "WaferRobotWaitIdleResult";
}

/// <summary>执行机器人空闲等待。</summary>
public sealed class WaferRobotWaitIdleNodeHandler : WorkflowNodeHandler<WaferRobotWaitIdleNodeModel>
{
    /// <inheritdoc />
    protected override async ValueTask<NodeExecutionResult> ExecuteAsync(WaferRobotWaitIdleNodeModel node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(node.RobotKey)) throw new InvalidOperationException("RobotKey 为空。");
        var key = node.RobotKey.Trim();
        var service = WaferRobotNodeRuntime.GetService(context);
        WaferRobotStateResult? state = null;
        var started = System.Diagnostics.Stopwatch.StartNew();
        while (node.TimeoutMs <= 0 || started.ElapsedMilliseconds < node.TimeoutMs)
        {
            state = await service.RefreshStateAsync(key, cancellationToken).ConfigureAwait(false);
            if (state.CanStartMotion) break;
            await Task.Delay(Math.Max(10, node.PollIntervalMs), cancellationToken).ConfigureAwait(false);
        }
        var matched = state?.CanStartMotion == true;
        var output = new WaferRobotWaitIdleNodeResult(key, matched, state?.IsConnected ?? false, state?.IsInitialized ?? false, state?.IsBusy ?? false, state?.IsPaused ?? false, state?.HasAlarm ?? false, state?.CanStartMotion ?? false, state?.StatusCode, state?.StatusText);
        if (!string.IsNullOrWhiteSpace(node.ResultVarKey)) context.SetVariable(node.ResultVarKey.Trim(), output);
        if (!matched && !node.TimeoutAsFalseBranch)
            return WorkflowRecoverableNodeFailure.Create(node, "等待机器人空闲超时。");
        return NodeExecutionResult.Continue(matched ? WorkflowPorts.Success : WorkflowPorts.Timeout, output);
    }
}
