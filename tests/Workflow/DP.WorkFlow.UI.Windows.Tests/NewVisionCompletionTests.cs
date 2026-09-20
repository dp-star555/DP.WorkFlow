using DP.Vision;
using DP.Vision.Algorithms;
using DP.Vision.OpenCv;
using DP.Vision.UI;
using DP.WorkFlow.Persistence.Json;
using DP.WorkFlow.UI;
using DP.WorkFlow.Vision.UI;
using OpenCvSharp;

namespace DP.WorkFlow.Tests;

public sealed class NewVisionCompletionTests
{
    [Fact]
    public async Task FolderPreparation_FreezesOrder_ResetsAndRejectsExhaustion()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()); Directory.CreateDirectory(directory);
        try
        {
            WritePng(Path.Combine(directory, "b.png"), 200); WritePng(Path.Combine(directory, "a.png"), 50);
            var node = new LoadVisionFolderNodeModel { Id = "folder", FolderPath = directory };
            var preparation = new WorkflowRunPreparationContext(new[] { node }, WorkflowRunScopeKind.Root);
            var session = new WorkflowVisionAcquisitionSession(new OpenCvImageFileReader());
            await session.PrepareAsync(preparation, default);
            WritePng(Path.Combine(directory, "0.png"), 1); // 运行清单已经冻结。
            using var first = await session.NextAsync(node.Id, default);
            using var second = await session.NextAsync(node.Id, default);
            var pixel = new byte[1]; first.CopyTo(0, pixel, 0, 1); Assert.Equal(50, pixel[0]);
            second.CopyTo(0, pixel, 0, 1); Assert.Equal(200, pixel[0]);
            await Assert.ThrowsAsync<InvalidOperationException>(() => session.NextAsync(node.Id, default));
            await session.PrepareAsync(preparation, default);
            using var reset = await session.NextAsync(node.Id, default);
            reset.CopyTo(0, pixel, 0, 1); Assert.Equal(1, pixel[0]);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task FrameScope_IsBoundedAndNewRunPreservesIndependentUiLease()
    {
        using var image = VisionImage.CopyFrom(new ImageInfo(1, 1, EPixelLayout.Gray8), new byte[] { 7 });
        using var frame = new ImageFrame("frame", image);
        using var scope = new WorkflowVisionFrameScope(maximumBytes: 1, maximumFrames: 1);
        var output = scope.Retain(frame); using var ui = output.Retain();
        Assert.Throws<InvalidOperationException>(() => scope.Retain(frame));
        await scope.PrepareAsync(new WorkflowRunPreparationContext(Array.Empty<IWorkflowNodeModel>(), WorkflowRunScopeKind.Root), default);
        Assert.Throws<ObjectDisposedException>(() => output.Retain());
        var bytes = new byte[1]; ui.Image.CopyTo(0, bytes, 0, 1); Assert.Equal(7, bytes[0]);
        Assert.NotNull(scope.Retain(frame));
    }

    [Fact]
    public async Task TypedCalibration_JsonAndOutputBinding_MapCoordinate()
    {
        var solve = new SolveVisionCalibrationNodeModel { Id = "solve", Points = new()
        {
            new() { SourceX = 0, SourceY = 0, TargetX = 10, TargetY = 20 },
            new() { SourceX = 1, SourceY = 0, TargetX = 12, TargetY = 20 },
            new() { SourceX = 0, SourceY = 1, TargetX = 10, TargetY = 23 }
        } };
        var map = new MapVisionCoordinateNodeModel { Id = "map", Calibration = WorkflowInput<AffineCalibration>.FromBinding(new("solve", "$")),
            X = WorkflowInput<double>.FromLiteral(2), Y = WorkflowInput<double>.FromLiteral(3) };
        var document = Document(solve, map);
        var nodes = new WorkflowNodeCatalog().RegisterImageNodes(); var store = new WorkflowDocumentJsonStore(nodes);
        var loaded = store.Deserialize(store.Serialize(document)).Document;
        var engine = new WorkflowEngine(new WorkflowCompiler(nodes).Compile(loaded), new WorkflowNodeHandlerCatalog().RegisterImageNodeHandlers(), new WorkflowContext());
        Assert.True((await engine.RunAsync()).Success);
        var point = Assert.IsType<Coordinate2D>(engine.RunState.NodeOutputs.Single(o => o.NodeId == "map").Value);
        Assert.Equal(14, point.X, 8); Assert.Equal(29, point.Y, 8);
    }

    [Fact]
    public void SharedRoiEditor_IsolatedCommitUndoAndNativeRenderers()
    {
        Exception? failure = null;
        var thread = UiTestThread.Create(() =>
        {
            try
            {
                var nodes = new WorkflowNodeCatalog().RegisterImageNodes();
                var document = Document(new AnalyzeVisionBlobsNodeModel { Id = "blob" });
                var session = new WorkflowDesignerSession(document, nodes);
                var source = (AnalyzeVisionBlobsNodeModel)document.CanvasProjection.Nodes[0].Node;
                var editor = new WorkflowNodeEditorModel(session, "blob", "blob", new[] { new VisionFrameEditorPageProvider() });
                var page = editor.Pages.Single(p => p.RendererKey == VisionFrameEditorPageProvider.RendererKey);
                var model = Assert.IsType<VisionFrameEditorPageModel>(page.Model);
                Assert.Empty(source.Regions); Assert.True(source.FullImage);
                model.Editor.Tool = ERoiTool.Ellipse;
                model.Editor.PointerDown(new PointD(2, 2), .1); model.Editor.PointerUp(new PointD(8, 6));
                Assert.Empty(source.Regions); // 仍只改EditingNode。
                Assert.Single(((AnalyzeVisionBlobsNodeModel)editor.EditingNode).Regions);
                using var control = new DP.WorkFlow.Vision.UI.WinForms.VisionFrameEditorRenderer().CreateControl(page);
                Assert.Contains(control.Controls.Cast<System.Windows.Forms.Control>(), c => c is DP.Vision.Winform.VisionCanvasControl);
                var wpf = new DP.WorkFlow.Vision.UI.Wpf.VisionFrameEditorRenderer().CreateElement(page);
                Assert.IsAssignableFrom<System.Windows.Controls.DockPanel>(wpf);
                editor.ApplyChanges();
                var committed = (AnalyzeVisionBlobsNodeModel)document.CanvasProjection.Nodes[0].Node;
                Assert.Single(committed.Regions); Assert.Equal(EWorkflowVisionRoiShape.Ellipse, committed.Regions[0].Shape);
                Assert.True(session.Undo());
                Assert.Empty(((AnalyzeVisionBlobsNodeModel)document.CanvasProjection.Nodes[0].Node).Regions);
                editor.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20))); Assert.Null(failure);
    }

    [Fact]
    public async Task RoiMasks_RoundTripAndPipelineHonorsExclusion()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".png");
        try
        {
            WritePng(path, 100, 5);
            var file = new LoadVisionFileNodeModel { Id = "file", FilePath = path };
            var color = new AnalyzeVisionColorNodeModel { Id = "color", Frame = WorkflowInput<ImageFrame>.FromBinding(new("file", "$")),
                Regions = new() { new() { Id = "include", CenterX = 2.5, CenterY = 2.5, Width = 3, Height = 3 },
                    new() { Id = "hole", Exclude = true, CenterX = 2.5, CenterY = 2.5, Width = 1, Height = 1 } } };
            var nodes = new WorkflowNodeCatalog().RegisterImageNodes(); var store = new WorkflowDocumentJsonStore(nodes);
            var doc = store.Deserialize(store.Serialize(Document(file, color))).Document;
            using var scope = new WorkflowVisionFrameScope();
            var services = new WorkflowServiceProvider().Add<IImageFileReader>(new OpenCvImageFileReader())
                .Add<IWorkflowVisionFrameScope>(scope).Add<IColorAnalyzer>(new RgbColorAnalyzer());
            using var host = new WorkflowRuntimeHost(nodes, new WorkflowNodeHandlerCatalog().RegisterImageNodeHandlers());
            host.Configure(doc, new WorkflowContext(services)); Assert.True((await host.RunAsync()).Success);
            var result = Assert.IsType<ColorAnalysisResult>(host.Engine!.RunState.NodeOutputs.Last().Value);
            Assert.Equal(8, result.PixelCount);
            using var preview = scope.Capture("color"); Assert.NotNull(preview); Assert.Equal(result.FrameId, preview.Frame.FrameId);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task TemplateNode_UsesTwoTypedFileOutputs()
    {
        string folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()); Directory.CreateDirectory(folder);
        try
        {
            string path = Path.Combine(folder, "image.png"), templatePath = Path.Combine(folder, "template.png");
            using (var image = new Mat(20, 20, MatType.CV_8UC1, Scalar.All(0)))
            {
                using var patch = new Mat(image, new Rect(8, 7, 5, 5)); patch.SetTo(Scalar.All(255));
                File.WriteAllBytes(path, image.ToBytes(".png"));
            }
            WritePng(templatePath, 255, 5);
            var template = new LoadVisionFileNodeModel { Id = "template", FilePath = templatePath };
            var file = new LoadVisionFileNodeModel { Id = "file", FilePath = path };
            var locate = new LocateVisionTemplateNodeModel { Id = "locate", MinimumScore = .999,
                Frame = WorkflowInput<ImageFrame>.FromBinding(new("file", "$")), Template = WorkflowInput<ImageFrame>.FromBinding(new("template", "$")) };
            var nodes = new WorkflowNodeCatalog().RegisterImageNodes();
            using var scope = new WorkflowVisionFrameScope();
            using var host = new WorkflowRuntimeHost(nodes, new WorkflowNodeHandlerCatalog().RegisterImageNodeHandlers());
            host.Configure(Document(template, file, locate), new WorkflowContext(new WorkflowServiceProvider()
                .Add<IImageFileReader>(new OpenCvImageFileReader()).Add<IWorkflowVisionFrameScope>(scope).Add<ITemplateLocator>(new OpenCvTemplateLocator())));
            Assert.True((await host.RunAsync()).Success);
            var result = Assert.IsType<TemplateLocationResult>(host.Engine!.RunState.NodeOutputs.Last().Value);
            Assert.True(result.Found); Assert.Equal(8, result.Bounds!.Value.X); Assert.Equal(7, result.Bounds.Value.Y);
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public async Task StopAsync_WaitsForPendingCaptureBeforeResourceRelease()
    {
        string path = Path.GetTempFileName();
        try
        {
            var reader = new PendingReader();
            using var scope = new WorkflowVisionFrameScope();
            using var host = new WorkflowRuntimeHost(new WorkflowNodeCatalog().RegisterImageNodes(), new WorkflowNodeHandlerCatalog().RegisterImageNodeHandlers());
            host.Configure(Document(new LoadVisionFileNodeModel { Id = "file", FilePath = path }), new WorkflowContext(new WorkflowServiceProvider()
                .Add<IImageFileReader>(reader).Add<IWorkflowVisionFrameScope>(scope)));
            var run = host.RunAsync(); await reader.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await host.StopAsync();
            Assert.True(run.IsCompleted); Assert.True(reader.Exited);
        }
        finally { File.Delete(path); }
    }

    private sealed class PendingReader : IImageFileReader
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Exited { get; private set; }
        public async Task<IImageSource> ReadAsync(string path, CancellationToken token = default)
        {
            Started.SetResult();
            try { await Task.Delay(Timeout.Infinite, token); throw new InvalidOperationException(); }
            finally { Exited = true; }
        }
    }

    [Fact]
    public async Task ShippedSample_IsRunnableAndRerunReleasesOldOwnedFrames()
    {
        var nodes = new WorkflowNodeCatalog().RegisterStandardNodes().RegisterImageNodes();
        using var workspace = new WorkflowDocumentWorkspace(nodes);
        workspace.New("demo"); Samples.WorkflowImageDemo.Populate(workspace.Navigator!.RootSession);
        using var frames = new WorkflowVisionFrameScope();
        var services = new WorkflowServiceProvider().Add<IImageFileReader>(new OpenCvImageFileReader())
            .Add<IBlobAnalyzer>(new OpenCvBlobAnalyzer()).Add<IColorAnalyzer>(new RgbColorAnalyzer())
            .Add<IWorkflowVisionFrameScope>(frames).Add<IWorkflowRunPreparationService>(frames);
        using var host = new WorkflowRuntimeHost(nodes, new WorkflowNodeHandlerCatalog().RegisterStandardNodeHandlers().RegisterImageNodeHandlers());
        host.Configure(workspace.Navigator.RootDocument, new WorkflowContext(services));
        Assert.True((await host.RunAsync()).Success);
        var output = host.Engine!.RunState.NodeOutputs;
        var oldFrame = Assert.Single(output.Select(o => o.Value).OfType<ImageFrame>());
        using var ui = oldFrame.Retain();
        Assert.Equal(2, Assert.Single(output.Select(o => o.Value).OfType<BlobAnalysisResult>()).Count);
        Assert.Equal(26.5625, Assert.Single(output.Select(o => o.Value).OfType<ColorAnalysisResult>()).Red, 6);
        Assert.True((await host.RunAsync()).Success);
        Assert.Throws<ObjectDisposedException>(() => oldFrame.Retain());
        Assert.NotEqual(ui.FrameId, Assert.Single(host.Engine!.RunState.NodeOutputs.Select(o => o.Value).OfType<ImageFrame>()).FrameId);
    }

    [Fact]
    public async Task DuplicatePreviewIdentityAcrossSubplans_IsRejectedBeforeRunning()
    {
        using var frames = new WorkflowVisionFrameScope();
        var context = new WorkflowRunPreparationContext(new IWorkflowNodeModel[]
        { new LoadVisionFileNodeModel { Id = "same" }, new CaptureVisionFrameNodeModel { Id = "same" } },
            WorkflowRunScopeKind.Root);
        await Assert.ThrowsAsync<InvalidOperationException>(() => frames.PrepareAsync(context, default).AsTask());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InvalidBackendFacts_AreNotPublished(bool returnNull)
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".png");
        try
        {
            WritePng(path, 1);
            using var frames = new WorkflowVisionFrameScope();
            var nodes = new WorkflowNodeCatalog().RegisterImageNodes();
            var file = new LoadVisionFileNodeModel { Id = "file", FilePath = path };
            var color = new AnalyzeVisionColorNodeModel { Id = "color", Frame = WorkflowInput<ImageFrame>.FromBinding(new("file", "$")) };
            using var host = new WorkflowRuntimeHost(nodes, new WorkflowNodeHandlerCatalog().RegisterImageNodeHandlers());
            host.Configure(Document(file, color), new WorkflowContext(new WorkflowServiceProvider()
                .Add<IImageFileReader>(new OpenCvImageFileReader()).Add<IWorkflowVisionFrameScope>(frames)
                .Add<IColorAnalyzer>(new InvalidColorAnalyzer(returnNull))));
            Assert.False((await host.RunAsync()).Success);
            Assert.DoesNotContain(host.Engine!.RunState.NodeOutputs, output => output.NodeId == "color");
            Assert.Null(frames.Capture("color"));
        }
        finally { File.Delete(path); }
    }

    private sealed class InvalidColorAnalyzer(bool returnNull) : IColorAnalyzer
    {
        public ColorAnalysisResult Analyze(ImageFrame frame, PixelBounds bounds, CancellationToken token = default, RegionGeometry? regionMask = null) =>
            returnNull ? null! : new ColorAnalysisResult("wrong-frame", 1, 1, 1, 1);
    }

    private static WorkflowDocument Document(params IWorkflowNodeModel[] nodes)
    {
        var doc = new WorkflowDocument { EntryNodeId = nodes[0].Id };
        foreach (var node in nodes) doc.CanvasProjection.Nodes.Add(new WorkflowCanvasNode { Node = node });
        for (int i = 1; i < nodes.Length; i++) doc.CanvasProjection.Connections.Add(new WorkflowConnectionModel
        { FromNodeId = nodes[i - 1].Id, ToNodeId = nodes[i].Id, FromPort = WorkflowPorts.Success, ToPort = WorkflowPorts.Input });
        return doc;
    }
    private static void WritePng(string path, byte value, int size = 1)
    {
        using var mat = new Mat(size, size, MatType.CV_8UC1, Scalar.All(value)); File.WriteAllBytes(path, mat.ToBytes(".png"));
    }
}
