namespace DP.WorkFlow;

/// <summary>查历史运行的条件。</summary>
/// <param name="From">开始时间不早于它。</param>
/// <param name="To">开始时间早于它。</param>
/// <param name="FlowName">只查这个流程；为空时查全部。</param>
/// <param name="MaxResults">最多返回多少条（最近的在前）。</param>
public sealed record WorkflowRunLogHistoryQuery(DateTimeOffset From, DateTimeOffset To, string? FlowName = null, int MaxResults = 500);

/// <summary>历史里的一次运行（列表用）。</summary>
/// <param name="FlowName">记在哪个名字下（和运行日志本里的名字一致，例如“上料”“试运行 · 上料”）。</param>
/// <param name="RunId">运行 ID。</param>
/// <param name="Started">开始时间。</param>
/// <param name="Ended">结束时间；不知道时为空。</param>
/// <param name="State">结果；不知道时为空。</param>
/// <param name="Key">存储自己用的定位信息（例如文件路径），原样交回 <see cref="IWorkflowRunLogHistory.LoadAsync"/>。</param>
public sealed record WorkflowRunLogHistoryEntry(
    string FlowName,
    Guid RunId,
    DateTimeOffset Started,
    DateTimeOffset? Ended = null,
    WorkflowRunLogState? State = null,
    string? Key = null);

/// <summary>
/// 运行日志的历史来源：运行日志页只通过它读历史，怎么存、存在哪由外部决定。
/// </summary>
/// <remarks>
/// 外部保存的是原始运行事件（挂在流程出口或运行宿主的 Sink 上，和运行日志本收到的相同）；
/// 读回来的事件交给 <see cref="WorkflowRunLogBook.Replay"/>，按和实时记录同一套规则重建，显示与当前查看的一致。
/// 可以用 <see cref="WorkflowRunEventJson"/> 一行一条地存，也可以用自己的格式。
/// </remarks>
public interface IWorkflowRunLogHistory
{
    /// <summary>按条件查有哪些运行（最近的在前）。</summary>
    /// <param name="query">条件。</param>
    /// <param name="cancellationToken">取消。</param>
    Task<IReadOnlyList<WorkflowRunLogHistoryEntry>> FindAsync(WorkflowRunLogHistoryQuery query, CancellationToken cancellationToken = default);

    /// <summary>读一次运行的全部事件（按收到的顺序）。</summary>
    /// <param name="entry"><see cref="FindAsync"/> 返回的一项。</param>
    /// <param name="cancellationToken">取消。</param>
    Task<IReadOnlyList<WorkflowRunEvent>> LoadAsync(WorkflowRunLogHistoryEntry entry, CancellationToken cancellationToken = default);
}
