namespace DP.WorkFlow;

/// <summary>
/// 控制单次工作流运行的安全边界。
/// </summary>
public sealed class WorkflowExecutionOptions
{
    /// <summary>获取或设置单次运行允许执行的节点总次数。</summary>
    public int MaxTotalNodeExecutions { get; set; } = 100_000;

    /// <summary>获取或设置同一节点在单次运行中允许执行的最大次数。</summary>
    public int MaxNodeExecutions { get; set; } = 10_000;

    /// <summary>获取或设置单次运行允许自动恢复的最大次数。</summary>
    public int MaxRecoveryAttempts { get; set; } = 100;

    /// <summary>当前引擎恢复策略；为空时使用运行上下文注册的策略。普通Block继承，处置子流程显式选择受限策略。</summary>
    public IWorkflowFaultRecoveryCoordinator? RecoveryCoordinator { get; set; }

    /// <summary>当前引擎所属的故障会话；用于处置步骤关联原始故障，不产生递归会话。</summary>
    public Guid? RecoveryCaseId { get; set; }

    /// <summary>当前引擎的联合准备屏障；处置子引擎不得继承此屏障。</summary>
    public IWorkflowRecoveryBarrier? RecoveryBarrier { get; set; }

    /// <summary>获取或设置内存中保留的最近运行事件条数；它同时是 UI 最近 Trace 窗口的上限。</summary>
    public int MaxTraceEntries { get; set; } = 2_000;

    /// <summary>获取或设置运行事件记录的有界容量、批量策略和顶层 Sink。</summary>
    public WorkflowRunRecordingOptions Recording { get; set; } = new();

    /// <summary>验证所有安全上限可用于启动引擎。</summary>
    /// <exception cref="ArgumentOutOfRangeException">执行次数、Trace 容量或记录容量非正，或恢复次数为负。</exception>
    internal void Validate()
    {
        ArgumentNullException.ThrowIfNull(Recording);
        Recording.Validate();
        if (MaxTotalNodeExecutions <= 0)
            throw new ArgumentOutOfRangeException(nameof(MaxTotalNodeExecutions));
        if (MaxNodeExecutions <= 0)
            throw new ArgumentOutOfRangeException(nameof(MaxNodeExecutions));
        if (MaxRecoveryAttempts < 0)
            throw new ArgumentOutOfRangeException(nameof(MaxRecoveryAttempts));
        if (MaxTraceEntries <= 0)
            throw new ArgumentOutOfRangeException(nameof(MaxTraceEntries));
    }
}
