using System.ComponentModel;

namespace DP.WorkFlow;

/// <summary>处理流程看到的一个停点：哪个流程、停在哪、为什么、能给哪些去向。</summary>
public sealed class WorkflowFaultStopInfo
{
    /// <summary>流程名。</summary>
    [DisplayName("流程")]
    public string FlowName { get; set; } = string.Empty;
    /// <summary>停下的位置（画布路径 / 节点 @ 圈次 [分支]）。</summary>
    [DisplayName("位置")]
    public string Position { get; set; } = string.Empty;
    /// <summary>节点 ID。</summary>
    [DisplayName("节点")]
    public string NodeId { get; set; } = string.Empty;
    /// <summary>节点显示名。</summary>
    [DisplayName("节点标题")]
    public string NodeTitle { get; set; } = string.Empty;
    /// <summary>停下的原因：故障、被中断、被要求停下。</summary>
    [DisplayName("原因")]
    public string Reason { get; set; } = string.Empty;
    /// <summary>故障说明。</summary>
    [DisplayName("说明")]
    public string Message { get; set; } = string.Empty;
    /// <summary>报警编号。</summary>
    [DisplayName("报警编号")]
    public int AlarmCode { get; set; }
    /// <summary>节点停下前报告的最后一步。</summary>
    [DisplayName("做到哪一步")]
    public string Progress { get; set; } = string.Empty;
    /// <summary>能否就地恢复。</summary>
    [DisplayName("可就地恢复")]
    public bool CanRecoverInPlace { get; set; }
    /// <summary>能否结束本圈。</summary>
    [DisplayName("可结束本圈")]
    public bool CanEndLap { get; set; }
    /// <summary>可以回去的恢复入口键。</summary>
    [DisplayName("可回退到的入口")]
    public List<string> Entries { get; set; } = new();

    /// <summary>由停点生成。</summary>
    /// <param name="point">停点。</param>
    public static WorkflowFaultStopInfo From(WorkflowStopPoint point) => new()
    {
        FlowName = point.FlowName,
        Position = point.Position.ToString(),
        NodeId = point.Position.NodeId,
        NodeTitle = point.NodeTitle,
        Reason = point.Reason switch
        {
            WorkflowStopReason.Fault => "故障",
            WorkflowStopReason.Interrupted => "被中断",
            _ => "被要求停下"
        },
        Message = point.Message ?? string.Empty,
        AlarmCode = point.AlarmCode,
        Progress = point.Progress ?? string.Empty,
        CanRecoverInPlace = point.Allows(WorkflowResumeDirective.RecoverInPlace(), out _),
        CanEndLap = point.CanEndLap,
        Entries = point.Entries.Select(entry => entry.Key).ToList()
    };
}

/// <summary>处理流程看到的故障全貌。</summary>
public sealed class WorkflowFaultInfo
{
    /// <summary>故障说明。</summary>
    [DisplayName("说明")]
    public string Message { get; set; } = string.Empty;
    /// <summary>报警编号。</summary>
    [DisplayName("报警编号")]
    public int AlarmCode { get; set; }
    /// <summary>上报方。</summary>
    [DisplayName("上报方")]
    public string Source { get; set; } = string.Empty;
    /// <summary>最先出故障的流程。</summary>
    [DisplayName("出故障的流程")]
    public string OriginFlow { get; set; } = string.Empty;
    /// <summary>处理流程第几次运行。</summary>
    [DisplayName("处理次数")]
    public int Attempt { get; set; }
    /// <summary>上一次处理没有完成的原因。</summary>
    [DisplayName("上次未完成原因")]
    public string LastFailure { get; set; } = string.Empty;
    /// <summary>故障条件是否都已消除。</summary>
    [DisplayName("故障已消除")]
    public bool ConditionsCleared { get; set; }
    /// <summary>受影响的流程。</summary>
    [DisplayName("受影响的流程")]
    public List<string> AffectedFlows { get; set; } = new();
    /// <summary>各流程的停点。</summary>
    [DisplayName("停点")]
    public List<WorkflowFaultStopInfo> Stops { get; set; } = new();
    /// <summary>上报方附带的信息。</summary>
    [DisplayName("附加信息")]
    public Dictionary<string, object?> Payload { get; set; } = new(StringComparer.Ordinal);

