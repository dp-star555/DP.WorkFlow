using System.Text.Json.Nodes;
using DP.Vision;
using DP.Vision.Algorithms;
using DP.WorkFlow.Persistence.Json;

namespace DP.WorkFlow.Tests;

public sealed class ExistingVisionAlgorithmMigrationTests
{
    [Theory]
    [InlineData("Vision.AcquireFrame", "opencv.image-read")]
    [InlineData("Vision.AnalyzeBlobs", "opencv.blob")]
    [InlineData("Vision.AnalyzeColor", "managed.color")]
    [InlineData("Vision.PreprocessImage", "opencv.preprocess")]
    [InlineData("Vision.ThresholdRegion", "opencv.region")]
    [InlineData("Vision.MorphRegion", "opencv.region")]
    [InlineData("Vision.SelectBlobs", "managed.blob-select")]
    [InlineData("Vision.MeasureCaliper", "managed.caliper")]
    [InlineData("Vision.FitRobustLine", "managed.robust-line")]
    [InlineData("Vision.LocateTemplatePose", "opencv.template-pose")]
    public void OldRecipeWithoutSelection_RetainsOriginalImplementation_AndNewSelectionRoundTrips(string nodeType, string expected)
    {
        var catalog = new WorkflowNodeCatalog().RegisterImageNodes();
        var node = catalog.GetOrThrow(nodeType).Factory(); node.Id = "operator";
        var store = new WorkflowDocumentJsonStore(catalog);
        var json = JsonNode.Parse(store.Serialize(Document(node)))!;
        RemoveSelection(json);
        var restored = Assert.Single(store.Deserialize(json.ToJsonString()).Document.CanvasProjection.Nodes).Node;
        var selection = Assert.Single(((IWorkflowVisionAlgorithmNode)restored).GetAlgorithmSlots()).Selection;
        Assert.Equal(expected, selection.ImplementationId);
        selection.ImplementationId = "test.alternative";
        selection.Settings["model"] = "independent-model";
        selection.Dependencies["preprocessor"] = new VisionAlgorithmSelection { ImplementationId = "test.dependency" };
        restored = Assert.Single(store.Deserialize(store.Serialize(Document(restored))).Document.CanvasProjection.Nodes).Node;
        selection = Assert.Single(((IWorkflowVisionAlgorithmNode)restored).GetAlgorithmSlots()).Selection;
        Assert.Equal("test.alternative", selection.ImplementationId);
        Assert.Equal("independent-model", selection.Settings["model"]);
        Assert.Equal("test.dependency", selection.Dependencies["preprocessor"].ImplementationId);
    }

    [Fact]
    public async Task FileAndColorNodes_ChoosePerNodeEngines_WithoutGlobalAlgorithms()
    {
        using var folder = new TemporaryFolder();
        var reader = new CountingReader();
        using var runtime = Runtime(reader);
        using var frames = new WorkflowVisionFrameScope();
        using var bindings = new WorkflowVisionAlgorithmBindings(runtime, frames);
        var source = new AcquireVisionImageNodeModel { Id = "source", FilePath = folder.File, Algorithm = Selection("test.read") };
        var first = new AnalyzeVisionColorNodeModel { Id = "first", Frame = Input<ImageFrame>(source.Id), Algorithm = Selection("test.color.first") };
        var second = new AnalyzeVisionColorNodeModel { Id = "second", Frame = Input<ImageFrame>(source.Id), Algorithm = Selection("test.color.second") };
        var document = Document(source, first, second);
        var nodes = new WorkflowNodeCatalog().RegisterImageNodes();
        // Engine choice must survive the real recipe serialization/compile snapshot.
        var store = new WorkflowDocumentJsonStore(nodes); document = store.Deserialize(store.Serialize(document)).Document;
        using var host = new WorkflowRuntimeHost(nodes, new WorkflowNodeHandlerCatalog().RegisterImageNodeHandlers());
        host.Configure(document, new WorkflowContext(Services(frames, bindings)));
        var result = await host.RunAsync();
        Assert.True(result.Success, result.Message);
        Assert.Equal(1, reader.Reads);
        Assert.Equal(11, Assert.IsType<ColorAnalysisResult>(host.Engine!.RunState.NodeOutputs.Single(o => o.NodeId == first.Id).Value).Red);
        Assert.Equal(22, Assert.IsType<ColorAnalysisResult>(host.Engine.RunState.NodeOutputs.Single(o => o.NodeId == second.Id).Value).Red);
    }

