namespace DP.WorkFlow.Tests;

/// <summary>R1-04 / R4-a：引擎回调不在执行线程上同步执行；订阅方慢或出错都不影响流程。</summary>
public sealed class WorkflowNotificationTests
{
    [Fact]
    public async Task SlowSubscriber_DoesNotDelayExecution_AndRunEndedArrivesLast()
    {
        var engine = CreateEngine();
        var received = new List<string>();
        using var release = new ManualResetEventSlim(false);
        engine.RunStarted += _ =>
        {
            // 订阅方卡住：流程照常跑完，不等它。
            release.Wait(TimeSpan.FromSeconds(5));
            lock (received) received.Add("RunStarted");
        };
        engine.NodeCompleted += node => { lock (received) received.Add("NodeCompleted:" + node.Id); };
        engine.RunEnded += result => { lock (received) received.Add("RunEnded:" + result.State); };

        var result = await engine.RunAsync().WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(result.Success, result.Message);
        lock (received) Assert.Empty(received);

        release.Set();
        await engine.FlushNotificationsAsync();

        Assert.Equal(new[] { "RunStarted", "NodeCompleted:A", "NodeCompleted:B", "RunEnded:Completed" }, received);
    }

    [Fact]
    public async Task ThrowingSubscriber_IsCountedAsDiagnostic_AndLaterNotificationsStillArrive()
    {
        var engine = CreateEngine();
        var completed = new List<string>();
        engine.NodeStarted += _ => throw new InvalidOperationException("界面崩了");
        engine.NodeCompleted += node => completed.Add(node.Id);

        var result = await engine.RunAsync();
        await engine.FlushNotificationsAsync();

        Assert.True(result.Success, result.Message);
        Assert.Equal(new[] { "A", "B" }, completed);
    }

    private static WorkflowEngine CreateEngine()
    {
        var document = new WorkflowDocument { Name = "回调", EntryNodeId = "A" };
        document.CanvasProjection.Nodes.Add(new WorkflowCanvasNode { Node = new TestNode { Id = "A", Title = "A" } });
        document.CanvasProjection.Nodes.Add(new WorkflowCanvasNode { Node = new TestNode { Id = "B", Title = "B" } });
        document.CanvasProjection.Connections.Add(new WorkflowConnectionModel
        {
            FromNodeId = "A", FromPort = WorkflowPorts.Success, ToNodeId = "B", ToPort = WorkflowPorts.Input
        });
        var catalog = new WorkflowNodeCatalog().Register<TestNode>(1,
            WorkflowPortDescriptor.Input(maxConnections: int.MaxValue), WorkflowPortDescriptor.Output());
        return new WorkflowEngine(
            new WorkflowCompiler(catalog).Compile(document),
            new WorkflowNodeHandlerCatalog().Register(new TestHandler()));
    }

    [WorkflowNode("NotificationTest")]
    private sealed class TestNode : WorkflowNodeModel
    {
        public override string NodeType => "NotificationTest";
    }

    private sealed class TestHandler : WorkflowNodeHandler<TestNode>
    {
        protected override ValueTask<NodeExecutionResult> ExecuteAsync(
            TestNode node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken) =>
            ValueTask.FromResult(NodeExecutionResult.Continue());
    }
}
