namespace DP.WorkFlow;

/// <summary>提供运控与设备节点的统一注册入口。</summary>
public static class WorkflowMotionNodes
{
    /// <summary>注册当前已经实现的运控节点模型。</summary>
    public static WorkflowNodeCatalog RegisterMotionNodes(this WorkflowNodeCatalog catalog)
    {
        Guard.NotNull(catalog);
        return catalog.RegisterAssembly(typeof(WorkflowMotionNodes).Assembly);
    }

    /// <summary>注册当前已经实现的运控节点处理器。</summary>
    public static WorkflowNodeHandlerCatalog RegisterMotionNodeHandlers(
        this WorkflowNodeHandlerCatalog handlers)
    {
        Guard.NotNull(handlers);
        return handlers.RegisterAssembly(typeof(WorkflowMotionNodes).Assembly);
    }
}

/// <summary>本模块处理器声明的宿主能力要求（R7-a）。</summary>
internal static class MotionCapabilities
{
    internal static readonly WorkflowRuntimeCapabilityRequirement IoCapability =
        WorkflowRuntimeCapabilityRequirement.Require<IWorkflowIoService>("访问宿主 IO 点位。");
    internal static readonly WorkflowRuntimeCapabilityRequirement AxisCapability =
        WorkflowRuntimeCapabilityRequirement.Require<IWorkflowAxisService>("控制并查询宿主运动轴。");
    internal static readonly WorkflowRuntimeCapabilityRequirement PneumaticCapability =
        WorkflowRuntimeCapabilityRequirement.Require<IWorkflowPneumaticService>("控制并查询宿主气动设备。");
    internal static readonly WorkflowRuntimeCapabilityRequirement CodeReaderCapability =
        WorkflowRuntimeCapabilityRequirement.Require<IWorkflowCodeReaderService>("控制并查询宿主读码器。");
}
