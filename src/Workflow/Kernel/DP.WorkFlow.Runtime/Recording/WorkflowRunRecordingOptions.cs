namespace DP.WorkFlow;

/// <summary>
/// 控制单次 Run 事件记录的有界容量、批量推送和 Payload 编码上限。
/// </summary>
/// <remarks>
/// 记录链路采用 fail-open：这里的任何上限都只影响记录本身，
/// 不会阻塞节点调度，也不会改变 Run 终态。
/// </remarks>
public sealed class WorkflowRunRecordingOptions
{
    /// <summary>获取或设置顶层事件 Sink；为空时只在内存中保留最近事件窗口。</summary>
    public IWorkflowRunEventSink? Sink { get; set; }

    /// <summary>获取或设置待推送队列容量；达到上限时淘汰最老的待推送事件。</summary>
    public int PendingQueueCapacity { get; set; } = 4_096;

    /// <summary>获取或设置单批推送的最大事件条数。</summary>
    public int BufferedBatchSize { get; set; } = 128;

    /// <summary>获取或设置 Buffered 事件的最长等待时间。</summary>
    public TimeSpan BufferedFlushInterval { get; set; } = TimeSpan.FromMilliseconds(100);

    /// <summary>获取或设置 Run 收尾时等待后台推送和 Flush 的上限。</summary>
    public TimeSpan CompletionFlushTimeout { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>获取或设置记录健康状态通知宿主的最小间隔。</summary>
    public TimeSpan HealthNotificationInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>获取或设置单条事件 Payload 的字节预算；超出时按编码顺序截断。</summary>
    public int MaxEventBytes { get; set; } = 16_384;

    /// <summary>获取或设置单个字符串值的最大保留长度。</summary>
    public int MaxStringLength { get; set; } = 1_024;

    /// <summary>获取或设置集合值在摘要中展开的最大项数。</summary>
    public int MaxCollectionItems { get; set; } = 32;

    /// <summary>获取或设置单个输出 DTO 自动提取的最大公开属性个数；超出部分截断并记录降级诊断。</summary>
    public int MaxOutputProperties { get; set; } = 32;

    /// <summary>验证所有上限可用于启动记录。</summary>
    /// <exception cref="ArgumentOutOfRangeException">容量、批量或长度上限非正，或等待时间为负。</exception>
    internal void Validate()
    {
        if (PendingQueueCapacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(PendingQueueCapacity));
        if (BufferedBatchSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(BufferedBatchSize));
        if (BufferedFlushInterval < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(BufferedFlushInterval));
        if (CompletionFlushTimeout < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(CompletionFlushTimeout));
        if (HealthNotificationInterval < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(HealthNotificationInterval));
        if (MaxEventBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(MaxEventBytes));
        if (MaxStringLength <= 0)
            throw new ArgumentOutOfRangeException(nameof(MaxStringLength));
        if (MaxCollectionItems <= 0)
            throw new ArgumentOutOfRangeException(nameof(MaxCollectionItems));
        if (MaxOutputProperties <= 0)
            throw new ArgumentOutOfRangeException(nameof(MaxOutputProperties));
    }
}

/// <summary>记录链路的健康状态。</summary>
public enum E_WorkflowRecordingHealth
{
    /// <summary>尚未发生记录失败。</summary>
    Healthy = 0,

    /// <summary>曾经发生失败，但最近一次写入已经成功。</summary>
    Degraded = 1,

    /// <summary>最近一次写入失败；宿主应检查 Sink 实现和外部存储。</summary>
    Failed = 2
}

/// <summary>
/// 表示记录链路的可观察健康状态；它只描述记录本身，不表示工作流执行异常。
/// </summary>
/// <param name="State">当前健康状态。</param>
/// <param name="RecordedCount">Recorder 已接受的事件总数。</param>
/// <param name="DroppedCount">因内存窗口或待推送队列容量上限淘汰的最老事件累计数。</param>
/// <param name="FailedWriteCount">外部 Sink 写入或刷新失败的累计次数。</param>
/// <param name="LastConfirmedSequence">外部 Sink 已确认的最大事件序号；无 Sink 时为零。</param>
/// <param name="LastError">最近一次 Sink 失败原因。</param>
/// <param name="LastFailureAt">最近一次 Sink 失败的 UTC 时间。</param>
/// <param name="DiagnosticCount">自动元数据或输出摘要降级的累计次数；与 Sink 失败分开计数。</param>
/// <param name="LastDiagnostic">最近一次降级诊断说明。</param>
/// <param name="LastDiagnosticAt">最近一次降级诊断的 UTC 时间。</param>
/// <remarks>
/// 降级诊断（<paramref name="DiagnosticCount"/>）表示记录内容不完整，例如无法自动识别输入槽或输出属性 getter 失败；
/// 它不代表 Sink 写入失败，因此与 <paramref name="FailedWriteCount"/> 分开统计。
/// </remarks>
public sealed record WorkflowRecordingHealth(
    E_WorkflowRecordingHealth State,
    long RecordedCount,
    long DroppedCount,
    long FailedWriteCount,
    long LastConfirmedSequence,
    string? LastError,
    DateTimeOffset? LastFailureAt,
    long DiagnosticCount = 0,
    string? LastDiagnostic = null,
    DateTimeOffset? LastDiagnosticAt = null)
{
    /// <summary>获取表示尚未发生任何失败的共享健康状态。</summary>
    public static WorkflowRecordingHealth Initial { get; } = new(
        E_WorkflowRecordingHealth.Healthy, 0, 0, 0, 0, null, null);
}
