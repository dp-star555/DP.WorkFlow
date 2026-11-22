namespace DP.WorkFlow;

/// <summary>
/// 控制单次工作流运行的安全边界。
/// </summary>
public sealed class WorkflowExecutionOptions
{
    /// <summary>
    /// 接住停点的一方（第 8 类：统管层的故障会话）。节点故障、被中断或被要求停下时，流程停在停点上等它给出去向；
    /// 为空时节点故障直接结束运行，要求停下被忽略。
    /// </summary>
    public IWorkflowStopPointHandler? StopPointHandler { get; set; }

    /// <summary>获取或设置内存中保留的最近运行事件条数；它同时是 UI 最近 Trace 窗口的上限。</summary>
    public int MaxTraceEntries { get; set; } = 2_000;

    /// <summary>获取或设置运行事件记录的有界容量、批量策略和顶层 Sink。</summary>
    public WorkflowRunRecordingOptions Recording { get; set; } = new();

    /// <summary>
    /// 获取或设置节点提示板（R1-01）。同一选项创建的每次运行及其子运行共用，提示跨运行保留直到确认清除。
    /// </summary>
    public WorkflowNoticeBoard Notices { get; set; } = new();

    /// <summary>验证所有安全上限可用于启动引擎。</summary>
    /// <exception cref="ArgumentOutOfRangeException">Trace 容量或记录容量非正。</exception>
    internal void Validate()
    {
        Guard.NotNull(Recording);
        Recording.Validate();
        if (MaxTraceEntries <= 0)
            throw new ArgumentOutOfRangeException(nameof(MaxTraceEntries));
    }
}
