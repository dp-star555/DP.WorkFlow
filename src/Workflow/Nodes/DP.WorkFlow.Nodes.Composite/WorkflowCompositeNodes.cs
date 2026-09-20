namespace DP.WorkFlow;

/// <summary>提供复合节点注册入口。</summary>
public static class WorkflowCompositeNodes
{
    /// <summary>注册 Block 等复合节点模型。</summary>
    public static WorkflowNodeCatalog RegisterCompositeNodes(this WorkflowNodeCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        return catalog.Register(WorkflowNodeDescriptor.Create<BlockNodeModel, BlockNodeOutput>(
            ports: new[]
            {
                WorkflowPortDescriptor.Input(maxConnections: int.MaxValue),
                WorkflowPortDescriptor.Output()
            }));
    }

    /// <summary>注册复合节点处理器。</summary>
    public static WorkflowNodeHandlerCatalog RegisterCompositeNodeHandlers(
        this WorkflowNodeHandlerCatalog handlers)
    {
        ArgumentNullException.ThrowIfNull(handlers);
        return handlers.Register(new BlockNodeHandler());
    }
}
