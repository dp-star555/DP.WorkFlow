namespace DP.WorkFlow.Tests;

/// <summary>
/// R1-04：快照只在调用方来取时构造；运行中引擎只递增变化计数，界面按帧率比较计数后再取。
/// </summary>
/// <remarks>
/// 观测点是 <see cref="WorkflowRuntimeSnapshot.Sequence"/>：它只在一次快照构造里被自增一次。
/// 因此"整轮运行结束后，第一次显式取快照拿到的序号是 1"与"运行期间一次都没有构造过快照"等价。
/// </remarks>
public sealed class WorkflowSnapshotPublicationTests
{
    [Fact]
    public async Task 运行中不构造快照()
    {
        var (document, catalog, handlers) = CreateTwoNodeWorkflow();
        var engine = new WorkflowEngine(new WorkflowCompiler(catalog).Compile(document), handlers);

        var result = await engine.RunAsync();

        Assert.True(result.Success, result.Message);
        Assert.Equal(1, engine.GetRuntimeSnapshot().Sequence);
    }

    [Fact]
    public async Task 每个状态迁移递增一次变化计数()
    {
        var (document, catalog, handlers) = CreateTwoNodeWorkflow();
        var engine = new WorkflowEngine(new WorkflowCompiler(catalog).Compile(document), handlers);
        Assert.Equal(0, engine.ChangeVersion);

        var result = await engine.RunAsync();

        // 运行开始 1 次、两个节点各"开始/结束"一次共 4 次、终态与收尾各 1 次，合计 7 次。
        // 计数只用于界面判断"要不要重新取"，这里钉住"每次迁移都记一次、一次都不漏"。
        Assert.True(result.Success, result.Message);
        Assert.Equal(7, engine.ChangeVersion);
        var snapshot = engine.GetRuntimeSnapshot();
        Assert.Equal(E_WorkflowExecutionState.Completed, snapshot.ExecutionState);
        Assert.Equal(7, engine.ChangeVersion); // 取快照不改变计数。
    }

    [Fact]
    public async Task 宿主运行中不构造快照()
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
    public async Task 宿主变化计数在运行中变化且按需取得最新快照()
    {
        using var proceed = new ManualResetEventSlim();
        var handler = new GatedSnapshotHandler("First", proceed);
        var (document, catalog, handlers) = CreateTwoNodeWorkflow(handler);
        using var host = new WorkflowRuntimeHost(catalog, handlers);
        host.Configure(document);
        var idleVersion = host.ChangeVersion;

        var runTask = host.RunAsync();
        await WaitUntilAsync(() => handler.Entered.IsSet);
        var runningVersion = host.ChangeVersion;
        var running = host.GetSnapshot();
        proceed.Set();
        var result = await runTask;

        Assert.True(result.Success, result.Message);
        Assert.NotEqual(idleVersion, runningVersion);
        Assert.Equal(E_NodeState.Running, running.Nodes["First"].State);
        Assert.NotEqual(runningVersion, host.ChangeVersion);
        Assert.Equal(E_WorkflowExecutionState.Completed, host.GetSnapshot().ExecutionState);
    }

    [Fact]
    public async Task 宿主转发引擎状态变化()
    {
        var (document, catalog, handlers) = CreateTwoNodeWorkflow();
        using var host = new WorkflowRuntimeHost(catalog, handlers);
        var states = new List<E_WorkflowExecutionState>();
        host.StateChanged += state =>
        {
            lock (states)
                states.Add(state);
        };
        host.Configure(document);

        var result = await host.RunAsync();
        await host.Engine!.FlushNotificationsAsync();

        Assert.True(result.Success, result.Message);
        lock (states)
        {
            Assert.Contains(E_WorkflowExecutionState.Running, states);
            Assert.Equal(E_WorkflowExecutionState.Completed, states[^1]);
        }
    }

    private static (WorkflowDocument Document, WorkflowNodeCatalog Catalog, WorkflowNodeHandlerCatalog Handlers)
        CreateTwoNodeWorkflow(WorkflowNodeHandler<SnapshotTestNode>? handler = null)
    {
        var first = new SnapshotTestNode { Id = "First", Title = "一" };
        var second = new SnapshotTestNode { Id = "Second", Title = "二" };
        var document = new WorkflowDocument { Name = "快照发布", EntryNodeId = first.Id };
        var canvas = document;
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
