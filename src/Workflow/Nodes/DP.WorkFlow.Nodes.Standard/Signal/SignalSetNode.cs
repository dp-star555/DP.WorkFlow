namespace DP.WorkFlow;

/// <summary>设置或翻转命名布尔信号。</summary>
[WorkflowNode("SignalSet", DisplayName = "信号设置", Category = "3.Control/信号", OutputType = typeof(SignalSetNodeResult))]
public sealed class SignalSetNodeModel : WorkflowNodeModel, IWorkflowPublicDataUser
{
    /// <inheritdoc />
    public override string NodeType => "SignalSet";

    /// <summary>获取或设置稳定信号键。</summary>
    [WorkflowPropertyEditor(WorkflowPropertyEditorKeys.PublicDataSignal)]
    public string SignalKey { get; set; } = string.Empty;

    /// <summary>获取或设置写入模式。</summary>
    public E_SignalStateWriteMode WriteMode { get; set; }

    /// <summary>获取或设置 Set 模式下的固定值或绑定值。</summary>
    public WorkflowInput<bool> State { get; set; } = WorkflowInput<bool>.FromLiteral(true);
    /// <inheritdoc />
    public IEnumerable<WorkflowPublicDataDeclaration> GetPublicDataUses() => PublicDataUses.Signal(SignalKey);
}

/// <summary>执行信号设置节点。</summary>
public sealed class SignalSetNodeHandler : WorkflowNodeHandler<SignalSetNodeModel>
{
    /// <inheritdoc />
    protected override IEnumerable<WorkflowRuntimeCapabilityRequirement> GetRequiredCapabilities(SignalSetNodeModel node) =>
        StandardCapabilities.Signal;

    /// <inheritdoc />
    protected override ValueTask<NodeExecutionResult> ExecuteAsync(
        SignalSetNodeModel node,
        IWorkflowNodeExecutionContext context,
        CancellationToken cancellationToken)
    {
        var service = GetService(context, node.SignalKey);
        var signalKey = node.SignalKey.Trim();
        var state = node.WriteMode == E_SignalStateWriteMode.Toggle
            ? !service.Read(signalKey)
            : context.ResolveInput(node.State);
        service.Write(signalKey, state);
        var output = new SignalSetNodeResult(signalKey, state, node.WriteMode);
        context.Trace("SignalSet", $"设置信号 {signalKey}={state}。", new Dictionary<string, object?>
        {
            ["SignalKey"] = signalKey,
            ["State"] = state,
            ["WriteMode"] = node.WriteMode
        });
        return Polyfill.FromResult(NodeExecutionResult.Continue(output: output));
    }

    internal static IWorkflowSignalService GetService(IWorkflowNodeExecutionContext context, string signalKey)
    {
        if (string.IsNullOrWhiteSpace(signalKey))
            throw new InvalidOperationException("信号节点没有配置 SignalKey。");
        return context.GetRequiredCapability<IWorkflowSignalService>();
    }
}
