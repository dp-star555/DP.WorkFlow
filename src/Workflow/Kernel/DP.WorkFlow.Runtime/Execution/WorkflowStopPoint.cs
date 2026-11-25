namespace DP.WorkFlow;

/// <summary>
/// 流程中的一个位置（第 8 类）：画布路径 / 节点 @ 各层圈次 [并行分支]。
/// 不论在循环、并行还是子画布里，都能说清"第几圈、哪个分支、哪个子画布里的哪个节点"。
/// </summary>
/// <param name="Canvas">从外到内经过的复合节点及圈序号；主画布为空。</param>
/// <param name="NodeId">节点 ID。</param>
/// <param name="LoopIterations">节点所在路径上画布循环的当前圈次（循环节点 ID → 圈次）。</param>
/// <param name="ScopeIds">从外到内的并行作用域编号；不在并行里时为空。</param>
/// <param name="TokenId">所在执行路径（令牌）。</param>
public sealed record WorkflowPosition(
    IReadOnlyList<WorkflowTraceParent> Canvas,
    string NodeId,
    IReadOnlyDictionary<string, int> LoopIterations,
    IReadOnlyList<long> ScopeIds,
    long TokenId)
{
    /// <summary>画布路径：主画布为 <c>$</c>，子画布如 <c>$/Block</c>。</summary>
    public string CanvasPath => Canvas.Count == 0 ? "$" : "$/" + string.Join("/", Canvas.Select(parent => parent.NodeId));

    /// <inheritdoc />
    public override string ToString()
    {
        var text = new System.Text.StringBuilder("$");
        foreach (var parent in Canvas)
        {
            text.Append('/').Append(parent.NodeId);
            if (parent.Cycle > 0)
                text.Append("@第").Append(parent.Cycle).Append("圈");
        }
        text.Append('/').Append(NodeId);
        if (LoopIterations.Count > 0)
            text.Append(" @ ").Append(string.Join(",", LoopIterations.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => $"{pair.Key}第{pair.Value}圈")));
        if (ScopeIds.Count > 0)
            text.Append(" [分支 ").Append(TokenId).Append(']');
        return text.ToString();
    }
}

/// <summary>一个可以回去的恢复入口：本圈、当前调用路径上已经经过的锚点。</summary>
/// <param name="Key">入口键。</param>
/// <param name="Position">入口节点的位置。</param>
public sealed record WorkflowReachableEntry(string Key, WorkflowPosition Position);

/// <summary>
/// 一个停点：流程停在这里等待去向。节点故障、被中断，或在两个节点之间被要求停下时产生。
/// 引擎不判断设备是否安全、是否允许继续，只保证位置合法、数据一致；去向由故障会话决定。
/// </summary>
public sealed record WorkflowStopPoint
{
    /// <summary>停点身份。</summary>
    public required Guid Id { get; init; }

    /// <summary>流程名称。</summary>
    public required string FlowName { get; init; }

    /// <summary>停下的位置。</summary>
    public required WorkflowPosition Position { get; init; }

    /// <summary>节点显示名。</summary>
    public required string NodeTitle { get; init; }

    /// <summary>节点类型。</summary>
    public required string NodeType { get; init; }

    /// <summary>停下的原因。</summary>
    public required WorkflowStopReason Reason { get; init; }

    /// <summary>故障或中断说明；被要求停下时为原因说明。</summary>
    public string? Message { get; init; }

    /// <summary>报警编号；没有时为 0。</summary>
    public int AlarmCode { get; init; }

    /// <summary>节点的恢复类别。</summary>
    public WorkflowRecoveryCategory Category { get; init; }

    /// <summary>节点停下前报告的最后一步。</summary>
    public string? Progress { get; init; }

    /// <summary>节点是否实现了「核实并补完」（可以"继续原操作"）。</summary>
    public bool CanContinueOperation { get; init; }

    /// <summary>所在路径上是否有可以结束本圈的循环（画布循环或循环块）。</summary>
    public bool CanEndLap { get; init; }

    /// <summary>可以回去的恢复入口。</summary>
    public IReadOnlyList<WorkflowReachableEntry> Entries { get; init; } = Array.Empty<WorkflowReachableEntry>();

    /// <summary>上一次给出的去向被拒绝的原因；第一次询问时为空。</summary>
    public string? Rejection { get; init; }