    [Theory]
    [InlineData("test.missing", 1)]
    [InlineData("test.color.first", 99)]
    public async Task InvalidDownstreamSelection_FailsPreparationBeforeAnyFileRead(string implementation, int version)
    {
        using var folder = new TemporaryFolder();
        var reader = new CountingReader();
        using var runtime = Runtime(reader);
        using var frames = new WorkflowVisionFrameScope();
        using var bindings = new WorkflowVisionAlgorithmBindings(runtime, frames);
        var source = new AcquireVisionImageNodeModel { Id = "source", FilePath = folder.File, Algorithm = Selection("test.read") };
        var color = new AnalyzeVisionColorNodeModel { Id = "color", Frame = Input<ImageFrame>(source.Id), Algorithm = new() { ImplementationId = implementation, SettingsVersion = version } };
        using var host = new WorkflowRuntimeHost(new WorkflowNodeCatalog().RegisterImageNodes(), new WorkflowNodeHandlerCatalog().RegisterImageNodeHandlers());
        host.Configure(Document(source, color), new WorkflowContext(Services(frames, bindings)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => host.RunAsync());
        Assert.Equal(0, reader.Reads);
        Assert.Empty(host.Engine!.RunState.NodeOutputs);
    }

    [Fact]
    public async Task FolderNode_UsesItsSelectedReader_InsteadOfSessionsLegacyReader()
    {
        using var folder = new TemporaryFolder();
        var selected = new CountingReader();
        var legacy = new CountingReader();
        var session = new WorkflowVisionAcquisitionSession(legacy);
        using var runtime = Runtime(selected);
        using var frames = new WorkflowVisionFrameScope(session);
        using var bindings = new WorkflowVisionAlgorithmBindings(runtime, frames);
        var node = new AcquireVisionImageNodeModel { SourceMode = EWorkflowVisionImageSource.Folder, RestartFolderEachRun = true, Id = "folder", FolderPath = folder.Path, Algorithm = Selection("test.read"), Extensions = ".png" };
        var services = Services(frames, bindings).Add<IWorkflowVisionFolderSource>(session);
        using var host = new WorkflowRuntimeHost(new WorkflowNodeCatalog().RegisterImageNodes(), new WorkflowNodeHandlerCatalog().RegisterImageNodeHandlers());
        host.Configure(Document(node), new WorkflowContext(services));
        Assert.True((await host.RunAsync()).Success);
        Assert.Equal(1, selected.Reads); Assert.Equal(0, legacy.Reads);
        // A fresh root run still rewinds the frozen list.
        Assert.True((await host.RunAsync()).Success);
        Assert.Equal(2, selected.Reads);
    }

    [Fact]
    public async Task FolderCallback_FailedDecodeDoesNotConsumeTheFile()
    {
        using var folder = new TemporaryFolder();
        var session = new WorkflowVisionAcquisitionSession(new CountingReader());
        var node = new AcquireVisionImageNodeModel { SourceMode = EWorkflowVisionImageSource.Folder, RestartFolderEachRun = true, Id = "folder", FolderPath = folder.Path };
        await session.PrepareAsync(new WorkflowRunPreparationContext(new[] { node }, WorkflowRunScopeKind.Root), default);
        await session.ReleasePreviousRunAsync(default);
        var attempted = new List<string>();
        var reader = new CountingReader();
        Task<IImageSource> Read(string path, CancellationToken token)
        {
            attempted.Add(path);
            return attempted.Count == 1 ? Task.FromException<IImageSource>(new IOException("decode failed")) : reader.ReadAsync(path, token);
        }
        await Assert.ThrowsAsync<IOException>(() => session.NextAsync(node.Id, Read, default));
        using var image = await session.NextAsync(node.Id, Read, default);
        Assert.Equal(new[] { folder.File, folder.File }, attempted);
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.NextAsync(node.Id, Read, default));
    }

