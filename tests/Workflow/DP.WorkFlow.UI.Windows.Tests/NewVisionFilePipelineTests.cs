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
            var frame = Assert.IsType<ImageFrame>(outputs.Single(o => o.NodeId == "File").Value);
            var blobs = Assert.IsType<BlobAnalysisResult>(outputs.Single(o => o.NodeId == "Blobs").Value);
            var color = Assert.IsType<ColorAnalysisResult>(outputs.Single(o => o.NodeId == "Color").Value);
            Assert.Equal(1, blobs.Count);
            Assert.Equal(1, blobs.Blobs[0].Area);
            Assert.Equal(1.5, blobs.Blobs[0].Centroid.X);
            Assert.Equal(frame.FrameId, blobs.FrameId);
            Assert.Equal(frame.FrameId, color.FrameId);
            Assert.Equal(8 * 255d / 9, color.Red, 8);
            Assert.Equal(0d, color.Blue);
            Assert.False(context.TryGetVariable<object>("VisionImage", out _));
            using var displayLease = frame.Retain();
            frames.Dispose();
            Assert.Throws<ObjectDisposedException>(() => frame.Retain());
            Assert.Equal(3, displayLease.Image.Info.Width);
            var pixels = new byte[27]; displayLease.Image.CopyTo(0, pixels, 0, pixels.Length);
            Assert.Equal(255, pixels[2]);
        }
        finally { File.Delete(path); }
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
        var input = WorkflowInput<ImageFrame>.FromBinding(new WorkflowBindingKey("File", "$"));
        document.CanvasProjection.Nodes.Add(new WorkflowCanvasNode { Node = new LoadVisionFileNodeModel { Id = "File", FilePath = path } });
        document.CanvasProjection.Nodes.Add(new WorkflowCanvasNode { Node = new AnalyzeVisionBlobsNodeModel { Id = "Blobs", Frame = input, MaximumGray = 0 } });
        document.CanvasProjection.Nodes.Add(new WorkflowCanvasNode { Node = new AnalyzeVisionColorNodeModel { Id = "Color", Frame = input } });
        document.CanvasProjection.Connections.Add(new WorkflowConnectionModel { FromNodeId = "File", FromPort = WorkflowPorts.Success, ToNodeId = "Blobs", ToPort = WorkflowPorts.Input });
        document.CanvasProjection.Connections.Add(new WorkflowConnectionModel { FromNodeId = "Blobs", FromPort = WorkflowPorts.Success, ToNodeId = "Color", ToPort = WorkflowPorts.Input });
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
