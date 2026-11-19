namespace DP.WorkFlow;

/// <summary>
/// 提供标准节点模型和处理器的统一注册入口：节点和处理器从本程序集自动发现（R7-a），
/// 端口、输出类型写在节点模型的特性上，能力要求写在各处理器里。
/// </summary>
public static class WorkflowStandardNodes
{
    /// <summary>向节点目录注册全部标准节点模型。</summary>
    /// <param name="catalog">节点目录。</param>
    /// <returns>目标目录，便于链式组合。</returns>
    public static WorkflowNodeCatalog RegisterStandardNodes(this WorkflowNodeCatalog catalog)
    {
        Guard.NotNull(catalog);
        return catalog.RegisterAssembly(typeof(WorkflowStandardNodes).Assembly);
    }

    /// <summary>向处理器目录注册全部标准节点处理器。</summary>
    /// <param name="handlers">目标处理器目录。</param>
    /// <returns>目标目录，便于链式组合。</returns>
    public static WorkflowNodeHandlerCatalog RegisterStandardNodeHandlers(
        this WorkflowNodeHandlerCatalog handlers)
    {
        Guard.NotNull(handlers);
        return handlers.RegisterAssembly(typeof(WorkflowStandardNodes).Assembly);
    }
}

/// <summary>标准节点处理器声明的宿主能力要求。</summary>
internal static class StandardCapabilities
{
    internal static readonly WorkflowRuntimeCapabilityRequirement[] ActionRegistry =
        { WorkflowRuntimeCapabilityRequirement.Require<IWorkflowActionRegistry>("执行宿主动作函数。") };
    internal static readonly WorkflowRuntimeCapabilityRequirement[] ConditionRegistry =
        { WorkflowRuntimeCapabilityRequirement.Require<IWorkflowConditionRegistry>("执行宿主条件函数。") };
    internal static readonly WorkflowRuntimeCapabilityRequirement[] Signal =
        { WorkflowRuntimeCapabilityRequirement.Require<IWorkflowSignalService>("读写和等待布尔流程信号。") };
    internal static readonly WorkflowRuntimeCapabilityRequirement[] ValueSignal =
        { WorkflowRuntimeCapabilityRequirement.Require<IWorkflowValueSignalService>("读写和等待强类型流程信号。") };
    internal static readonly WorkflowRuntimeCapabilityRequirement[] Queue =
        { WorkflowRuntimeCapabilityRequirement.Require<IWorkflowQueueService>("访问流程队列。") };
    internal static readonly WorkflowRuntimeCapabilityRequirement[] None = Array.Empty<WorkflowRuntimeCapabilityRequirement>();
}
