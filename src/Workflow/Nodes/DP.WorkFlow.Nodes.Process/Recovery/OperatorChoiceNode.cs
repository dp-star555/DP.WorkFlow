namespace DP.WorkFlow;

/// <summary>弹出人工多选一确认框并按选项 Key 路由。</summary>
[WorkflowNode("OperatorChoice", DisplayName = "人工选择", Category = "8.Process/异常恢复")]
public sealed class OperatorChoiceNodeModel : WorkflowNodeModel, IWorkflowDynamicPortProvider
{
    /// <inheritdoc />
    public override string NodeType => "OperatorChoice";
    /// <summary>提示内容；为空时从中断上下文生成。</summary>
    public string Message { get; set; } = string.Empty;
    /// <summary>选项配置，格式“显示文本=Key”，多项以逗号或分号分隔。</summary>
    public string OptionsText { get; set; } = "继续=Continue,停止=Stop";
    /// <summary>解析当前配置对应的动态端口。</summary>
    public IReadOnlyList<WorkflowPortDescriptor> GetPorts(IReadOnlyList<WorkflowPortDescriptor> basePorts) =>
        new[] { WorkflowPortDescriptor.Input(maxConnections: int.MaxValue) }
            .Concat(ParseOptions().Select(option => WorkflowPortDescriptor.Output(option.Key)))
            .ToArray();
    /// <summary>解析配置选项。</summary>
    public IReadOnlyList<OperatorChoiceOption> ParseOptions() => (OptionsText ?? string.Empty)
        .Split(new[] { ',', '，', ';', '；', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
        .Select(entry => entry.Trim()).Where(entry => entry.Length > 0)
        .Select(entry => { var index = entry.IndexOf('='); return index > 0 && index < entry.Length - 1 ? new OperatorChoiceOption(entry[(index + 1)..].Trim(), entry[..index].Trim()) : new OperatorChoiceOption(entry, entry); })
        .Where(option => !string.IsNullOrWhiteSpace(option.Key))
        .GroupBy(option => option.Key, StringComparer.OrdinalIgnoreCase).Select(group => group.First()).ToArray();
}

/// <summary>执行人工选择节点。</summary>
public sealed class OperatorChoiceNodeHandler : WorkflowNodeHandler<OperatorChoiceNodeModel>
{
    /// <inheritdoc />
    protected override async ValueTask<NodeExecutionResult> ExecuteAsync(OperatorChoiceNodeModel node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
    {
        var options = node.ParseOptions();
        if (options.Count < 2) throw new InvalidOperationException("人工选择节点至少需要两个有效选项。");
        var service = WarningPromptNodeHandler.GetService(context);
        var title = WarningPromptNodeHandler.ResolveTitle(node.Title, context, "人工确认");
        var message = string.IsNullOrWhiteSpace(node.Message) ? WarningPromptNodeHandler.ResolveMessage(context, "请选择下一步操作。") : node.Message;
        var choice = await service.AskChoiceAsync(title, message, options, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        var selected = options.FirstOrDefault(option => string.Equals(option.Key, choice, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("操作员服务返回了未配置的选项 Key: " + choice);
        // 选择只路由到工程师配置的分支，不按按钮名称暗中生成恢复裁决。
        return NodeExecutionResult.Continue(selected.Key, selected.Key);
    }
}
