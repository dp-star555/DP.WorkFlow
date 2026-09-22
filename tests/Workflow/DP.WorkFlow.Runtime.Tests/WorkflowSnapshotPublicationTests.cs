namespace DP.WorkFlow.Tests;

/// <summary>
/// AR-17 阶段 1：快照只在真的有人要的时候才构造。
/// </summary>
/// <remarks>
/// 观测点是 <see cref="WorkflowRuntimeSnapshot.Sequence"/>：它只在一次快照构造里被自增一次。
/// 因此"整轮运行结束后，第一次显式取快照拿到的序号是 1"与"运行期间一次都没有构造过快照"等价，
/// 而不需要去数分配或计时间——分配次数和锁等待时间在本仓都不可观测。
/// </remarks>
public sealed class WorkflowSnapshotPublicationTests
{
    [Fact]
    public async Task 无订阅者时不构造快照()
    {
        var (document, catalog, handlers) = CreateTwoNodeWorkflow();
        var engine = new WorkflowEngine(new WorkflowCompiler(catalog).Compile(document), handlers);

        var result = await engine.RunAsync();

        Assert.True(result.Success, result.Message);
        Assert.Equal(1, engine.GetRuntimeSnapshot().Sequence);
    }

    [Fact]
    public async Task 有订阅者时每个状态迁移各发布一次快照()
    {
        var (document, catalog, handlers) = CreateTwoNodeWorkflow();
        var engine = new WorkflowEngine(new WorkflowCompiler(catalog).Compile(document), handlers);
        var snapshots = new List<WorkflowRuntimeSnapshot>();
        engine.SnapshotChanged += snapshots.Add;

        var result = await engine.RunAsync();

        // 运行开始 1 次、两个节点各"开始/结束"一次共 4 次、终态与收尾各 1 次，合计 7 次。
        // 这条断言是"每次状态迁移一次发布"的契约：任何合并发布的优化都必须先改这里。
        Assert.True(result.Success, result.Message);
        Assert.Equal(7, snapshots.Count);
        Assert.True(snapshots.Zip(snapshots.Skip(1), (left, right) => left.Sequence < right.Sequence).All(value => value));
        Assert.Equal(E_WorkflowExecutionState.Completed, snapshots[^1].ExecutionState);
    }

    [Fact]
    public async Task 宿主无订阅者时不构造快照()
    {
        var (document, catalog, handlers) = CreateTwoNodeWorkflow();
        using var host = new WorkflowRuntimeHost(catalog, handlers);
        host.Configure(document);

        var result = await host.RunAsync();

        Assert.True(result.Success, result.Message);
        Assert.NotNull(host.Engine);
        Assert.Equal(1, host.Engine!.GetRuntimeSnapshot().Sequence);
    }

    [Fact]
    public async Task 宿主订阅后立即退订则不再构造快照()
    {
        var (document, catalog, handlers) = CreateTwoNodeWorkflow();
        using var host = new WorkflowRuntimeHost(catalog, handlers);
        Action<WorkflowRuntimeSnapshot> observer = _ => { };
        host.SnapshotChanged += observer;
        host.SnapshotChanged -= observer;
        host.Configure(document);

        var result = await host.RunAsync();

        Assert.True(result.Success, result.Message);
        Assert.Equal(1, host.Engine!.GetRuntimeSnapshot().Sequence);
    }

    [Fact]
    public async Task 宿主有订阅者时按序收到快照()
    {
        var (document, catalog, handlers) = CreateTwoNodeWorkflow();
        using var host = new WorkflowRuntimeHost(catalog, handlers);
        var snapshots = new List<WorkflowRuntimeSnapshot>();
        host.SnapshotChanged += snapshots.Add;
        host.Configure(document);

        var result = await host.RunAsync();

        Assert.True(result.Success, result.Message);
        Assert.NotEmpty(snapshots);
        Assert.True(snapshots.Zip(snapshots.Skip(1), (left, right) => left.Sequence < right.Sequence).All(value => value));
        Assert.Contains(snapshots, snapshot => snapshot.ExecutionState == E_WorkflowExecutionState.Completed);
    }

    [Fact]
    public async Task 运行中订阅后立即开始收到快照()
    {
        using var gate = new ManualResetEventSlim();
        var (document, catalog, handlers) = CreateTwoNodeWorkflow(
            new GatedSnapshotHandler("First", gate));
        using var host = new WorkflowRuntimeHost(catalog, handlers);
        host.Configure(document);

        var runTask = host.RunAsync();
        await WaitUntilAsync(() => host.Engine is not null);
        var snapshots = new List<WorkflowRuntimeSnapshot>();
        host.SnapshotChanged += snapshots.Add;
        gate.Set();

        var result = await runTask;

        Assert.True(result.Success, result.Message);
        Assert.NotEmpty(snapshots);
    }

