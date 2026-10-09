using DP.LabelInspection.Contracts;
using DP.LabelInspection.Runtime;
using DP.Vision;
using DP.Vision.Algorithms;
using DP.WorkFlow.LabelInspection;

namespace DP.WorkFlow.Tests;

public sealed class LabelInspectionCacheTests
{
    [Fact]
    public async Task PrepareRun_100Recipes_OnlyRegistersMetadataAndReleasesRegistrationOnRollback()
    {
        await using var runtime = new WorkflowLabelInspectionRuntime(() => Path.GetTempPath());
        await using var rig = new LabelInspectionPipelineTests.Rig(false);
        var nodes = Enumerable.Range(0, 100).Select(i =>
        {
            var node = (InspectLabelNodeModel)WorkflowNodeConfigurationSnapshotter.Capture(rig.Node);
            node.Id = "recipe-" + i; node.RecognitionModelPath = "absent.onnx"; return node;
        }).ToArray();
        await using (var prepared = await runtime.PrepareRunAsync(new(nodes, WorkflowRunScopeKind.Root, BindingScopeId: Guid.NewGuid()), default))
        {
            prepared.Commit();
            Assert.Equal(100, runtime.CacheStatistics.RegisteredRecipes);
            Assert.Equal(0, runtime.ResourceLoadCount);
            Assert.Equal(0, runtime.CacheStatistics.ModelLoads);
            Assert.Equal(0, runtime.CachedResourceCount);
        }
        Assert.Equal(0, runtime.CacheStatistics.RegisteredRecipes);
    }

    [Fact]
    public async Task Run_UnselectedRecipesWithMissingModels_DoNotLoadOrBlockSelectedRecipe()
    {
        await using var rig = new LabelInspectionPipelineTests.Rig(false);
        var router = new LabelInspectionPipelineTests.SelectBranch { Id = "select" };
        rig.Document.CanvasProjection.Nodes.Add(new() { Node = router });
        var original = rig.Document.CanvasProjection.Connections.Single(c => c.FromNodeId == "image");
        rig.Document.CanvasProjection.Connections.Remove(original);
        rig.Document.CanvasProjection.Connections.Add(new() { FromNodeId = "image", FromPort = WorkflowPorts.Success, ToNodeId = "select", ToPort = WorkflowPorts.Input });
        rig.Document.CanvasProjection.Connections.Add(new() { FromNodeId = "select", FromPort = WorkflowPorts.True, ToNodeId = "inspect", ToPort = WorkflowPorts.Input });
        for (int i = 0; i < 100; i++)
        {
            var node = (InspectLabelNodeModel)WorkflowNodeConfigurationSnapshotter.Capture(rig.Node);
            node.Id = "unused-" + i; node.RecognitionModelPath = "absent.onnx";
            rig.Document.CanvasProjection.Nodes.Add(new() { Node = node });
            rig.Document.CanvasProjection.Connections.Add(new() { FromNodeId = i == 0 ? "select" : "unused-" + (i - 1),
                FromPort = i == 0 ? WorkflowPorts.False : WorkflowPorts.Success, ToNodeId = node.Id, ToPort = WorkflowPorts.Input });
        }
        Assert.True((await rig.RunAsync()).Success);
        Assert.Equal(1, rig.Runtime.ResourceLoadCount);
        Assert.Equal(0, rig.Runtime.CacheStatistics.ModelLoads);
    }

