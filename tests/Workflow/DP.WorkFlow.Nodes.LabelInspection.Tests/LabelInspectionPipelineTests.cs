using DP.LabelInspection.Contracts;
using DP.LabelInspection.Runtime;
using DP.LabelInspection.Storage;
using DP.Vision;
using DP.Vision.Algorithms;
using DP.WorkFlow.LabelInspection;
using DP.WorkFlow.Persistence.Json;

namespace DP.WorkFlow.Tests;

public sealed class LabelInspectionPipelineTests
{
    [Theory]
    [InlineData(false, EInspectionVerdict.Ok)]
    [InlineData(true, EInspectionVerdict.Ng)]
    public async Task RunAsync_RealBlankInspection_ReturnsReportAndContinuesToTypedConsumer(bool ink, EInspectionVerdict expected)
    {
        await using var rig = new Rig(ink);
        var result = await rig.RunAsync();
        Assert.True(result.Success, result.Message);
        var output = rig.Output();
        Assert.Equal(expected, output.Verdict);
        Assert.Equal(expected == EInspectionVerdict.Ok, output.IsQualified);
        Assert.NotEmpty(output.Regions);
        Assert.Equal("blank", output.RecipeName);
        Assert.Equal(64, output.RecipeSha256.Length);
        Assert.Equal(64, output.ResourceIdentity.Length);
        Assert.Equal("cycle-1", output.CycleId);
        Assert.Equal(expected, rig.Host.Engine!.RunState.NodeOutputs.Single(o => o.NodeId == "consumer").Value);
        using var image = rig.Frames.Capture("image");
        using var inspection = rig.Frames.Capture("inspect");
        Assert.NotNull(inspection);
        Assert.Equal(image!.Frame.FrameId, output.FrameId);
        Assert.Same(output, inspection!.Facts);
        rig.Frames.InvalidateFrom(inspection.ExecutionSequence);
        Assert.Null(rig.Frames.Capture("inspect"));
    }

    [Theory]
    [InlineData(true, EInspectionVerdict.Ng, WorkflowPorts.Failed)]
    [InlineData(false, EInspectionVerdict.Ok, WorkflowPorts.Success)]
    public async Task RunAsync_NgToFailedPort_RoutesNotOkReportToFailedBranchWithReport(bool ink, EInspectionVerdict expected, string port)
    {
        await using var rig = new Rig(ink);
        rig.Node.NgToFailedPort = true;
        var ok = rig.Document.CanvasProjection.Connections.Single(c => c.FromNodeId == "inspect");
        rig.Document.CanvasProjection.Nodes.Add(new() { Node = new Consumer { Id = "ng", Verdict = WorkflowInput<EInspectionVerdict>.FromBinding(new("inspect", "Verdict")) } });
        rig.Document.CanvasProjection.Connections.Add(new() { FromNodeId = "inspect", FromPort = WorkflowPorts.Failed, ToNodeId = "ng", ToPort = WorkflowPorts.Input });
        var result = await rig.RunAsync();
        Assert.True(result.Success, result.Message);
        Assert.Equal(expected, rig.Output().Verdict);
        var outputs = rig.Host.Engine!.RunState.NodeOutputs;
        // NG：报告照常提交，失败支路能读到判定；成功支路不执行。OK：反之。
        Assert.Equal(port == WorkflowPorts.Failed, outputs.Any(o => o.NodeId == "ng"));
        Assert.Equal(port == WorkflowPorts.Success, outputs.Any(o => o.NodeId == ok.ToNodeId));
        if (port == WorkflowPorts.Failed) Assert.Equal(EInspectionVerdict.Ng, outputs.Single(o => o.NodeId == "ng").Value);
    }

