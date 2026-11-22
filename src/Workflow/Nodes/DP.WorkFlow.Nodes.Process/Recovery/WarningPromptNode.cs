namespace DP.WorkFlow;

/// <summary>显示告警信息并等待操作员确认后继续。</summary>
[WorkflowNode("WarningPrompt", DisplayName = "告警提示", Category = "8.Process/异常恢复", OutputType = typeof(bool))]
public sealed class WarningPromptNodeModel : WorkflowNodeModel
{
    /// <inheritdoc />
    public override string NodeType => "WarningPrompt";
    /// <summary>提示正文；为空时根据当前故障生成。</summary>
    public string Message { get; set; } = string.Empty;
}

/// <summary>执行告警提示节点。</summary>
public sealed class WarningPromptNodeHandler : WorkflowNodeHandler<WarningPromptNodeModel>
{
    /// <inheritdoc />
    protected override IEnumerable<WorkflowRuntimeCapabilityRequirement> GetRequiredCapabilities(WarningPromptNodeModel node) =>
        new[] { ProcessCapabilities.OperatorCapability };

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
        var title = string.IsNullOrWhiteSpace(configuredTitle) ? fallback : configuredTitle;
        var session = WorkflowFaultSessionAccess.TryGet(context);
        var flow = session?.OriginFlow ?? session?.StopPoints.Select(point => point.FlowName).FirstOrDefault();
        return flow is not null ? $"[{flow}] {title}" : title;
    }
    internal static string ResolveMessage(IWorkflowNodeExecutionContext context, string fallback)
    {
        if (WorkflowFaultSessionAccess.TryGet(context) is not { } session) return fallback;
        var fault = session.PrimaryFault;
        var lines = new List<string>();
        if (!string.IsNullOrWhiteSpace(fault.Source)) lines.Add($"上报方：{fault.Source}");
        if (fault.AlarmCode != 0) lines.Add($"报警码：{fault.AlarmCode}");
        lines.Add($"原因：{fault.Message}");
        foreach (var point in session.StopPoints)
        {
            var line = $"{point.FlowName} 停在 {point.NodeTitle}（{point.Position}）";
            if (!string.IsNullOrWhiteSpace(point.Progress)) line += $"，做到：{point.Progress}";
            lines.Add(line);
        }
        if (session.Attempt > 1 && !string.IsNullOrWhiteSpace(session.LastFailure)) lines.Add($"上次处理未完成：{session.LastFailure}");
        return string.Join(Environment.NewLine, lines);
    }
}
