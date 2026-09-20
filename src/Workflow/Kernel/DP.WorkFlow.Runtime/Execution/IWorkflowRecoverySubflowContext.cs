namespace DP.WorkFlow;

/// <summary>由引擎提供的受监管异常处理子流程入口；独立于主流程的Recovery Hold，但遵守其他暂停/阻断。</summary>
public interface IWorkflowRecoverySubflowContext
{
    /// <summary>完成能力预检与准备后执行处理子流程，并纳入父引擎监控和取消。</summary>
    /// <param name="plan">已绑定的处理子计划。</param>
    /// <param name="context">隔离的处理数据上下文。</param>
    /// <param name="options">子运行上限；恢复处理应禁用递归故障处理。</param>
    /// <param name="cancellationToken">本次处理取消令牌。</param>
    /// <returns>处理子流程的实际运行结果。</returns>
    Task<WorkflowRunResult> RunRecoverySubflowAsync(WorkflowBoundExecutionPlan plan,
        WorkflowContext context, WorkflowExecutionOptions options, CancellationToken cancellationToken);
}
