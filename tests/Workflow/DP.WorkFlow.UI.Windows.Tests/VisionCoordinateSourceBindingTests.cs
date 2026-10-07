using DP.Vision;
using DP.Vision.Algorithms;
using DP.WorkFlow.Persistence.Json;
using DP.WorkFlow.UI;
using DP.WorkFlow.Vision.UI;

namespace DP.WorkFlow.Tests;

public sealed class VisionCoordinateSourceBindingTests
{
    [Theory]
    [InlineData("Vision.MeasureCaliper")]
    [InlineData("Vision.FindLine")]
    [InlineData("Vision.FindCircle")]
    [InlineData("Vision.AnalyzeBlobs")]
    public void ReplacingTemplateOrCoordinateDefinition_KeepsConsumerBindingAndLocalValues_WithoutPreview(string nodeType)
    {
        var nodes = new WorkflowNodeCatalog(); var handlers = new WorkflowNodeHandlerCatalog();
        var composition = new WorkflowRuntimePluginCatalog(nodes, handlers).Register(new WorkflowImageRuntimePluginModule());
        Assert.Equal(1, composition.LoadPlugins(Path.Combine(AppContext.BaseDirectory, "plugins", "workflow.vision.geometry"), new WorkflowPluginLoader()));
        composition.Freeze();
        var image = new AcquireVisionImageNodeModel { Id = "image", FilePath = Path.Combine(AppContext.BaseDirectory, "VisionData", "demo.pgm") };
        var match = new LocateVisionTemplatePoseNodeModel { Id = "match", Frame = Input<ImageFrame>("image"),
            TemplateSource = EWorkflowVisionTemplateSource.Resource, TemplateReferenceDefinition = Definition("old") };
        match.ModelAlgorithm.Settings["templatePath"] = "Resources/Templates/old/manifest.json";
        var coordinate = GeometryPluginTestCatalog.BuildFromTemplate(nodes, "coordinate", "image", "part", "match");
        var consumer = Assert.IsAssignableFrom<AnalyzeVisionFrameNodeModel>(nodes.GetOrThrow(nodeType).Factory());
        consumer.Id = "consumer"; consumer.Frame = Input<ImageFrame>("image");
        if (consumer is MeasureVisionCaliperNodeModel caliper)
        { caliper.StartX = 70; caliper.StartY = -75; caliper.EndX = 70; caliper.EndY = -55; }
        else consumer.Regions = [new WorkflowVisionRoi { Id = "roi", Shape = nodeType == "Vision.FindCircle" ? EWorkflowVisionRoiShape.Ellipse : EWorkflowVisionRoiShape.Rectangle,
            CenterX = 130, CenterY = 55, Width = 92, Height = 92 }];
        var scope = new IWorkflowNodeModel[] { image, match, coordinate, consumer };
        var producer = Assert.IsAssignableFrom<IWorkflowVisionCoordinateProducerNode>(coordinate);
        consumer.Coordinates = GeometryPluginTestCatalog.Follow("coordinate", producer.GetCoordinateDefinition());
        var binding = consumer.Coordinates;
        var ranges = consumer.Regions.Select(r => (r.CenterX, r.CenterY, r.Width, r.Height)).ToArray();
        var document = new WorkflowDocument { EntryNodeId = "image" };
        foreach (var node in scope) document.CanvasProjection.Nodes.Add(new WorkflowCanvasNode { Node = node });
        for (var i = 1; i < scope.Length; i++) document.CanvasProjection.Connections.Add(new WorkflowConnectionModel
        { FromNodeId = scope[i - 1].Id, FromPort = WorkflowPorts.Success, ToNodeId = scope[i].Id, ToPort = WorkflowPorts.Input });
        var compiler = new WorkflowCompiler(nodes); _ = compiler.Compile(document);

        match.TemplateReferenceDefinition = Definition("new");
        match.ModelAlgorithm.Settings["templatePath"] = "Resources/Templates/new/manifest.json";
        _ = compiler.Compile(document);
        Assert.Equal(producer.GetCoordinateDefinition().Signature, producer.ResolveDefinition(scope)!.Signature);
        GeometryPluginTestCatalog.Set(coordinate, "CoordinateId", "new-part");
        GeometryPluginTestCatalog.Set(coordinate, "DefinitionVersion", 2);
        GeometryPluginTestCatalog.Set(coordinate, "Unit", EVisionCoordinateUnit.Millimeter);
        _ = compiler.Compile(document);

        Assert.Same(binding, consumer.Coordinates);
        Assert.Equal("coordinate", consumer.Coordinates!.System.Binding!.Value.NodeId);
        Assert.Equal(ranges, consumer.Regions.Select(r => (r.CenterX, r.CenterY, r.Width, r.Height)).ToArray());
        if (consumer is MeasureVisionCaliperNodeModel measured)
            Assert.Equal((70d, -75d, 70d, -55d), (measured.StartX, measured.StartY, measured.EndX, measured.EndY));
        var session = new WorkflowDesignerSession(document, nodes) { SelectedNodeId = "consumer" };
        using var frames = new WorkflowVisionFrameScope();
        using var inspector = new WorkflowPropertyInspectorModel(session, "image", null,
            WorkflowVisionCoordinateProperties.CreateProvider(frames, () => session.Canvas.Nodes.Select(n => n.Node).ToArray()));
        Assert.DoesNotContain(inspector.Entries, e => e.Name == "Coordinates.ConfirmReference");
        Assert.Null(frames.Capture("image")); Assert.Null(frames.Capture("coordinate"));
        var store = new WorkflowDocumentJsonStore(nodes);
        _ = compiler.Compile(store.Deserialize(store.Serialize(document)).Document);
    }