    [Fact]
    public async Task Run_TwoNodesSameModelContentAtDifferentPaths_SharesModelsAndKeepsRecipesIndependent()
    {
        int ocrCreated = 0, ocrDisposed = 0, cnnCreated = 0, cnnDisposed = 0;
        await using var rig = new LabelInspectionPipelineTests.Rig(false,
            ocr: _ => { Interlocked.Increment(ref ocrCreated); return new TestRecognizer(() => Interlocked.Increment(ref ocrDisposed)); },
            anomaly: _ => { Interlocked.Increment(ref cnnCreated); return new TestAnomaly(() => Interlocked.Increment(ref cnnDisposed)); });
        File.WriteAllBytes(Path.Combine(rig.Root, "ocr.onnx"), [1, 2, 3]);
        File.WriteAllBytes(Path.Combine(rig.Root, "ocr-copy.onnx"), [1, 2, 3]);
        File.WriteAllBytes(Path.Combine(rig.Root, "cnn.onnx"), [4, 5, 6]);
        rig.Node.RecognitionModelPath = "ocr.onnx"; rig.Node.AnomalyBackbonePath = "cnn.onnx";
        var second = AddSecond(rig);
        second.RecognitionModelPath = "ocr-copy.onnx";
        Assert.True((await rig.RunAsync()).Success);
        Assert.Equal(1, ocrCreated); Assert.Equal(1, cnnCreated);
        Assert.Equal(2, rig.Runtime.ResourceLoadCount);
        Assert.Equal(2, rig.Runtime.CacheStatistics.ModelLoads);
        Assert.Equal("second", Assert.IsType<WorkflowLabelInspectionResult>(rig.Host.Engine!.RunState.NodeOutputs.Single(o => o.NodeId == "inspect2").Value).RecipeName);
        Assert.Equal("blank", rig.Output().RecipeName);
        Assert.Equal(0, ocrDisposed); Assert.Equal(0, cnnDisposed);
        rig.Runtime.CleanupIdleResources(true);
        Assert.Equal(0, rig.Runtime.CachedResourceCount); Assert.Equal(0, rig.Runtime.CacheStatistics.CachedModels);
        Assert.Equal(1, ocrDisposed); Assert.Equal(1, cnnDisposed);
    }

    [Fact]
    public async Task Run_IdleExpiration_ReleasesRecipeAndModelAndReloadsOnNextUse()
    {
        var clock = new TestClock(); int created = 0, disposed = 0;
        await using var rig = new LabelInspectionPipelineTests.Rig(false, new() { TimeProvider = clock },
            _ => { created++; return new TestRecognizer(() => disposed++); });
        File.WriteAllBytes(Path.Combine(rig.Root, "ocr.onnx"), [1]); rig.Node.RecognitionModelPath = "ocr.onnx";
        Assert.True((await rig.RunAsync()).Success);
        clock.Advance(TimeSpan.FromDays(1) + TimeSpan.FromSeconds(1));
        rig.Runtime.CleanupIdleResources();
        Assert.Equal(0, rig.Runtime.CachedResourceCount); Assert.Equal(0, rig.Runtime.CacheStatistics.CachedModels);
        Assert.Equal(1, disposed);
        Assert.True((await rig.RunAsync()).Success);
        Assert.Equal(2, created); Assert.Equal(2, rig.Runtime.ResourceLoadCount);
    }

    [Fact]
    public async Task Run_BackgroundExpiration_CleansIdleResourcesWithoutManualSweep()
    {
        await using var rig = new LabelInspectionPipelineTests.Rig(false, new()
        { IdleExpiration = TimeSpan.FromMilliseconds(200), CleanupInterval = TimeSpan.FromMilliseconds(20) });
        Assert.True((await rig.RunAsync()).Success);
        Assert.Equal(1, rig.Runtime.ResourceLoadCount);
        var deadline = System.Diagnostics.Stopwatch.StartNew();
        while (rig.Runtime.CachedResourceCount != 0 && deadline.Elapsed < TimeSpan.FromSeconds(5)) await Task.Delay(20);
        Assert.Equal(0, rig.Runtime.CachedResourceCount);
        Assert.Equal(1, rig.Runtime.CacheStatistics.RecipeEvictions);
    }

