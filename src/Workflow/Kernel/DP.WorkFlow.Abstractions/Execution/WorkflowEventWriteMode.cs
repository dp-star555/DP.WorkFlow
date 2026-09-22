namespace DP.WorkFlow;

/// <summary>运行事件的推送优先级。</summary>
/// <remarks>
/// <see cref="FlushRequested"/> 只表示“要求 Recorder 立即封包并请求 Sink Flush”，
/// 不是引擎执行的前置条件，也不承诺同步持久化。
/// 工作流不等待外部持久化确认，因此崩溃持久性取决于顶层 Sink 的实际确认进度。
/// </remarks>
public enum WorkflowEventWriteMode
{
    /// <summary>批量推送；进程突然终止时允许丢失尚未确认的尾部事件。</summary>
    Buffered = 0,

    /// <summary>高优先级并要求 Recorder 立即封包和请求 Flush；不阻塞节点和 Run 结果。</summary>
    FlushRequested = 1
}
