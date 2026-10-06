using DP.Plugins;
using DP.Vision.Algorithms;
using DP.WorkFlow.Persistence.Json;

namespace DP.WorkFlow.Tests;

public sealed class WorkflowAlgorithmPluginTests
{
    [Theory]
    [InlineData("fixture.add", 3, 13)]
    [InlineData("fixture.multiply", 3, 30)]
    public async Task ExternalCapability_DirectoryDiscoveryAndPersistence_RunWithoutHostReference(string implementation, int amount, int expected)
    {
        using var rig = new Rig();
        Assert.DoesNotContain(typeof(WorkflowAlgorithmPluginTests).Assembly.GetReferencedAssemblies(), a => a.Name!.StartsWith("Intensity.", StringComparison.Ordinal));
        var node = rig.Node("same", implementation, amount);
        var store = new WorkflowDocumentJsonStore(rig.Nodes);
        var document = store.Deserialize(store.Serialize(Document(node))).Document;
        using var host = rig.Host(document);

        var result = await host.RunAsync();

        Assert.True(result.Success, result.Message);
        Assert.Equal(expected, Assert.Single(host.Engine!.RunState.NodeOutputs).Value);
        Assert.Same(rig.Slot(node).ContractType, rig.Algorithms.GetRequired(implementation).ContractType);
        Assert.NotEqual(typeof(WorkflowAlgorithmPluginTests).Assembly, node.GetType().Assembly);
    }

    [Fact]
    public async Task MissingEngine_NodeRemainsEditableAndFailsBeforeExecution()
    {
        using var rig = new Rig(includeEngine: false);
        var node = rig.Node("missing", "fixture.add", 3);
        using var host = rig.Host(Document(node));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => host.RunAsync());