    [Fact]
    public async Task RunAsync_UnchangedResources_ReusesLoadedEngineAcrossRuns_AndReloadsAfterChange()
    {
        await using var rig = new Rig(false);
        Assert.True((await rig.RunAsync()).Success);
        Assert.True((await rig.RunAsync()).Success);
        Assert.True((await rig.RunAsync()).Success);
        Assert.Equal(1, rig.Runtime.ResourceLoadCount);
        Assert.Equal(1, rig.Runtime.CachedResourceCount);
        // 配方变化：下一轮重新加载，缓存仍只保留当前一份。
        rig.Node.RecipeJson = rig.Json(new InspectionRecipe("changed", 64, 48, EInspectionMode.Free, EAlignmentMode.AssumeAligned,
            new[] { new InspectionRegion("blank", ERegionKind.Blank, new PixelBounds(8, 8, 48, 32)) }, Rig.Options));
        Assert.True((await rig.RunAsync()).Success);
        Assert.Equal("changed", rig.Output().RecipeName);
        Assert.Equal(2, rig.Runtime.ResourceLoadCount);
        Assert.Equal(1, rig.Runtime.CachedResourceCount);
    }

    [Fact]
    public async Task RunAsync_ValidReviewReport_IsNormalOutputNotNodeFault()
    {
        await using var rig = new Rig(false);
        rig.InspectionService = new ReviewService();
        Assert.True((await rig.RunAsync()).Success);
        Assert.Equal(EInspectionVerdict.Review, rig.Output().Verdict);
        Assert.False(rig.Output().IsQualified);
        Assert.Equal(EInspectionVerdict.Review, rig.Host.Engine!.RunState.NodeOutputs.Single(o => o.NodeId == "consumer").Value);
    }

    [Fact]
    public async Task PrepareRunAsync_LaterNodeFailure_RollsBackAndAllowsSubsequentRun()
    {
        await using var rig = new Rig(false);
        var second = (InspectLabelNodeModel)WorkflowNodeConfigurationSnapshotter.Capture(rig.Node);
        second.Id = "inspect2"; second.RecipeJson = "{\"name\":\"invalid\",\"width\":0}";
        rig.Document.CanvasProjection.Nodes.Add(new() { Node = second });
        var connection = rig.Document.CanvasProjection.Connections.Single(c => c.FromNodeId == "inspect");
        rig.Document.CanvasProjection.Connections.Remove(connection);
        rig.Document.CanvasProjection.Connections.Add(new() { FromNodeId = "inspect", FromPort = WorkflowPorts.Success, ToNodeId = "inspect2", ToPort = WorkflowPorts.Input });
        rig.Document.CanvasProjection.Connections.Add(new() { FromNodeId = "inspect2", FromPort = WorkflowPorts.Success, ToNodeId = "consumer", ToPort = WorkflowPorts.Input });
        await Assert.ThrowsAnyAsync<Exception>(() => rig.RunAsync());
        Assert.Empty(rig.Host.Engine!.RunState.NodeOutputs);
        second.RecipeJson = rig.Node.RecipeJson;
        Assert.True((await rig.RunAsync()).Success);
        Assert.Equal(EInspectionVerdict.Ok, rig.Output().Verdict);
    }

    [Fact]
    public async Task RunAsync_NothingConfigured_ReturnsSdkNgNotFakeOk()
    {
        await using var rig = new Rig(false);
        rig.Node.RecipeJson = rig.Json(new InspectionRecipe("empty", 64, 48, EInspectionMode.Free, EAlignmentMode.AssumeAligned,
            Array.Empty<InspectionRegion>(), Rig.Options));
        Assert.True((await rig.RunAsync()).Success);
        Assert.Equal(EInspectionVerdict.Ng, rig.Output().Verdict);
        Assert.False(rig.Output().IsQualified);
    }

