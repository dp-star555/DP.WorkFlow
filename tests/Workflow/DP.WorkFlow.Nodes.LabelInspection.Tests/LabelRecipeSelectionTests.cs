using System.Text.Json;
using DP.LabelInspection.Contracts;
using DP.Vision;
using DP.Vision.Algorithms;
using DP.WorkFlow.LabelInspection;

namespace DP.WorkFlow.Tests;

public sealed class LabelRecipeSelectionTests
{
    [Fact]
    public async Task SamePreparedScope_ChangesRecipePerRequest_WithoutRecompileOrRestart_AndReusesBothCaches()
    {
        await using var rig = new LabelInspectionPipelineTests.Rig(false);
        await PublishPair(rig);
        await using var prepared = await Prepare(rig);
        var context = new Context(rig.Node, Scope(prepared));
        using var frame = Frame();
        var a = await rig.Runtime.InspectRecipeAsync(context, frame, "A", "one", null, null, default);
        var b = await rig.Runtime.InspectRecipeAsync(context, frame, "B@1", "two", null, null, default);
        var again = await rig.Runtime.InspectRecipeAsync(context, frame, "A@1", "three", null, null, default);
        Assert.Equal(EInspectionVerdict.Ok, a.Verdict); Assert.Equal(EInspectionVerdict.Ng, b.Verdict);
        Assert.Equal("A", a.RecipeId); Assert.Equal(1, a.RecipeVersion); Assert.Equal("B", b.RecipeId);
        Assert.Equal("one", a.CycleId); Assert.Equal("two", b.CycleId); Assert.Equal("three", again.CycleId);
        Assert.Equal(a.ResourceIdentity, again.ResourceIdentity);
        Assert.Equal(2, rig.Runtime.ResourceLoadCount); Assert.Equal(2, rig.Runtime.CachedResourceCount);
        Assert.Equal(1, rig.Runtime.CacheStatistics.RegisteredRecipes);
        Assert.NotNull(a.RecipeJson); Assert.NotEqual(a.RecipeJson, b.RecipeJson);
    }

