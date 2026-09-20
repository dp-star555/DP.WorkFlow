namespace DP.WorkFlow;

/// <summary>
/// 表示一次节点运行跟踪记录。
/// </summary>
/// <param name="Sequence">当前运行内单调递增的跟踪序号。</param>
/// <param name="Timestamp">记录产生的 UTC 时间。</param>
/// <param name="NodeId">产生记录的节点实例 ID。</param>
/// <param name="NodeType">产生记录的稳定节点类型键。</param>
/// <param name="Step">供筛选和聚合使用的稳定步骤键。</param>
/// <param name="Message">面向使用者的可选说明。</param>
/// <param name="Data">节点附加的可选结构化诊断数据。</param>
/// <param name="TokenId">产生记录的执行 Token；零用于缺少执行身份的兼容记录。</param>
/// <param name="ScopeIds">产生记录时从外到内的并行作用域 ID。</param>
public sealed record WorkflowTraceEntry(
    long Sequence,
    DateTimeOffset Timestamp,
    string NodeId,
    string NodeType,
    string Step,
    string? Message,
    IReadOnlyDictionary<string, object?>? Data,
    long TokenId = 0,
    IReadOnlyList<long>? ScopeIds = null);
