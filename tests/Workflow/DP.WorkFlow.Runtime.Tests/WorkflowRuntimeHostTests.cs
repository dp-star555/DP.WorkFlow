namespace DP.WorkFlow.Tests;

public sealed class WorkflowRuntimeHostTests
{
    [Fact]
    public async Task RunAsync_ReusesActiveTaskAndPublishesEngineAndSnapshots()
    {
        var node = new HostTestNode { Id = "Node", Title = "节点" };
        var canvasDocument = new WorkflowDocument { Name = "Host" };
        var canvas = canvasDocument.CanvasProjection;
        canvas.Nodes.Add(new WorkflowCanvasNode { Node = node });
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new HostTestHandler(release.Task);
        var nodeCatalog = new WorkflowNodeCatalog().Register<HostTestNode>();
        var handlerCatalog = new WorkflowNodeHandlerCatalog().Register(handler);
        using var host = new WorkflowRuntimeHost(nodeCatalog, handlerCatalog);
        var engineChanges = 0;
        var snapshots = 0;
        host.EngineChanged += _ => engineChanges++;
        host.SnapshotChanged += _ => snapshots++;
        canvasDocument.EntryNodeId = node.Id;
        host.Configure(canvasDocument);

        var firstTask = host.RunAsync();
        var secondTask = host.RunAsync();
        release.TrySetResult(true);
        var result = await firstTask;

        Assert.Same(firstTask, secondTask);
        Assert.True(result.Success, result.Message);
        Assert.Equal(1, engineChanges);
        Assert.True(snapshots > 0);
        Assert.Equal(result, host.LastRunResult);
    }

    [Fact]
    public async Task RunAsync_DoesNotExecuteSynchronousCpuHandlerOnCallingThread()
    {
        var node = new HostTestNode { Id = "CpuNode", Title = "CPU 节点" };
        var canvasDocument = new WorkflowDocument { Name = "BackgroundBoundary" };
        var canvas = canvasDocument.CanvasProjection;
        canvas.Nodes.Add(new WorkflowCanvasNode { Node = node });
        using var release = new ManualResetEventSlim();
        using var fallback = new System.Threading.Timer(_ => release.Set(), null, 800, Timeout.Infinite);
        using var host = new WorkflowRuntimeHost(
            new WorkflowNodeCatalog().Register<HostTestNode>(),
            new WorkflowNodeHandlerCatalog().Register(new BlockingCpuHostTestHandler(release)));
        canvasDocument.EntryNodeId = node.Id;
        host.Configure(canvasDocument);

        var watch = System.Diagnostics.Stopwatch.StartNew();
        var runTask = host.RunAsync();
        watch.Stop();
        release.Set();
        var result = await runTask;

        Assert.True(result.Success, result.Message);
        Assert.True(watch.ElapsedMilliseconds < 400,
            $"RunAsync 在调用线程同步阻塞了 {watch.ElapsedMilliseconds}ms。运行边界应立即返回 Task。");
    }

    [Fact]
    public async Task RunAsync_InvokesRootRunPreparationBeforeEveryRun()
    {
        var events = new List<string>();
        var preparation = new TestRunPreparation(_ => events.Add("prepare"));
        var services = new WorkflowServiceProvider().Add<IWorkflowRunPreparationService>(preparation);
        var node = new HostTestNode { Id = "Node" };
        var canvasDocument = new WorkflowDocument { Name = "Preparation" };
        var canvas = canvasDocument.CanvasProjection;
        canvas.Nodes.Add(new WorkflowCanvasNode { Node = node });
        using var host = new WorkflowRuntimeHost(
            new WorkflowNodeCatalog().Register<HostTestNode>(),
            new WorkflowNodeHandlerCatalog().Register(new CountingHostTestHandler(() => events.Add("execute"))));
        canvasDocument.EntryNodeId = node.Id;
        host.Configure(canvasDocument, new WorkflowContext(services));

        Assert.True((await host.RunAsync()).Success);
        Assert.True((await host.RunAsync()).Success);

        Assert.Equal(new[] { "prepare", "execute", "prepare", "execute" }, events);
    }

    [Fact]
    public async Task RunPreparation_ReceivesRootAndNestedPlanNodes()
    {
        IReadOnlyList<string>? preparedNodeIds = null;
        var childNode = new HostTestNode { Id = "Child" };
        var childDocument = new WorkflowDocument { EntryNodeId = childNode.Id };
        childDocument.CanvasProjection.Nodes.Add(new WorkflowCanvasNode { Node = childNode });
        var rootNode = new HostCompositeNode { Id = "Root", SubDocument = childDocument };
        var rootDocument = new WorkflowDocument { EntryNodeId = rootNode.Id };
        rootDocument.CanvasProjection.Nodes.Add(new WorkflowCanvasNode { Node = rootNode });
        var preparation = new TestRunPreparation(context =>
            preparedNodeIds = context.Nodes.Select(node => node.Id).ToArray());
        var services = new WorkflowServiceProvider().Add<IWorkflowRunPreparationService>(preparation);
        using var host = new WorkflowRuntimeHost(
            new WorkflowNodeCatalog().Register<HostCompositeNode>().Register<HostTestNode>(),
            new WorkflowNodeHandlerCatalog()
                .Register(new HostCompositeHandler())
                .Register(new CountingHostTestHandler(() => { })));
        host.Configure(rootDocument, new WorkflowContext(services));

        var result = await host.RunAsync();

        Assert.True(result.Success, result.Message);
        Assert.Equal(new[] { "Root", "Child" }, preparedNodeIds);
    }