    [Fact]
    public async Task Handler_CatalogModeWithNoEmbeddedRecipe_ReturnsActualProfileVersionAndTypedDownstreamOutput()
    {
        await using var rig = new LabelInspectionPipelineTests.Rig(false);
        await PublishPair(rig); rig.Node.RecipeJson = string.Empty;
        rig.Node.RecipeKey = WorkflowInput<string>.FromLiteral("B");
        Assert.True((await rig.RunAsync()).Success);
        Assert.Equal("B", rig.Output().RecipeId); Assert.Equal(1, rig.Output().RecipeVersion);
        Assert.Equal(EInspectionVerdict.Ng, rig.Output().Verdict);
        Assert.Equal(EInspectionVerdict.Ng, rig.Host.Engine!.RunState.NodeOutputs.Single(o => o.NodeId == "consumer").Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("unknown")]
    [InlineData("A@999")]
    public async Task UnknownSelection_DoesNotFallbackToEmbeddedRecipe(string key)
    {
        await using var rig = new LabelInspectionPipelineTests.Rig(false); await PublishPair(rig);
        rig.Node.RecipeKey = WorkflowInput<string>.FromLiteral(key);
        Assert.False((await rig.RunAsync()).Success);
        Assert.DoesNotContain(rig.Host.Engine!.RunState.NodeOutputs, o => o.NodeId == "inspect");
        Assert.Equal(0, rig.Runtime.ResourceLoadCount);
    }

    [Fact]
    public async Task SelectedProfiles_UseTheirOwnReferencePose_NotTheNodeDraftPose()
    {
        await using var rig = new LabelInspectionPipelineTests.Rig(false);
        var a = Profile(rig, "A", 1) with { ReferencePose = new(1, 0, 0, 0, 1, 0) };
        var b = Profile(rig, "B", 1) with { ReferencePose = new(1, 0, 10, 0, 1, 5) };
        await Publish(rig, a); await Publish(rig, b);
        rig.Node.SetReferencePose(CoordinateMatrix2D.FromAffine(1, 0, 1000, 0, 1, 1000));
        await using var prepared = await Prepare(rig); var context = new Context(rig.Node, Scope(prepared));
        using var frame = Frame(84, 58);
        var coordinates = new VisionCoordinateSystem(new VisionCoordinateDefinition("label", "标签"), frame.FrameId, 84, 58,
            CoordinateMatrix2D.FromAffine(1, 0, 10, 0, 1, 5));
        var first = await rig.Runtime.InspectRecipeAsync(context, frame, "A", null, null, coordinates, default);
        var second = await rig.Runtime.InspectRecipeAsync(context, frame, "B", null, null, coordinates, default);
        Assert.Equal((10d, 5d), first.Placement!.Map(0, 0)); Assert.Equal((0d, 0d), second.Placement!.Map(0, 0));
        Assert.Equal(EInspectionVerdict.Ok, first.Verdict); Assert.Equal(EInspectionVerdict.Ok, second.Verdict);
    }

    [Fact]
    public async Task CatalogRefresh_UpdatesFutureSelection_WhileLoadingRequestKeepsCapturedOldVersion()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim(); int modelLoads = 0;
        await using var rig = new LabelInspectionPipelineTests.Rig(false, ocr: _ =>
        { Interlocked.Increment(ref modelLoads); entered.TrySetResult(); if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException(); return new NoReadRecognizer(); });
        File.WriteAllBytes(Path.Combine(rig.Root, "ocr.onnx"), [1, 2]);
        var original = Profile(rig, "A", 1) with { RecognitionModelPath = "ocr.onnx" };
        await Publish(rig, original);
        await using var prepared = await Prepare(rig); var context = new Context(rig.Node, Scope(prepared)); using var frame = Frame();
        var first = rig.Runtime.InspectRecipeAsync(context, frame, "A", "old", null, null, default);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Publish(rig, original with { Version = 2 });
            await rig.Runtime.ReloadRecipeCatalogAsync(rig.Root, Index);
            var second = rig.Runtime.InspectRecipeAsync(context, frame, "A", "new", null, null, default);
            release.Set();
            var outputs = await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(1, outputs[0].RecipeVersion); Assert.Equal(2, outputs[1].RecipeVersion);
            Assert.Equal("old", outputs[0].CycleId); Assert.Equal("new", outputs[1].CycleId);
            Assert.Equal(1, modelLoads);
            var pinned = await rig.Runtime.InspectRecipeAsync(context, frame, "A@1", null, null, null, default);
            Assert.Equal(1, pinned.RecipeVersion);
        }
        finally { release.Set(); }
    }

    [Fact]
    public async Task Prewarm_UsesSameProductionCache_WithoutReportOrGlobalSelection()
    {
        await using var rig = new LabelInspectionPipelineTests.Rig(false); await PublishPair(rig);
        await rig.Runtime.PrewarmRecipeAsync(rig.Root, Index, "A");
        Assert.Equal(1, rig.Runtime.ResourceLoadCount); Assert.Null(rig.Frames.Capture("inspect"));
        await using var prepared = await Prepare(rig); var context = new Context(rig.Node, Scope(prepared)); using var frame = Frame();
        var output = await rig.Runtime.InspectRecipeAsync(context, frame, "A", null, null, null, default);
        Assert.Equal("A", output.RecipeId); Assert.Equal(1, rig.Runtime.ResourceLoadCount);
        Assert.Equal(1, rig.Runtime.CacheStatistics.RecipeHits);
    }

    [Fact]
    public async Task Publish_SameVersionDifferentContent_IsRejected_AndExistingIndexRemainsUsable()
    {
        await using var rig = new LabelInspectionPipelineTests.Rig(false);
        var profile = Profile(rig, "A", 1); var first = await Publish(rig, profile);
        var repeated = await Publish(rig, profile); Assert.Equal(first, repeated);
        var changed = profile with { AuthorImagePath = "input.png" };
        await Assert.ThrowsAsync<InvalidOperationException>(() => Publish(rig, changed));
        var entries = await WorkflowLabelRecipeCatalogStore.ReadIndexAsync(rig.Root, Index);
        Assert.Single(entries); Assert.Equal(first, entries[0]);
        var loaded = await WorkflowLabelRecipeCatalogStore.ReadProfileAsync(rig.Root, entries[0]);
        Assert.Equal(profile.RecipeJson, loaded.RecipeJson);
    }

    [Fact]
    public async Task RemovingIndexEntry_DoesNotPermitRewritingPublishedHistory()
    {
        await using var rig = new LabelInspectionPipelineTests.Rig(false);
        var original = Profile(rig, "A", 1); await Publish(rig, original);
        await File.WriteAllTextAsync(Path.Combine(rig.Root, Index), "{\"schemaVersion\":1,\"recipes\":[]}");
        await Assert.ThrowsAsync<InvalidOperationException>(() => Publish(rig, original with { AuthorImagePath = "input.png" }));
        Assert.Empty(await WorkflowLabelRecipeCatalogStore.ReadIndexAsync(rig.Root, Index));
        var restored = await Publish(rig, original); Assert.Equal("A@1", restored.Key);
    }

    [Fact]
    public void WorkflowDocumentRoundTrip_PreservesCatalogPath_AndTaskSelectionBinding()
    {
        var node = new InspectLabelNodeModel { Id = "inspect", RecipeCatalogPath = "recipes/index.json",
            RecipeKey = WorkflowInput<string>.FromBinding(new("task", "RecipeKey")) };
        var document = new WorkflowDocument { Name = "catalog", EntryNodeId = node.Id };
        document.CanvasProjection.Nodes.Add(new() { Node = node });
        var nodes = new WorkflowNodeCatalog(); nodes.Register(WorkflowNodeDescriptor.Create<InspectLabelNodeModel, WorkflowLabelInspectionResult>());
        var store = new DP.WorkFlow.Persistence.Json.WorkflowDocumentJsonStore(nodes);
        var roundTrip = store.Deserialize(store.Serialize(document)).Document.CanvasProjection.Nodes.Single().Node;
        var restored = Assert.IsType<InspectLabelNodeModel>(roundTrip);
        Assert.Equal(node.RecipeCatalogPath, restored.RecipeCatalogPath);
        Assert.Equal(node.RecipeKey.Binding, restored.RecipeKey.Binding);
        Assert.True(restored.UsesRecipeCatalog);
    }

    [Fact]
    public async Task Refresh_InvalidIndex_LeavesOldPublishedDirectoryActive()
    {
        await using var rig = new LabelInspectionPipelineTests.Rig(false); await PublishPair(rig);
        await using var prepared = await Prepare(rig); var context = new Context(rig.Node, Scope(prepared));
        await File.WriteAllTextAsync(Path.Combine(rig.Root, Index), "{broken");
        await Assert.ThrowsAsync<JsonException>(() => rig.Runtime.ReloadRecipeCatalogAsync(rig.Root, Index));
        using var frame = Frame();
        var output = await rig.Runtime.InspectRecipeAsync(context, frame, "A", null, null, null, default);
        Assert.Equal("A", output.RecipeId);
    }

    [Fact]
    public async Task ColdLoad_TamperedProfileOrAsset_RejectsWithoutUsingStaleEmbeddedDraft()
    {
        await using var rig = new LabelInspectionPipelineTests.Rig(false); await PublishPair(rig);
        await using var prepared = await Prepare(rig); var context = new Context(rig.Node, Scope(prepared)); using var frame = Frame();
        var entries = await WorkflowLabelRecipeCatalogStore.ReadIndexAsync(rig.Root, Index);
        var a = entries.Single(e => e.Id == "A");
        await File.AppendAllTextAsync(Path.Combine(rig.Root, a.ProfilePath), " ");
        await Assert.ThrowsAsync<InvalidDataException>(() => rig.Runtime.InspectRecipeAsync(context, frame, "A", null, null, null, default));
        rig.WriteImage("b-reference.png", false);
        await Assert.ThrowsAsync<InvalidDataException>(() => rig.Runtime.InspectRecipeAsync(context, frame, "B", null, null, null, default));
        Assert.Equal(0, rig.Runtime.ResourceLoadCount);
    }

    [Fact]
    public async Task LightweightCatalog_100MissingDetailedProfiles_CanRegisterWithoutLoadingThem()
    {
        await using var rig = new LabelInspectionPipelineTests.Rig(false);
        Directory.CreateDirectory(Path.Combine(rig.Root, "recipes"));
        var entries = Enumerable.Range(0, 100).Select(i => new WorkflowLabelRecipeCatalogEntry("P" + i, 1, "absent-" + i + ".json", new string('a', 64))).ToArray();
        await File.WriteAllTextAsync(Path.Combine(rig.Root, Index), JsonSerializer.Serialize(new { schemaVersion = 1, recipes = entries }));
        rig.Node.RecipeCatalogPath = Index; rig.Node.RecipeJson = string.Empty;
        await using var prepared = await Prepare(rig);
        Assert.Equal(0, rig.Runtime.ResourceLoadCount); Assert.Equal(1, rig.Runtime.CacheStatistics.RegisteredRecipes);
    }

    [Theory]
    [InlineData("../outside.json")]
    [InlineData("..\\outside.json")]
    public async Task Catalog_PathEscape_IsRejected(string path)
    {
        await using var rig = new LabelInspectionPipelineTests.Rig(false);
        Directory.CreateDirectory(Path.Combine(rig.Root, "recipes"));
        await File.WriteAllTextAsync(Path.Combine(rig.Root, Index), JsonSerializer.Serialize(new { recipes = new[]
        { new WorkflowLabelRecipeCatalogEntry("A", 1, path, new string('a', 64)) } }));
        await Assert.ThrowsAsync<InvalidDataException>(() => WorkflowLabelRecipeCatalogStore.ReadIndexAsync(rig.Root, Index));
    }

    [Fact]
    public async Task CatalogCapacity_OneSlot_CanSwitchAwayAndReloadEvictedProfile()
    {
        await using var rig = new LabelInspectionPipelineTests.Rig(false, new() { MaximumCachedRecipes = 1 }); await PublishPair(rig);
        await using var prepared = await Prepare(rig); var context = new Context(rig.Node, Scope(prepared)); using var frame = Frame();
        foreach (var key in new[] { "A", "B", "A" })
        {
            var output = await rig.Runtime.InspectRecipeAsync(context, frame, key, null, null, null, default);
            Assert.Equal(key, output.RecipeId); Assert.Equal(1, rig.Runtime.CachedResourceCount);
        }
        Assert.Equal(3, rig.Runtime.ResourceLoadCount);
    }

    private const string Index = "recipes/index.json";
    private static WorkflowLabelRecipeProfile Profile(LabelInspectionPipelineTests.Rig rig, string id, int version) => WorkflowLabelRecipeProfile.FromNode(rig.Node, id, version);
    private static Task<WorkflowLabelRecipeCatalogEntry> Publish(LabelInspectionPipelineTests.Rig rig, WorkflowLabelRecipeProfile profile)
    { rig.Node.RecipeCatalogPath = Index; return WorkflowLabelRecipeCatalogStore.PublishAsync(rig.Root, Index, profile); }
    private static async Task PublishPair(LabelInspectionPipelineTests.Rig rig)
    {
        var a = Profile(rig, "A", 1); rig.WriteImage("b-reference.png", true);
        var b = a with { Id = "B", ReferenceImagePath = "b-reference.png", RecipeJson = rig.Json(new InspectionRecipe("fixed-B", 64, 48,
            EInspectionMode.Template, EAlignmentMode.AssumeAligned, new[] { new InspectionRegion("fixed", ERegionKind.Fixed, new PixelBounds(4, 4, 56, 40)) },
            LabelInspectionPipelineTests.Rig.Options)) };
        await Publish(rig, a); await Publish(rig, b);
    }
    // 作用域包装保证测试在同一真实准备登记中执行多次请求，而非每次调用重建计划。
    private sealed class Prepared(IWorkflowPreparedRun inner, Guid scope) : IWorkflowPreparedRun
    { internal Guid Scope { get; } = scope; public void Commit() => inner.Commit(); public ValueTask DisposeAsync() => inner.DisposeAsync(); }
    private static Guid Scope(Prepared prepared) => prepared.Scope;
    private static async Task<Prepared> Prepare(LabelInspectionPipelineTests.Rig rig)
    {
        var scope = Guid.NewGuid();
        var inner = await rig.Runtime.PrepareRunAsync(new(new[] { rig.Node }, WorkflowRunScopeKind.Root, BindingScopeId: scope), default);
        inner.Commit(); return new(inner, scope);
    }
    private static ImageFrame Frame(int width = 64, int height = 48)
    {
        using var image = VisionImage.CopyFrom(new ImageInfo(width, height, EPixelLayout.Gray8), Enumerable.Repeat((byte)255, width * height).ToArray());
        return new ImageFrame("selection-test", image);
    }
    private sealed class NoReadRecognizer : ITextLineRecognizer
    { public TextLineRecognition Recognize(IImageSource frame, PixelBounds bounds, CancellationToken token) => throw new NotSupportedException(); public void Dispose() { } }
    private sealed class Context(InspectLabelNodeModel node, Guid scope) : IWorkflowNodeExecutionContext
    {
        public Guid BindingScopeId => scope; public IWorkflowNodeModel Node => node;
        public WorkflowExecutionIdentity ExecutionIdentity => default!; public int NodeExecutionCount => 1;
        public IServiceProvider Services => new WorkflowServiceProvider();
        public T GetRequiredCapability<T>() where T : class => throw new NotSupportedException();
        public bool TryGetVariable<T>(string key, out T? value) { value = default; return false; }
        public void SetVariable(string key, object value) => throw new NotSupportedException(); public bool RemoveVariable(string key) => false;
        public bool TryGetPublicData<T>(string key, out T? value) { value = default; return false; }
        public void PublishData(string key, object value) => throw new NotSupportedException(); public bool RemovePublicData(string key) => false;
        public T? ResolveInput<T>(WorkflowInput<T> input) => throw new NotSupportedException();
        public T? ResolveDynamicInput<T>(string inputKey, WorkflowInput<T> input) => throw new NotSupportedException();
        public void RaiseWorkflowSignal(string signalKey) => throw new NotSupportedException(); public bool ContainsWorkflowSignal(string signalKey) => false;
        public ValueTask<bool> WaitAllWorkflowSignalsAsync(IReadOnlyList<string> keys, TimeSpan? timeout, CancellationToken token) => throw new NotSupportedException();
        public void Trace(string step, string? message = null, IReadOnlyDictionary<string, object?>? data = null) { }
        public void Trace(string step, string? message, IReadOnlyDictionary<string, object?>? data, WorkflowEventWriteMode mode) { }
    }
}