    [Fact]
    public async Task EditorReader_PreparesExplicitEngineWithoutConcreteProvider()
    {
        var reader = new CountingReader();
        using var runtime = Runtime(reader);
        var adapter = new WorkflowVisionImageFileReader(runtime, Selection("test.read"));
        using var image = await adapter.ReadAsync("editor-image");
        Assert.Equal(1, reader.Reads);
        Assert.Equal(4, image.Info.Width);
        var missing = new WorkflowVisionImageFileReader(runtime, Selection("test.missing"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => missing.ReadAsync("editor-image"));
        Assert.Equal(1, reader.Reads);
    }

    [Fact]
    public async Task LegacyHost_DoesNotSilentlyIgnoreNonDefaultNodeSelection()
    {
        using var folder = new TemporaryFolder();
        using var frames = new WorkflowVisionFrameScope();
        var source = new AcquireVisionImageNodeModel { Id = "source", FilePath = folder.File };
        var color = new AnalyzeVisionColorNodeModel { Id = "color", Frame = Input<ImageFrame>(source.Id), Algorithm = Selection("test.color.second") };
        var services = new WorkflowServiceProvider().Add<IImageFileReader>(new CountingReader()).Add<IColorAnalyzer>(new FixedColor(11))
            .Add<IWorkflowVisionFrameScope>(frames).Add<IWorkflowRunPreparationService>(frames).Add<IWorkflowRunResourceOwner>(frames);
        using var host = new WorkflowRuntimeHost(new WorkflowNodeCatalog().RegisterImageNodes(), new WorkflowNodeHandlerCatalog().RegisterImageNodeHandlers());
        host.Configure(Document(source, color), new WorkflowContext(services));
        var run = await host.RunAsync();
        Assert.False(run.Success); Assert.Contains("算法绑定服务", run.Message);
        Assert.DoesNotContain(host.Engine!.RunState.NodeOutputs, o => o.NodeId == color.Id);
    }

    private static VisionAlgorithmRuntime Runtime(CountingReader reader) => new(VisionAlgorithmCatalog.Compose(new[] { new TestModule(reader) }));
    private static VisionAlgorithmSelection Selection(string id) => new() { ImplementationId = id };
    private static WorkflowInput<T> Input<T>(string id) => WorkflowInput<T>.FromBinding(new WorkflowBindingKey(id, "$"));
    private static WorkflowServiceProvider Services(WorkflowVisionFrameScope frames, WorkflowVisionAlgorithmBindings bindings) => new WorkflowServiceProvider()
        .Add<IWorkflowVisionFrameScope>(frames).Add<IWorkflowVisionAlgorithmBindings>(bindings).Add<IWorkflowNodeCapabilityProvider>(bindings)
        .Add<IWorkflowRunPreparationService>(bindings).Add<IWorkflowRunResourceOwner>(frames);
    private static WorkflowDocument Document(params IWorkflowNodeModel[] nodes)
    {
        var document = new WorkflowDocument { EntryNodeId = nodes[0].Id };
        foreach (var node in nodes) document.CanvasProjection.Nodes.Add(new WorkflowCanvasNode { Node = node });
        for (int index = 1; index < nodes.Length; index++) document.CanvasProjection.Connections.Add(new WorkflowConnectionModel
            { FromNodeId = nodes[index - 1].Id, FromPort = WorkflowPorts.Success, ToNodeId = nodes[index].Id, ToPort = WorkflowPorts.Input });
        return document;
    }
    private static void RemoveSelection(JsonNode? node)
    {
        if (node is JsonObject obj)
            foreach (var property in obj.ToArray())
                if (property.Key.Equals("algorithm", StringComparison.OrdinalIgnoreCase)) obj.Remove(property.Key);
                else RemoveSelection(property.Value);
        else if (node is JsonArray array) foreach (var child in array) RemoveSelection(child);
    }
    private sealed class TestModule(CountingReader reader) : IVisionAlgorithmModule
    {
        public string ExtensionId => "test.existing-nodes";
        public void Register(IVisionAlgorithmRegistration registrations)
        {
            registrations.Add(new VisionAlgorithmDescriptor("test.read", "Test", "1", VisionAlgorithmFactory<IImageFileReader>.Stateless(() => reader)));
            registrations.Add(new VisionAlgorithmDescriptor("test.color.first", "Test", "1", VisionAlgorithmFactory<IColorAnalyzer>.Stateless(() => new FixedColor(11))));
            registrations.Add(new VisionAlgorithmDescriptor("test.color.second", "Test", "1", VisionAlgorithmFactory<IColorAnalyzer>.Stateless(() => new FixedColor(22))));
        }
    }
    private sealed class FixedColor(double red) : IColorAnalyzer
    {
        public ColorAnalysisResult Analyze(ImageFrame frame, PixelBounds bounds, CancellationToken token = default, RegionGeometry? regionMask = null)
        { token.ThrowIfCancellationRequested(); return new ColorAnalysisResult(frame.FrameId, 1, red, red, red); }
    }
    private sealed class CountingReader : IImageFileReader
    {
        public int Reads { get; private set; }
        public Task<IImageSource> ReadAsync(string path, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested(); Reads++;
            return Task.FromResult<IImageSource>(VisionImage.CopyFrom(new ImageInfo(4, 4, EPixelLayout.Gray8), new byte[16]));
        }
    }
    private sealed class TemporaryFolder : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "algorithm-migration-" + Guid.NewGuid().ToString("N"));
        public string File => System.IO.Path.Combine(Path, "01.png");
        public TemporaryFolder() { Directory.CreateDirectory(Path); System.IO.File.WriteAllText(File, "test reader input"); }
        public void Dispose() => Directory.Delete(Path, true);
    }
}
