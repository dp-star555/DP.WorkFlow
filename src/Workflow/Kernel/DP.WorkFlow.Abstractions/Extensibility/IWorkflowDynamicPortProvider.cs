namespace DP.WorkFlow;

/// <summary>由配置决定端口集合的节点实现。</summary>
public interface IWorkflowDynamicPortProvider
{
    /// <summary>根据节点实例配置和静态基础端口生成当前有效端口。</summary>
    /// <param name="basePorts">节点描述器注册的静态基础端口；实现不应修改该只读集合。</param>
    /// <returns>当前节点实例可用于设计、连线校验和编译的完整端口集合。</returns>
    IReadOnlyList<WorkflowPortDescriptor> GetPorts(IReadOnlyList<WorkflowPortDescriptor> basePorts);
}

/// <summary>节点描述器动态端口辅助方法。</summary>
public static class WorkflowNodeDescriptorPortExtensions
{
    /// <summary>获取指定节点实例当前生效的端口。</summary>
    /// <param name="descriptor">节点类型的描述器，提供静态基础端口。</param>
    /// <param name="node">要计算端口的节点实例；动态端口实现可读取其当前配置。</param>
    /// <returns>动态节点计算后的端口；普通节点直接返回描述器的静态端口。</returns>
    public static IReadOnlyList<WorkflowPortDescriptor> GetPorts(this WorkflowNodeDescriptor descriptor, IWorkflowNodeModel node) =>
        node is IWorkflowDynamicPortProvider dynamicProvider ? dynamicProvider.GetPorts(descriptor.Ports) : descriptor.Ports;
}
