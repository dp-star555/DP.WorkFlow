using DP.Vision.Algorithms;

namespace DP.WorkFlow;

/// <summary>独立几何节点包；无需宿主引用节点类或具体引擎。</summary>
public sealed class WorkflowVisionGeometryModule : IWorkflowRuntimePluginModule
{
    /// <inheritdoc/>
    public string ExtensionId => "workflow.vision.geometry";
    /// <inheritdoc/>
    public void Register(WorkflowRuntimePluginCatalog extensions)
    {
        Guard.NotNull(extensions);
        WorkflowVisionOutputNames.EnsureRegistered();
        extensions.Nodes.RegisterAssembly(typeof(WorkflowVisionGeometryModule).Assembly);
        extensions.Handlers.RegisterAssembly(typeof(WorkflowVisionGeometryModule).Assembly);
    }
}
