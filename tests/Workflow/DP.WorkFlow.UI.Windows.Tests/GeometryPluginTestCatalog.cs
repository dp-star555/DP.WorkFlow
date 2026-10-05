using DP.Plugins;
using DP.Vision;
using DP.Vision.Algorithms;

namespace DP.WorkFlow.Tests;

/// <summary>按宿主方式从插件目录加载几何节点包（含“定义/构建本帧坐标系”），与图像节点组成同一目录。</summary>
internal static class GeometryPluginTestCatalog
{
    /// <summary>复制已投放的几何插件到独立目录并加载。</summary>
    /// <param name="root">本次运行目录，插件包复制到其下。</param>
    /// <param name="configure">冻结前追加注册。</param>
    /// <returns>节点与处理器目录。</returns>
    internal static (WorkflowNodeCatalog Nodes, WorkflowNodeHandlerCatalog Handlers) Create(string root,
        Action<WorkflowNodeCatalog, WorkflowNodeHandlerCatalog>? configure = null)
    {
        var nodes = new WorkflowNodeCatalog(); var handlers = new WorkflowNodeHandlerCatalog();
        var package = Path.Combine(root, "workflow.vision.geometry"); Directory.CreateDirectory(package);
        foreach (var file in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "plugins", "workflow.vision.geometry")))
            File.Copy(file, Path.Combine(package, Path.GetFileName(file)));
        var load = new PluginLoadSession(); _ = new VisionAlgorithmModuleLoader(load);
        load.RegisterSharedAssembly(typeof(IWorkflowVisionAlgorithmNode).Assembly);
        var loader = new WorkflowPluginLoader(load);
        var composition = new WorkflowRuntimePluginCatalog(nodes, handlers).Register(new WorkflowImageRuntimePluginModule());
        Assert.Equal(1, composition.LoadPlugins(root, loader)); Assert.Empty(loader.DiscoveryFailures);
        configure?.Invoke(nodes, handlers); composition.Freeze();
        return (nodes, handlers);
    }

    /// <summary>构建节点自带的坐标系定义。</summary>
    internal static VisionCoordinateDefinition Definition(IWorkflowNodeModel build) => ((IWorkflowVisionCoordinateProducerNode)build).GetCoordinateDefinition();

    /// <summary>用目录工厂创建“构建本帧坐标系”节点，模板方式绑定模板匹配结果。</summary>
    internal static AnalyzeVisionFrameNodeModel BuildFromTemplate(WorkflowNodeCatalog nodes, string id, string frameId, string coordinateId, string templateNodeId)
    {
        var node = (AnalyzeVisionFrameNodeModel)nodes.GetOrThrow("Vision.BuildCoordinateSystem").Factory(); node.Id = id;
        node.Frame = WorkflowInput<ImageFrame>.FromBinding(new(frameId, "$"));
        Set(node, "CoordinateId", coordinateId);
        var mode = node.GetType().GetProperty("Mode")!; mode.SetValue(node, Enum.Parse(mode.PropertyType, "Template"));
        Set(node, "Template", WorkflowInput<TemplatePoseResult>.FromBinding(new(templateNodeId, "$")));
        return node;
    }

    /// <summary>ROI随动绑定：定义签名包含模板参考签名。</summary>
    internal static WorkflowVisionCoordinateBinding Follow(string buildNodeId, VisionCoordinateDefinition definition) => new()
    {
        System = WorkflowInput<VisionCoordinateSystem>.FromBinding(new(buildNodeId, "CoordinateSystem")),
        CoordinateSystemId = definition.Id, DefinitionVersion = definition.Version, DefinitionSignature = definition.Signature
    };

    internal static void Set<T>(IWorkflowNodeModel node, string name, T value)
    {
        var property = node.GetType().GetProperty(name) ?? throw new InvalidOperationException(name);
        property.SetValue(node, value);
    }
}
