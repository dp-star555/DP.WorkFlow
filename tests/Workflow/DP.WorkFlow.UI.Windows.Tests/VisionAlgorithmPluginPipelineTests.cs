using DP.Vision;
using DP.Vision.Algorithms;
using DP.Vision.OpenCv;
using DP.WorkFlow.Persistence.Json;
using DP.WorkFlow.UI;
using DP.WorkFlow.Vision.UI;

namespace DP.WorkFlow.Tests;

public sealed class VisionAlgorithmPluginPipelineTests
{
    [Fact]
    public async Task TemplateNode_BindsPluginBeforeExecution_AndPreservesFrameEvidence()
    {
        var path = Path.Combine(Path.GetTempPath(), "template-plugin-" + Guid.NewGuid().ToString("N") + ".pgm");
        File.WriteAllText(path, "P2\n4 4\n255\n0 0 0 0\n0 255 0 0\n0 0 255 0\n0 0 0 0\n");
        try
        {
            var nodes = new WorkflowNodeCatalog().RegisterImageNodes();
            var document = new WorkflowDocument { EntryNodeId = "source" };
            var source = new AcquireVisionImageNodeModel { Id = "source", FilePath = path };
            var template = new AcquireVisionImageNodeModel { Id = "template", FilePath = path };
            var locate = new LocateVisionTemplatePoseNodeModel { Id = "locate", Frame = Input<ImageFrame>(source.Id), Template = Input<ImageFrame>(template.Id) };
            foreach (var node in new IWorkflowNodeModel[] { source, template, locate }) document.CanvasProjection.Nodes.Add(new WorkflowCanvasNode { Node = node });
            Connect(document, "source", "template"); Connect(document, "template", "locate");
            var store = new WorkflowDocumentJsonStore(nodes); document = store.Deserialize(store.Serialize(document)).Document;
            using var frames = new WorkflowVisionFrameScope();
            using var runtime = new VisionAlgorithmRuntime(VisionAlgorithmCatalog.Compose(new[] { new OpenCvVisionAlgorithmModule() }));
            using var bindings = new WorkflowVisionAlgorithmBindings(runtime, frames);
            var services = new WorkflowServiceProvider().Add<IWorkflowVisionFrameScope>(frames).Add<IWorkflowVisionAlgorithmBindings>(bindings)
                .Add<IWorkflowNodeCapabilityProvider>(bindings).Add<IWorkflowRunPreparationService>(bindings).Add<IWorkflowRunResourceOwner>(frames);
            using var host = new WorkflowRuntimeHost(nodes, new WorkflowNodeHandlerCatalog().RegisterImageNodeHandlers());
            host.Configure(document, new WorkflowContext(services));

            Assert.True((await host.RunAsync()).Success);
            var result = Assert.IsType<TemplatePoseResult>(host.Engine!.RunState.NodeOutputs.Single(o => o.NodeId == "locate").Value);
            var templateFrame = Assert.IsType<ImageFrame>(host.Engine.RunState.NodeOutputs.Single(o => o.NodeId == "template").Value);
            Assert.Equal(templateFrame.FrameId, result.TemplateFrameId);
            Assert.True(result.Found);
            using var preview = frames.Capture("locate"); Assert.IsType<TemplatePoseResult>(preview!.Facts);
            Assert.True((await host.RunAsync()).Success);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void EngineParameterDescriptions_CreateTypedProperties_WithRangeValidationAndUndo()
    {
        var algorithms = VisionAlgorithmCatalog.Compose(new[] { new OpenCvVisionAlgorithmModule() });
        var nodes = new WorkflowNodeCatalog().Register(WorkflowNodeDescriptor.Create<ParameterNode>());
        var document = new WorkflowDocument { EntryNodeId = "parameter" };
        var node = new ParameterNode { Id = "parameter" }; document.CanvasProjection.Nodes.Add(new WorkflowCanvasNode { Node = node });
        var session = new WorkflowDesignerSession(document, nodes) { SelectedNodeId = node.Id };
        using var inspector = new WorkflowPropertyInspectorModel(session, node.Id, choiceProvider: null, additionalProperties: WorkflowVisionAlgorithmProperties.CreateProvider(algorithms));
        var scale = inspector.Entries.Single(e => e.Name == "Algorithm.patch.scale");
        var model = inspector.Entries.Single(e => e.Name == "Algorithm.patch.backbonePath");
        Assert.Equal(WorkflowPropertyEditorKind.Number, scale.EditorKind);
        Assert.Equal(2d, scale.Value); Assert.Equal(WorkflowPropertyEditorKeys.FilePath, model.EditorKey);
        inspector.SetValue(scale, "1.5"); Assert.Equal("1.5", node.Algorithm.Settings["scale"]);
        Assert.True(session.Undo()); Assert.False(node.Algorithm.Settings.ContainsKey("scale"));
        Assert.True(session.Redo()); Assert.Equal("1.5", node.Algorithm.Settings["scale"]);
        Assert.Throws<ArgumentOutOfRangeException>(() => inspector.SetValue(inspector.Entries.Single(e => e.Name == "Algorithm.patch.scale"), "99"));
        Assert.Equal("1.5", node.Algorithm.Settings["scale"]);
        var provider = WorkflowVisionAlgorithmChoices.CreateProvider(algorithms);
        Assert.Contains(provider(WorkflowPropertyEditorKeys.VisionAlgorithmPrefix + "location.template-pose", "ImplementationId"), c => Equals(c.Value, "opencv.template-pose"));
        Assert.Equal(2, provider(WorkflowPropertyEditorKeys.VisionAlgorithmPrefix + "anomaly.patch", "ImplementationId").Count);
    }

    [Fact]
    public void ExistingNode_ChoiceFiltersByContract_PreservesMissingSelection_AndSupportsUndo()
    {
        var algorithms = VisionAlgorithmCatalog.Compose(new IVisionAlgorithmModule[] { new ManagedVisionAlgorithmModule(), new OpenCvVisionAlgorithmModule() });
        var nodes = new WorkflowNodeCatalog().RegisterImageNodes();
        var node = new AnalyzeVisionColorNodeModel { Id = "color", Algorithm = new() { ImplementationId = "company.color" } };
        var document = new WorkflowDocument { EntryNodeId = node.Id };
        document.CanvasProjection.Nodes.Add(new WorkflowCanvasNode { Node = node });
        var session = new WorkflowDesignerSession(document, nodes) { SelectedNodeId = node.Id };
        using var inspector = new WorkflowPropertyInspectorModel(session, node.Id, null, WorkflowVisionAlgorithmProperties.CreateProvider(algorithms));
        var choice = inspector.Entries.Single(e => e.Name == "Algorithm.algorithm.ImplementationId");
        Assert.Equal(WorkflowPropertyEditorKind.Choice, choice.EditorKind);
        Assert.Equal(new[] { "managed.color", "company.color" }, choice.Choices.Select(c => c.Value));
        inspector.SetValue(choice, "managed.color");
        Assert.Equal("managed.color", node.Algorithm.ImplementationId);
        Assert.True(session.Undo()); Assert.Equal("company.color", node.Algorithm.ImplementationId);
        Assert.True(session.Redo()); Assert.Equal("managed.color", node.Algorithm.ImplementationId);
        node.Algorithm.Settings["model"] = "recipe-model";
        inspector.SetValue(inspector.Entries.Single(e => e.Name == choice.Name), "company.color");
        Assert.Empty(node.Algorithm.Settings);
        Assert.True(session.Undo()); Assert.Equal("managed.color", node.Algorithm.ImplementationId); Assert.Equal("recipe-model", node.Algorithm.Settings["model"]);
        Assert.True(session.Redo()); Assert.Empty(node.Algorithm.Settings);
        Assert.Equal("company.color", node.Algorithm.ImplementationId);
        var restored = new WorkflowDocumentJsonStore(nodes).Deserialize(new WorkflowDocumentJsonStore(nodes).Serialize(document)).Document;
        Assert.Equal("company.color", Assert.IsType<AnalyzeVisionColorNodeModel>(Assert.Single(restored.CanvasProjection.Nodes).Node).Algorithm.ImplementationId);
    }

    private static WorkflowInput<T> Input<T>(string id) => WorkflowInput<T>.FromBinding(new WorkflowBindingKey(id, "$"));
    private static void Connect(WorkflowDocument document, string from, string to) => document.CanvasProjection.Connections.Add(
        new WorkflowConnectionModel { FromNodeId = from, FromPort = WorkflowPorts.Success, ToNodeId = to, ToPort = WorkflowPorts.Input });
    private sealed class ParameterNode : WorkflowNodeModel, IWorkflowVisionAlgorithmNode
    {
        public override string NodeType => "Test.Parameter";
        public VisionAlgorithmSelection Algorithm { get; set; } = new() { ImplementationId = "opencv.cnn-patch" };
        public IReadOnlyList<WorkflowVisionAlgorithmSlot> GetAlgorithmSlots() => new[] { new WorkflowVisionAlgorithmSlot("patch", typeof(IPatchAnomalyDetector), Algorithm) };
    }
}
