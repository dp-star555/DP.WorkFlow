using DP.Vision;
using DP.Vision.Acquisition;
using DP.Vision.Algorithms;

namespace DP.WorkFlow;

/// <summary>只贡献独立DP.Vision新版节点的宿主入口，不将已取消的OCR/切字/打印质量放回工具箱。</summary>
public sealed class WorkflowImageRuntimePluginModule : IWorkflowRuntimePluginModule
{
    /// <inheritdoc/>
    public string ExtensionId => "Workflow.Nodes.Vision.ImageSource";
    /// <inheritdoc/>
    public void Register(WorkflowRuntimePluginCatalog extensions)
    {
        Guard.NotNull(extensions);
        extensions.Nodes.RegisterImageNodes(); extensions.Handlers.RegisterImageNodeHandlers();
    }
}

/// <summary>新版节点及能力要求的统一注册入口。</summary>
public static class WorkflowImageNodes
{
    /// <summary>注册视觉节点及强类型标准输出。</summary>
    /// <param name="catalog">宿主目录。</param>
    /// <returns>目录。</returns>
    public static WorkflowNodeCatalog RegisterImageNodes(this WorkflowNodeCatalog catalog)
    {
        Guard.NotNull(catalog);
        WorkflowVisionOutputNames.EnsureRegistered();
        return catalog.RegisterAssembly(typeof(WorkflowImageNodes).Assembly);
    }

    /// <summary>注册处理器；能力要求写在各处理器里（R7-a）。</summary>
    /// <param name="handlers">宿主处理器目录。</param>
    /// <returns>目录。</returns>
    public static WorkflowNodeHandlerCatalog RegisterImageNodeHandlers(this WorkflowNodeHandlerCatalog handlers)
    {
        Guard.NotNull(handlers);
        return handlers.RegisterAssembly(typeof(WorkflowImageNodes).Assembly);
    }
}
