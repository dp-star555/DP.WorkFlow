namespace DP.WorkFlow;

/// <summary>提供运行准备阶段可检查的全部根计划和子计划节点配置快照。</summary>
/// <param name="Nodes">按根计划优先的稳定顺序递归展开的节点配置。</param>
public sealed record WorkflowRunPreparationContext(IReadOnlyList<IWorkflowNodeModel> Nodes);

/// <summary>由根运行宿主在每次新运行真正开始前调用，用于释放上一轮资源或准备运行级服务。</summary>
public interface IWorkflowRunPreparationService
{
    /// <summary>在新建引擎开始执行首节点前准备运行级资源和全部计划节点绑定。</summary>
    /// <param name="context">本次已编译计划的递归节点快照。</param>
    /// <param name="cancellationToken">宿主取消本次运行时触发的令牌。</param>
    /// <returns>资源释放、验证和绑定准备完成时结束的异步操作。</returns>
    ValueTask PrepareAsync(
        WorkflowRunPreparationContext context,
        CancellationToken cancellationToken);
}
