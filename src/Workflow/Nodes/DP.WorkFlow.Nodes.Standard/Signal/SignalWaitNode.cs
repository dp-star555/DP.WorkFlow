namespace DP.WorkFlow;

/// <summary>等待命名布尔信号达到期望状态。</summary>
[WorkflowNode("SignalWait", DisplayName = "信号等待", Category = "3.Control/信号")]
public sealed class SignalWaitNodeModel : WorkflowNodeModel
{
    /// <inheritdoc />
    public override string NodeType => "SignalWait";

    /// <summary>获取或设置稳定信号键。</summary>
    public string SignalKey { get; set; } = string.Empty;

    /// <summary>获取或设置期望状态。</summary>
    public WorkflowInput<bool> ExpectedState { get; set; } = WorkflowInput<bool>.FromLiteral(true);

    /// <summary>获取或设置等待超时。</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>获取或设置匹配后是否将信号复位为 false。</summary>
    public bool AutoResetAfterMatched { get; set; }
}

/// <summary>执行信号等待节点。</summary>
public sealed class SignalWaitNodeHandler : WorkflowNodeHandler<SignalWaitNodeModel>
{
    /// <inheritdoc />
    protected override async ValueTask<NodeExecutionResult> ExecuteAsync(
        SignalWaitNodeModel node,
        IWorkflowNodeExecutionContext context,
        CancellationToken cancellationToken)
    {
        var service = SignalSetNodeHandler.GetService(context, node.SignalKey);
        var signalKey = node.SignalKey.Trim();
        var expected = context.ResolveInput(node.ExpectedState);
        var success = await service.WaitAsync(signalKey, expected, node.Timeout, cancellationToken).ConfigureAwait(false);
        if (success && node.AutoResetAfterMatched)
            service.Write(signalKey, false);
        var output = new SignalWaitNodeResult(signalKey, expected, success, node.AutoResetAfterMatched);
        var port = success ? WorkflowPorts.Success : WorkflowPorts.Timeout;
        context.Trace("SignalWait", success ? "信号条件已满足。" : "等待信号超时。", new Dictionary<string, object?>
        {
            ["SignalKey"] = signalKey,
            ["ExpectedState"] = expected,
            ["AutoReset"] = output.AutoReset
        });
        return NodeExecutionResult.Continue(port, output);
    }
}
