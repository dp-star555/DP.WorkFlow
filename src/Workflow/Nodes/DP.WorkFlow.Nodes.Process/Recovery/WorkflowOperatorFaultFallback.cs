namespace DP.WorkFlow;

/// <summary>
/// 用人工交互服务实现的兜底界面（第 8 类）：处理流程出错、没有给出去向或去向不被允许时，
/// 把原因和各流程停点告诉操作员，让他选重新运行处理流程（需再确认）、对全部停下的流程直接给同一个去向，或停止全部流程。
/// 回退再继续要逐个流程选入口，请用故障工作台。
/// WinForms / WPF 宿主把自己的人工交互服务传进来即可，不用另写窗口。
/// </summary>
public sealed class WorkflowOperatorFaultFallback : IWorkflowFaultFallback
{
    private const string Rerun = "Rerun";
    private const string Stop = "Stop";
    private readonly IWorkflowOperatorService _operators;

    /// <summary>创建兜底界面。</summary>
    /// <param name="operators">人工交互服务。</param>
    public WorkflowOperatorFaultFallback(IWorkflowOperatorService operators) =>
        _operators = operators ?? throw new ArgumentNullException(nameof(operators));

    /// <inheritdoc />
    public async Task<WorkflowFaultFallbackDecision> DecideAsync(WorkflowFaultFallbackRequest request, CancellationToken cancellationToken)
    {
        Guard.NotNull(request);
        var stops = request.Session.StopPoints.ToArray();
        var flows = stops.Select(stop => stop.FlowName).Distinct(StringComparer.Ordinal).ToArray();
        var faulted = stops.Where(stop => stop.Reason != WorkflowStopReason.Requested).ToArray();
        var targets = faulted.Length > 0 ? faulted : stops;
        var directives = new List<(string Key, string Text, WorkflowResumeDirective Directive)>
        {
            ("RecoverInPlace", "全部就地恢复", WorkflowResumeDirective.RecoverInPlace("兜底：就地恢复")),
            ("EndLap", "全部结束本圈", WorkflowResumeDirective.EndLap("兜底：结束本圈"))
        };
        var options = new List<OperatorChoiceOption>();
        if (request.HasHandler)
            options.Add(new OperatorChoiceOption(Rerun, "重新运行处理流程"));
        var allowed = directives.Where(item => targets.All(stop => stop.Allows(item.Directive, out _))).ToArray();
        options.AddRange(allowed.Select(item => new OperatorChoiceOption(item.Key, item.Text)));
        options.Add(new OperatorChoiceOption(Stop, "停止全部流程"));
        var message = $"故障：{request.Session.PrimaryFault.Message}\n没有处理完的原因：{request.Failure}\n\n停下的流程：\n"
                      + string.Join("\n", stops.Select(Describe));
        while (true)
        {
            var choice = await _operators.AskChoiceAsync("故障处理没有完成", message, options, cancellationToken).ConfigureAwait(false);
            if (choice == Rerun)
            {
                var confirm = await _operators.AskChoiceAsync("重新运行处理流程",
                    "处理流程会从头再跑一遍。已经回退过的动作不会再回退；请确认现场可以重新处理。",
                    new[] { new OperatorChoiceOption("Yes", "确定重新运行"), new OperatorChoiceOption("No", "返回") },
                    cancellationToken).ConfigureAwait(false);
                if (confirm == "Yes")
                    return WorkflowFaultFallbackDecision.Rerun;
                continue;
            }
            var index = Array.FindIndex(allowed, item => item.Key == choice);
            if (index >= 0)
                return new WorkflowFaultFallbackDecision(WorkflowFaultFallbackChoice.UseDirectives,
                    flows.ToDictionary(flow => flow, _ => allowed[index].Directive, StringComparer.Ordinal));
            return WorkflowFaultFallbackDecision.StopAll;
        }
    }

    private static string Describe(WorkflowStopPoint stop) =>
        $"· {stop.FlowName}：{stop.NodeTitle}（{stop.Position}）"
        + (stop.Reason == WorkflowStopReason.Requested ? " 被要求停下" : $" {stop.Message}")
        + (string.IsNullOrWhiteSpace(stop.Progress) ? string.Empty : $"，做到：{stop.Progress}");
}
