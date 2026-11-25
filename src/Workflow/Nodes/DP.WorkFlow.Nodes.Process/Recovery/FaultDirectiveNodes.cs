namespace DP.WorkFlow;

/// <summary>工艺设计者标出的恢复入口（锚点）："回退再继续"只能回到这些节点。不是设备位置快照。</summary>
[WorkflowNode("RecoveryEntry", DisplayName = "恢复入口", Category = "8.Process/异常恢复", OutputType = typeof(string))]
public sealed class RecoveryEntryNodeModel : WorkflowNodeModel, IWorkflowRecoveryEntryNode, IWorkflowNodeConfigurationValidator
{
    /// <inheritdoc />
    public override string NodeType => "RecoveryEntry";
    /// <summary>工程师配置的、文档内唯一的入口键。</summary>
    [WorkflowProperty("入口键", "文档内唯一名称；只能回到本圈、当前路径上已经经过的入口。", Category = "恢复配置")]
    public string EntryKey { get; set; } = string.Empty;
    /// <inheritdoc />
    public string RecoveryEntryKey => (EntryKey ?? string.Empty).Trim();
    /// <inheritdoc />
    public IReadOnlyList<string> ValidateConfiguration() => string.IsNullOrWhiteSpace(EntryKey)
        ? new[] { "恢复入口键不能为空。" } : Array.Empty<string>();
}

/// <summary>经过入口时由引擎记下位置；节点本身只输出入口键。</summary>
public sealed class RecoveryEntryNodeHandler : WorkflowSideEffectFreeNodeHandler<RecoveryEntryNodeModel>
{
    /// <inheritdoc />
    protected override ValueTask<NodeExecutionResult> ExecuteAsync(RecoveryEntryNodeModel node,
        IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(node.EntryKey)) throw new InvalidOperationException("恢复入口键不能为空。");
        return Polyfill.FromResult(NodeExecutionResult.Continue(output: node.RecoveryEntryKey));
    }
}

/// <summary>去向节点的共同配置：给哪个流程、说明。</summary>
public abstract class FaultDirectiveNodeModel : WorkflowNodeModel
{
    /// <summary>流程名；为空时为出故障的流程。</summary>
    [WorkflowProperty("流程", "给哪个流程的去向；为空时为出故障的流程。", Category = "故障处理")]
    public string FlowName { get; set; } = string.Empty;

    /// <summary>说明，记入运行记录。</summary>
    [WorkflowProperty("说明", "记入运行记录。", Category = "故障处理")]
    public string Note { get; set; } = string.Empty;
}

/// <summary>
/// 去向节点的执行：把去向交给故障会话。去向不被允许时节点出错，处理流程因此进入兜底。
/// 处理流程结束后由统管层核对全部去向并一起放行。
/// </summary>
public abstract class FaultDirectiveNodeHandler<TModel> : WorkflowNodeHandler<TModel>
    where TModel : FaultDirectiveNodeModel
{
    /// <summary>节点给出的去向。</summary>
    /// <param name="node">节点配置。</param>
    /// <param name="note">说明；没有时为空。</param>
    protected abstract WorkflowResumeDirective CreateDirective(TModel node, string? note);

    /// <inheritdoc />
    protected sealed override ValueTask<NodeExecutionResult> ExecuteAsync(TModel node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var session = WorkflowFaultSessionAccess.Get(context);
        var directive = CreateDirective(node, string.IsNullOrWhiteSpace(node.Note) ? null : node.Note.Trim());
        var flow = string.IsNullOrWhiteSpace(node.FlowName) ? null : node.FlowName.Trim();
        session.SetDirective(flow, directive);
        context.Trace("给出去向", $"{flow ?? "出故障的流程"}：{directive.DisplayName}", new Dictionary<string, object?>
        {
            ["Flow"] = flow,
            ["Directive"] = directive.DisplayName
        });
        return Polyfill.FromResult(NodeExecutionResult.Continue(output: directive.DisplayName));
    }
}

/// <summary>去向：就地恢复（故障 / 中断的节点调用自己的就地恢复，由节点判断补完还是重做；被要求停下的节点照常执行）。</summary>
[WorkflowNode("WarnRecoverInPlace", DisplayName = "就地恢复", Category = "8.Process/异常恢复", OutputType = typeof(string))]
public sealed class RecoverInPlaceNodeModel : FaultDirectiveNodeModel
{
    /// <inheritdoc />
    public override string NodeType => "WarnRecoverInPlace";
}

/// <summary>执行"就地恢复"。</summary>
public sealed class RecoverInPlaceNodeHandler : FaultDirectiveNodeHandler<RecoverInPlaceNodeModel>
{
    /// <inheritdoc />
    protected override WorkflowResumeDirective CreateDirective(RecoverInPlaceNodeModel node, string? note) =>
        WorkflowResumeDirective.RecoverInPlace(note);
}

/// <summary>去向：结束本圈（放弃当前这一圈，最内层循环进入下一圈）。</summary>
[WorkflowNode("WarnEndLap", DisplayName = "结束本圈", Category = "8.Process/异常恢复", OutputType = typeof(string))]
public sealed class EndLapNodeModel : FaultDirectiveNodeModel
{
    /// <inheritdoc />
    public override string NodeType => "WarnEndLap";
}

/// <summary>执行"结束本圈"。</summary>
public sealed class EndLapNodeHandler : FaultDirectiveNodeHandler<EndLapNodeModel>
{
    /// <inheritdoc />
    protected override WorkflowResumeDirective CreateDirective(EndLapNodeModel node, string? note) =>
        WorkflowResumeDirective.EndLap(note);
}

/// <summary>去向：停止流程（终止该流程本次运行）。</summary>
[WorkflowNode("WarnStopStation", DisplayName = "停止流程", Category = "8.Process/异常恢复", OutputType = typeof(string))]
public sealed class StopCurrentStationNodeModel : FaultDirectiveNodeModel
{
    /// <inheritdoc />
    public override string NodeType => "WarnStopStation";
    /// <summary>停止原因。</summary>
    [WorkflowProperty("原因", "为空时使用默认说明。", Category = "故障处理")]
    public string Reason { get; set; } = string.Empty;
}

/// <summary>执行"停止流程"。</summary>
public sealed class StopCurrentStationNodeHandler : FaultDirectiveNodeHandler<StopCurrentStationNodeModel>
{
    /// <inheritdoc />
    protected override WorkflowResumeDirective CreateDirective(StopCurrentStationNodeModel node, string? note) =>
        WorkflowResumeDirective.Stop(string.IsNullOrWhiteSpace(node.Reason) ? note ?? "处理流程要求停止流程。" : node.Reason.Trim());
}
