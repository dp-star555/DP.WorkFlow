namespace DP.WorkFlow;

/// <summary>注册全部标准节点及其执行处理器的 Runtime 插件 Module。</summary>
public sealed class WorkflowStandardRuntimePluginModule : IWorkflowRuntimePluginModule
{
    /// <inheritdoc />
    public string ExtensionId => "Workflow.Nodes.Standard";

    /// <inheritdoc />
    public void Register(WorkflowRuntimePluginCatalog extensions)
    {
        ArgumentNullException.ThrowIfNull(extensions);
        extensions.Nodes.RegisterStandardNodes();
        extensions.Handlers.RegisterStandardNodeHandlers();
    }
}