    /// <summary>该去向对这个停点是否允许；不允许时给出原因。</summary>
    /// <param name="directive">去向。</param>
    /// <param name="reason">不允许的原因。</param>
    public bool Allows(WorkflowResumeDirective directive, out string? reason)
    {
        Guard.NotNull(directive);
        reason = directive.Kind switch
        {
            WorkflowResumeKind.Continue when Reason != WorkflowStopReason.Requested && !CanContinueOperation =>
                $"节点 {NodeTitle} 没有实现核实并补完，不能继续原操作。",
            WorkflowResumeKind.Redo when Reason != WorkflowStopReason.Requested && Category != WorkflowRecoveryCategory.Redoable =>
                $"节点 {NodeTitle} 是\"{WorkflowRecoveryCategories.DisplayName(Category)}\"，不能直接重做。",
            WorkflowResumeKind.EnterAt when !Entries.Any(entry => string.Equals(entry.Key, directive.EntryKey, StringComparison.Ordinal)) =>
                $"恢复入口 {directive.EntryKey} 不在本圈、当前路径上已经经过的入口里。",
            WorkflowResumeKind.EndLap when !CanEndLap => "所在路径上没有可以结束本圈的循环。",
            _ => null
        };
        return reason is null;
    }
}

/// <summary>去向的种类（第 8 类：处理流程的去向节点）。</summary>
public enum WorkflowResumeKind
{
    /// <summary>继续原操作：故障 / 中断的节点调用核实并补完；被要求停下的节点照常执行。</summary>
    Continue = 0,

    /// <summary>重做本节点：重新执行一次（仅"可直接重做"的节点）。</summary>
    Redo = 1,

    /// <summary>回到入口 X：回到本圈、当前路径上已经经过的恢复入口，之后的结果作废。</summary>
    EnterAt = 2,

    /// <summary>结束本圈：放弃当前这一圈，最内层循环进入下一圈。</summary>
    EndLap = 3,

    /// <summary>停止流程：终止本次运行。</summary>
    Stop = 4
}

/// <summary>给一个停点的去向。</summary>
/// <param name="Kind">去向种类。</param>
/// <param name="EntryKey">回到入口时的入口键。</param>
/// <param name="Message">说明，记入运行记录。</param>
public sealed record WorkflowResumeDirective(WorkflowResumeKind Kind, string? EntryKey = null, string? Message = null)
{
    /// <summary>继续原操作。</summary>
    public static WorkflowResumeDirective Continue(string? message = null) => new(WorkflowResumeKind.Continue, Message: message);

    /// <summary>重做本节点。</summary>
    public static WorkflowResumeDirective Redo(string? message = null) => new(WorkflowResumeKind.Redo, Message: message);

    /// <summary>回到入口。</summary>
    /// <param name="entryKey">入口键。</param>
    /// <param name="message">说明。</param>
    public static WorkflowResumeDirective EnterAt(string entryKey, string? message = null)
    {
        Guard.NotNullOrWhiteSpace(entryKey);
        return new(WorkflowResumeKind.EnterAt, entryKey.Trim(), message);
    }

    /// <summary>结束本圈。</summary>
    public static WorkflowResumeDirective EndLap(string? message = null) => new(WorkflowResumeKind.EndLap, Message: message);

    /// <summary>停止流程。</summary>
    public static WorkflowResumeDirective Stop(string? message = null) => new(WorkflowResumeKind.Stop, Message: message);

    /// <summary>显示用名称。</summary>
    public string DisplayName => Kind switch
    {
        WorkflowResumeKind.Continue => "继续原操作",
        WorkflowResumeKind.Redo => "重做本节点",
        WorkflowResumeKind.EnterAt => $"回到入口 {EntryKey}",
        WorkflowResumeKind.EndLap => "结束本圈",
        _ => "停止流程"
    };
}

/// <summary>
/// 接住停点的一方（统管层的故障会话）。引擎在停点上等待它给出去向；
/// 给出的去向不被允许时，引擎带着拒绝原因再问一次。等待期间流程的其他分支在节点之间停住。
/// </summary>
public interface IWorkflowStopPointHandler
{
    /// <summary>流程停在一个停点上，等待去向。</summary>
    /// <param name="engine">停下的引擎（根运行）。</param>
    /// <param name="stopPoint">停点。</param>
    /// <param name="cancellationToken">运行取消令牌；取消时停止等待。</param>
    /// <returns>去向。</returns>
    Task<WorkflowResumeDirective> OnStoppedAsync(WorkflowEngine engine, WorkflowStopPoint stopPoint, CancellationToken cancellationToken);
}
