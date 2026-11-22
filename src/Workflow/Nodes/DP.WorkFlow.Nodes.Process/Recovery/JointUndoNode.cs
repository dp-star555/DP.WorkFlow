namespace DP.WorkFlow;

/// <summary>
/// 联合逆序撤回（处理流程里用）：把一个或多个流程从现在撤回到指定节点之后，
/// 按全部流程真实执行的倒序撤回动作（例如先抬起 Z 再松爪）。
/// 声明了撤回动作的节点自动撤回；可直接重做的节点跳过；其余节点弹出确认，由人处理好后继续。
/// 已经撤回过的动作不会再撤回（重新运行处理流程时）。
/// </summary>
[WorkflowNode("JointUndo", DisplayName = "联合逆序撤回", Category = "8.Process/异常恢复", OutputType = typeof(string))]
public sealed class JointUndoNodeModel : WorkflowNodeModel
{
    /// <inheritdoc />
    public override string NodeType => "JointUndo";

    /// <summary>撤回目标，每行（或用分号隔开）一项。</summary>
    [WorkflowProperty("撤回目标",
        "每行一项，写成“流程=节点ID”：把该流程撤回到这个节点之后（节点本身不撤回）。" +
        "只写节点ID时为出故障的流程；只写“流程=”时撤回该流程记录里的全部动作；整个为空时撤回出故障流程的全部动作。",
        Category = "故障处理")]
    public string TargetsText { get; set; } = string.Empty;

    /// <summary>解析撤回目标。</summary>
    /// <param name="originFlow">出故障的流程。</param>
    public IReadOnlyList<WorkflowUndoTarget> ParseTargets(string? originFlow)
    {
        var lines = (TargetsText ?? string.Empty)
            .Split(new[] { '\r', '\n', ';', '；' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .ToArray();
        if (lines.Length == 0)
            return originFlow is null ? Array.Empty<WorkflowUndoTarget>() : new[] { new WorkflowUndoTarget(originFlow, null) };
        return lines.Select(line =>
        {
            var split = line.IndexOf('=');
            if (split < 0)
                return new WorkflowUndoTarget(originFlow ?? throw new InvalidOperationException($"撤回目标“{line}”没有写流程，而本次故障没有出故障的流程。"), line);
            var flow = line.Substring(0, split).Trim();
            var node = line.Substring(split + 1).Trim();
            if (flow.Length == 0)
                flow = originFlow ?? throw new InvalidOperationException($"撤回目标“{line}”没有写流程，而本次故障没有出故障的流程。");
            return new WorkflowUndoTarget(flow, node.Length == 0 ? null : node);
        }).ToArray();
    }
}

/// <summary>执行联合逆序撤回。</summary>
public sealed class JointUndoNodeHandler : WorkflowNodeHandler<JointUndoNodeModel>
{
    /// <inheritdoc />
    protected override async ValueTask<NodeExecutionResult> ExecuteAsync(JointUndoNodeModel node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var session = WorkflowFaultSessionAccess.Get(context);
        var targets = node.ParseTargets(session.OriginFlow);
        if (targets.Count == 0)
            throw new InvalidOperationException("没有撤回目标：请在节点里写明要撤回的流程。");
        var operators = context.Services.GetService(typeof(IWorkflowOperatorService)) as IWorkflowOperatorService;
        var title = string.IsNullOrWhiteSpace(node.Title) ? "联合逆序撤回" : node.Title;
        Func<WorkflowExecutionRecordEntry, CancellationToken, Task<bool>>? confirm = operators is null
            ? null
            : async (entry, token) =>
            {
                await operators.ConfirmStepAsync($"[{entry.FlowName}] {title}",
                    $"“{entry.NodeTitle}”（{entry.Position}）没有自动撤回动作，请人工处理好后确认。", token).ConfigureAwait(false);
                return true;
            };
        var report = await session.Center.ExecutionRecord.UndoAsync(targets, confirm, cancellationToken).ConfigureAwait(false);
        var summary = report.Steps.Count == 0
            ? "没有需要撤回的动作。"
            : string.Join("；", report.Steps.Select(step => $"{step.Entry.FlowName} {step.Entry.NodeTitle}：{Describe(step.Kind)}"));
        context.Trace("联合逆序撤回", summary, new Dictionary<string, object?>
        {
            ["Targets"] = string.Join(",", targets.Select(target => $"{target.FlowName}={target.BackToNodeId}")),
            ["Steps"] = report.Steps.Count,
            ["Failure"] = report.Failure
        });
        if (!report.Success)
            return NodeExecutionResult.Fail(report.Failure!);
        return NodeExecutionResult.Continue(output: summary);
    }

    private static string Describe(WorkflowUndoStepKind kind) => kind switch
    {
        WorkflowUndoStepKind.Undone => "已撤回",
        WorkflowUndoStepKind.Skipped => "可重做，跳过",
        _ => "人工处理"
    };
}
