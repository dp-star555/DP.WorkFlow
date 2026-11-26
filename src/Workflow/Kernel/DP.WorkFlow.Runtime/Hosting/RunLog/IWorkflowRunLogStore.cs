namespace DP.WorkFlow;

/// <summary>交给存储的一行运行日志。</summary>
/// <param name="FlowName">记在哪个名字下（和运行日志本里的名字一致，例如“上料”“试运行 · 上料”）。</param>
/// <param name="RunId">这一行属于哪次运行（根运行的 ID）。</param>
/// <param name="RunStarted">这次运行的开始时间。</param>
/// <param name="Line">一行文字（不含换行）：前面给人看，后面是回放用的数据；格式归 Workflow，存储原样保存、原样交回。</param>
/// <param name="IsRunStart">是这次运行的第一行（存储可以在这时新开一份）。</param>
/// <param name="RunEnded">这次运行已结束时的结束时间；未结束为空。</param>
/// <param name="RunState">这次运行已结束时的结果；未结束为空。</param>
public sealed record WorkflowRunLogRecord(
    string FlowName,
    Guid RunId,
    DateTimeOffset RunStarted,
    string Line,
    bool IsRunStart,
    DateTimeOffset? RunEnded,
    WorkflowRunLogState? RunState);

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
/// 运行日志的存储：只决定存在哪里（文件、数据库……），不碰内容。
/// </summary>
/// <remarks>
/// 设到 <see cref="WorkflowRunLogBook.Store"/> 后，日志本每收到一条事件就交来一行（<see cref="WorkflowRunLogRecord"/>）。
/// 行的格式归 Workflow：前面和流程出口推送的文字一样，直接能看；后面附回放用的数据。
/// 查历史时把同一次运行的行原样、按原来的顺序交回，Workflow 自己解析并按实时记录的同一套规则重建。
/// </remarks>
public interface IWorkflowRunLogStore
{
    /// <summary>存一行。在记录线程上按顺序调用，要快、线程安全；抛出的异常会被吞掉（记一条 Trace 警告），不影响运行。</summary>
    /// <param name="record">一行。</param>
    void Append(WorkflowRunLogRecord record);

    /// <summary>按条件查有哪些运行（最近的在前）。</summary>
    /// <param name="query">条件。</param>
    /// <param name="cancellationToken">取消。</param>
    Task<IReadOnlyList<WorkflowRunLogHistoryEntry>> FindAsync(WorkflowRunLogHistoryQuery query, CancellationToken cancellationToken = default);

    /// <summary>读回一次运行存的全部行（原样、按存的顺序）。</summary>
    /// <param name="entry"><see cref="FindAsync"/> 返回的一项。</param>
    /// <param name="cancellationToken">取消。</param>
    Task<IReadOnlyList<string>> ReadAsync(WorkflowRunLogHistoryEntry entry, CancellationToken cancellationToken = default);
}