    [Fact]
    public async Task ExternalHoldAddedBeforeRun_BlocksFirstNodeUntilRemoved()
    {
        var node = new HostTestNode { Id = "Node", Title = "节点" };
        var canvasDocument = new WorkflowDocument { Name = "PreHold" };
        var canvas = canvasDocument.CanvasProjection;
        canvas.Nodes.Add(new WorkflowCanvasNode { Node = node });
        var executed = 0;
        var handler = new CountingHostTestHandler(() => Interlocked.Increment(ref executed));
        var nodeCatalog = new WorkflowNodeCatalog().Register<HostTestNode>();
        var handlerCatalog = new WorkflowNodeHandlerCatalog().Register(handler);
        using var host = new WorkflowRuntimeHost(nodeCatalog, handlerCatalog);
        canvasDocument.EntryNodeId = node.Id;
        host.Configure(canvasDocument);
        host.AddExternalHold("MachineNotReady");

        var runTask = host.RunAsync();
        await WaitUntilAsync(() => host.State == E_WorkflowExecutionState.Paused);

        Assert.Equal(0, Volatile.Read(ref executed));
        host.RemoveExternalHold("MachineNotReady");
        var result = await runTask;

        Assert.True(result.Success, result.Message);
        Assert.Equal(1, executed);
    }

    [Fact]
    public async Task ResetAsync_CancelsRunningEngineAndReturnsHostToIdle()
    {
        var node = new HostTestNode { Id = "Node", Title = "节点" };
        var canvasDocument = new WorkflowDocument { Name = "Reset" };
        var canvas = canvasDocument.CanvasProjection;
        canvas.Nodes.Add(new WorkflowCanvasNode { Node = node });
        var nodeCatalog = new WorkflowNodeCatalog().Register<HostTestNode>();
        var handlerCatalog = new WorkflowNodeHandlerCatalog()
            .Register(new HostTestHandler(Task.Delay(Timeout.InfiniteTimeSpan)));
        using var host = new WorkflowRuntimeHost(nodeCatalog, handlerCatalog);
        canvasDocument.EntryNodeId = node.Id;
        host.Configure(canvasDocument);
        var runTask = host.RunAsync();

        await host.ResetAsync();
        var result = await runTask;

        Assert.Equal(E_WorkflowExecutionState.Canceled, result.State);
        Assert.Equal(E_WorkflowExecutionState.Idle, host.State);
        Assert.Null(host.Engine);
        Assert.Null(host.LastRunResult);
    }

    [WorkflowNode("HostTest")]
    private sealed class HostTestNode : WorkflowNodeModel
    {
        public override string NodeType => "HostTest";
    }

    private sealed class HostCompositeNode : WorkflowNodeModel, IWorkflowSubDocumentNode
    {
        public override string NodeType => "HostComposite";
        public WorkflowDocument SubDocument { get; set; } = new();
    }

    private sealed class HostCompositeHandler : WorkflowNodeHandler<HostCompositeNode>
    {
        protected override ValueTask<NodeExecutionResult> ExecuteAsync(
            HostCompositeNode node,
            IWorkflowNodeExecutionContext context,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(NodeExecutionResult.Complete());
    }

    private sealed class TestRunPreparation(Action<WorkflowRunPreparationContext> action) : IWorkflowRunPreparationService
    {
        public ValueTask PrepareAsync(
            WorkflowRunPreparationContext context,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.NotEmpty(context.Nodes);
            action(context);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class BlockingCpuHostTestHandler(ManualResetEventSlim release) : WorkflowNodeHandler<HostTestNode>
    {
        protected override ValueTask<NodeExecutionResult> ExecuteAsync(
            HostTestNode node,
            IWorkflowNodeExecutionContext context,
            CancellationToken cancellationToken)
        {
            // 模拟 ONNX Runtime 等没有异步切换点的 CPU 密集型调用。
            release.Wait(cancellationToken);
            return ValueTask.FromResult(NodeExecutionResult.Complete());
        }
    }

    private sealed class CountingHostTestHandler : WorkflowNodeHandler<HostTestNode>
    {
        private readonly Action _action;

        public CountingHostTestHandler(Action action)
        {
            _action = action;
        }

        protected override ValueTask<NodeExecutionResult> ExecuteAsync(
            HostTestNode node,
            IWorkflowNodeExecutionContext context,
            CancellationToken cancellationToken)
        {
            _action();
            return ValueTask.FromResult(NodeExecutionResult.Complete());
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        while (!condition())
            await Task.Delay(10, timeout.Token);
    }

    private sealed class HostTestHandler : WorkflowNodeHandler<HostTestNode>
    {
        private readonly Task _waitTask;

        public HostTestHandler(Task waitTask)
        {
            _waitTask = waitTask;
        }

        protected override async ValueTask<NodeExecutionResult> ExecuteAsync(
            HostTestNode node,
            IWorkflowNodeExecutionContext context,
            CancellationToken cancellationToken)
        {
            await _waitTask.WaitAsync(cancellationToken);
            return NodeExecutionResult.Complete();
        }
    }
}
