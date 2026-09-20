namespace DP.WorkFlow;

/// <summary>批量初始化强类型数据信号到默认值。</summary>
[WorkflowNode("SignalValueBatchInitialize", DisplayName = "批量初始化数据信号节点", Category = "3.Control/信号")]
public sealed class SignalValueBatchInitializeNodeModel : WorkflowNodeModel
{
    /// <inheritdoc />
    public override string NodeType => "SignalValueBatchInitialize";
    /// <summary>初始化项。</summary>
    public List<SignalValueInitItem> Items { get; set; } = new();
    /// <summary>结果变量键；为空时使用节点结果键。</summary>
    public string ResultVarKey { get; set; } = string.Empty;
}

/// <summary>执行数据信号批量初始化。</summary>
public sealed class SignalValueBatchInitializeNodeHandler : WorkflowNodeHandler<SignalValueBatchInitializeNodeModel>
{
    /// <inheritdoc />
    protected override ValueTask<NodeExecutionResult> ExecuteAsync(SignalValueBatchInitializeNodeModel node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
    {
        if (node.Items.Count == 0) throw new InvalidOperationException("初始化项为空。");
        var service = SignalValueSetNodeHandler.GetService(context, node.Items[0].SignalKey);
        foreach (var item in node.Items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(item.SignalKey)) throw new InvalidOperationException("初始化项 SignalKey 为空。");
            service.WriteValue(item.SignalKey.Trim(), SignalValueSetNodeHandler.ConvertValue(item.DefaultValueText, item.ValueType));
        }
        var output = new SignalValueBatchInitializeResult(node.Items.Count, string.Join(",", node.Items.Select(item => item.SignalKey).Distinct(StringComparer.Ordinal)));
        context.SetVariable(string.IsNullOrWhiteSpace(node.ResultVarKey) ? node.Id + ".Result" : node.ResultVarKey.Trim(), output);
        return ValueTask.FromResult(NodeExecutionResult.Continue(output: output));
    }
}