        Assert.Contains("fixture.add", error.Message);
        Assert.Contains("missing", error.Message);
        Assert.Empty(host.Engine!.RunState.NodeOutputs);
    }

    [Fact]
    public void MissingNodePlugin_PreservesUnknownConfigurationForRoundTrip()
    {
        using var rig = new Rig();
        var store = new WorkflowDocumentJsonStore(rig.Nodes);
        var json = store.Serialize(Document(rig.Node("unknown", "fixture.multiply", 7)));
        var missingStore = new WorkflowDocumentJsonStore(new WorkflowNodeCatalog());

        var loaded = missingStore.Deserialize(json).Document;
        Assert.IsType<UnknownWorkflowNodeModel>(Assert.Single(loaded.CanvasProjection.Nodes).Node);
        var restored = store.Deserialize(missingStore.Serialize(loaded)).Document;

        var selection = rig.Slot(Assert.Single(restored.CanvasProjection.Nodes).Node).Selection;
        Assert.Equal("fixture.multiply", selection.ImplementationId);
        Assert.Equal("7", selection.Settings["amount"]);
    }

    [Fact]
    public async Task TwoConcurrentHosts_SameNodeIdUseTheirOwnFrozenConfiguration()
    {
        using var rig = new Rig();
        var first = rig.Node("same", "fixture.add", 3);
        var second = rig.Node("same", "fixture.multiply", 5);
        using var hostA = rig.Host(Document(first)); using var hostB = rig.Host(Document(second));
        // 修改画布不能影响已经编译的节点快照。
        rig.Slot(first).Selection.Settings["amount"] = "99";

        var results = await Task.WhenAll(hostA.RunAsync(), hostB.RunAsync());

        Assert.All(results, result => Assert.True(result.Success, result.Message));
        Assert.Equal(13, Assert.Single(hostA.Engine!.RunState.NodeOutputs).Value);
        Assert.Equal(50, Assert.Single(hostB.Engine!.RunState.NodeOutputs).Value);
    }

    [Fact]
    public async Task TwoChildPlans_SameNodeIdRemainBoundToTheirOwnImplementation()
    {
        using var rig = new Rig();
        var a = new BlockNodeModel { Id = "a", SubDocument = Document(rig.Node("same", "fixture.add", 3)) };
        var b = new BlockNodeModel { Id = "b", SubDocument = Document(rig.Node("same", "fixture.multiply", 5)) };
        a.OutputMappings.Add(new BlockOutputMapping { TargetVariableName = "A", Source = E_BlockOutputSource.ChildNodeBinding, ChildBinding = new WorkflowBindingKey("same", "$") });
        b.OutputMappings.Add(new BlockOutputMapping { TargetVariableName = "B", Source = E_BlockOutputSource.ChildNodeBinding, ChildBinding = new WorkflowBindingKey("same", "$") });
        var document = Document(a); document.CanvasProjection.Nodes.Add(new WorkflowCanvasNode { Node = b });
        document.CanvasProjection.Connections.Add(new WorkflowConnectionModel { FromNodeId = "a", FromPort = WorkflowPorts.Success, ToNodeId = "b", ToPort = WorkflowPorts.Input });
        using var host = rig.Host(document, out var context);

        Assert.True((await host.RunAsync()).Success);
        Assert.True(context.TryGetVariable<int>("A", out var first)); Assert.Equal(13, first);
        Assert.True(context.TryGetVariable<int>("B", out var second)); Assert.Equal(50, second);
    }

    [Fact]
    public async Task UnsupportedSettingsVersion_IsDiagnosedBeforeFirstNode()
    {
        using var rig = new Rig();
        var node = rig.Node("version", "fixture.add", 1); rig.Slot(node).Selection.SettingsVersion = 99;
        using var host = rig.Host(Document(node));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => host.RunAsync());

        Assert.Contains("参数版本", error.Message);
        Assert.Empty(host.Engine!.RunState.NodeOutputs);
    }

    [Fact]
    public async Task LatePreparationFailure_RollsBackCandidateAndKeepsCommittedScope()
    {
        using var rig = new Rig();
        var root = rig.Node("root", "fixture.add", 1);
        var committed = await rig.Bindings.PrepareRunAsync(new WorkflowRunPreparationContext(new[] { root }, WorkflowRunScopeKind.Root, BindingScopeId: Guid.NewGuid()), default);
        committed.Commit();
        using var failing = new WorkflowVisionAlgorithmBindings(rig.Runtime, new RejectPreparation());
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await failing.PrepareRunAsync(
            new WorkflowRunPreparationContext(new[] { root }, WorkflowRunScopeKind.Nested, BindingScopeId: Guid.NewGuid()), default));
        await committed.DisposeAsync();
        using var host = rig.Host(Document(root));
        Assert.True((await host.RunAsync()).Success);
    }

    private sealed class RejectPreparation : IWorkflowRunPreparationService
    {
        public ValueTask PrepareAsync(WorkflowRunPreparationContext context, CancellationToken cancellationToken) => throw new InvalidOperationException("模拟采集准备失败");
    }

    private static WorkflowDocument Document(IWorkflowNodeModel node)
    {
        var document = new WorkflowDocument { EntryNodeId = node.Id };
        document.CanvasProjection.Nodes.Add(new WorkflowCanvasNode { Node = node }); return document;
    }

    private sealed class Rig : IDisposable
    {
        private readonly string _directory = Path.Combine(AppContext.BaseDirectory, "PluginTestRuns", Guid.NewGuid().ToString("N"));
        public WorkflowNodeCatalog Nodes { get; } = new();
        private WorkflowNodeHandlerCatalog Handlers { get; } = new();
        public VisionAlgorithmCatalog Algorithms { get; }
        public VisionAlgorithmRuntime Runtime { get; }
        public WorkflowVisionAlgorithmBindings Bindings { get; }
        private WorkflowServiceProvider Services { get; }
        public Rig(bool includeEngine = true)
        {
            var repo = new DirectoryInfo(AppContext.BaseDirectory);
            while (repo is not null && !File.Exists(Path.Combine(repo.FullName, "DP.WorkFlow.sln"))) repo = repo.Parent;
            Assert.NotNull(repo);
            var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
            Copy("Intensity.Contracts", "netstandard2.0", "contracts");
            Copy("Intensity.Nodes", "net8.0", "nodes");
            if (includeEngine) Copy("Intensity.Engine", "net8.0", "engine");
            var session = new PluginLoadSession();
            // 核心契约必须在加载未知能力契约之前共享。
            var algorithmLoader = new VisionAlgorithmModuleLoader(session);
            session.RegisterSharedAssembly(typeof(IWorkflowVisionAlgorithmNode).Assembly);
            session.RegisterSharedContracts(_directory);
            var composition = new WorkflowRuntimePluginCatalog(Nodes, Handlers);
            var loader = new WorkflowPluginLoader(session);
            Assert.Equal(1, composition.LoadPlugins(_directory, loader));
            composition.Register(new WorkflowCompositeRuntimePluginModule());
            Assert.Empty(loader.DiscoveryFailures); composition.Freeze();
            Algorithms = algorithmLoader.Load(_directory); Assert.Empty(Algorithms.Diagnostics);
            Runtime = new VisionAlgorithmRuntime(Algorithms); Bindings = new WorkflowVisionAlgorithmBindings(Runtime);
            Services = new WorkflowServiceProvider().Add<IWorkflowVisionAlgorithmBindings>(Bindings)
                .Add<IWorkflowNodeCapabilityProvider>(Bindings).Add<IWorkflowRunPreparationService>(Bindings);
            return;
            void Copy(string project, string framework, string package)
            {
                var destination = Path.Combine(_directory, package); Directory.CreateDirectory(destination);
                File.Copy(Path.Combine(repo.FullName, "tests", "PluginFixtures", project, "bin", configuration, framework, project + ".dll"), Path.Combine(destination, project + ".dll"));
            }
        }
        public IWorkflowNodeModel Node(string id, string implementation, int amount)
        {
            var node = Nodes.GetOrThrow("Fixture.Intensity").Factory(); node.Id = id;
            var selection = Slot(node).Selection; selection.ImplementationId = implementation;
            selection.Settings["amount"] = amount.ToString(System.Globalization.CultureInfo.InvariantCulture); return node;
        }
        public WorkflowVisionAlgorithmSlot Slot(IWorkflowNodeModel node) => Assert.Single(((IWorkflowVisionAlgorithmNode)node).GetAlgorithmSlots());
        public WorkflowRuntimeHost Host(WorkflowDocument document)
            => Host(document, out _);
        public WorkflowRuntimeHost Host(WorkflowDocument document, out WorkflowContext context)
        {
            context = new WorkflowContext(Services);
            var host = new WorkflowRuntimeHost(Nodes, Handlers); host.Configure(document, context); return host;
        }
        public void Dispose()
        {
            Bindings.Dispose(); Runtime.Dispose();
            // 非可卸载包会话持有 DLL 到进程退出；下次 Build 清理测试目录。
        }
    }
}
