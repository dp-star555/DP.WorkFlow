namespace DP.WorkFlow;

/// <summary>
/// 单个 Run 的事件记录深模块：分配序号、编码 Payload、维护最近窗口并向顶层 Sink 推送。
/// </summary>
/// <remarks>
/// Engine 只提交事件草稿和推送优先级，不了解宿主最终选择 SQL、文件、远程系统还是不持久化。
/// 所有方法都不得阻塞节点执行，也不得把外部写入失败传播为工作流故障。
/// </remarks>
public interface IWorkflowRunRecorder : IAsyncDisposable
{
    /// <summary>提交一条事件草稿并立即返回，不等待外部 Sink。</summary>
    /// <param name="event">未分配序号和未编码 Payload 的事件草稿。</param>
    /// <param name="writeMode">推送优先级；FlushRequested 会触发立即封包和调度，但不阻塞调用方。</param>
    /// <returns>分配后的序号以及是否请求了立即推送。</returns>
    WorkflowRunEventReceipt Record(
        WorkflowRunEventDraft @event,
        WorkflowEventWriteMode writeMode = WorkflowEventWriteMode.Buffered);

    /// <summary>读取内存中保留的最近事件窗口。</summary>
    /// <param name="afterSequence">只返回序号大于该值的事件；零表示整个窗口。</param>
    /// <returns>按序号升序排列的事件批次。</returns>
    WorkflowRunEventBatch GetRecent(long afterSequence = 0);

    /// <summary>获取当前记录健康状态。</summary>
    WorkflowRecordingHealth Health { get; }

    /// <summary>
    /// 报告一次"记录内容不完整"的降级诊断，例如无法自动识别输入槽或输出属性 getter 失败。
    /// </summary>
    /// <param name="message">面向诊断的说明。</param>
    /// <remarks>
    /// 本方法不得抛出异常，也不得改变工作流结果；它只累计独立的诊断计数、
    /// 把健康状态从 Healthy 降为 Degraded，并按 <see cref="WorkflowRunRecordingOptions.HealthNotificationInterval"/> 节流通知宿主。
    /// </remarks>
    void ReportDegraded(string message);

    /// <summary>记录健康状态变化时发生；宿主据此暴露记录链路故障。</summary>
    event Action<WorkflowRecordingHealth>? HealthChanged;

    /// <summary>Run 结束时尽力推送并刷新剩余事件，然后停止后台推送。</summary>
    /// <param name="completion">已经确定的 Run 终态；Flush 失败不会改写它。</param>
    /// <param name="cancellationToken">保留参数；实现按自身上限完成收尾。</param>
    ValueTask CompleteAsync(WorkflowRunCompletion completion, CancellationToken cancellationToken = default);
}
