using DP.Vision;
using DP.Vision.Algorithms;
using DP.Vision.OpenCv;
using DP.Vision.UI;
using DP.WorkFlow.Persistence.Json;
using DP.WorkFlow.UI;
using DP.WorkFlow.Vision.UI;
using OpenCvSharp;

namespace DP.WorkFlow.Tests;

public sealed class VisionCoordinatePipelineTests
{
    [Fact]
    public async Task ActualMatching_RoiAndResultsFollowNewImage_WithoutChangingDocument()
    {
        using var data = new Images();
        var nodes = new WorkflowNodeCatalog().RegisterImageNodes(); var store = new WorkflowDocumentJsonStore(nodes);
        var document = store.Deserialize(store.Serialize(data.Pipeline())).Document;
        string configuration = store.Serialize(document);
        using var scope = new WorkflowVisionFrameScope(); using var host = new WorkflowRuntimeHost(nodes, new WorkflowNodeHandlerCatalog().RegisterImageNodeHandlers());
        host.Configure(document, new WorkflowContext(Services(scope)));
        var first = await host.RunAsync(); Assert.True(first.Success, first.Message);
        var a = Output<BlobAnalysisResult>(host, "blob"); var ac = Assert.Single(a.LocatedCentroids!);
        Assert.Equal(19.5, ac.ImagePosition.X, 6); Assert.Equal(11.5, ac.ImagePosition.Y, 6);
        Assert.Equal(9.5, ac.LocalPosition.X, 6); Assert.Equal(1.5, ac.LocalPosition.Y, 6);
        Assert.Equal(8, Assert.Single(a.Blobs).Area);
        Assert.Equal(8, Output<RegionAnalysisResult>(host, "threshold").Area);
        Assert.Equal(255, Output<ColorAnalysisResult>(host, "color").Red);
        Assert.NotNull(Output<BlobAnalysisResult>(host, "select").CoordinateSystem);
        Assert.NotNull(Output<RegionAnalysisResult>(host, "morph").CoordinateSystem);
        var localLine = Output<RobustLineResult>(host, "fit"); Assert.InRange(localLine.LocatedA!.LocalPosition.X, 7.8, 8.2);
        data.WriteScene(rotated: true);
        var second = await host.RunAsync(); Assert.True(second.Success, second.Message);
        var b = Output<BlobAnalysisResult>(host, "blob"); var bc = Assert.Single(b.LocatedCentroids!);
        Assert.NotEqual(a.FrameId, b.FrameId); Assert.Equal(a.CoordinateSystem!.CoordinateSystemId, b.CoordinateSystem!.CoordinateSystemId);
        Assert.Equal(38.5, bc.ImagePosition.X, 6); Assert.Equal(29.5, bc.ImagePosition.Y, 6);
        Assert.Equal(ac.LocalPosition.X, bc.LocalPosition.X, 6); Assert.Equal(ac.LocalPosition.Y, bc.LocalPosition.Y, 6);
        Assert.Equal(8, Assert.Single(b.Blobs).Area); Assert.Equal(255, Output<ColorAnalysisResult>(host, "color").Red);
        Assert.Equal(Math.PI / 2, b.CoordinateSystem.Pose.AngleRadians, 8);
        Assert.InRange(Output<RobustLineResult>(host, "fit").LocatedA!.LocalPosition.X, 7.8, 8.2);
        Assert.All(Output<CaliperResult>(host, "c0").LocatedEdges!, p => Assert.Equal(b.FrameId, p.FrameId));
        Assert.Equal(configuration, store.Serialize(document));
        // 第三帧定位失败，不能复用第二帧矩阵或保留第二帧消费者输出。
        data.WriteScene(absent: true);
        Assert.False((await host.RunAsync()).Success);
        Assert.Null(Output<TemplatePoseResult>(host, "pose").CoordinateSystem);
        Assert.DoesNotContain(host.Engine!.RunState.NodeOutputs, o => o.NodeId == "blob");
    }

