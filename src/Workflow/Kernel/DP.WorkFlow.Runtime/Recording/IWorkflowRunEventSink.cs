namespace DP.WorkFlow;

/// <summary>
/// 顶层宿主提供的运行事件写入出口；SQL、文件和远程分析系统都由宿主自行适配。
/// </summary>
/// <remarks>
/// Runtime 只负责按序号推送，不决定是否持久化、保存格式、容量和保留期。
/// 实现抛出异常或返回失败都不会改变工作流执行结果，只会更新 <see cref="WorkflowRecordingHealth"/>。
/// </remarks>
public interface IWorkflowRunEventSink
{
    /// <summary>写入一批按序号升序排列的运行事件。</summary>
    /// <param name="events">同一 Run 内按 <see cref="WorkflowRunEvent.Sequence"/> 升序排列的事件。</param>
    /// <param name="requestImmediateFlush">是否由 Durable 事件或 Run 收尾触发，要求实现尽快落盘。</param>
    /// <param name="cancellationToken">Recorder 释放时取消写入的令牌；正常 Run 结束不会取消它。</param>
    /// <returns>Sink 是否确认接收；失败原因用于健康状态报告。</returns>
    ValueTask<WorkflowRunEventWriteResult> WriteAsync(
        IReadOnlyList<WorkflowRunEvent> events,
        bool requestImmediateFlush,
        CancellationToken cancellationToken);

    /// <summary>刷新实现内部尚未落盘的缓冲。</summary>
    /// <param name="cancellationToken">Recorder 释放时取消刷新的令牌。</param>
    ValueTask FlushAsync(CancellationToken cancellationToken);
}
