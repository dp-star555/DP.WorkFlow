namespace DP.WorkFlow;

/// <summary>提供复合节点注册入口：节点和处理器从本程序集自动发现（R7-a）。</summary>
public static class WorkflowCompositeNodes
{
    /// <summary>注册 Block、循环块等复合节点模型。</summary>
    /// <param name="catalog">节点目录。</param>
    /// <returns>目标目录。</returns>
    public static WorkflowNodeCatalog RegisterCompositeNodes(this WorkflowNodeCatalog catalog)
    {
        Guard.NotNull(catalog);
        return catalog.RegisterAssembly(typeof(WorkflowCompositeNodes).Assembly);
    }

    /// <summary>注册复合节点处理器。</summary>
    /// <param name="handlers">处理器目录。</param>
    /// <returns>目标目录。</returns>
    public static WorkflowNodeHandlerCatalog RegisterCompositeNodeHandlers(
        this WorkflowNodeHandlerCatalog handlers)
    {
        Guard.NotNull(handlers);
        return handlers.RegisterAssembly(typeof(WorkflowCompositeNodes).Assembly);
    }
}
