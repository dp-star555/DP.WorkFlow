using DP.Vision;
using DP.Vision.Algorithms;

namespace DP.WorkFlow.Tests;

/// <summary>图像预处理、区域形态学、筛选连通域的ROI：只在ROI内生效。</summary>
public sealed class VisionPostProcessingRoiTests
{
    private const int Size = 8;

    // 左半 (x 0..3) 的矩形ROI。
    private static WorkflowVisionRoi LeftHalf() => new()
    { Id = "roi", Shape = EWorkflowVisionRoiShape.Rectangle, CenterX = 2, CenterY = 4, Width = 4, Height = 8 };

    [Fact]
    public async Task Preprocess_WithRoi_OnlyChangesPixelsInsideRoi()
    {
        var source = Source();
        var invert = new PreprocessVisionImageNodeModel { Id = "invert", Frame = Input<ImageFrame>(source.Id), Operation = EImagePreprocessing.Invert, Algorithm = Selection("test.invert") };
        invert.Regions.Add(LeftHalf());
        // 帧由运行期帧仓持有，在运行环境释放前读出像素。
        var pixels = await RunAsync<ImageFrame, byte[]>(invert.Id, frame =>
        {
            var copy = new byte[Size * Size]; frame.Image.CopyTo(0, copy, 0, copy.Length); return copy;
        }, source, invert);
        Assert.All(Enumerable.Range(0, Size * Size), i => Assert.Equal(i % Size < 4 ? 245 : 10, pixels[i]));
    }