    [Fact]
    public async Task RunAsync_ReferenceReplacedAfterPreparation_UsesSnapshotThenRefreshesNextRun()
    {
        await using var rig = new Rig(false);
        rig.Node.ReferenceImagePath = "reference.png";
        rig.WriteImage("reference.png", false);
        rig.Node.RecipeJson = rig.Json(new InspectionRecipe("fixed", 64, 48, EInspectionMode.Template, EAlignmentMode.AssumeAligned,
            new[] { new InspectionRegion("fixed", ERegionKind.Fixed, new PixelBounds(4, 4, 56, 40)) }, Rig.Options));
        rig.BeforeFirstNode = () => rig.WriteImage("reference.png", true);
        Assert.True((await rig.RunAsync()).Success);
        var first = rig.Output();
        Assert.Equal(EInspectionVerdict.Ok, first.Verdict);
        rig.BeforeFirstNode = null;
        Assert.True((await rig.RunAsync()).Success);
        var second = rig.Output();
        Assert.Equal(EInspectionVerdict.Ng, second.Verdict);
        Assert.Equal(first.RecipeSha256, second.RecipeSha256);
        Assert.NotEqual(first.ResourceIdentity, second.ResourceIdentity);
        Assert.Equal("reference.png", rig.Node.ReferenceImagePath);
    }

    [Theory]
    [InlineData("missing-reference")]
    [InlineData("outside-root")]
    [InlineData("invalid-recipe")]
    [InlineData("missing-model")]
    public async Task RunAsync_InvalidActiveResource_FailsBeforeFirstNode(string fault)
    {
        await using var rig = new Rig(false);
        switch (fault)
        {
            case "invalid-recipe": rig.Node.RecipeJson = "{\"name\":\"broken\",\"width\":0}"; break;
            case "missing-model": rig.Node.RecognitionModelPath = "absent.onnx"; break;
            default:
                rig.Node.RecipeJson = rig.Json(new InspectionRecipe("template", 64, 48, EInspectionMode.Template, EAlignmentMode.AssumeAligned,
                    new[] { new InspectionRegion("blank", ERegionKind.Blank, new PixelBounds(4, 4, 56, 40)) }, Rig.Options));
                rig.Node.ReferenceImagePath = fault == "outside-root" ? "../outside.png" : "absent.png"; break;
        }
        await Assert.ThrowsAnyAsync<Exception>(() => rig.RunAsync());
        Assert.Empty(rig.Host.Engine!.RunState.NodeOutputs);
        Assert.Null(rig.Frames.Capture("image"));
        Assert.Null(rig.Frames.Capture("inspect"));
    }

    [Fact]
    public async Task RunAsync_MismatchedInputSize_FaultsWithoutInspectionOutput()
    {
        await using var rig = new Rig(false);
        rig.Node.RecipeJson = rig.Json(new InspectionRecipe("wrong-size", 32, 24, EInspectionMode.Free, EAlignmentMode.AssumeAligned,
            new[] { new InspectionRegion("blank", ERegionKind.Blank, new PixelBounds(4, 4, 20, 16)) }, Rig.Options));
        Assert.False((await rig.RunAsync()).Success);
        Assert.DoesNotContain(rig.Host.Engine!.RunState.NodeOutputs, o => o.NodeId == "inspect");
        Assert.Null(rig.Frames.Capture("inspect"));
    }

    [Fact]
    public async Task RunAsync_DisabledReferenceBranch_DoesNotLoadStaleReference()
    {
        await using var rig = new Rig(false);
        rig.Node.ReferenceImagePath = "../missing-unused.png";
        Assert.True((await rig.RunAsync()).Success);
        Assert.Equal(EInspectionVerdict.Ok, rig.Output().Verdict);
    }

    [Fact]
    public async Task DocumentJson_RoundTrip_PreservesNativeRecipeTasksAndBindings()
    {
        await using var rig = new Rig(false);
        var text = new InspectionRegion("text", ERegionKind.Text, new PixelBounds(4, 4, 40, 20), true,
            new FieldSettings(expected: "ABC")).WithTasks(new RoiInspectionTasks(false, false, false));
        rig.Node.RecipeJson = rig.Json(new InspectionRecipe("retained", 64, 48, EInspectionMode.Free, EAlignmentMode.AssumeAligned,
            new[] { text }, Rig.Options));
        var store = new WorkflowDocumentJsonStore(rig.Nodes);
        var document = store.Deserialize(store.Serialize(rig.Document)).Document;
        _ = new WorkflowCompiler(rig.Nodes).Compile(document);
        var node = Assert.IsType<InspectLabelNodeModel>(document.CanvasProjection.Nodes.Single(n => n.Node.Id == "inspect").Node);
        Assert.Equal(rig.Node.RecipeJson, node.RecipeJson);
        var recipe = new InspectionRecipeSerializer(new OpenCvImageCodec()).Deserialize(node.RecipeJson);
        Assert.Equal("ABC", recipe.Regions[0].Field.Expected);
        Assert.False(recipe.Regions[0].Tasks.ReadData);
        Assert.False(recipe.Regions[0].Tasks.CheckQuality);
        Assert.Equal("image", node.Frame.Binding!.Value.NodeId);
    }

