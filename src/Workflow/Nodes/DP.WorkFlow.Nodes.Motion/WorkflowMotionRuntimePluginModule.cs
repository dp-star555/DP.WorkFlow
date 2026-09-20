namespace DP.WorkFlow;

/// <summary>注册全部运控节点及其执行处理器的 Runtime 插件 Module。</summary>
public sealed class WorkflowMotionRuntimePluginModule : IWorkflowRuntimePluginModule
{
    /// <inheritdoc />
    public string ExtensionId => "Workflow.Nodes.Motion";

    /// <inheritdoc />
    public void Register(WorkflowRuntimePluginCatalog extensions)
    {
        ArgumentNullException.ThrowIfNull(extensions);
        extensions.Nodes.RegisterMotionNodes();
        extensions.Handlers.RegisterMotionNodeHandlers();
    }
}