    [Fact]
    public void Preprocess_FormatConversionWithRoi_IsRejected()
    {
        var node = new PreprocessVisionImageNodeModel { Operation = EImagePreprocessing.Grayscale };
        node.Regions.Add(LeftHalf());
        Assert.Contains(node.ValidateConfiguration(), e => e.Contains("只能整图处理", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Morph_WithRoi_ResultStaysInsideRoi()
    {
        var source = Source();
        var threshold = new ThresholdVisionRegionNodeModel { Id = "threshold", Frame = Input<ImageFrame>(source.Id), Algorithm = Selection("test.region") };
        var morph = new MorphVisionRegionNodeModel { Id = "morph", Frame = Input<ImageFrame>(source.Id), InputRegion = Input<RegionAnalysisResult>(threshold.Id), Algorithm = Selection("test.region") };
        morph.Regions.Add(LeftHalf());
        var result = await RunAsync<RegionAnalysisResult>(morph.Id, source, threshold, morph);
        Assert.Equal(4 * Size, result.Area);
        Assert.All(result.Region.Runs, run => Assert.True(run.Start >= 0 && run.EndExclusive <= 4));
    }

    [Fact]
    public async Task SelectBlobs_WithRoi_KeepsBlobsWhoseCentroidIsInsideRoi()
    {
        var source = Source();
        var blobs = new AnalyzeVisionBlobsNodeModel { Id = "blobs", Frame = Input<ImageFrame>(source.Id), Algorithm = Selection("test.blobs") };
        var select = new SelectVisionBlobsNodeModel { Id = "select", Frame = Input<ImageFrame>(source.Id), Blobs = Input<BlobAnalysisResult>(blobs.Id), Algorithm = Selection("test.select") };
        select.Regions.Add(LeftHalf());
        var result = await RunAsync<BlobAnalysisResult>(select.Id, source, blobs, select);
        var blob = Assert.Single(result.Blobs);
        Assert.True(blob.Centroid.X < 4);
    }

    // 文件内容由测试读图器忽略，只需存在。
    private static readonly string SourceFile = CreateSourceFile();
    private static string CreateSourceFile()
    {
        var path = Path.Combine(Path.GetTempPath(), "post-processing-roi-" + Guid.NewGuid().ToString("N") + ".png");
        File.WriteAllText(path, "test reader input");
        return path;
    }

    private static AcquireVisionImageNodeModel Source() => new() { Id = "source", FilePath = SourceFile, Algorithm = Selection("test.read") };

    private static Task<T> RunAsync<T>(string outputNodeId, params IWorkflowNodeModel[] nodes) => RunAsync<T, T>(outputNodeId, value => value, nodes);

    private static async Task<TResult> RunAsync<T, TResult>(string outputNodeId, Func<T, TResult> read, params IWorkflowNodeModel[] nodes)
    {
        using var runtime = new VisionAlgorithmRuntime(VisionAlgorithmCatalog.Compose(new[] { new TestModule() }));
        using var frames = new WorkflowVisionFrameScope();
        using var bindings = new WorkflowVisionAlgorithmBindings(runtime, frames);
        var services = new WorkflowServiceProvider()
            .Add<IWorkflowVisionFrameScope>(frames).Add<IWorkflowVisionAlgorithmBindings>(bindings).Add<IWorkflowNodeCapabilityProvider>(bindings)
            .Add<IWorkflowRunPreparationService>(bindings).Add<IWorkflowRunResourceOwner>(frames);
        var document = new WorkflowDocument { EntryNodeId = nodes[0].Id };
        foreach (var node in nodes) document.CanvasProjection.Nodes.Add(new WorkflowCanvasNode { Node = node });
        for (int i = 1; i < nodes.Length; i++) document.CanvasProjection.Connections.Add(new WorkflowConnectionModel
            { FromNodeId = nodes[i - 1].Id, FromPort = WorkflowPorts.Success, ToNodeId = nodes[i].Id, ToPort = WorkflowPorts.Input });
        using var host = new WorkflowRuntimeHost(new WorkflowNodeCatalog().RegisterImageNodes(), new WorkflowNodeHandlerCatalog().RegisterImageNodeHandlers());
        host.Configure(document, new WorkflowContext(services));
        var run = await host.RunAsync();
        Assert.True(run.Success, run.Message);
        return read(Assert.IsAssignableFrom<T>(host.Engine!.RunState.NodeOutputs.Single(o => o.NodeId == outputNodeId).Value));
    }

    private static VisionAlgorithmSelection Selection(string id) => new() { ImplementationId = id };
    private static WorkflowInput<T> Input<T>(string id) => WorkflowInput<T>.FromBinding(new WorkflowBindingKey(id, "$"));

    private sealed class TestModule : IVisionAlgorithmModule
    {
        public string ExtensionId => "test.post-processing-roi";
        public void Register(IVisionAlgorithmRegistration registrations)
        {
            registrations.Add(new VisionAlgorithmDescriptor("test.read", "Test", "1", VisionAlgorithmFactory<IImageFileReader>.Stateless(() => new FlatReader())));
            registrations.Add(new VisionAlgorithmDescriptor("test.invert", "Test", "1", VisionAlgorithmFactory<IImagePreprocessor>.Stateless(() => new Invert())));
            registrations.Add(new VisionAlgorithmDescriptor("test.region", "Test", "1", VisionAlgorithmFactory<IRegionProcessor>.Stateless(() => new WholeRegion())));
            registrations.Add(new VisionAlgorithmDescriptor("test.blobs", "Test", "1", VisionAlgorithmFactory<IBlobAnalyzer>.Stateless(() => new TwoBlobs())));
            registrations.Add(new VisionAlgorithmDescriptor("test.select", "Test", "1", VisionAlgorithmFactory<IBlobSelector>.Stateless(() => new BlobSelector())));
        }
    }

    private sealed class FlatReader : IImageFileReader
    {
        public Task<IImageSource> ReadAsync(string path, CancellationToken token = default) =>
            Task.FromResult(VisionImage.CopyFrom(new ImageInfo(Size, Size, EPixelLayout.Gray8), Enumerable.Repeat((byte)10, Size * Size).ToArray()));
    }

    private sealed class Invert : IImagePreprocessor
    {
        public IImageSource Process(IImageSource image, ImagePreprocessingOptions options, CancellationToken token = default)
        {
            var pixels = new byte[image.Info.ByteLength]; image.CopyTo(0, pixels, 0, pixels.Length);
            return VisionImage.CopyFrom(image.Info, pixels.Select(p => (byte)(255 - p)).ToArray());
        }
    }

    private sealed class WholeRegion : IRegionProcessor
    {
        public RegionAnalysisResult Threshold(ImageFrame frame, PixelBounds bounds, int minimumGray, int maximumGray, RegionGeometry? mask = null, CancellationToken token = default) =>
            new(frame.FrameId, Size, Size, new RegionGeometry(Enumerable.Range(0, Size).Select(y => new RegionRun(y, 0, Size))));
        // 膨胀到整张图，验证结果会被裁回ROI。
        public RegionAnalysisResult Morphology(RegionAnalysisResult input, ERegionMorphology operation, int radius = 1, ERegionKernel kernel = ERegionKernel.Rectangle, CancellationToken token = default) =>
            new(input.FrameId, Size, Size, new RegionGeometry(Enumerable.Range(0, Size).Select(y => new RegionRun(y, 0, Size))));
    }

    private sealed class TwoBlobs : IBlobAnalyzer
    {
        public BlobAnalysisResult Analyze(ImageFrame frame, PixelBounds bounds, BlobOptions options, CancellationToken token = default, RegionGeometry? regionMask = null) =>
            new(frame.FrameId, new[]
            {
                new BlobObservation(new RegionGeometry(new[] { new RegionRun(1, 1, 3), new RegionRun(2, 1, 3) })),
                new BlobObservation(new RegionGeometry(new[] { new RegionRun(5, 5, 7), new RegionRun(6, 5, 7) }))
            });
    }
}
