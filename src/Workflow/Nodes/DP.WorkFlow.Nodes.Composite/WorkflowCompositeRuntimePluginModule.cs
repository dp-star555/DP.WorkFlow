namespace DP.WorkFlow;

/// <summary>注册全部复合节点及其执行处理器的 Runtime 插件 Module。</summary>
public sealed class WorkflowCompositeRuntimePluginModule : IWorkflowRuntimePluginModule
{
    /// <inheritdoc />
    public string ExtensionId => "Workflow.Nodes.Composite";

    /// <inheritdoc />
    public void Register(WorkflowRuntimePluginCatalog extensions)
    {
        ArgumentNullException.ThrowIfNull(extensions);
        extensions.Nodes.RegisterCompositeNodes();
        extensions.Handlers.RegisterCompositeNodeHandlers();
    }
}
