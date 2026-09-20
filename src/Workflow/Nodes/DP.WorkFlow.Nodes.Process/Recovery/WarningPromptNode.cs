namespace DP.WorkFlow;

/// <summary>显示告警信息并等待操作员确认后继续。</summary>
[WorkflowNode("WarningPrompt", DisplayName = "告警提示", Category = "8.Process/异常恢复")]
public sealed class WarningPromptNodeModel : WorkflowNodeModel
{
    /// <inheritdoc />
    public override string NodeType => "WarningPrompt";
    /// <summary>提示正文；为空时根据中断上下文生成。</summary>
    public string Message { get; set; } = string.Empty;
}

/// <summary>执行告警提示节点。</summary>
public sealed class WarningPromptNodeHandler : WorkflowNodeHandler<WarningPromptNodeModel>
{
    /// <inheritdoc />
    protected override async ValueTask<NodeExecutionResult> ExecuteAsync(WarningPromptNodeModel node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
    {
        var title = ResolveTitle(node.Title, context, "告警提示");
        var message = string.IsNullOrWhiteSpace(node.Message) ? ResolveMessage(context, "发生可恢复告警，请处理。") : node.Message;
        await GetService(context).ConfirmStepAsync(title, message, cancellationToken).ConfigureAwait(false);
        context.Trace("提示已确认", "告警提示已确认。");
        return NodeExecutionResult.Continue(output: true);
    }
    internal static IWorkflowOperatorService GetService(IWorkflowNodeExecutionContext context) =>
        context.GetRequiredCapability<IWorkflowOperatorService>();
    internal static string ResolveTitle(string configuredTitle, IWorkflowNodeExecutionContext context, string fallback)
    {
        context.TryGetVariable<WorkflowInterruptContext>(WorkflowRecoveryRuntimeKeys.InterruptContext, out var interrupt);
        var title = string.IsNullOrWhiteSpace(configuredTitle) ? fallback : configuredTitle;
        return interrupt is not null ? $"[{(string.IsNullOrWhiteSpace(interrupt.WorkflowName) ? "未命名流程" : interrupt.WorkflowName)}] {title}" : title;
    }
    internal static string ResolveMessage(IWorkflowNodeExecutionContext context, string fallback)
    {
        if (!context.TryGetVariable<WorkflowInterruptContext>(WorkflowRecoveryRuntimeKeys.InterruptContext, out var item) || item is null) return fallback;
        var lines = new List<string> { $"来源流程：{(string.IsNullOrWhiteSpace(item.WorkflowName) ? "未命名流程" : item.WorkflowName)}" };
        if (item.AlarmCode != 0) lines.Add($"报警码：{item.AlarmCode}");
        if (!string.IsNullOrWhiteSpace(item.AlarmName)) lines.Add($"报警名称：{item.AlarmName}");
        if (!string.IsNullOrWhiteSpace(item.Category)) lines.Add($"分类：{item.Category}");
        if (!string.IsNullOrWhiteSpace(item.FaultNodeTitle)) lines.Add($"当前节点：{item.FaultNodeTitle}");
        if (!string.IsNullOrWhiteSpace(item.StationId)) lines.Add($"工站：{item.StationId}");
        if (!string.IsNullOrWhiteSpace(item.Message)) lines.Add($"原因：{item.Message}");
        if (!string.IsNullOrWhiteSpace(item.RecoveryGuide)) lines.Add($"建议：{item.RecoveryGuide}");
        if (item.SuggestedChecks.Count > 0) { lines.Add("检查项："); lines.AddRange(item.SuggestedChecks.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => "  - " + value)); }
        return string.Join(Environment.NewLine, lines);
    }
}
