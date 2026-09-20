using System.Text.Json;
using DP.Vision;
using DP.WorkFlow.Persistence.Json;

namespace DP.WorkFlow.Tests;

public sealed class VisionModuleTests
{
    [Fact]
    public void Module_RegistersOnlyNewContractsAndUniqueHandlers()
    {
        var nodes = new WorkflowNodeCatalog(); var handlers = new WorkflowNodeHandlerCatalog();
        new WorkflowRuntimePluginCatalog(nodes, handlers).Register(new WorkflowImageRuntimePluginModule()).Freeze();
        Assert.Equal(18, nodes.Snapshot().Count);
        Assert.Equal(typeof(ImageFrame), nodes.GetOrThrow("Vision.LoadFile").OutputType);
        Assert.DoesNotContain(nodes.Snapshot().Keys, type => type is "Vision.AcquireImage" or "Vision.RunTool" or "Vision.Blob" or "Vision.Ocr" or "Vision.PaddleOcr");
        Assert.DoesNotContain(typeof(WorkflowImageRuntimePluginModule).Assembly.GetReferencedAssemblies(), a => a.Name!.StartsWith("MachineVision", StringComparison.Ordinal));
    }

    [Fact]
    public void Manifest_LoadsOneNewModuleWithoutCompatibilityRegistration()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()); Directory.CreateDirectory(root);
        try
        {
            string package = Path.Combine(root, "vision"); Directory.CreateDirectory(package);
            string assembly = typeof(WorkflowImageRuntimePluginModule).Assembly.Location;
            string file = Path.GetFileName(assembly); File.Copy(assembly, Path.Combine(package, file));
            File.WriteAllText(Path.Combine(package, "plugin.json"), JsonSerializer.Serialize(new WorkflowPluginManifest
            {
                PluginId = "workflow.vision", Version = "1.0.0",
                Modules = new Dictionary<string, string[]> { [WorkflowPluginModuleGroups.Runtime] = new[] { file } }
            }));
            var nodes = new WorkflowNodeCatalog(); var handlers = new WorkflowNodeHandlerCatalog();
            var composition = new WorkflowRuntimePluginCatalog(nodes, handlers);
            Assert.Equal(1, composition.LoadPlugins(root)); composition.Freeze();
            Assert.Equal(18, nodes.Snapshot().Count);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Mainline_HasNoRetiredProjectsOrReferences()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "DP.WorkFlow.sln"))) root = root.Parent;
        Assert.NotNull(root);
        Assert.False(Directory.Exists(Path.Combine(root.FullName, "src", "Vision")));
        Assert.False(Directory.Exists(Path.Combine(root.FullName, "src", "Workflow", "Nodes", "DP.WorkFlow.Nodes.Vision.PaddleOcr")));
        Assert.DoesNotContain("MachineVision", File.ReadAllText(Path.Combine(root.FullName, "DP.WorkFlow.sln")), StringComparison.Ordinal);
        foreach (var project in Directory.EnumerateFiles(root.FullName, "*.csproj", SearchOption.AllDirectories))
        {
            var xml = System.Xml.Linq.XDocument.Load(project);
            Assert.DoesNotContain(xml.Descendants("ProjectReference"), reference =>
                ((string?)reference.Attribute("Include"))?.Contains("MachineVision", StringComparison.Ordinal) == true);
        }
    }

    [Fact]
    public void OldNodeType_IsNotSilentlyConvertedOnLoad()
    {
        var nodes = new WorkflowNodeCatalog().RegisterImageNodes();
        var store = new WorkflowDocumentJsonStore(nodes);
        var document = new WorkflowDocument { EntryNodeId = "file" };
        document.CanvasProjection.Nodes.Add(new WorkflowCanvasNode { Node = new LoadVisionFileNodeModel { Id = "file" } });
        var json = store.Serialize(document).Replace("Vision.LoadFile", "Vision.AcquireImage", StringComparison.Ordinal);
        var loaded = store.Deserialize(json).Document;
        Assert.Equal("Vision.AcquireImage", loaded.CanvasProjection.Nodes[0].Node.NodeType);
        Assert.IsType<UnknownWorkflowNodeModel>(loaded.CanvasProjection.Nodes[0].Node);
        var plan = new WorkflowCompiler(nodes).Compile(loaded);
        var binder = new WorkflowRuntimeBinder(new WorkflowNodeHandlerCatalog().RegisterImageNodeHandlers());
        var error = Assert.Throws<InvalidOperationException>(() => binder.Bind(plan));
        Assert.Contains("Vision.AcquireImage", error.Message, StringComparison.Ordinal);
    }
}
