using DP.Vision;
using DP.Vision.Algorithms;
using DP.Vision.OpenCv;
using DP.WorkFlow.Persistence.Json;
using DP.WorkFlow.UI;
using DP.WorkFlow.Vision.UI;
using OpenCvSharp;

namespace DP.WorkFlow.Tests;

public sealed class VisionOperatorPipelineTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Preprocess_Region_Morphology_BlobSelectionAndColor_RoundTripAndRerun(bool usePlugins)
    {
        string path = Image(20, 20, (x, y) => x >= 4 && x <= 8 && y >= 4 && y <= 8 && (x == 4 || x == 8 || y == 4 || y == 8) || x == 15 && y == 15 ? (byte)255 : (byte)0);
        try
        {
            var document = RegionPipeline(path); var nodes = new WorkflowNodeCatalog().RegisterImageNodes();
            var store = new WorkflowDocumentJsonStore(nodes); document = store.Deserialize(store.Serialize(document)).Document;
            using var scope = new WorkflowVisionFrameScope();
            using var runtime = PluginRuntime();
            using var bindings = new WorkflowVisionAlgorithmBindings(runtime, scope);
            using var host = new WorkflowRuntimeHost(nodes, new WorkflowNodeHandlerCatalog().RegisterImageNodeHandlers());
            host.Configure(document, new WorkflowContext(usePlugins ? PluginServices(scope, bindings) : Services(scope)));
            Assert.True((await host.RunAsync()).Success);
            var original = Output<ImageFrame>(host, "file"); var processed = Output<ImageFrame>(host, "process");
            Assert.NotEqual(original.FrameId, processed.FrameId);
            Assert.Equal(17, Output<RegionAnalysisResult>(host, "threshold").Area);
            Assert.Equal(26, Output<RegionAnalysisResult>(host, "morph").Area);
            Assert.Equal(2, Output<BlobAnalysisResult>(host, "blob").Count);
            var selected = Output<BlobAnalysisResult>(host, "select");
            Assert.Equal(25, Assert.Single(selected.Blobs).Area); Assert.Equal(20, selected.Blobs[0].Features.GridPerimeter);
            Assert.Equal(processed.FrameId, selected.FrameId);
            Assert.Equal(26, Output<ColorAnalysisResult>(host, "color").PixelCount);
            using var ui = processed.Retain();
            using var preview = scope.Capture("morph"); Assert.IsType<RegionAnalysisResult>(preview!.Facts);
            Assert.True((await host.RunAsync()).Success);
            Assert.NotEqual(processed.FrameId, Output<ImageFrame>(host, "process").FrameId);
            Assert.Throws<ObjectDisposedException>(() => processed.Retain());
            var pixel = new byte[1]; ui.Image.CopyTo(0, pixel, 0, 1); Assert.Equal(0, pixel[0]);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task ActualSharedOperatorSample_RunsWithNewServices()
    {
        var nodes = new WorkflowNodeCatalog().RegisterStandardNodes().RegisterImageNodes();
        using var workspace = new WorkflowDocumentWorkspace(nodes); workspace.New("算子示例");
        DP.WorkFlow.Samples.WorkflowImageDemo.PopulateProcessing(workspace.Navigator!.RootSession);
        using var scope = new WorkflowVisionFrameScope();
        using var runtime = PluginRuntime();
        using var bindings = new WorkflowVisionAlgorithmBindings(runtime, scope);
        using var host = new WorkflowRuntimeHost(nodes, new WorkflowNodeHandlerCatalog().RegisterStandardNodeHandlers().RegisterImageNodeHandlers());
        host.Configure(workspace.Navigator.RootSession.Document, new WorkflowContext(PluginServices(scope, bindings)));
        var run = await host.RunAsync(); Assert.True(run.Success, run.Message);
        var outputs = host.Engine!.RunState.NodeOutputs.Select(o => o.Value).ToArray();
        Assert.Equal(2, outputs.OfType<ImageFrame>().Count());
        Assert.All(outputs.OfType<BlobAnalysisResult>(), b => Assert.Equal(2, b.Count));
        Assert.Equal(170, Assert.Single(outputs.OfType<ColorAnalysisResult>()).Red);
    }

    [Fact]
    public async Task CrossFrameMask_FailsWithoutCommittingConsumerOutput()
    {
        string path = Image(20, 20, (_, _) => 255);
        try
        {
            var document = RegionPipeline(path);
            ((AnalyzeVisionBlobsNodeModel)document.CanvasProjection.Nodes.Single(n => n.Node.Id == "blob").Node).Frame = Input<ImageFrame>("file");
            using var scope = new WorkflowVisionFrameScope();
            using var host = new WorkflowRuntimeHost(new WorkflowNodeCatalog().RegisterImageNodes(), new WorkflowNodeHandlerCatalog().RegisterImageNodeHandlers());
            host.Configure(document, new WorkflowContext(Services(scope)));
            Assert.False((await host.RunAsync()).Success);
            Assert.DoesNotContain(host.Engine!.RunState.NodeOutputs, o => o.NodeId is "blob" or "select" or "color");
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task MissingNewCapability_IsRejectedBeforeAcquisition()
    {
        string path = Image(20, 20, (_, _) => 255);
        try
        {
            using var scope = new WorkflowVisionFrameScope();
            using var host = new WorkflowRuntimeHost(new WorkflowNodeCatalog().RegisterImageNodes(), new WorkflowNodeHandlerCatalog().RegisterImageNodeHandlers());
            host.Configure(RegionPipeline(path), new WorkflowContext(new WithoutPreprocessor(Services(scope))));
            var error = await Assert.ThrowsAsync<WorkflowRuntimeCapabilityException>(() => host.RunAsync());
            Assert.Contains(nameof(IImagePreprocessor), error.Message, StringComparison.Ordinal);
            Assert.Empty(host.Engine!.RunState.NodeOutputs);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task ThreeCalipers_BindEvidenceIntoRobustLine(bool mixFrames, bool usePlugins)
    {
        string path = Image(64, 32, (x, y) => (byte)Math.Round(255 / (1 + Math.Exp(-(x + .5 - (20.25 + .2 * (y + .5))) / 1.2))));
        try
        {
            var calipers = new[] { 6.5, 14.5, 22.5 }.Select((y, i) => new MeasureVisionCaliperNodeModel
            { Id = "caliper" + i, Frame = Input<ImageFrame>("file"), StartX = .5, EndX = 63.5, StartY = y, EndY = y, HalfWidth = 0, Polarity = ECaliperPolarity.Rising }).ToArray();
            var fit = new FitVisionRobustLineNodeModel { Id = "fit", Frame = Input<ImageFrame>("file"), DistanceThreshold = .2,
                Samples = calipers.Select(c => Input<VisionCaliperMeasurement>(c.Id)).ToList() };
            var sequence = new List<IWorkflowNodeModel> { new LoadVisionFileNodeModel { Id = "file", FilePath = path } };
            if (mixFrames)
            {
                sequence.Add(new LoadVisionFileNodeModel { Id = "other", FilePath = path });
                calipers[1].Frame = Input<ImageFrame>("other");
            }
            sequence.AddRange(calipers); sequence.Add(fit);
            var nodes = new WorkflowNodeCatalog().RegisterImageNodes(); var store = new WorkflowDocumentJsonStore(nodes);
            var document = store.Deserialize(store.Serialize(Document(sequence.ToArray()))).Document;
            using var scope = new WorkflowVisionFrameScope();
            using var runtime = PluginRuntime();
            using var bindings = new WorkflowVisionAlgorithmBindings(runtime, scope);
            using var host = new WorkflowRuntimeHost(nodes, new WorkflowNodeHandlerCatalog().RegisterImageNodeHandlers());
            host.Configure(document, new WorkflowContext(usePlugins ? PluginServices(scope, bindings) : Services(scope)));
            var run = await host.RunAsync();
            if (mixFrames)
            {
                Assert.False(run.Success);
                Assert.DoesNotContain(host.Engine!.RunState.NodeOutputs, o => o.NodeId == "fit");
                return;
            }
            Assert.True(run.Success, run.Message);
            var result = Output<RobustLineResult>(host, "fit"); Assert.Equal(3, result.InlierCount); Assert.InRange(result.RmsError, 0, .1);
            Assert.InRange(Math.Abs(result.A.X - (20.25 + .2 * result.A.Y)), 0, .15);
            Assert.Equal(Output<ImageFrame>(host, "file").FrameId, result.FrameId);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Pose_BindsTransformAndRejectsMappingWhenNotFound(bool absent, bool usePlugins)
    {
        var pixels = new byte[] { 30,200,70,100,180,90,255,10,130,50,160,40,230,80,210 };
        string template = Image(5, 3, (x, y) => pixels[y * 5 + x]);
        string scene = Image(32, 32, (x, y) => !absent && x >= 12 && x < 17 && y >= 10 && y < 13 ? pixels[(y - 10) * 5 + x - 12] : (byte)5);
        try
        {
            var document = Document(new LoadVisionFileNodeModel { Id = "scene", FilePath = scene },
                new LoadVisionFileNodeModel { Id = "template", FilePath = template },
                new LocateVisionTemplatePoseNodeModel { Id = "pose", Frame = Input<ImageFrame>("scene"), Template = Input<ImageFrame>("template"), MinimumScore = .999,
                    MinimumAngleRadians = 0, MaximumAngleRadians = Math.PI / 2, AngleStepRadians = Math.PI / 2 },
                new MapVisionPoseCoordinateNodeModel { Id = "map", Pose = Input<TemplatePoseResult>("pose") },
                new MapVisionPoseCoordinateNodeModel { Id = "inverse", Pose = Input<TemplatePoseResult>("pose"), Inverse = true,
                    X = WorkflowInput<double>.FromBinding(new WorkflowBindingKey("map", "X")), Y = WorkflowInput<double>.FromBinding(new WorkflowBindingKey("map", "Y")) });
            var nodes = new WorkflowNodeCatalog().RegisterImageNodes(); var store = new WorkflowDocumentJsonStore(nodes);
            document = store.Deserialize(store.Serialize(document)).Document;
            using var scope = new WorkflowVisionFrameScope();
            using var runtime = PluginRuntime();
            using var bindings = new WorkflowVisionAlgorithmBindings(runtime, scope);
            using var host = new WorkflowRuntimeHost(nodes, new WorkflowNodeHandlerCatalog().RegisterImageNodeHandlers());
            host.Configure(document, new WorkflowContext(usePlugins ? PluginServices(scope, bindings) : Services(scope)));
            var run = await host.RunAsync();
            Assert.Equal(!absent, Output<TemplatePoseResult>(host, "pose").Found);
            if (absent)
            { Assert.False(run.Success); Assert.DoesNotContain(host.Engine!.RunState.NodeOutputs, o => o.NodeId is "map" or "inverse"); }
            else
            {
                Assert.True(run.Success, run.Message);
                // 参考坐标原点在模板参考点；图像模板的参考点是模板中心。
                var pose = Output<TemplatePoseResult>(host, "pose"); Assert.Equal(14.5, pose.CenterX, 8); Assert.Equal(0, pose.AngleDegrees, 8);
                var mapped = Output<Coordinate2D>(host, "map"); Assert.Equal(14.5, mapped.X, 8); Assert.Equal(11.5, mapped.Y, 8);
                var inverse = Output<Coordinate2D>(host, "inverse"); Assert.Equal(0, inverse.X, 8); Assert.Equal(0, inverse.Y, 8);
                // 匹配框和参考轴共用一条摘要标注，不在参考点处重复叠加；拾取参考轴仍得到同一说明。
                using var page = new VisionFrameEditorPageModel(document.CanvasProjection.Nodes.Single(n => n.Node.Id == "pose").Node, scope);
                using var canvas = page.Capture(1);
                var facts = canvas!.Overlay!.Layers.Single(l => l.Id == "facts").Visuals;
                Assert.Equal(4, facts.Count); Assert.Single(facts, v => v.Caption is not null);
                Assert.Equal(pose.Summary, page.Pick(new PointD(pose.ReferenceX + 3, pose.ReferenceY), .2));
            }
        }
        finally { File.Delete(template); File.Delete(scene); }
    }

    [Fact]
    public void ProcessingPages_DoNotOfferIgnoredRoiEdits()
    {
        var nodes = new WorkflowNodeCatalog().RegisterImageNodes();
        var model = new PreprocessVisionImageNodeModel { Id = "process", Frame = Input<ImageFrame>("file") };
        var document = Document(model); var session = new WorkflowDesignerSession(document, nodes);
        using var page = new VisionFrameEditorPageModel(model);
        Assert.False(page.CanEdit);
        page.Editor.Tool = DP.Vision.UI.ERoiTool.Rectangle;
        page.Editor.PointerDown(new PointD(1, 1), .1); page.Editor.PointerUp(new PointD(5, 5));
        Assert.True(model.FullImage); Assert.Empty(model.Regions);
        using var threshold = new VisionFrameEditorPageModel(new ThresholdVisionRegionNodeModel());
        Assert.True(threshold.CanEdit); Assert.True(threshold.SupportsRegions);
    }

    private static WorkflowDocument RegionPipeline(string path) => Document(
        new LoadVisionFileNodeModel { Id = "file", FilePath = path },
        new PreprocessVisionImageNodeModel { Id = "process", Frame = Input<ImageFrame>("file"), Operation = EImagePreprocessing.GainOffset },
        new ThresholdVisionRegionNodeModel { Id = "threshold", Frame = Input<ImageFrame>("process"), MinimumGray = 255, MaximumGray = 255 },
        new MorphVisionRegionNodeModel { Id = "morph", Frame = Input<ImageFrame>("process"), InputRegion = Input<RegionAnalysisResult>("threshold"), Operation = ERegionMorphology.FillHoles },
        new AnalyzeVisionBlobsNodeModel { Id = "blob", Frame = Input<ImageFrame>("process"), Mask = Input<RegionAnalysisResult>("morph"), MaximumGray = 255 },
        new SelectVisionBlobsNodeModel { Id = "select", Frame = Input<ImageFrame>("process"), Blobs = Input<BlobAnalysisResult>("blob"), MinimumArea = 5 },
        new AnalyzeVisionColorNodeModel { Id = "color", Frame = Input<ImageFrame>("process"), Mask = Input<RegionAnalysisResult>("morph") });

    private static VisionAlgorithmRuntime PluginRuntime() => new(VisionAlgorithmCatalog.Compose(new IVisionAlgorithmModule[]
        { new ManagedVisionAlgorithmModule(), new OpenCvVisionAlgorithmModule() }));
    private static WorkflowServiceProvider PluginServices(WorkflowVisionFrameScope scope, WorkflowVisionAlgorithmBindings bindings) => new WorkflowServiceProvider()
        .Add<IWorkflowVisionFrameScope>(scope).Add<IWorkflowVisionAlgorithmBindings>(bindings).Add<IWorkflowNodeCapabilityProvider>(bindings)
        .Add<IWorkflowRunPreparationService>(bindings).Add<IWorkflowRunResourceOwner>(scope);
    private static WorkflowServiceProvider Services(WorkflowVisionFrameScope scope) => new WorkflowServiceProvider()
        .Add<IImageFileReader>(new OpenCvImageFileReader()).Add<IImagePreprocessor>(new OpenCvImagePreprocessor())
        .Add<IRegionProcessor>(new OpenCvRegionProcessor()).Add<IBlobAnalyzer>(new OpenCvBlobAnalyzer()).Add<IBlobSelector>(new BlobSelector())
        .Add<IColorAnalyzer>(new RgbColorAnalyzer()).Add<ICaliperMeasurer>(new CaliperMeasurer()).Add<IRobustLineFitter>(new RobustLineFitter())
        .Add<ITemplatePoseLocator>(new OpenCvTemplatePoseLocator())
        .Add<IWorkflowVisionFrameScope>(scope).Add<IWorkflowRunPreparationService>(scope)
        // AR-01 阶段2：退役上一轮租约是运行所有者的独立职责，与示例装配保持一致。
        .Add<IWorkflowRunResourceOwner>(scope);
    private sealed class WithoutPreprocessor(IServiceProvider inner) : IServiceProvider
    {
        public object? GetService(Type serviceType) => serviceType == typeof(IImagePreprocessor) ? null : inner.GetService(serviceType);
    }

    private static WorkflowInput<T> Input<T>(string id) => WorkflowInput<T>.FromBinding(new WorkflowBindingKey(id, "$"));
    private static T Output<T>(WorkflowRuntimeHost host, string id) => Assert.IsType<T>(host.Engine!.RunState.NodeOutputs.Single(o => o.NodeId == id).Value);
    private static WorkflowDocument Document(params IWorkflowNodeModel[] nodes)
    {
        var document = new WorkflowDocument { EntryNodeId = nodes[0].Id };
        foreach (var node in nodes) document.CanvasProjection.Nodes.Add(new WorkflowCanvasNode { Node = node });
        for (int i = 1; i < nodes.Length; i++) document.CanvasProjection.Connections.Add(new WorkflowConnectionModel
        { FromNodeId = nodes[i - 1].Id, FromPort = WorkflowPorts.Success, ToNodeId = nodes[i].Id, ToPort = WorkflowPorts.Input });
        return document;
    }
    private static string Image(int width, int height, Func<int, int, byte> pixel)
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".png");
        using var mat = new Mat(height, width, MatType.CV_8UC1);
        for (int y = 0; y < height; y++) for (int x = 0; x < width; x++) mat.Set(y, x, pixel(x, y));
        Cv2.ImWrite(path, mat); return path;
    }
}
