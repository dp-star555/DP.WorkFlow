using DP.Vision;
using DP.Vision.Algorithms;
using DP.WorkFlow.Persistence.Json;

namespace DP.WorkFlow.Tests;

public sealed class UnifiedVisionAcquisitionTests
{
    [Theory]
    [InlineData(EWorkflowVisionImageSource.File)]
    [InlineData(EWorkflowVisionImageSource.Folder)]
    [InlineData(EWorkflowVisionImageSource.AreaCamera)]
    [InlineData(EWorkflowVisionImageSource.LineCamera)]
    public void RecipeCompilation_DoesNotThrowCameraOnlyGetterExceptions(EWorkflowVisionImageSource mode)
    {
        var directory = Path.Combine(Path.GetTempPath(), "vision-compile-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, "image.png");
        File.WriteAllText(file, "compiler only checks existence");
        try
        {
            var node = new AcquireVisionImageNodeModel { Id = "image", SourceMode = mode,
                FilePath = file, FolderPath = directory, Source = new("Camera.Top") };
            var document = new WorkflowDocument { EntryNodeId = node.Id };
            document.CanvasProjection.Nodes.Add(new() { Node = node });
            var catalog = new WorkflowNodeCatalog().RegisterImageNodes();
            var store = new WorkflowDocumentJsonStore(catalog);
            var threadId = Environment.CurrentManagedThreadId;
            var exceptions = new List<string>();
            EventHandler<System.Runtime.ExceptionServices.FirstChanceExceptionEventArgs> observe = (_, args) =>
            {
                if (Environment.CurrentManagedThreadId == threadId
                    && args.Exception is InvalidOperationException
                    && args.Exception.Message == "当前图像来源不是相机。")
                    exceptions.Add(args.Exception + Environment.NewLine + Environment.StackTrace);
            };
            AppDomain.CurrentDomain.FirstChanceException += observe;
            try
            {
                var restored = store.Deserialize(store.Serialize(document)).Document;
                _ = new WorkflowCompiler(catalog).Compile(restored);
            }
            finally { AppDomain.CurrentDomain.FirstChanceException -= observe; }
            Assert.True(exceptions.Count == 0, string.Join(Environment.NewLine, exceptions));
        }
        finally { File.Delete(file); Directory.Delete(directory); }
    }

    [Fact]
    public async Task File_UsesSelectedEngine_WithoutCameraOrGlobalDecoder_AndKeepsFrameIdentityFresh()
    {
        using var rig = new Rig(EWorkflowVisionImageSource.File);
        Assert.True((await rig.Host.RunAsync()).Success);
        Assert.Equal(1, rig.Pixel());
        var first = rig.FrameId();
        Assert.True((await rig.Host.RunAsync()).Success);
        Assert.NotEqual(first, rig.FrameId());
        Assert.Equal(2, rig.Reader.Reads);
        Assert.Equal(0, rig.LegacyReader.Reads);
        using var preview = rig.Frames.Capture(rig.Node.Id);
        Assert.Equal(rig.FrameId(), preview!.Frame.FrameId);
    }

    [Fact]
    public async Task Folder_DefaultContinuesAcrossRootRuns_AndExhaustionDoesNotRepeat()
    {
        using var rig = new Rig(EWorkflowVisionImageSource.Folder);
        Assert.True((await rig.Host.RunAsync()).Success); Assert.Equal(1, rig.Pixel());
        var first = rig.FrameId();
        Assert.True((await rig.Host.RunAsync()).Success); Assert.Equal(2, rig.Pixel());
        Assert.NotEqual(first, rig.FrameId());
        var exhausted = await rig.Host.RunAsync();
        Assert.False(exhausted.Success);
        Assert.Equal(2, rig.Reader.Reads);
        Assert.Equal(0, rig.LegacyReader.Reads);
    }

    [Theory]
    [InlineData(true, false, 1, 1, 1)]
    [InlineData(false, true, 1, 2, 1)]
    public async Task Folder_ResetAndLoopAreExplicit(bool restart, bool loop, int first, int second, int third)
    {
        using var rig = new Rig(EWorkflowVisionImageSource.Folder, restart, loop);
        foreach (var expected in new[] { first, second, third })
        { Assert.True((await rig.Host.RunAsync()).Success); Assert.Equal(expected, rig.Pixel()); }
    }

    [Fact]
    public async Task Folder_DecodeFailureDoesNotSkipFile()
    {
        using var rig = new Rig(EWorkflowVisionImageSource.Folder);
        rig.Reader.FailNext = true;
        Assert.False((await rig.Host.RunAsync()).Success);
        Assert.True((await rig.Host.RunAsync()).Success);
        Assert.Equal(1, rig.Pixel());
        Assert.Equal(2, rig.Reader.Reads);
    }

    [Fact]
    public async Task Folder_ChangingListingOrLeavingModeResetsSequence()
    {
        using var rig = new Rig(EWorkflowVisionImageSource.Folder);
        Assert.True((await rig.Host.RunAsync()).Success); Assert.Equal(1, rig.Pixel());
        File.WriteAllText(Path.Combine(rig.DirectoryPath, "00.png"), "0");
        Assert.True((await rig.Host.RunAsync()).Success); Assert.Equal(0, rig.Pixel());
        rig.Node.SourceMode = EWorkflowVisionImageSource.File; rig.Configure();
        Assert.True((await rig.Host.RunAsync()).Success); Assert.Equal(1, rig.Pixel());
        rig.Node.SourceMode = EWorkflowVisionImageSource.Folder; rig.Configure();
        Assert.True((await rig.Host.RunAsync()).Success); Assert.Equal(0, rig.Pixel());
    }

    [Theory]
    [InlineData(EWorkflowVisionImageSource.File)]
    [InlineData(EWorkflowVisionImageSource.Folder)]
    public async Task MissingSelectedDecoderFailsBeforeReading(EWorkflowVisionImageSource mode)
    {
        using var rig = new Rig(mode);
        rig.Node.Algorithm.ImplementationId = "missing.decoder"; rig.Configure();
        await Assert.ThrowsAsync<InvalidOperationException>(() => rig.Host.RunAsync());
        Assert.Equal(0, rig.Reader.Reads);
        Assert.Empty(rig.Host.Engine!.RunState.NodeOutputs);
    }

    [Theory]
    [InlineData(EWorkflowVisionImageSource.File)]
    [InlineData(EWorkflowVisionImageSource.Folder)]
    [InlineData(EWorkflowVisionImageSource.AreaCamera)]
    [InlineData(EWorkflowVisionImageSource.LineCamera)]
    public void SerializationKeepsDormantSettings_AndStoresCameraSourceOnlyOnce(EWorkflowVisionImageSource mode)
    {
        var node = new AcquireVisionImageNodeModel { Id = "input", SourceMode = mode, FilePath = "file.png", FolderPath = "folder",
            Source = new("Camera.Top"), Algorithm = new() { ImplementationId = "private.reader", Settings = new() { ["model"] = "private" } } };
        var store = new WorkflowDocumentJsonStore(new WorkflowNodeCatalog().RegisterImageNodes());
        var document = new WorkflowDocument { EntryNodeId = node.Id }; document.CanvasProjection.Nodes.Add(new() { Node = node });
        var json = store.Serialize(document);
        Assert.DoesNotContain("AreaSource", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("LineSource", json, StringComparison.OrdinalIgnoreCase);
        var restored = Assert.IsType<AcquireVisionImageNodeModel>(store.Deserialize(json).Document.CanvasProjection.Nodes.Single().Node);
        Assert.Equal(mode, restored.SourceMode); Assert.Equal("Camera.Top", restored.Source!.SourceId);
        Assert.Equal("file.png", restored.FilePath); Assert.Equal("folder", restored.FolderPath);
        Assert.Equal("private", restored.Algorithm.Settings["model"]);
        Assert.Equal(mode is EWorkflowVisionImageSource.File or EWorkflowVisionImageSource.Folder ? 1 : 0, restored.GetAlgorithmSlots().Count);
    }

    [Fact]
    public async Task SuccessfulDecodeCanceledBeforeDelivery_DoesNotAdvanceAndReleasesImage()
    {
        using var rig = new Rig(EWorkflowVisionImageSource.Folder);
        await rig.Folder.PrepareAsync(new([rig.Node], WorkflowRunScopeKind.Root), default);
        await rig.Folder.ReleasePreviousRunAsync(default);
        using var cancellation = new CancellationTokenSource();
        IImageSource? decoded = null;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => rig.Folder.NextAsync(rig.Node.Id, async (path, token) =>
        {
            decoded = await rig.Reader.ReadAsync(path, token);
            cancellation.Cancel();
            return decoded;
        }, cancellation.Token));
        Assert.Throws<ObjectDisposedException>(() => decoded!.Retain());
        using var next = await rig.Folder.NextAsync(rig.Node.Id, default);
        var pixels = new byte[1]; next.CopyTo(0, pixels, 0, 1); Assert.Equal(1, pixels[0]);
    }

    private sealed class Rig : IDisposable
    {
        public string DirectoryPath { get; } = Path.Combine(Path.GetTempPath(), "unified-vision-" + Guid.NewGuid().ToString("N"));
        public Reader Reader { get; } = new();
        public Reader LegacyReader { get; } = new();
        public AcquireVisionImageNodeModel Node { get; }
        public WorkflowVisionAcquisitionSession Folder { get; }
        public WorkflowVisionFrameScope Frames { get; }
        public WorkflowRuntimeHost Host { get; }
        private readonly VisionAlgorithmRuntime _runtime;
        private readonly WorkflowVisionAlgorithmBindings _bindings;
        private readonly WorkflowServiceProvider _services;
        public Rig(EWorkflowVisionImageSource mode, bool restart = false, bool loop = false)
        {
            Directory.CreateDirectory(DirectoryPath);
            File.WriteAllText(Path.Combine(DirectoryPath, "01.png"), "1"); File.WriteAllText(Path.Combine(DirectoryPath, "02.png"), "2");
            Node = new() { Id = "input", SourceMode = mode, FilePath = Path.Combine(DirectoryPath, "01.png"),
                FolderPath = DirectoryPath, Extensions = ".png", RestartFolderEachRun = restart, Loop = loop,
                Algorithm = new() { ImplementationId = "test.unified-read" } };
            Folder = new(LegacyReader); Frames = new(Folder);
            _runtime = new(VisionAlgorithmCatalog.Compose([new ReaderModule(Reader)]));
            _bindings = new(_runtime, Frames);
            _services = new WorkflowServiceProvider().Add<IWorkflowVisionFrameScope>(Frames).Add<IWorkflowVisionFolderSource>(Folder)
                .Add<IWorkflowVisionAlgorithmBindings>(_bindings).Add<IWorkflowNodeCapabilityProvider>(_bindings)
                .Add<IWorkflowRunPreparationService>(_bindings).Add<IWorkflowRunResourceOwner>(Frames);
            Host = new(new WorkflowNodeCatalog().RegisterImageNodes(), new WorkflowNodeHandlerCatalog().RegisterImageNodeHandlers());
            Configure();
        }
        public void Configure()
        {
            var document = new WorkflowDocument { EntryNodeId = Node.Id }; document.CanvasProjection.Nodes.Add(new() { Node = Node });
            Host.Configure(document, new WorkflowContext(_services));
        }
        private ImageFrame Output() => Assert.IsType<ImageFrame>(Host.Engine!.RunState.NodeOutputs.Single(output => output.NodeId == Node.Id).Value);
        public string FrameId() => Output().FrameId;
        public int Pixel() { var pixel = new byte[1]; Output().Image.CopyTo(0, pixel, 0, 1); return pixel[0]; }
        public void Dispose() { Host.Dispose(); _bindings.Dispose(); _runtime.Dispose(); Frames.Dispose(); Directory.Delete(DirectoryPath, true); }
    }

    private sealed class Reader : IImageFileReader
    {
        public int Reads { get; private set; }
        public bool FailNext { get; set; }
        public Task<IImageSource> ReadAsync(string path, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested(); Reads++;
            if (FailNext) { FailNext = false; throw new InvalidOperationException("decode failed"); }
            return Task.FromResult<IImageSource>(VisionImage.CopyFrom(new(1, 1, EPixelLayout.Gray8), [byte.Parse(Path.GetFileNameWithoutExtension(path))]));
        }
    }

    private sealed class ReaderModule(Reader reader) : IVisionAlgorithmModule
    {
        public string ExtensionId => "test.unified-reader";
        public void Register(IVisionAlgorithmRegistration registrations) => registrations.Add(new("test.unified-read", "Test", "1",
            VisionAlgorithmFactory<IImageFileReader>.Stateless(() => reader)));
    }
}
