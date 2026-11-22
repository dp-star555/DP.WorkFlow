namespace DP.WorkFlow;

/// <summary>提供工艺流程、产品流和异常恢复节点的统一注册入口。</summary>
public static class WorkflowProcessNodes
{
    /// <summary>注册当前已经实现的工艺流程节点模型。</summary>
    public static WorkflowNodeCatalog RegisterProcessNodes(this WorkflowNodeCatalog catalog)
    {
        Guard.NotNull(catalog);
        return catalog.RegisterAssembly(typeof(WorkflowProcessNodes).Assembly);
    }

    /// <summary>注册当前已经实现的工艺流程节点处理器。</summary>
    public static WorkflowNodeHandlerCatalog RegisterProcessNodeHandlers(
        this WorkflowNodeHandlerCatalog handlers)
    {
        Guard.NotNull(handlers);
        return handlers.RegisterAssembly(typeof(WorkflowProcessNodes).Assembly);
    }
}

/// <summary>本模块处理器声明的宿主能力要求（R7-a）。</summary>
internal static class ProcessCapabilities
{
    internal static readonly WorkflowRuntimeCapabilityRequirement OperatorCapability =
        WorkflowRuntimeCapabilityRequirement.Require<IWorkflowOperatorService>("显示操作员提示并接收人工选择。");
    internal static readonly WorkflowRuntimeCapabilityRequirement ProcessCapability =
        WorkflowRuntimeCapabilityRequirement.Require<IWorkflowProcessService>("创建工艺产品记录。");
    internal static readonly WorkflowRuntimeCapabilityRequirement ProductFlowCapability =
        WorkflowRuntimeCapabilityRequirement.Require<IWorkflowProductFlowService>("执行产品流转和工站状态操作。");
    internal static readonly WorkflowRuntimeCapabilityRequirement WaferRobotCapability =
        WorkflowRuntimeCapabilityRequirement.Require<IWorkflowWaferRobotService>("控制并查询晶圆机器人。");
}
