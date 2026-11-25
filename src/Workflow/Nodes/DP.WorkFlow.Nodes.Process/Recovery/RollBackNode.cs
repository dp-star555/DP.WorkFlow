namespace DP.WorkFlow;

/// <summary>
/// 去向：回退再继续（处理流程里用）。把一个或多个流程从现在逆序回退到恢复入口（入口本身不回退），
/// 全部目标按真实执行的倒序一起回退（例如先抬起 Z 再松爪），然后这些流程回到入口重新执行。
/// 支持回退的节点自动回退；不支持的节点弹出确认，由人处理好后继续。已经回退过的动作不会再回退（重新运行处理流程时）。
/// </summary>
[WorkflowNode("WarnRollBack", DisplayName = "回退再继续", Category = "8.Process/异常恢复", OutputType = typeof(string))]
public sealed class RollBackNodeModel : WorkflowNodeModel, IWorkflowNodeConfigurationValidator
{
    /// <inheritdoc />
    public override string NodeType => "WarnRollBack";

    /// <summary>回退目标，每行（或用分号隔开）一项。</summary>
    [WorkflowProperty("回退目标",
        "每行一项，写成“流程=入口键”：把该流程回退到这个恢复入口再继续。只写入口键时为出故障的流程。" +
        "多个流程写在一起时按真实执行的倒序一起回退。",
        Category = "故障处理")]
    public string TargetsText { get; set; } = string.Empty;

    /// <summary>说明，记入运行记录。</summary>
    [WorkflowProperty("说明", "记入运行记录。", Category = "故障处理")]
    public string Note { get; set; } = string.Empty;

    /// <summary>解析回退目标。</summary>
    public IReadOnlyList<WorkflowRollBackTarget> ParseTargets() =>
        (TargetsText ?? string.Empty)
            .Split(new[] { '\r', '\n', ';', '；' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .Select(line =>
            {
                var split = line.IndexOf('=');
                if (split < 0)
                    return new WorkflowRollBackTarget(null, line);
                var flow = line.Substring(0, split).Trim();
                var entry = line.Substring(split + 1).Trim();
                if (entry.Length == 0)
                    throw new InvalidOperationException($"回退目标“{line}”没有写入口键。");
                return new WorkflowRollBackTarget(flow.Length == 0 ? null : flow, entry);
            })
            .ToArray();

    /// <inheritdoc />
    public IReadOnlyList<string> ValidateConfiguration() => string.IsNullOrWhiteSpace(TargetsText)
        ? new[] { "回退目标不能为空。" } : Array.Empty<string>();
}

/// <summary>执行回退再继续：先逆序回退，回退完成后给出去向。回退没完成时节点失败，处理流程因此进入兜底。</summary>
public sealed class RollBackNodeHandler : WorkflowNodeHandler<RollBackNodeModel>
{
    /// <inheritdoc />
    protected override async ValueTask<NodeExecutionResult> ExecuteAsync(RollBackNodeModel node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var session = WorkflowFaultSessionAccess.Get(context);
        var targets = node.ParseTargets();
        if (targets.Count == 0)
            throw new InvalidOperationException("没有回退目标：请在节点里写明回到哪个恢复入口。");
        var operators = context.Services.GetService(typeof(IWorkflowOperatorService)) as IWorkflowOperatorService;
        var title = string.IsNullOrWhiteSpace(node.Title) ? "回退再继续" : node.Title;
        Func<WorkflowExecutionRecordEntry, CancellationToken, Task<bool>>? confirm = operators is null
            ? null
            : async (entry, token) =>
            {
                await operators.ConfirmStepAsync($"[{entry.FlowName}] {title}",
                    $"“{entry.NodeTitle}”（{entry.Position}）不支持回退，请人工处理好后确认。", token).ConfigureAwait(false);
                return true;
            };
        var note = string.IsNullOrWhiteSpace(node.Note) ? null : node.Note.Trim();
        var report = await session.RollBackAsync(targets, confirm, note, cancellationToken).ConfigureAwait(false);
        var summary = report.Steps.Count == 0
            ? "没有需要回退的动作。"
            : string.Join("；", report.Steps.Select(step =>
                $"{step.Entry.FlowName} {step.Entry.NodeTitle}：{(step.Kind == WorkflowUndoStepKind.Undone ? "已回退" : "人工处理")}"));
        context.Trace("回退再继续", summary, new Dictionary<string, object?>
        {
            ["Targets"] = string.Join(",", targets.Select(target => $"{target.FlowName}={target.EntryKey}")),
            ["Steps"] = report.Steps.Count,
            ["Failure"] = report.Failure
        });
        return report.Success ? NodeExecutionResult.Continue(output: summary) : NodeExecutionResult.Fail(report.Failure!);
    }
}