    [Fact]
    public void Binding_UsesCurrentSourceOutput_NotAuthoredDefinitionSnapshot()
    {
        using var image = VisionImage.CopyFrom(new ImageInfo(8, 8, EPixelLayout.Gray8), new byte[64]);
        using var frame = new ImageFrame("current", image);
        var old = new VisionCoordinateSystem(new VisionCoordinateDefinition("old-part", "旧基准", reference: "old-model"), frame.FrameId, 8, 8, CoordinateMatrix2D.Identity);
        var binding = WorkflowVisionCoordinateBinding.Capture("coordinate", old);
        var current = new VisionCoordinateSystem(new VisionCoordinateDefinition("new-part", "新基准", 2, EVisionCoordinateUnit.Millimeter, "new-model"),
            frame.FrameId, 8, 8, CoordinateMatrix2D.FromAffine(2, 0, 4, 0, 2, 6));

        binding.Validate(current, frame);
        var mapped = current.ToImageGeometry(new RectangleGeometry(new PointD(1, 1), 2, 2));

        Assert.Equal(new PointD(6, 8), Assert.IsType<RectangleGeometry>(mapped).Center);
        Assert.Equal("coordinate", binding.System.Binding!.Value.NodeId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Binding_StillRejectsForeignFrameOrSize(bool wrongSize)
    {
        using var image = VisionImage.CopyFrom(new ImageInfo(8, 8, EPixelLayout.Gray8), new byte[64]);
        using var frame = new ImageFrame("current", image);
        var system = new VisionCoordinateSystem(new VisionCoordinateDefinition("part", "工件"), wrongSize ? "current" : "previous", wrongSize ? 16 : 8, 8, CoordinateMatrix2D.Identity);
        var binding = WorkflowVisionCoordinateBinding.Capture("coordinate", system);

        Assert.Throws<InvalidOperationException>(() => binding.Validate(system, frame));
    }

    [Fact]
    public void Binding_DoesNotRequireAnAuthoredSnapshot()
    {
        using var image = VisionImage.CopyFrom(new ImageInfo(8, 8, EPixelLayout.Gray8), new byte[64]);
        using var frame = new ImageFrame("current", image);
        var system = new VisionCoordinateSystem(new VisionCoordinateDefinition("part", "工件"), frame.FrameId, 8, 8, CoordinateMatrix2D.Identity);
        var binding = new WorkflowVisionCoordinateBinding { System = WorkflowInput<VisionCoordinateSystem>.FromBinding(new("coordinate", "CoordinateSystem")) };

        binding.Validate(system, frame);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("self")]
    [InlineData("wrong-type")]
    public void SourceBinding_StillRejectsMissingSelfOrWrongTypeAtCompilation(string fault)
    {
        var nodes = new WorkflowNodeCatalog().RegisterImageNodes();
        var image = new AcquireVisionImageNodeModel { Id = "image", FilePath = "image.png" };
        var consumer = new MeasureVisionCaliperNodeModel { Id = "consumer", Frame = Input<ImageFrame>("image"), StartX = 1, EndX = 5,
            Coordinates = new WorkflowVisionCoordinateBinding
            { System = WorkflowInput<VisionCoordinateSystem>.FromBinding(new(fault == "missing" ? "missing" : fault == "self" ? "consumer" : "image", fault == "wrong-type" ? "$" : "CoordinateSystem")) } };
        var document = new WorkflowDocument { EntryNodeId = image.Id };
        document.CanvasProjection.Nodes.Add(new WorkflowCanvasNode { Node = image });
        document.CanvasProjection.Nodes.Add(new WorkflowCanvasNode { Node = consumer });
        document.CanvasProjection.Connections.Add(new WorkflowConnectionModel { FromNodeId = image.Id, FromPort = WorkflowPorts.Success, ToNodeId = consumer.Id, ToPort = WorkflowPorts.Input });

        Assert.Throws<WorkflowCompilationException>(() => new WorkflowCompiler(nodes).Compile(document));
    }

    private static WorkflowInput<T> Input<T>(string node) => WorkflowInput<T>.FromBinding(new WorkflowBindingKey(node, "$"));
    private static VisionTemplateDefinition Definition(string identity) => new()
    { SourceWidth = 64, SourceHeight = 64, Width = 64, Height = 64, OriginX = 32, OriginY = 32, ReferenceIdentity = identity };
}
