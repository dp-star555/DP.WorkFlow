namespace DP.WorkFlow;

/// <summary>一次故障处理会话的可审计进展。请求被拒绝时原故障仍保留。</summary>
/// <param name="CaseId">故障会话身份。</param>
/// <param name="FaultNodeId">故障节点。</param>
/// <param name="Attempt">处理尝试次数。</param>
/// <param name="Stage">Waiting、Rejected、Applied或Stopped。</param>
/// <param name="Message">处理结果或拒绝原因。</param>
/// <param name="OperationId">原操作身份。</param>
/// <param name="Timestamp">UTC时间。</param>
public sealed record WorkflowRecoveryEvent(Guid CaseId, string FaultNodeId, int Attempt,
    string Stage, string? Message, Guid? OperationId, DateTimeOffset Timestamp);