    /// <summary>由故障会话生成。</summary>
    /// <param name="session">故障会话。</param>
    public static WorkflowFaultInfo From(WorkflowFaultSession session)
    {
        Guard.NotNull(session);
        var primary = session.PrimaryFault;
        var info = new WorkflowFaultInfo
        {
            Message = primary.Message,
            AlarmCode = primary.AlarmCode,
            Source = primary.Source ?? string.Empty,
            OriginFlow = session.OriginFlow ?? string.Empty,
            Attempt = session.Attempt,
            LastFailure = session.LastFailure ?? string.Empty,
            ConditionsCleared = session.ConditionsCleared,
            AffectedFlows = session.AffectedFlows.ToList(),
            Stops = session.StopPoints.Select(WorkflowFaultStopInfo.From).ToList()
        };
        foreach (var fault in session.Faults)
        foreach (var pair in fault.Payload)
            info.Payload[pair.Key] = pair.Value;
        return info;
    }
}

/// <summary>处理流程节点取得故障会话。</summary>
internal static class WorkflowFaultSessionAccess
{
    internal static WorkflowFaultSession Get(IWorkflowNodeExecutionContext context) =>
        context.Services.GetService(typeof(WorkflowFaultSession)) as WorkflowFaultSession
        ?? throw new InvalidOperationException("该节点只能在故障处理流程（告警处理块或路由表里的处理流程）中使用。");

    internal static WorkflowFaultSession? TryGet(IWorkflowNodeExecutionContext context) =>
        context.Services.GetService(typeof(WorkflowFaultSession)) as WorkflowFaultSession;
}

/// <summary>处理流程入口：读取当前故障的全貌。</summary>
[WorkflowNode("WarningHandlerStart", DisplayName = "告警处理起点", Category = "8.Process/异常恢复", OutputType = typeof(WorkflowFaultInfo))]
public sealed class WarningHandlerStartNodeModel : WorkflowNodeModel
{
    /// <inheritdoc />
    public override string NodeType => "WarningHandlerStart";
}

/// <summary>输出故障全貌。</summary>
public sealed class WarningHandlerStartNodeHandler : WorkflowNodeHandler<WarningHandlerStartNodeModel>
{
    /// <inheritdoc />
    protected override ValueTask<NodeExecutionResult> ExecuteAsync(WarningHandlerStartNodeModel node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var info = WorkflowFaultInfo.From(WorkflowFaultSessionAccess.Get(context));
        context.Trace("读取故障", "处理流程读取当前故障。", new Dictionary<string, object?>
        {
            ["Message"] = info.Message,
            ["Attempt"] = info.Attempt,
            ["Flows"] = string.Join(",", info.Stops.Select(stop => stop.FlowName).Distinct())
        });
        return Polyfill.FromResult(NodeExecutionResult.Continue(output: info));
    }
}

/// <summary>读取一个流程停下的位置（有多个停点时取故障 / 中断的那个）。</summary>
[WorkflowNode("FaultStopPoint", DisplayName = "读取停点", Category = "8.Process/异常恢复", OutputType = typeof(WorkflowFaultStopInfo))]
public sealed class FaultStopPointNodeModel : WorkflowNodeModel
{
    /// <inheritdoc />
    public override string NodeType => "FaultStopPoint";
    /// <summary>流程名；为空时为出故障的流程。</summary>
    [WorkflowProperty("流程", "为空时为出故障的流程。", Category = "故障处理")]
    public string FlowName { get; set; } = string.Empty;
}

/// <summary>执行读取停点。</summary>
public sealed class FaultStopPointNodeHandler : WorkflowNodeHandler<FaultStopPointNodeModel>
{
    /// <inheritdoc />
    protected override ValueTask<NodeExecutionResult> ExecuteAsync(FaultStopPointNodeModel node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var session = WorkflowFaultSessionAccess.Get(context);
        var flow = string.IsNullOrWhiteSpace(node.FlowName)
            ? session.OriginFlow ?? session.StopPoints.Select(point => point.FlowName).FirstOrDefault()
            : node.FlowName.Trim();
        var points = flow is null ? Array.Empty<WorkflowStopPoint>() : session.GetStopPoints(flow);
        var point = points.FirstOrDefault(item => item.Reason != WorkflowStopReason.Requested) ?? points.FirstOrDefault()
            ?? throw new InvalidOperationException($"流程 {flow} 没有停在本次故障处理里。");
        return Polyfill.FromResult(NodeExecutionResult.Continue(output: WorkflowFaultStopInfo.From(point)));
    }
}
