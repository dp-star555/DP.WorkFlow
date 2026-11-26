namespace DP.WorkFlow;

/// <summary>交给存储的一条运行日志（一条运行事件）。</summary>
/// <param name="FlowName">记在哪个名字下（和运行日志本里的名字一致，例如“上料”“试运行 · 上料”）。</param>
/// <param name="RunId">属于哪次运行（根运行的 ID）。</param>
/// <param name="RunStarted">这次运行的开始时间。</param>
/// <param name="Time">这条事件的时间。</param>
/// <param name="Data">回放用的数据（一行文字，不含换行）：Workflow 写、Workflow 解读，存储原样保存、原样交回，不要改。</param>
/// <param name="IsRunStart">是这次运行的第一条（存储可以在这时新开一份、记下有这次运行）。</param>
/// <param name="EndState">这条让这次运行结束时为结果（存储可以在这时记下结束时间 <paramref name="Time"/> 和结果）；其余为空。</param>
public sealed record WorkflowRunLogRecord(
    string FlowName,
    Guid RunId,
    DateTimeOffset RunStarted,
    DateTimeOffset Time,
    string Data,
    bool IsRunStart,
    WorkflowRunLogState? EndState);

/// <summary>查历史运行的条件。</summary>
/// <param name="From">开始时间不早于它。</param>
/// <param name="To">开始时间早于它。</param>
/// <param name="FlowName">只查这个流程；为空时查全部。</param>
/// <param name="MaxResults">最多返回多少条（最近的在前）。</param>
public sealed record WorkflowRunLogHistoryQuery(DateTimeOffset From, DateTimeOffset To, string? FlowName = null, int MaxResults = 500);

/// <summary>历史里的一次运行（列表用）。</summary>
/// <param name="FlowName">记在哪个名字下。</param>
/// <param name="RunId">运行 ID。</param>
/// <param name="Started">开始时间。</param>
/// <param name="Ended">结束时间；不知道时为空。</param>
/// <param name="State">结果；不知道时为空。</param>
/// <param name="Key">存储自己用的定位信息（例如文件路径），原样交回 <see cref="IWorkflowRunLogStore.ReadAsync"/>。</param>
public sealed record WorkflowRunLogHistoryEntry(
    string FlowName,
    Guid RunId,
    DateTimeOffset Started,
    DateTimeOffset? Ended = null,
    WorkflowRunLogState? State = null,
    string? Key = null);

/// <summary>
/// 运行日志的存储：读和写在一起，存在哪里（数据库、文本文件……）由实现决定，内容不碰。
/// </summary>
/// <remarks>
/// 运行日志只为回放和查历史，和给人看的 Trace（流程出口推送的文字）是两回事，Trace 照旧由宿主的日志系统处理。
/// 设到 <see cref="WorkflowRunLogBook.Store"/> 后，日志本每收到一条事件就交来一条（<see cref="WorkflowRunLogRecord"/>）；
/// 查历史时把同一次运行的 <see cref="WorkflowRunLogRecord.Data"/> 原样、按原来的顺序交回，Workflow 解读并按实时记录的同一套规则重建。
/// 不设就只记在内存里，运行日志页也没有“历史”。
/// </remarks>
public interface IWorkflowRunLogStore
{
    /// <summary>存一条。在记录线程上按顺序调用，要快、线程安全（放进缓冲、批量落盘即可）；抛出的异常会被吞掉（记一条 Trace 警告），不影响运行。</summary>
    /// <param name="record">一条。</param>
    void Append(WorkflowRunLogRecord record);

    /// <summary>按条件查有哪些运行（最近的在前）。</summary>
    /// <param name="query">条件。</param>
    /// <param name="cancellationToken">取消。</param>
    Task<IReadOnlyList<WorkflowRunLogHistoryEntry>> FindAsync(WorkflowRunLogHistoryQuery query, CancellationToken cancellationToken = default);

    /// <summary>读回一次运行存的全部 <see cref="WorkflowRunLogRecord.Data"/>（原样、按存的顺序）。</summary>
    /// <param name="entry"><see cref="FindAsync"/> 返回的一项。</param>
    /// <param name="cancellationToken">取消。</param>
    Task<IReadOnlyList<string>> ReadAsync(WorkflowRunLogHistoryEntry entry, CancellationToken cancellationToken = default);
}
