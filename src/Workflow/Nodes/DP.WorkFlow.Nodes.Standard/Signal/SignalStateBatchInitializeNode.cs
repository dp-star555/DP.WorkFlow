namespace DP.WorkFlow;

/// <summary>批量初始化布尔信号到默认状态。</summary>
[WorkflowNode("SignalStateBatchInitialize", DisplayName = "批量初始化布尔信号节点", Category = "3.Control/信号")]
public sealed class SignalStateBatchInitializeNodeModel : WorkflowNodeModel
{
    /// <inheritdoc />
    public override string NodeType => "SignalStateBatchInitialize";
    /// <summary>初始化项。</summary>
    public List<SignalStateInitItem> Items { get; set; } = new();
    /// <summary>结果变量键；为空时使用节点结果键。</summary>
    public string ResultVarKey { get; set; } = string.Empty;
}

/// <summary>执行布尔信号批量初始化。</summary>
public sealed class SignalStateBatchInitializeNodeHandler : WorkflowNodeHandler<SignalStateBatchInitializeNodeModel>
{
    /// <inheritdoc />
    protected override ValueTask<NodeExecutionResult> ExecuteAsync(SignalStateBatchInitializeNodeModel node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
    {
        if (node.Items.Count == 0) throw new InvalidOperationException("初始化项为空。");
        var service = SignalSetNodeHandler.GetService(context, node.Items[0].SignalKey);
        foreach (var item in node.Items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(item.SignalKey)) throw new InvalidOperationException("初始化项 SignalKey 为空。");
            service.Write(item.SignalKey.Trim(), item.DefaultState);
        }
        var output = new SignalStateBatchInitializeResult(node.Items.Count, string.Join(",", node.Items.Select(item => item.SignalKey).Distinct(StringComparer.Ordinal)));
        context.SetVariable(string.IsNullOrWhiteSpace(node.ResultVarKey) ? node.Id + ".Result" : node.ResultVarKey.Trim(), output);
        return ValueTask.FromResult(NodeExecutionResult.Continue(output: output));
    }
}
