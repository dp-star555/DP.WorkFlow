namespace DP.WorkFlow;

/// <summary>
/// 表示一次完整工作流运行的最终结果。
/// </summary>
/// <param name="Success">仅当流程正常到达终点时为 <see langword="true"/>。</param>
/// <param name="State">运行结束时的 Completed、Faulted 或 Canceled 状态。</param>
/// <param name="Elapsed">从引擎启动到终态的耗时。</param>
/// <param name="Message">完成说明、取消说明或最终故障消息。</param>
/// <param name="FailedNodeId">故障或取消时最后定位到的节点 ID。</param>
public sealed record WorkflowRunResult(
    bool Success,
    E_WorkflowExecutionState State,
    TimeSpan Elapsed,
    string? Message = null,
    string? FailedNodeId = null);