    [Fact]
    public async Task AllAreaOperatorsAndChildPose_FollowParentWithoutDoubleCompensation()
    {
        using var data = new Images(); var document = data.Pipeline();
        var original = (AnalyzeVisionBlobsNodeModel)document.CanvasProjection.Nodes.Single(n => n.Node.Id == "blob").Node;
        var parent = original.Coordinates!;
        List<WorkflowVisionRoi> Search() => new() { new() { Id = "search", CenterX = 2.5, CenterY = 1.5, Width = 5, Height = 3 } };
        var child = new LocateVisionTemplatePoseNodeModel { Id = "child", Frame = Input<ImageFrame>("scene"), Template = Input<ImageFrame>("template"),
            Coordinates = parent, CoordinateSystemId = "child-definition", Regions = Search(), MinimumScore = .9999 };
        var translation = new LocateVisionTemplateNodeModel { Id = "translation", Frame = Input<ImageFrame>("scene"), Template = Input<ImageFrame>("template"),
            Coordinates = parent, Regions = Search(), MinimumScore = .9999 };
        var edges = new MeasureVisionEdgesNodeModel { Id = "edges", Frame = Input<ImageFrame>("scene"), Coordinates = parent,
            Regions = new() { new() { Id = "line", CenterX = 19.5, CenterY = 7.5, Width = 3, Height = 10 } } };
        var consumer = new AnalyzeVisionBlobsNodeModel { Id = "child-blob", Frame = Input<ImageFrame>("scene"), MinimumGray = 255, MaximumGray = 255,
            Regions = original.Regions, Coordinates = new WorkflowVisionCoordinateBinding { CoordinateSystemId = "child-definition", TemplateSignature = parent.TemplateSignature,
                System = WorkflowInput<LocatedCoordinateSystem>.FromBinding(new("child", "CoordinateSystem")) } };
        string previous = "fit";
        foreach (var node in new IWorkflowNodeModel[] { edges, translation, child, consumer })
        {
            document.CanvasProjection.Nodes.Add(new WorkflowCanvasNode { Node = node });
            document.CanvasProjection.Connections.Add(new WorkflowConnectionModel { FromNodeId = previous, FromPort = WorkflowPorts.Success, ToNodeId = node.Id, ToPort = WorkflowPorts.Input });
            previous = node.Id;
            using var page = new VisionFrameEditorPageModel(node); Assert.True(page.CanBindCoordinates);
        }
        var nodes = new WorkflowNodeCatalog().RegisterImageNodes(); var store = new WorkflowDocumentJsonStore(nodes);
        document = store.Deserialize(store.Serialize(document)).Document;
        using var scope = new WorkflowVisionFrameScope(); using var host = new WorkflowRuntimeHost(nodes, new WorkflowNodeHandlerCatalog().RegisterImageNodeHandlers());
        host.Configure(document, new WorkflowContext(Services(scope)));
        foreach (bool rotated in new[] { false, true })
        {
            data.WriteScene(rotated); var run = await host.RunAsync(); Assert.True(run.Success, run.Message);
            var located = Output<TemplatePoseResult>(host, "child"); Assert.True(located.Found);
            Assert.Equal("child-definition", located.CoordinateSystem!.CoordinateSystemId);
            Assert.Equal("part-definition", located.SearchCoordinateSystem!.CoordinateSystemId);
            Assert.Equal(rotated ? Math.PI / 2 : 0, located.Transform!.AngleRadians, 8);
            Assert.Equal(rotated ? 38.5 : 12.5, located.Transform.Center.X, 6);
            var match = Output<TemplateLocationResult>(host, "translation"); Assert.True(match.Found);
            Assert.Equal(located.Transform.Center, match.Transform!.Center);
            Assert.IsType<RectangleGeometry>(match.MatchGeometry);
            var result = Output<BlobAnalysisResult>(host, "child-blob");
            Assert.Equal(rotated ? 38.5 : 19.5, Assert.Single(result.Blobs).Centroid.X, 6);
            Assert.Equal(9.5, Assert.Single(result.LocatedCentroids!).LocalPosition.X, 6);
            Assert.InRange(Output<EdgeMeasurementResult>(host, "edges").LocatedA!.LocalPosition.X, 19, 21);
        }
        child.Coordinates = new WorkflowVisionCoordinateBinding { CoordinateSystemId = "child-definition", TemplateSignature = parent.TemplateSignature,
            System = WorkflowInput<LocatedCoordinateSystem>.FromBinding(new("child", "CoordinateSystem")) };
        Assert.Contains(child.ValidateConfiguration(), e => e.Contains("自身", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("frame")]
    [InlineData("definition")]
    [InlineData("template")]
    public async Task ConsumerRejectsMixedFrameOrChangedDefinitionBeforeOutput(string fault)
    {
        using var data = new Images(); var document = data.Pipeline();
        var blob = (AnalyzeVisionBlobsNodeModel)document.CanvasProjection.Nodes.Single(n => n.Node.Id == "blob").Node;
        if (fault == "frame") blob.Frame = Input<ImageFrame>("template");
        if (fault == "definition") blob.Coordinates!.CoordinateSystemId = "another-template";
        if (fault == "template") data.ChangeTemplateOnePixel();
        using var scope = new WorkflowVisionFrameScope(); using var host = new WorkflowRuntimeHost(new WorkflowNodeCatalog().RegisterImageNodes(), new WorkflowNodeHandlerCatalog().RegisterImageNodeHandlers());
        host.Configure(document, new WorkflowContext(Services(scope)));
        Assert.False((await host.RunAsync()).Success);
        Assert.True(Output<TemplatePoseResult>(host, "pose").Found); // 不是通过匹配失败间接蒙混过关。
        Assert.DoesNotContain(host.Engine!.RunState.NodeOutputs, o => o.NodeId == "blob");
        using var preview = scope.Capture("blob"); Assert.Null(preview);
    }

    [Fact]
    public async Task EditorConvertsToLocal_IsolatedCommitUndoAndNewFramePreviewDoNotRewriteConfiguration()
    {
        using var data = new Images(); var document = data.Pipeline();
        var source = (AnalyzeVisionBlobsNodeModel)document.CanvasProjection.Nodes.Single(n => n.Node.Id == "blob").Node;
        source.Coordinates = null; source.Regions = new() { new() { Id = "drawn", CenterX = 19.5, CenterY = 11.5, Width = 3, Height = 3 } };
        var nodes = new WorkflowNodeCatalog().RegisterImageNodes(); var store = new WorkflowDocumentJsonStore(nodes);
        using var scope = new WorkflowVisionFrameScope(); using var host = new WorkflowRuntimeHost(nodes, new WorkflowNodeHandlerCatalog().RegisterImageNodeHandlers());
        host.Configure(document, new WorkflowContext(Services(scope))); Assert.True((await host.RunAsync()).Success);
        var session = new WorkflowDesignerSession(document, nodes);
        var editor = new WorkflowNodeEditorModel(session, "scene", "blob", new[] { new VisionFrameEditorPageProvider(scope) });
        try
        {
            var page = Assert.IsType<VisionFrameEditorPageModel>(editor.Pages.Single(p => p.RendererKey == VisionFrameEditorPageProvider.RendererKey).Model);
            Assert.Contains(page.CoordinateSources, s => s.NodeId == "pose");
            using (var canvas = page.Capture(0)) Assert.NotNull(canvas);
            page.BindCoordinates("pose");
            var editing = (AnalyzeVisionBlobsNodeModel)editor.EditingNode;
            Assert.Null(source.Coordinates); Assert.Equal(19.5, source.Regions[0].CenterX);
            Assert.Equal(9.5, editing.Regions[0].CenterX, 8); Assert.Equal(1.5, editing.Regions[0].CenterY, 8);
            Assert.NotNull(editing.Coordinates);
            page.Editor.Tool = ERoiTool.Ellipse;
            page.Editor.PointerDown(new PointD(18.5, 10.5), .1); page.Editor.PointerUp(new PointD(20.5, 12.5));
            Assert.Equal(2, editing.Regions.Count); Assert.Single(source.Regions);
            Assert.Equal(9.5, editing.Regions[1].CenterX, 8);
            editor.ApplyChanges();
            Assert.NotNull(((AnalyzeVisionBlobsNodeModel)document.CanvasProjection.Nodes.Single(n => n.Node.Id == "blob").Node).Coordinates);
            Assert.True(session.Undo());
            Assert.Null(((AnalyzeVisionBlobsNodeModel)document.CanvasProjection.Nodes.Single(n => n.Node.Id == "blob").Node).Coordinates);
            Assert.True(session.Redo());
            var saved = store.Serialize(document); var editingBefore = editing.Regions.Select(r => (r.CenterX, r.CenterY, r.Angle)).ToArray();
            // 重新配置运行计划后，参考ROI随新的定位显示，配置仍是局部值。
            data.WriteScene(rotated: true); host.Configure(document, new WorkflowContext(Services(scope)));
            Assert.True((await host.RunAsync()).Success);
            using (var canvas = page.Capture(0)) Assert.NotNull(canvas);
            var displayed = Assert.IsType<RectangleGeometry>(page.Editor.Document.Rois[0].Shape);
            Assert.Equal(38.5, displayed.Center.X, 6); Assert.Equal(29.5, displayed.Center.Y, 6);
            Assert.Equal(editingBefore, editing.Regions.Select(r => (r.CenterX, r.CenterY, r.Angle)).ToArray());
            Assert.Equal(saved, store.Serialize(document));
            page.UnbindCoordinates(); Assert.Null(editing.Coordinates);
            Assert.Equal(38.5, editing.Regions[0].CenterX, 6);
            Assert.Equal(saved, store.Serialize(document)); // 未确认解除，不改正式文档。
        }
        finally { await editor.DisposeAsync(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InvalidNewPreviewDisablesLocalEditing_AndRecoveryDoesNotKeepUncommittedGestures(bool changedTemplate)
    {
        using var data = new Images(); var document = data.Pipeline();
        var node = (AnalyzeVisionBlobsNodeModel)document.CanvasProjection.Nodes.Single(n => n.Node.Id == "blob").Node;
        using var scope = new WorkflowVisionFrameScope(); using var host = new WorkflowRuntimeHost(new WorkflowNodeCatalog().RegisterImageNodes(), new WorkflowNodeHandlerCatalog().RegisterImageNodeHandlers());
        host.Configure(document, new WorkflowContext(Services(scope))); Assert.True((await host.RunAsync()).Success);
        using var page = new VisionFrameEditorPageModel(node, scope);
        using (var frame = page.Capture(0)) Assert.NotNull(frame);
        Assert.True(page.CoordinateEditingReady);
        var saved = node.Regions.Select(r => (r.Id, r.CenterX, r.CenterY)).ToArray();
        if (changedTemplate) data.ChangeTemplateOnePixel(); else data.WriteScene(absent: true);
        Assert.False((await host.RunAsync()).Success);
        Assert.Throws<InvalidOperationException>(() => page.Capture(0)); Assert.False(page.CoordinateEditingReady);
        page.Editor.Tool = ERoiTool.Rectangle;
        page.Editor.PointerDown(new PointD(1, 1), .1); page.Editor.PointerUp(new PointD(4, 4));
        Assert.Equal(saved, node.Regions.Select(r => (r.Id, r.CenterX, r.CenterY)).ToArray());
        Assert.Throws<InvalidOperationException>(page.UnbindCoordinates);
        if (!changedTemplate)
        {
            data.WriteScene(rotated: true); Assert.True((await host.RunAsync()).Success);
            using (var frame = page.Capture(0)) Assert.NotNull(frame);
            Assert.True(page.CoordinateEditingReady); Assert.Equal(saved.Length, page.Editor.Document.Rois.Count);
            Assert.Equal(saved, node.Regions.Select(r => (r.Id, r.CenterX, r.CenterY)).ToArray());
        }
    }

    [Fact]
    public void AreaCapabilityEnablesCoordinates_WholeImageOperatorsStillRejectThem()
    {
        var node = new MeasureVisionEdgesNodeModel { Frame = Input<ImageFrame>("scene"), Coordinates = new WorkflowVisionCoordinateBinding
        { System = WorkflowInput<LocatedCoordinateSystem>.FromBinding(new("pose", "CoordinateSystem")), CoordinateSystemId = "id", TemplateSignature = "sig" } };
        node.Regions.Add(new WorkflowVisionRoi { Width = 5, Height = 5, CenterX = 10, CenterY = 10 });
        Assert.Empty(node.ValidateConfiguration());
        using var page = new VisionFrameEditorPageModel(node);
        Assert.True(page.SupportsRegions); Assert.True(page.CanBindCoordinates);
        var preprocessing = new PreprocessVisionImageNodeModel { Frame = Input<ImageFrame>("scene"), Coordinates = node.Coordinates };
        Assert.Contains(preprocessing.ValidateConfiguration(), e => e.Contains("不支持定位", StringComparison.Ordinal));
    }

    [Fact]
    public void ExternalAreaNodeUsesTheSharedEditorWithoutTypeWhitelists()
    {
        var node = new ExternalAreaNode { Id = "external", Frame = Input<ImageFrame>("scene") };
        Assert.True(node.SupportsCoordinates); Assert.Empty(node.ValidateConfiguration());
        using var page = new VisionFrameEditorPageModel(node);
        Assert.True(page.CanEdit); Assert.True(page.SupportsRegions); Assert.True(page.CanBindCoordinates);
    }

    private sealed class ExternalAreaNode : AnalyzeVisionFrameNodeModel
    {
        public override string NodeType => "External.Area";
        public override EWorkflowVisionRange RangeCapability => EWorkflowVisionRange.Region;
    }

    private static WorkflowServiceProvider Services(WorkflowVisionFrameScope scope) => new WorkflowServiceProvider()
        .Add<IImageFileReader>(new OpenCvImageFileReader()).Add<ITemplatePoseLocator>(new OpenCvTemplatePoseLocator())
        .Add<IEdgeMeasurer>(new OpenCvEdgeMeasurer()).Add<ITemplateLocator>(new OpenCvTemplateLocator())
        .Add<IBlobAnalyzer>(new OpenCvBlobAnalyzer()).Add<IColorAnalyzer>(new RgbColorAnalyzer()).Add<IRegionProcessor>(new OpenCvRegionProcessor())
        .Add<IBlobSelector>(new BlobSelector()).Add<ICaliperMeasurer>(new CaliperMeasurer()).Add<IRobustLineFitter>(new RobustLineFitter())
        .Add<IWorkflowVisionFrameScope>(scope).Add<IWorkflowRunPreparationService>(scope);
    private static WorkflowInput<T> Input<T>(string id) => WorkflowInput<T>.FromBinding(new(id, "$"));
    private static T Output<T>(WorkflowRuntimeHost host, string id) => Assert.IsType<T>(host.Engine!.RunState.NodeOutputs.Single(o => o.NodeId == id).Value);
    private sealed class Images : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "coordinates-" + Guid.NewGuid());
        private readonly byte[] _template = { 30,200,70,100,180,90,240,10,130,50,160,40,230,80,210 };
        public string Scene => Path.Combine(_directory, "scene.png");
        public string Template => Path.Combine(_directory, "template.png");
        public Images() { Directory.CreateDirectory(_directory); WriteTemplate(); WriteScene(); }
        public void WriteScene(bool rotated = false, bool absent = false)
        {
            using var mat = new Mat(64, 64, MatType.CV_8UC1, Scalar.All(0));
            if (!absent)
            {
                for (int y = 0; y < 3; y++) for (int x = 0; x < 5; x++)
                    mat.Set(rotated ? 20 + x : 10 + y, rotated ? 39 - y : 10 + x, _template[y * 5 + x]);
                for (int y = 0; y < 16; y++) for (int x = 20; x < 24; x++)
                    mat.Set(rotated ? 20 + x : 10 + y, rotated ? 39 - y : 10 + x, (byte)255);
                for (int y = 0; y < 3; y++) for (int x = 8; x < 11; x++)
                    mat.Set(rotated ? 20 + x : 10 + y, rotated ? 39 - y : 10 + x, (byte)255);
            }
            Cv2.ImWrite(Scene, mat);
        }
        private void WriteTemplate()
        {
            using var mat = new Mat(3, 5, MatType.CV_8UC1);
            for (int y = 0; y < 3; y++) for (int x = 0; x < 5; x++) mat.Set(y, x, _template[y * 5 + x]);
            Cv2.ImWrite(Template, mat);
        }
        public void ChangeTemplateOnePixel() { _template[0]++; WriteTemplate(); }
        private WorkflowVisionCoordinateBinding Binding()
        {
            using var image = VisionImage.CopyFrom(new ImageInfo(5, 3, EPixelLayout.Gray8), _template);
            return new WorkflowVisionCoordinateBinding { System = WorkflowInput<LocatedCoordinateSystem>.FromBinding(new("pose", "CoordinateSystem")),
                CoordinateSystemId = "part-definition", TemplateSignature = LocatedCoordinateSystem.ComputeTemplateSignature(image) };
        }
        private static List<WorkflowVisionRoi> Regions() => new()
        {
            new() { Id = "include", CenterX = 9.5, CenterY = 1.5, Width = 3, Height = 3 },
            new() { Id = "hole", CenterX = 9.5, CenterY = 1.5, Width = 1, Height = 1, Exclude = true }
        };
        public WorkflowDocument Pipeline()
        {
            var nodes = new List<IWorkflowNodeModel>
            {
                new LoadVisionFileNodeModel { Id = "scene", FilePath = Scene }, new LoadVisionFileNodeModel { Id = "template", FilePath = Template },
                new LocateVisionTemplatePoseNodeModel { Id = "pose", Frame = Input<ImageFrame>("scene"), Template = Input<ImageFrame>("template"),
                    CoordinateSystemId = "part-definition", AnglesRadians = new() { 0, Math.PI / 2 }, MinimumScore = .9999 },
                new AnalyzeVisionBlobsNodeModel { Id = "blob", Frame = Input<ImageFrame>("scene"), MinimumGray = 255, MaximumGray = 255, Coordinates = Binding(), Regions = Regions() },
                new AnalyzeVisionColorNodeModel { Id = "color", Frame = Input<ImageFrame>("scene"), Coordinates = Binding(), Regions = Regions() },
                new ThresholdVisionRegionNodeModel { Id = "threshold", Frame = Input<ImageFrame>("scene"), MinimumGray = 255, MaximumGray = 255, Coordinates = Binding(), Regions = Regions() },
                new MorphVisionRegionNodeModel { Id = "morph", Frame = Input<ImageFrame>("scene"), InputRegion = Input<RegionAnalysisResult>("threshold"), Radius = 0 },
                new SelectVisionBlobsNodeModel { Id = "select", Frame = Input<ImageFrame>("scene"), Blobs = Input<BlobAnalysisResult>("blob") }
            };
            for (int i = 0; i < 3; i++) nodes.Add(new MeasureVisionCaliperNodeModel { Id = "c" + i, Frame = Input<ImageFrame>("scene"),
                Coordinates = Binding(), StartX = 4, EndX = 15, StartY = i + .5, EndY = i + .5, HalfWidth = 0, Polarity = ECaliperPolarity.Rising });
            nodes.Add(new FitVisionRobustLineNodeModel { Id = "fit", Frame = Input<ImageFrame>("scene"), Coordinates = Binding(),
                Samples = Enumerable.Range(0, 3).Select(i => Input<CaliperResult>("c" + i)).ToList() });
            var document = new WorkflowDocument { EntryNodeId = nodes[0].Id };
            foreach (var node in nodes) document.CanvasProjection.Nodes.Add(new WorkflowCanvasNode { Node = node });
            for (int i = 1; i < nodes.Count; i++) document.CanvasProjection.Connections.Add(new WorkflowConnectionModel
            { FromNodeId = nodes[i - 1].Id, FromPort = WorkflowPorts.Success, ToNodeId = nodes[i].Id, ToPort = WorkflowPorts.Input });
            return document;
        }
        public void Dispose() => Directory.Delete(_directory, true);
    }
}