    [Fact]
    public async Task Run_RecipeCapacityOne_EvictsIdleRecipeButReusesSharedModel()
    {
        int created = 0;
        await using var rig = new LabelInspectionPipelineTests.Rig(false, new() { MaximumCachedRecipes = 1 },
            _ => { created++; return new TestRecognizer(); });
        File.WriteAllBytes(Path.Combine(rig.Root, "ocr.onnx"), [1]); rig.Node.RecognitionModelPath = "ocr.onnx";
        AddSecond(rig);
        Assert.True((await rig.RunAsync()).Success);
        Assert.Equal(1, created); Assert.Equal(1, rig.Runtime.CachedResourceCount);
        Assert.Equal(1, rig.Runtime.CacheStatistics.RecipeEvictions);
        Assert.True((await rig.RunAsync()).Success);
        Assert.Equal(1, created); Assert.Equal(4, rig.Runtime.ResourceLoadCount);
    }

    [Fact]
    public async Task Run_ModelCapacityOne_ReclaimsIdleRecipeBeforeLoadingDifferentModel()
    {
        int created = 0, disposed = 0;
        await using var rig = new LabelInspectionPipelineTests.Rig(false, new() { MaximumCachedModels = 1 },
            _ => { created++; return new TestRecognizer(() => disposed++); });
        File.WriteAllBytes(Path.Combine(rig.Root, "a.onnx"), [1]); File.WriteAllBytes(Path.Combine(rig.Root, "b.onnx"), [2]);
        rig.Node.RecognitionModelPath = "a.onnx"; AddSecond(rig).RecognitionModelPath = "b.onnx";
        Assert.True((await rig.RunAsync()).Success);
        Assert.Equal(2, created); Assert.Equal(1, disposed);
        Assert.Equal(1, rig.Runtime.CachedResourceCount); Assert.Equal(1, rig.Runtime.CacheStatistics.CachedModels);
    }

    [Fact]
    public async Task Models_RealCnnBackbone_SharedAcrossTwoBorrowersAndReloadedAfterCleanup()
    {
        var root = RepositoryRoot();
        var model = Path.Combine(root, "DP.Vision/tests/DP.Vision.Algorithms.Tests/Assets/ppocrv4_det_backbone.onnx");
        await using var models = new WorkflowLabelInspectionModels(new());
        using (var first = await models.AcquireAsync(root, model, false, default))
        using (var second = await models.AcquireAsync(root, model, false, default))
        {
            Assert.Same(first!.Value.Anomaly, second!.Value.Anomaly);
            Assert.Equal(1, models.Loads);
            models.Cleanup(true); Assert.Equal(1, models.Count);
        }
        models.Cleanup(true); Assert.Equal(0, models.Count);
        using var next = await models.AcquireAsync(root, model, false, default);
        Assert.Equal(2, models.Loads);
    }

