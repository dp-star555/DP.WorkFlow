namespace DP.WorkFlow.Tests;

/// <summary>R4-a 出口事件：节点等待的开始与结束（等什么、等到 / 超时 / 中断）。</summary>
public sealed class NodeWaitEventTests
{
    [Theory]
    [InlineData(true, "Satisfied", "Success")]
    [InlineData(false, "TimedOut", "Timeout")]
    public async Task SignalWait_RecordsWaitStartAndOutcome(bool raised, string outcome, string port)
    {
        var signals = new WorkflowSignalService();
        if (raised)
            signals.Write("Ready", true);
        var engine = CreateEngine(new SignalWaitNodeModel
        {
            Id = "Wait",
            Title = "等待信号",
            SignalKey = "Ready",
            ExpectedState = WorkflowInput<bool>.FromLiteral(true),
            Timeout = TimeSpan.FromMilliseconds(30)
        }, new WorkflowServiceProvider().Add<IWorkflowSignalService>(signals), new SignalWaitNodeHandler());

        var result = await engine.RunAsync();

        Assert.True(result.Success, result.Message);
        var entries = engine.GetTraceBatch().Entries.Where(entry => entry.NodeId == "Wait").ToArray();
        var started = Assert.Single(entries, entry => entry.Step == WorkflowNodeWait.StartedEvent);
        var ended = Assert.Single(entries, entry => entry.Step == WorkflowNodeWait.EndedEvent);
        Assert.Equal("信号 Ready = True", started.Data!["Target"]);
        Assert.Equal(30L, Convert.ToInt64(started.Data["TimeoutMs"]));
        Assert.Equal(outcome, ended.Data!["Outcome"]);
        Assert.True(started.Sequence < ended.Sequence);
        Assert.Equal(port, Assert.Single(entries, entry => entry.Step == "NodeCompleted").Data!["SelectedPort"]?.ToString());
    }

    [Fact]
    public async Task CanceledDelay_RecordsInterruptedWait()
    {
        var engine = CreateEngine(new DelayNodeModel { Id = "Wait", Title = "延时", DelayMs = 10_000 },
            new WorkflowServiceProvider(), new DelayNodeHandler());
        using var cancellation = new CancellationTokenSource();

        var run = Task.Run(() => engine.RunAsync(cancellation.Token));
        await WaitUntilAsync(() => engine.GetTraceBatch().Entries.Any(entry => entry.Step == WorkflowNodeWait.StartedEvent));
        cancellation.Cancel();
        var result = await run;

        Assert.False(result.Success);
        var ended = Assert.Single(engine.GetTraceBatch().Entries, entry => entry.Step == WorkflowNodeWait.EndedEvent);
        Assert.Equal("Interrupted", ended.Data!["Outcome"]);
    }

    private static WorkflowEngine CreateEngine(IWorkflowNodeModel node, WorkflowServiceProvider services, IWorkflowNodeHandler handler)
    {
        var document = new WorkflowDocument { Name = "等待", EntryNodeId = node.Id };
        document.Nodes.Add(new WorkflowCanvasNode { Node = node });
        var plan = new WorkflowCompiler(new WorkflowNodeCatalog().RegisterStandardNodes()).Compile(document);
        return new WorkflowEngine(plan, new WorkflowNodeHandlerCatalog().Register(handler), new WorkflowContext(services));
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition())
            await Task.Delay(5, timeout.Token);
    }
}
