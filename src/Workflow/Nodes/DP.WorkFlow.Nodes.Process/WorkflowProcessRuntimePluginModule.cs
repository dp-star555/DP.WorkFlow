namespace DP.WorkFlow;

/// <summary>注册全部工艺流程节点及其执行处理器的 Runtime 插件 Module。</summary>
public sealed class WorkflowProcessRuntimePluginModule : IWorkflowRuntimePluginModule
{
    /// <inheritdoc />
    public string ExtensionId => "Workflow.Nodes.Process";

    /// <inheritdoc />
    public void Register(WorkflowRuntimePluginCatalog extensions)
    {
        ArgumentNullException.ThrowIfNull(extensions);
        extensions.Nodes.RegisterProcessNodes();
        extensions.Handlers.RegisterProcessNodeHandlers();
    }
}