    [Fact]
    public async Task 运行中退订后引擎不再构造快照()
    {
        using var proceed = new ManualResetEventSlim();
        var handler = new GatedSnapshotHandler("First", proceed);
        var (document, catalog, handlers) = CreateTwoNodeWorkflow(handler);
        using var host = new WorkflowRuntimeHost(catalog, handlers);
        var snapshots = new List<WorkflowRuntimeSnapshot>();
        Action<WorkflowRuntimeSnapshot> observer = snapshots.Add;
        host.SnapshotChanged += observer;
        host.Configure(document);

        var runTask = host.RunAsync();
        await WaitUntilAsync(() => handler.Entered.IsSet);
        // 首节点的"开始"发布已经发生，而"结束"发布被 handler 挡在 proceed 后面，
        // 所以此处读到的数量是稳定的，不会有在途发布改写它。
        var countBeforeUnsubscribe = snapshots.Count;
        Assert.True(countBeforeUnsubscribe > 0, "订阅者应当在退订之前已经收到过快照。");
        host.SnapshotChanged -= observer;
        proceed.Set();

        var result = await runTask;

        Assert.True(result.Success, result.Message);
        Assert.Equal(countBeforeUnsubscribe, snapshots.Count);
        // 退订之后还剩 5 次状态迁移（首节点结束、次节点开始/结束、终态、收尾），
        // 它们都不应该再构造快照；+1 是本断言自己这一次显式取快照。
        Assert.Equal(countBeforeUnsubscribe + 1, host.Engine!.GetRuntimeSnapshot().Sequence);
    }

    private static (WorkflowDocument Document, WorkflowNodeCatalog Catalog, WorkflowNodeHandlerCatalog Handlers)
        CreateTwoNodeWorkflow(WorkflowNodeHandler<SnapshotTestNode>? handler = null)
    {
        var first = new SnapshotTestNode { Id = "First", Title = "一" };
        var second = new SnapshotTestNode { Id = "Second", Title = "二" };
        var document = new WorkflowDocument { Name = "快照发布", EntryNodeId = first.Id };
        var canvas = document.CanvasProjection;
        canvas.Nodes.Add(new WorkflowCanvasNode { Node = first });
        canvas.Nodes.Add(new WorkflowCanvasNode { Node = second });
        canvas.Connections.Add(new WorkflowConnectionModel
        {
            FromNodeId = first.Id,
            FromPort = WorkflowPorts.Success,
            ToNodeId = second.Id,
            ToPort = WorkflowPorts.Input
        });
        return (
            document,
            new WorkflowNodeCatalog().Register<SnapshotTestNode>(
                1,
                WorkflowPortDescriptor.Input(),
                WorkflowPortDescriptor.Output()),
            new WorkflowNodeHandlerCatalog().Register(handler ?? new SnapshotTestHandler()));
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition())
            await Task.Delay(5, timeout.Token);
    }

    [WorkflowNode("SnapshotTest")]
    private sealed class SnapshotTestNode : WorkflowNodeModel
    {
        public override string NodeType => "SnapshotTest";
    }

    private sealed class SnapshotTestHandler : WorkflowNodeHandler<SnapshotTestNode>
    {
        protected override ValueTask<NodeExecutionResult> ExecuteAsync(
            SnapshotTestNode node,
            IWorkflowNodeExecutionContext context,
            CancellationToken cancellationToken) =>
            // 必须是 Continue：Complete 表示"本节点终结当前路径"，链路不会走到第二个节点。
            ValueTask.FromResult(NodeExecutionResult.Continue());
    }

    /// <summary>让指定节点在"已经进入执行、但尚未返回"处停住，用来把发布时序钉死。</summary>
    private sealed class GatedSnapshotHandler : WorkflowNodeHandler<SnapshotTestNode>
    {
        private readonly string _gateNodeId;
        private readonly ManualResetEventSlim _proceed;

        public GatedSnapshotHandler(string gateNodeId, ManualResetEventSlim proceed)
        {
            _gateNodeId = gateNodeId;
            _proceed = proceed;
        }

        /// <summary>门控节点已经进入执行。</summary>
        public ManualResetEventSlim Entered { get; } = new();

        protected override ValueTask<NodeExecutionResult> ExecuteAsync(
            SnapshotTestNode node,
            IWorkflowNodeExecutionContext context,
            CancellationToken cancellationToken)
        {
            if (string.Equals(node.Id, _gateNodeId, StringComparison.Ordinal))
            {
                Entered.Set();
                _proceed.Wait(cancellationToken);
            }
            return ValueTask.FromResult(NodeExecutionResult.Continue());
        }
    }
}
