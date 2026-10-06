using DP.WorkFlow.Persistence.Json;
using DP.Vision;
using DP.Vision.Algorithms;
using DP.Vision.OpenCv;
using OpenCvSharp;

namespace DP.WorkFlow.Tests;

public sealed class NewVisionFilePipelineTests
{
    [Fact]
    public async Task RealFile_JsonRoundTrip_BindingsAndStandardOutputs()
    {
        string path = CreateImage();
        try
        {
            var nodes = new WorkflowNodeCatalog().RegisterImageNodes();
            var handlers = new WorkflowNodeHandlerCatalog().RegisterImageNodeHandlers();
            new WorkflowRuntimePluginCatalog(nodes, handlers).Freeze();
            var store = new WorkflowDocumentJsonStore(nodes);
            var source = CreateDocument(path);
            var document = store.Deserialize(store.Serialize(source)).Document;
            var plan = new WorkflowCompiler(nodes).Compile(document);
            var bound = new WorkflowRuntimeBinder(handlers).Bind(plan);
            using var frames = new WorkflowVisionFrameScope();
            var services = Services(frames);
            WorkflowRuntimeCapabilityValidator.Validate(bound, services);
            var context = new WorkflowContext(services);
            var engine = new WorkflowEngine(plan, handlers, context);
            var run = await engine.RunAsync();
            Assert.True(run.Success, run.Message);
            var outputs = engine.RunState.NodeOutputs;
            // 连通域分析只支持8位灰度：取图节点按“8位灰度”输出；颜色分析读取原样彩色帧。
            var frame = Assert.IsType<ImageFrame>(outputs.Single(o => o.NodeId == "File").Value);
            var original = Assert.IsType<ImageFrame>(outputs.Single(o => o.NodeId == "Original").Value);
            Assert.Equal(EPixelLayout.Gray8, frame.Image.Info.Layout);
            Assert.Equal(EPixelLayout.Bgr24, original.Image.Info.Layout);
            var blobs = Assert.IsType<BlobAnalysisResult>(outputs.Single(o => o.NodeId == "Blobs").Value);
            var color = Assert.IsType<ColorAnalysisResult>(outputs.Single(o => o.NodeId == "Color").Value);
            Assert.Equal(1, blobs.Count);
            Assert.Equal(1, blobs.Blobs[0].Area);
            Assert.Equal(1.5, blobs.Blobs[0].Centroid.X);
            Assert.Equal(frame.FrameId, blobs.FrameId);
            Assert.Equal(original.FrameId, color.FrameId);
            Assert.Equal(8 * 255d / 9, color.Red, 8);
            Assert.Equal(0d, color.Blue);
            Assert.False(context.TryGetVariable<object>("VisionImage", out _));
            using var displayLease = frame.Retain();
            frames.Dispose();
            Assert.Throws<ObjectDisposedException>(() => frame.Retain());
            Assert.Equal(3, displayLease.Image.Info.Width);
            var pixels = new byte[9]; displayLease.Image.CopyTo(0, pixels, 0, pixels.Length);
            Assert.Equal(76, pixels[0]); Assert.Equal(0, pixels[4]);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task OriginalColorFrame_IsRejectedByBlobAnalysis()
    {
        string path = CreateImage();
        try
        {
            var document = CreateDocument(path);
            var acquire = (AcquireVisionImageNodeModel)document.CanvasProjection.Nodes.Single(n => n.Node.Id == "File").Node;
            acquire.PixelFormat = EWorkflowVisionPixelFormat.Original;
            using var frames = new WorkflowVisionFrameScope();
            var nodes = new WorkflowNodeCatalog().RegisterImageNodes();
            var engine = new WorkflowEngine(new WorkflowCompiler(nodes).Compile(document),
                new WorkflowNodeHandlerCatalog().RegisterImageNodeHandlers(), new WorkflowContext(Services(frames)));
            var run = await engine.RunAsync();
            Assert.False(run.Success);
            Assert.Contains("只支持8位灰度图像", run.Message);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void PixelFormat_OffersLabeledChoices()
    {
        var choices = new AcquireVisionImageNodeModel().GetPropertyChoices(nameof(AcquireVisionImageNodeModel.PixelFormat), []);
        Assert.Equal(["保持原样", "8位灰度"], choices.Select(c => c.Key));
        Assert.Equal([EWorkflowVisionPixelFormat.Original, EWorkflowVisionPixelFormat.Gray8], choices.Select(c => (EWorkflowVisionPixelFormat)c.Value!));
        Assert.Equal(EWorkflowVisionPixelFormat.Original, new AcquireVisionImageNodeModel().PixelFormat);
    }

    [Fact]
    public async Task Host_MissingBlobCapability_FailsBeforeFileRead()
    {
        string path = CreateImage();
        try
        {
            using var frames = new WorkflowVisionFrameScope();
            var reader = new CountingReader();
            var services = new WorkflowServiceProvider()
                .Add<IImageFileReader>(reader).Add<IWorkflowVisionFrameScope>(frames)
                .Add<IColorAnalyzer>(new RgbColorAnalyzer());
            using var host = new WorkflowRuntimeHost(new WorkflowNodeCatalog().RegisterImageNodes(),
                new WorkflowNodeHandlerCatalog().RegisterImageNodeHandlers());
            host.Configure(CreateDocument(path), new WorkflowContext(services));
            await Assert.ThrowsAsync<WorkflowRuntimeCapabilityException>(() => host.RunAsync());
            Assert.Equal(0, reader.ReadCount);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void InvalidThreshold_IsRejectedDuringCompilation()
    {
        string path = CreateImage();
        try
        {
            var document = CreateDocument(path);
            var node = (AnalyzeVisionBlobsNodeModel)document.CanvasProjection.Nodes.Single(n => n.Node.Id == "Blobs").Node;
            node.MinimumGray = 200; node.MaximumGray = 20;
            Assert.Throws<WorkflowCompilationException>(() => new WorkflowCompiler(new WorkflowNodeCatalog().RegisterImageNodes()).Compile(document));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task CorruptFile_ProducesNoStandardOutput()
    {
        string path = CreateImage();
        try
        {
            File.WriteAllBytes(path, new byte[] { 1, 2, 3 });
            using var frames = new WorkflowVisionFrameScope();
            var nodes = new WorkflowNodeCatalog().RegisterImageNodes();
            var plan = new WorkflowCompiler(nodes).Compile(CreateDocument(path));
            var engine = new WorkflowEngine(plan, new WorkflowNodeHandlerCatalog().RegisterImageNodeHandlers(),
                new WorkflowContext(Services(frames)));
            Assert.False((await engine.RunAsync()).Success);
            Assert.Empty(engine.RunState.NodeOutputs);
        }
        finally { File.Delete(path); }
    }

    private static WorkflowServiceProvider Services(WorkflowVisionFrameScope frames) => new WorkflowServiceProvider()
        .Add<IImageFileReader>(new OpenCvImageFileReader()).Add<IWorkflowVisionFrameScope>(frames)
        .Add<IBlobAnalyzer>(new OpenCvBlobAnalyzer()).Add<IColorAnalyzer>(new RgbColorAnalyzer());

    private static WorkflowDocument CreateDocument(string path)
    {
        var document = new WorkflowDocument { EntryNodeId = "File" };
        document.CanvasProjection.Nodes.Add(new WorkflowCanvasNode { Node = new AcquireVisionImageNodeModel
        {
            Id = "File", SourceMode = EWorkflowVisionImageSource.File, FilePath = path, PixelFormat = EWorkflowVisionPixelFormat.Gray8
        } });
        document.CanvasProjection.Nodes.Add(new WorkflowCanvasNode { Node = new AnalyzeVisionBlobsNodeModel
        {
            Id = "Blobs", Frame = WorkflowInput<ImageFrame>.FromBinding(new WorkflowBindingKey("File", "$")), MaximumGray = 0
        } });
        document.CanvasProjection.Nodes.Add(new WorkflowCanvasNode { Node = new LoadVisionFileNodeModel { Id = "Original", FilePath = path } });
        document.CanvasProjection.Nodes.Add(new WorkflowCanvasNode { Node = new AnalyzeVisionColorNodeModel
        {
            Id = "Color", Frame = WorkflowInput<ImageFrame>.FromBinding(new WorkflowBindingKey("Original", "$"))
        } });
        document.CanvasProjection.Connections.Add(new WorkflowConnectionModel { FromNodeId = "File", FromPort = WorkflowPorts.Success, ToNodeId = "Blobs", ToPort = WorkflowPorts.Input });
        document.CanvasProjection.Connections.Add(new WorkflowConnectionModel { FromNodeId = "Blobs", FromPort = WorkflowPorts.Success, ToNodeId = "Original", ToPort = WorkflowPorts.Input });
        document.CanvasProjection.Connections.Add(new WorkflowConnectionModel { FromNodeId = "Original", FromPort = WorkflowPorts.Success, ToNodeId = "Color", ToPort = WorkflowPorts.Input });
        return document;
    }

    private static string CreateImage()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".png");
        using var image = new Mat(3, 3, MatType.CV_8UC3, new Scalar(0, 0, 255));
        image.Set(1, 1, new Vec3b(0, 0, 0));
        File.WriteAllBytes(path, image.ToBytes(".png"));
        return path;
    }

    private sealed class CountingReader : IImageFileReader
    {
        public int ReadCount { get; private set; }
        public Task<IImageSource> ReadAsync(string path, CancellationToken token = default)
        {
            ReadCount++;
            throw new InvalidOperationException("Must not read when a downstream capability is missing.");
        }
    }
}