    [Fact]
    public async Task PrepareRunAsync_Cancelled_DoesNotCommitOrPublish()
    {
        await using var rig = new Rig(false);
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => rig.Runtime.PrepareRunAsync(
            new(new[] { rig.Node }, WorkflowRunScopeKind.Root, BindingScopeId: Guid.NewGuid()), cancel.Token).AsTask());
        Assert.Null(rig.Frames.Capture("inspect"));
    }

    [Fact]
    public async Task RunAsync_MissingHostCapability_IsRejectedBeforeExecution()
    {
        await using var rig = new Rig(false);
        using var host = new WorkflowRuntimeHost(rig.Nodes, rig.Handlers);
        host.Configure(rig.Document, new WorkflowContext(new WorkflowServiceProvider().Add<IImageFileReader>(new DP.Vision.OpenCv.OpenCvImageFileReader()).Add<IWorkflowVisionFrameScope>(rig.Frames)));
        await Assert.ThrowsAsync<WorkflowRuntimeCapabilityException>(() => host.RunAsync());
        Assert.Null(rig.Frames.Capture("image"));
    }

    internal sealed class Rig : IAsyncDisposable
    {
        internal static InspectionOptions Options => new(minimumContrast: 0, minimumSharpness: 0, tolerancePixels: 0);
        internal string Root { get; } = Path.Combine(Path.GetTempPath(), "label-node-test-" + Guid.NewGuid().ToString("N"));
        internal WorkflowNodeCatalog Nodes { get; } = new();
        internal WorkflowNodeHandlerCatalog Handlers { get; } = new();
        internal WorkflowVisionFrameScope Frames { get; } = new();
        internal WorkflowLabelInspectionRuntime Runtime { get; }
        internal WorkflowRuntimeHost Host { get; }
        internal WorkflowDocument Document { get; } = new() { EntryNodeId = "image" };
        internal InspectLabelNodeModel Node { get; }
        internal Action? BeforeFirstNode;
        internal IWorkflowLabelInspectionService? InspectionService;
        internal Rig(bool ink)
        {
            Directory.CreateDirectory(Root);
            var composition = new WorkflowRuntimePluginCatalog(Nodes, Handlers).Register(new WorkflowImageRuntimePluginModule()).Register(new WorkflowLabelInspectionModule());
            Nodes.Register(WorkflowNodeDescriptor.Create<Consumer, EInspectionVerdict>(ports: new[] { WorkflowPortDescriptor.Input(), WorkflowPortDescriptor.Output() }));
            Handlers.Register(new ConsumerHandler()); composition.Freeze();
            WriteImage("input.png", ink);
            Node = new InspectLabelNodeModel { Id = "inspect", ResourceRoot = Root, Frame = WorkflowInput<ImageFrame>.FromBinding(new("image", "$")),
                CycleId = WorkflowInput<string>.FromLiteral("cycle-1"), RecipeJson = Json(new InspectionRecipe("blank", 64, 48,
                    EInspectionMode.Free, EAlignmentMode.AssumeAligned, new[] { new InspectionRegion("blank", ERegionKind.Blank, new PixelBounds(4, 4, 56, 40)) }, Options)) };
            IWorkflowNodeModel[] models = [new AcquireVisionImageNodeModel { Id = "image", FilePath = Path.Combine(Root, "input.png") }, Node,
                new Consumer { Id = "consumer", Verdict = WorkflowInput<EInspectionVerdict>.FromBinding(new("inspect", "Report.Verdict")) }];
            foreach (var model in models) Document.CanvasProjection.Nodes.Add(new() { Node = model });
            for (int i = 1; i < models.Length; i++) Document.CanvasProjection.Connections.Add(new() { FromNodeId = models[i - 1].Id,
                FromPort = WorkflowPorts.Success, ToNodeId = models[i].Id, ToPort = WorkflowPorts.Input });
            Runtime = new WorkflowLabelInspectionRuntime(() => Root, new CallbackPreparation(this));
            Host = new WorkflowRuntimeHost(Nodes, Handlers);
        }
        internal string Json(InspectionRecipe recipe) => new InspectionRecipeSerializer(new OpenCvImageCodec()).Serialize(recipe);
        internal void WriteImage(string name, bool ink)
        {
            var bytes = Enumerable.Repeat((byte)255, 64 * 48).ToArray();
            if (ink) for (int y = 12; y < 24; y++) for (int x = 12; x < 24; x++) bytes[y * 64 + x] = 0;
            File.WriteAllBytes(Path.Combine(Root, name), new OpenCvImageCodec().EncodePng(new PixelSnapshot(64, 48, EImagePixelFormat.Gray8, bytes)));
        }
        internal Task<WorkflowRunResult> RunAsync()
        {
            var services = new WorkflowServiceProvider().Add<IImageFileReader>(new DP.Vision.OpenCv.OpenCvImageFileReader())
                .Add<IWorkflowVisionFrameScope>(Frames).Add<IWorkflowLabelInspectionService>(InspectionService ?? Runtime)
                .Add<IWorkflowRunPreparationService>(Runtime).Add<IWorkflowRunResourceOwner>(Frames);
            Host.Configure(Document, new WorkflowContext(services)); return Host.RunAsync();
        }
        internal WorkflowLabelInspectionResult Output() => Assert.IsType<WorkflowLabelInspectionResult>(Host.Engine!.RunState.NodeOutputs.Single(o => o.NodeId == "inspect").Value);
        public async ValueTask DisposeAsync()
        {
            try { await Host.StopAsync(); } catch (Exception) { /* 已由测试断言的运行准备异常在StopAsync再次传播。 */ }
            Host.Dispose(); await Runtime.DisposeAsync(); Frames.Dispose(); Directory.Delete(Root, true);
        }
        private sealed class CallbackPreparation(Rig rig) : IWorkflowRunPreparationService
        {
            public async ValueTask PrepareAsync(WorkflowRunPreparationContext context, CancellationToken token)
            { rig.BeforeFirstNode?.Invoke(); await rig.Frames.PrepareAsync(context, token); }
        }
    }
    private sealed class ReviewService : IWorkflowLabelInspectionService
    {
        public Task<WorkflowLabelInspectionResult> InspectAsync(IWorkflowNodeExecutionContext context, ImageFrame frame, string? cycleId,
            TaskDataSnapshot? taskData, CancellationToken cancellationToken) => Task.FromResult(new WorkflowLabelInspectionResult(frame.FrameId,
                cycleId, "review", "test-recipe", "test-resource", new InspectionReport("test", EInspectionVerdict.Review,
                    new BackendAnalysis(double.NaN, double.NaN, Array.Empty<RegionInspectionResult>()), Array.Empty<InspectionFinding>(), 1)));
    }

    [WorkflowNode("Test.Label.Consumer")]
    public sealed class Consumer : WorkflowNodeModel
    {
        public override string NodeType => "Test.Label.Consumer";
        public WorkflowInput<EInspectionVerdict> Verdict { get; set; } = WorkflowInput<EInspectionVerdict>.FromLiteral(EInspectionVerdict.Review);
    }
    private sealed class ConsumerHandler : WorkflowNodeHandler<Consumer>
    {
        protected override ValueTask<NodeExecutionResult> ExecuteAsync(Consumer node, IWorkflowNodeExecutionContext context, CancellationToken token) =>
            ValueTask.FromResult(NodeExecutionResult.Continue(output: context.ResolveInput(node.Verdict)));
    }
}
