using System.Security.Cryptography;
using DP.LabelInspection.Contracts;
using DP.LabelInspection.Runtime;
using DP.Vision;
using DP.Vision.Algorithms;
using DP.WorkFlow.LabelInspection;

namespace DP.WorkFlow.Tests;

public sealed class AnomalyProviderCacheTests
{
    [Fact]
    public async Task NativeRuntimeIsSharedAcrossRecipesAndIdleCleanupReleasesTheLease()
    {
        var provider = new Provider("test.native.a", false);
        await using var rig = new LabelInspectionPipelineTests.Rig(false, implementations: [provider]);
        var key = Publish(rig, provider.ImplementationId);
        var recipe = Recipe(key);
        rig.Node.RecipeJson = rig.Json(recipe);
        var second = (InspectLabelNodeModel)WorkflowNodeConfigurationSnapshotter.Capture(rig.Node); second.Id = "inspect2";
        rig.Document.CanvasProjection.Nodes.Add(new() { Node = second });
        var connection = rig.Document.CanvasProjection.Connections.Single(c => c.FromNodeId == "inspect");
        rig.Document.CanvasProjection.Connections.Remove(connection);
        rig.Document.CanvasProjection.Connections.Add(new() { FromNodeId = "inspect", FromPort = WorkflowPorts.Success, ToNodeId = "inspect2", ToPort = WorkflowPorts.Input });
        rig.Document.CanvasProjection.Connections.Add(new() { FromNodeId = "inspect2", FromPort = WorkflowPorts.Success, ToNodeId = "consumer", ToPort = WorkflowPorts.Input });
        Assert.Equal(0, provider.Loads);
        Assert.True((await rig.RunAsync()).Success);
        Assert.Equal(1, provider.Loads); Assert.Equal(1, rig.Runtime.CacheStatistics.ModelLoads);
        Assert.Equal(2, rig.Runtime.ResourceLoadCount); Assert.Equal(0, provider.Disposals);
        rig.Runtime.CleanupIdleResources(true);
        Assert.Equal(0, rig.Runtime.CacheStatistics.CachedModels); Assert.Equal(1, provider.Disposals);
    }

    [Fact]
    public async Task FailedNativeLoadNeverUsesPreviouslySuccessfulImplementation()
    {
        var a = new Provider("test.native.a", false); var b = new Provider("test.native.b", true);
        await using var rig = new LabelInspectionPipelineTests.Rig(false, implementations: [a, b]);
        rig.Node.RecipeJson = rig.Json(Recipe(Publish(rig, a.ImplementationId)));
        Assert.True((await rig.RunAsync()).Success); Assert.Equal(1, a.Inspects);
        rig.Node.RecipeJson = rig.Json(Recipe(Publish(rig, b.ImplementationId)));
        var result = await rig.RunAsync(); Assert.False(result.Success);
        Assert.Equal(1, a.Inspects); Assert.Equal(1, b.Loads); Assert.Equal(0, b.Inspects);
        rig.Node.RecipeJson = rig.Json(Recipe(Publish(rig, a.ImplementationId)));
        Assert.True((await rig.RunAsync()).Success); Assert.Equal(2, a.Inspects);
        Assert.Equal(1, a.Loads);
    }

    private static InspectionRecipe Recipe(string id) => new("native", 64, 48, EInspectionMode.Free, EAlignmentMode.AssumeAligned,
        [new InspectionRegion("native", ERegionKind.Text, new PixelBounds(4, 4, 32, 32), true)
            .WithAnomaly(new AnomalySettings(id, 2)).WithTasks(new RoiInspectionTasks(false, false, true))], LabelInspectionPipelineTests.Rig.Options);
    private static string Publish(LabelInspectionPipelineTests.Rig rig, string implementation)
    {
        var store = new DP.LabelInspection.Storage.InspectionStore(Path.Combine(rig.Root, rig.Node.DataDirectory), new OpenCvImageCodec());
        string id = store.AnomalyLibraries.CreateAnomalyLibrary("native");
        var asset = new AnomalyModelAsset(implementation, "test-native.v1", 32, 32, .3, 3, "source-held-out",
            new Dictionary<string, byte[]> { ["model.bin"] = [1, 2, 3] });
        var bytes = asset.ToBytes(); string sha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        store.AnomalyLibraries.PutAnomalyModels(id, 1, [new AnomalyModelEntry("native", bytes, sha, implementation, 32, 32, 0, 3, .3, 0, 4, 1)]);
        return id;
    }
    private sealed class Provider(string id, bool fail) : IAnomalyImplementation
    {
        internal int Loads, Disposals, Inspects;
        public string ImplementationId => id; public string DisplayName => id;
        public ILoadedAnomalyModel Load(AnomalyModelAsset asset, CancellationToken token = default)
        { Interlocked.Increment(ref Loads); if (fail) throw new InvalidDataException("native load rejected"); return new Loaded(this, asset); }
        private sealed class Loaded(Provider owner, AnomalyModelAsset asset) : ILoadedAnomalyModel
        {
            public AnomalyModelAsset Asset => asset;
            public PatchAnomalyResult Inspect(IImageSource image, AnomalyDetectionOptions options, CancellationToken token = default)
            { Interlocked.Increment(ref owner.Inspects); return AnomalyScoreMap.Measure(new float[1024], 32, 32, .3, 1, asset.ImplementationId); }
            public void Dispose() => Interlocked.Increment(ref owner.Disposals);
        }
    }
}
