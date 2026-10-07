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
            // 按面积降序：首个为环形连通域（填孔后25像素），孤立像素排在后面。
            Assert.Equal(new long[] { 25, 1 }, selected.Blobs.Select(b => b.Area)); Assert.Equal(20, selected.First!.Features.GridPerimeter);
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
            var sequence = new List<IWorkflowNodeModel> { new AcquireVisionImageNodeModel { Id = "file", FilePath = path } };
            if (mixFrames)
            {
                sequence.Add(new AcquireVisionImageNodeModel { Id = "other", FilePath = path });
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
    [InlineData(false)]
    [InlineData(true)]
    public async Task FindLine_SpreadsCalipersAcrossSearchBox_AndFitsSlantedEdge(bool usePlugins)
    {
        // 边缘 X = 20.25 + 0.2·Y：搜索框宽度方向竖直（卡尺上下排布），高度方向水平（从左向右扫描，由暗到亮）。
        string path = Image(64, 40, (x, y) => (byte)Math.Round(255 / (1 + Math.Exp(-(x + .5 - (20.25 + .2 * (y + .5))) / 1.2))));
        try
        {
            var find = new FindVisionLineNodeModel { Id = "line", Frame = Input<ImageFrame>("file"), CaliperCount = 8, Polarity = ECaliperPolarity.Rising,
                DistanceThreshold = .3, Regions = new() { new() { Id = "box", CenterX = 26, CenterY = 20, Width = 28, Height = 40, Angle = -Math.PI / 2 } } };
            Assert.DoesNotContain(find.ValidateConfiguration(), e => !e.Contains("输入图像", StringComparison.Ordinal));
            var nodes = new WorkflowNodeCatalog().RegisterImageNodes(); var store = new WorkflowDocumentJsonStore(nodes);
            var document = store.Deserialize(store.Serialize(Document(new AcquireVisionImageNodeModel { Id = "file", FilePath = path }, find))).Document;
            using var scope = new WorkflowVisionFrameScope();
            using var runtime = PluginRuntime();
            using var bindings = new WorkflowVisionAlgorithmBindings(runtime, scope);
            using var host = new WorkflowRuntimeHost(nodes, new WorkflowNodeHandlerCatalog().RegisterImageNodeHandlers());
            host.Configure(document, new WorkflowContext(usePlugins ? PluginServices(scope, bindings) : Services(scope)));
            var run = await host.RunAsync(); Assert.True(run.Success, run.Message);
            var result = Output<VisionFindLineResult>(host, "line");
            Assert.Equal(8, result.CaliperCount); Assert.Equal(8, result.FoundCount); Assert.Equal(8, result.InlierCount);
            foreach (var point in result.EdgePoints) Assert.InRange(Math.Abs(point.ImagePosition.X - (20.25 + .2 * point.ImagePosition.Y)), 0, .15);
            Assert.InRange(Math.Abs(result.AngleDegrees - Math.Atan2(1, .2) * 180 / Math.PI), 0, .3);
            Assert.Equal(Output<ImageFrame>(host, "file").FrameId, result.MeasuredLine.A.FrameId);
            Assert.Equal(1 + 8 + 8, result.DisplayGeometry.Count);
            Assert.Contains("8/8", result.Summary);
            Assert.All(result.Inliers, Assert.True);
            Assert.Empty(result.OutlierPoints);
            Assert.Equal(8, result.CaliperScans.Count);
            Assert.Empty(result.EdgePairs);
            Assert.Null(result.MeanWidth);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FindCircle_RadialCalipersRecoverDiskEdge_AndSkipCalipersOutsideImage(bool usePlugins)
    {
        // 亮圆盘：圆心 (40.3, 39.7)、半径 20.5；期望圆偏开0.3像素，由内向外扫描为由亮到暗。
        string path = Image(80, 80, (x, y) =>
        {
            double d = Math.Sqrt(Math.Pow(x + .5 - 40.3, 2) + Math.Pow(y + .5 - 39.7, 2));
            return (byte)Math.Round(255 / (1 + Math.Exp((d - 20.5) / 1.0)));
        });
        try
        {
            var find = new FindVisionCircleNodeModel { Id = "circle", Frame = Input<ImageFrame>("file"), CaliperCount = 16, SearchLength = 12,
                Polarity = ECaliperPolarity.Falling, DistanceThreshold = .3,
                Regions = new() { new() { Id = "circle", Shape = EWorkflowVisionRoiShape.Ellipse, CenterX = 40, CenterY = 40, Width = 40, Height = 40 } } };
            var nodes = new WorkflowNodeCatalog().RegisterImageNodes(); var store = new WorkflowDocumentJsonStore(nodes);
            var document = store.Deserialize(store.Serialize(Document(new AcquireVisionImageNodeModel { Id = "file", FilePath = path }, find))).Document;
            using var scope = new WorkflowVisionFrameScope();
            using var runtime = PluginRuntime();
            using var bindings = new WorkflowVisionAlgorithmBindings(runtime, scope);
            using var host = new WorkflowRuntimeHost(nodes, new WorkflowNodeHandlerCatalog().RegisterImageNodeHandlers());
            host.Configure(document, new WorkflowContext(usePlugins ? PluginServices(scope, bindings) : Services(scope)));
            var run = await host.RunAsync(); Assert.True(run.Success, run.Message);
            var result = Output<VisionFindCircleResult>(host, "circle");
            Assert.Equal(16, result.FoundCount); Assert.Equal(16, result.InlierCount);
            Assert.Equal(40.3, result.MeasuredCenter.ImagePosition.X, 1); Assert.Equal(39.7, result.MeasuredCenter.ImagePosition.Y, 1);
            Assert.InRange(Math.Abs(result.Radius - 20.5), 0, .1); Assert.Equal(2 * result.Radius, result.Diameter, 12);
            Assert.Null(result.LocalRadius);

            // 加大期望圆和搜索长度：上下左右四把卡尺的扫描端超出图像被跳过并计数，其余卡尺仍拟合出同一个圆。
            var shifted = (FindVisionCircleNodeModel)document.CanvasProjection.Nodes.Single(n => n.Node.Id == "circle").Node;
            shifted.SearchLength = 30; shifted.Regions[0].Width = shifted.Regions[0].Height = 50; shifted.DistanceThreshold = .5;
            using var second = new WorkflowRuntimeHost(nodes, new WorkflowNodeHandlerCatalog().RegisterImageNodeHandlers());
            second.Configure(document, new WorkflowContext(usePlugins ? PluginServices(scope, bindings) : Services(scope)));
            run = await second.RunAsync(); Assert.True(run.Success, run.Message);
            var partial = Output<VisionFindCircleResult>(second, "circle");
            Assert.True(partial.FoundCount < 16); Assert.Contains("超出图像", partial.Summary);
            Assert.InRange(Math.Abs(partial.Radius - 20.5), 0, .15);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void FindShapes_RequireOneSearchRoiOfTheRightShape()
    {
        var line = new FindVisionLineNodeModel { Frame = Input<ImageFrame>("file") };
        Assert.Empty(line.ValidateConfiguration()); // 新节点已带可编辑的默认搜索框。
        line.Regions.Clear();
        Assert.Contains(line.ValidateConfiguration(), e => e.Contains("矩形搜索框", StringComparison.Ordinal));
        line.Regions.Add(new WorkflowVisionRoi { Id = "e", Shape = EWorkflowVisionRoiShape.Ellipse, Width = 10, Height = 10 });
        Assert.Contains(line.ValidateConfiguration(), e => e.Contains("矩形搜索框", StringComparison.Ordinal));
        Assert.False(line.SupportsRegionMask);
        var circle = new FindVisionCircleNodeModel { Frame = Input<ImageFrame>("file") };
        circle.Regions.Clear();
        circle.Regions.Add(new WorkflowVisionRoi { Id = "e", Shape = EWorkflowVisionRoiShape.Ellipse, CenterX = 20, CenterY = 20, Width = 20, Height = 12 });
        Assert.Contains(circle.ValidateConfiguration(), e => e.Contains("宽高相等", StringComparison.Ordinal));
        circle.Regions[0].Height = 20; circle.SearchLength = 30;
        Assert.Contains(circle.ValidateConfiguration(), e => e.Contains("不能超过期望圆半径", StringComparison.Ordinal));
        circle.SearchLength = 10; circle.MinimumInliers = 2;
        Assert.Contains(circle.ValidateConfiguration(), e => e.Contains("最少内点", StringComparison.Ordinal));
        circle.MinimumInliers = 3; Assert.Empty(circle.ValidateConfiguration());
        Assert.Equal(new[] { "caliper", "fitter" }, ((IWorkflowVisionAlgorithmNode)circle).GetAlgorithmSlots().Select(s => s.Name));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Pose_ReportsTransform_OrNotFoundWithoutFakePose(bool absent, bool usePlugins)
    {
        var pixels = new byte[] { 30,200,70,100,180,90,255,10,130,50,160,40,230,80,210 };
        string template = Image(5, 3, (x, y) => pixels[y * 5 + x]);
        string scene = Image(32, 32, (x, y) => !absent && x >= 12 && x < 17 && y >= 10 && y < 13 ? pixels[(y - 10) * 5 + x - 12] : (byte)5);
        try
        {
            var document = Document(new AcquireVisionImageNodeModel { Id = "scene", FilePath = scene },
                new AcquireVisionImageNodeModel { Id = "template", FilePath = template },
                new LocateVisionTemplatePoseNodeModel { Id = "pose", Frame = Input<ImageFrame>("scene"), Template = Input<ImageFrame>("template"), MinimumScore = .999,
                    MinimumAngleRadians = 0, MaximumAngleRadians = Math.PI / 2, AngleStepRadians = Math.PI / 2 });
            var nodes = new WorkflowNodeCatalog().RegisterImageNodes(); var store = new WorkflowDocumentJsonStore(nodes);
            document = store.Deserialize(store.Serialize(document)).Document;
            using var scope = new WorkflowVisionFrameScope();
            using var runtime = PluginRuntime();
            using var bindings = new WorkflowVisionAlgorithmBindings(runtime, scope);
            using var host = new WorkflowRuntimeHost(nodes, new WorkflowNodeHandlerCatalog().RegisterImageNodeHandlers());
            host.Configure(document, new WorkflowContext(usePlugins ? PluginServices(scope, bindings) : Services(scope)));
            var run = await host.RunAsync();
            Assert.True(run.Success, run.Message);
            var pose = Output<TemplatePoseResult>(host, "pose");
            Assert.Equal(!absent, pose.Found);
            if (absent)
            { Assert.True(double.IsNaN(pose.CenterX)); Assert.Null(pose.ReferenceToImage); }
            else
            {
                // 参考点即图像模板中心；参考映射把参考原点映射到参考点。
                Assert.Equal(14.5, pose.CenterX, 8); Assert.Equal(0, pose.AngleDegrees, 8);
                var origin = pose.ReferenceToImage!.Map(new Coordinate2D(0, 0)); Assert.Equal(14.5, origin.X, 8); Assert.Equal(11.5, origin.Y, 8);
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
        new AcquireVisionImageNodeModel { Id = "file", FilePath = path },
        new PreprocessVisionImageNodeModel { Id = "process", Frame = Input<ImageFrame>("file"), Operation = EImagePreprocessing.GainOffset },
        new ThresholdVisionRegionNodeModel { Id = "threshold", Frame = Input<ImageFrame>("process"), MinimumGray = 255, MaximumGray = 255 },
        new MorphVisionRegionNodeModel { Id = "morph", Frame = Input<ImageFrame>("process"), InputRegion = Input<RegionAnalysisResult>("threshold"), Operation = ERegionMorphology.FillHoles },
        new AnalyzeVisionBlobsNodeModel { Id = "blob", Frame = Input<ImageFrame>("process"), Mask = Input<RegionAnalysisResult>("morph"), MaximumGray = 255 },
        new SelectVisionBlobsNodeModel { Id = "select", Frame = Input<ImageFrame>("process"), Blobs = Input<BlobAnalysisResult>("blob"),
            SortKey = EBlobSortKey.Area, Descending = true },
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
        .Add<IRobustCircleFitter>(new RobustCircleFitter()).Add<ITemplatePoseLocator>(new OpenCvTemplatePoseLocator())
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