    [Fact]
    public async Task Cache_ConcurrentFirstUseAndOneCancelledWaiter_OnlyLoadsOnceAndOtherWaiterSucceeds()
    {
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int loads = 0;
        await using var cache = Cache();
        using var cancel = new CancellationTokenSource();
        async Task<Tracked> Load(CancellationToken token)
        { Interlocked.Increment(ref loads); start.SetResult(); await finish.Task.WaitAsync(token); return new(); }
        var first = cache.AcquireAsync("one", Load, cancel.Token);
        await start.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = cache.AcquireAsync("one", Load, default);
        cancel.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        finish.SetResult();
        using var lease = await second.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, loads); Assert.Equal(1, cache.Hits);
    }

    [Fact]
    public async Task Cache_ActiveLease_IsNotExpiredOrForceCleaned_AndShutdownWaitsForRelease()
    {
        var clock = new TestClock(); var cache = Cache(clock);
        var item = new Tracked(); var lease = await cache.AcquireAsync("one", _ => Task.FromResult(item), default);
        clock.Advance(TimeSpan.FromDays(2)); cache.Cleanup(true);
        Assert.False(item.Disposed); Assert.Equal(1, cache.Count);
        var shutdown = cache.DisposeAsync().AsTask(); Assert.False(shutdown.IsCompleted);
        lease.Dispose(); await shutdown.WaitAsync(TimeSpan.FromSeconds(5)); Assert.True(item.Disposed);
    }

    [Fact]
    public async Task Cache_CapacityFullAndActive_RejectsColdLoadWithoutEvictingActiveResource()
    {
        await using var cache = Cache();
        using var lease = await cache.AcquireAsync("one", _ => Task.FromResult(new Tracked()), default);
        await Assert.ThrowsAsync<LabelInspectionCacheCapacityException>(() => cache.AcquireAsync("two", _ => Task.FromResult(new Tracked()), default));
        Assert.False(lease.Value.Disposed);
    }

    [Fact]
    public async Task Cache_FailedLoad_IsNotCachedAndCanBeRetried()
    {
        await using var cache = Cache();
        await Assert.ThrowsAsync<InvalidDataException>(() => cache.AcquireAsync("one", _ => Task.FromException<Tracked>(new InvalidDataException("bad")), default));
        Assert.Equal(0, cache.Count);
        using var retry = await cache.AcquireAsync("one", _ => Task.FromResult(new Tracked()), default);
        Assert.Equal(1, cache.Loads);
    }

    [Fact]
    public async Task Cache_ExpiredResource_ReloadsWithoutWaitingForMaintenanceTimer()
    {
        var clock = new TestClock(); await using var cache = Cache(clock);
        var first = new Tracked(); using (await cache.AcquireAsync("one", _ => Task.FromResult(first), default)) { }
        clock.Advance(TimeSpan.FromDays(2));
        using var next = await cache.AcquireAsync("one", _ => Task.FromResult(new Tracked()), default);
        Assert.True(first.Disposed); Assert.NotSame(first, next.Value); Assert.Equal(2, cache.Loads);
    }

    [Fact]
    public async Task Cache_VersionReplacement_DoesNotReleaseActiveOldVersionOrBypassCapacity()
    {
        await using var cache = Cache(); var old = new Tracked();
        var lease = await cache.AcquireAsync("v1", _ => Task.FromResult(old), default, "node");
        await Assert.ThrowsAsync<LabelInspectionCacheCapacityException>(() => cache.AcquireAsync("v2", _ => Task.FromResult(new Tracked()), default, "node"));
        Assert.False(old.Disposed);
        lease.Dispose();
        using var next = await cache.AcquireAsync("v2", _ => Task.FromResult(new Tracked()), default, "node");
        Assert.True(old.Disposed); Assert.Equal(2, cache.Loads);
    }

    [Fact]
    public async Task Models_ConcurrentRecognition_IsSerializedAcrossBorrowers_AndHostDoesNotOwnBorrowedModel()
    {
        int active = 0, peak = 0, disposed = 0;
        var directory = Path.Combine(Path.GetTempPath(), "shared-ocr-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory); File.WriteAllBytes(Path.Combine(directory, "ocr.onnx"), [1]);
        try
        {
            await using var models = new WorkflowLabelInspectionModels(new(), _ => new ConcurrentRecognizer(
                () => { if (Interlocked.Increment(ref active) == 1) Interlocked.CompareExchange(ref peak, 1, 0); else Interlocked.Exchange(ref peak, 2); },
                () => Interlocked.Decrement(ref active), () => disposed++));
            using (var first = await models.AcquireAsync(directory, "ocr.onnx", true, default))
            using (var second = await models.AcquireAsync(directory, "ocr.onnx", true, default))
            {
                using (var host = LabelInspectionHost.CreateWithBorrowedModels(new(Path.Combine(directory, "data")), first!.Value.Recognizer, null, null))
                using (var engine = host.CreateEngine()) { }
                Assert.Equal(0, disposed);
                using var image = VisionImage.CopyFrom(new ImageInfo(8, 8, EPixelLayout.Gray8), Enumerable.Repeat((byte)255, 64).ToArray());
                var calls = Enumerable.Range(0, 8).Select(i => Task.Run(() =>
                    (i % 2 == 0 ? first!.Value : second!.Value).Recognizer!.Recognize(image, new PixelBounds(0, 0, 8, 8), default))).ToArray();
                var results = await Task.WhenAll(calls).WaitAsync(TimeSpan.FromSeconds(5));
                Assert.Equal(1, peak);
                Assert.All(results, result => Assert.Equal(new PixelBounds(0, 0, 8, 8), result.Bounds));
            }
            models.Cleanup(true); Assert.Equal(1, disposed);
        }
        finally { Directory.Delete(directory, true); }
    }

    private sealed class ConcurrentRecognizer(Action enter, Action exit, Action dispose) : ITextLineRecognizer
    {
        public TextLineRecognition Recognize(IImageSource frame, PixelBounds bounds, CancellationToken token)
        {
            enter();
            try { Thread.Sleep(10); return new(bounds, "test", 8, 8, new[] { new CtcStep(0, 1) }, Array.Empty<CtcToken>()); }
            finally { exit(); }
        }
        public void Dispose() => dispose();
    }

    private static LabelInspectionResourceCache<Tracked> Cache(TimeProvider? clock = null) => new(1, 1, TimeSpan.FromDays(1), clock ?? TimeProvider.System);
    private static InspectLabelNodeModel AddSecond(LabelInspectionPipelineTests.Rig rig)
    {
        var second = (InspectLabelNodeModel)WorkflowNodeConfigurationSnapshotter.Capture(rig.Node);
        second.Id = "inspect2";
        second.RecipeJson = rig.Json(new InspectionRecipe("second", 64, 48, EInspectionMode.Free, EAlignmentMode.AssumeAligned,
            new[] { new InspectionRegion("blank", ERegionKind.Blank, new PixelBounds(8, 8, 32, 24)) }, LabelInspectionPipelineTests.Rig.Options));
        rig.Document.CanvasProjection.Nodes.Add(new() { Node = second });
        var link = rig.Document.CanvasProjection.Connections.Single(c => c.FromNodeId == "inspect");
        rig.Document.CanvasProjection.Connections.Remove(link);
        rig.Document.CanvasProjection.Connections.Add(new() { FromNodeId = "inspect", FromPort = WorkflowPorts.Success, ToNodeId = "inspect2", ToPort = WorkflowPorts.Input });
        rig.Document.CanvasProjection.Connections.Add(new() { FromNodeId = "inspect2", FromPort = WorkflowPorts.Success, ToNodeId = "consumer", ToPort = WorkflowPorts.Input });
        return second;
    }
    private static string RepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (Directory.Exists(Path.Combine(dir.FullName, "DP.Vision/src"))) return dir.FullName;
        throw new DirectoryNotFoundException("Cannot locate model test fixture.");
    }
    internal sealed class TestClock : TimeProvider
    {
        private long _timestamp;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Interlocked.Read(ref _timestamp);
        internal void Advance(TimeSpan elapsed) => Interlocked.Add(ref _timestamp, elapsed.Ticks);
    }
    private sealed class Tracked : IDisposable
    { internal bool Disposed; public void Dispose() => Disposed = true; }
    private sealed class TestRecognizer(Action? onDispose = null) : ITextLineRecognizer
    {
        public TextLineRecognition Recognize(IImageSource frame, PixelBounds bounds, CancellationToken token) => throw new NotSupportedException();
        public void Dispose() => onDispose?.Invoke();
    }
    private sealed class TestAnomaly(Action onDispose) : IPatchAnomalyDetector, IDisposable
    {
        public PatchAnomalyModel Train(IReadOnlyList<IImageSource> good, PatchAnomalyOptions options, CancellationToken token = default) => throw new NotSupportedException();
        public PatchAnomalyResult Detect(IImageSource image, PatchAnomalyModel model, PatchAnomalyOptions options, CancellationToken token = default) => throw new NotSupportedException();
        public void Dispose() => onDispose();
    }
}
