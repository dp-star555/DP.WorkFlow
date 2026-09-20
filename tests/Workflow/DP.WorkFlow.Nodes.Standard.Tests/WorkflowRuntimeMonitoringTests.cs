namespace DP.WorkFlow.Tests;

public sealed class WorkflowRuntimeMonitoringTests
{
    [Fact]
    public async Task Pause_AfterFirstNode_BlocksNextNodeUntilResume()
    {
        var (definition, handlers, context) = CreateTwoActionWorkflow();
        var engine = new WorkflowEngine(definition, handlers, context);
        var secondStarted = false;
        engine.NodeStarted += node =>
        {
            if (node.Id == "Action2")
                secondStarted = true;
        };
        engine.NodeCompleted += node =>
        {
            if (node.Id == "Action1")
                engine.Pause();
        };

        var runTask = engine.RunAsync();
        await WaitUntilAsync(() => engine.State == E_WorkflowExecutionState.Paused);

        Assert.False(secondStarted);
        Assert.Equal(E_WorkflowExecutionState.Paused, engine.GetRuntimeSnapshot().ExecutionState);
        engine.Resume();
        var result = await runTask;

        Assert.True(result.Success, result.Message);
        Assert.True(secondStarted);
    }

    [Fact]
    public async Task ExternalHold_BeforeRun_BlocksStartUntilReasonIsRemoved()
    {
        var (definition, handlers, context) = CreateTwoActionWorkflow();
        var engine = new WorkflowEngine(definition, handlers, context);
        var anyNodeStarted = false;
        engine.NodeStarted += _ => anyNodeStarted = true;
        engine.AddExternalHold("DeviceNotReady");

        var runTask = engine.RunAsync();
        await WaitUntilAsync(() => engine.State == E_WorkflowExecutionState.Paused);

        Assert.False(anyNodeStarted);
        Assert.Contains("DeviceNotReady", engine.GetRuntimeSnapshot().ExternalHoldReasons);
        engine.RemoveExternalHold("DeviceNotReady");
        var result = await runTask;

        Assert.True(result.Success, result.Message);
        Assert.True(anyNodeStarted);
    }

    [Fact]
    public async Task SnapshotsAndTrace_AreOrderedAndContainNodeTiming()
    {
        var (definition, handlers, context) = CreateTwoActionWorkflow();
        var engine = new WorkflowEngine(definition, handlers, context);
        var snapshots = new List<WorkflowRuntimeSnapshot>();
        engine.SnapshotChanged += snapshot => snapshots.Add(snapshot);
        engine.SnapshotChanged += _ => throw new InvalidOperationException("观察者异常不应中断流程。");

        var result = await engine.RunAsync();
        var finalSnapshot = snapshots.Last(snapshot => snapshot.ExecutionState == E_WorkflowExecutionState.Completed);
        var traceBatch = engine.GetTraceBatch();

        Assert.True(result.Success, result.Message);
        Assert.True(snapshots.Zip(snapshots.Skip(1), (left, right) => left.Sequence < right.Sequence).All(value => value));
        Assert.Equal(E_NodeState.Completed, finalSnapshot.Nodes["Action1"].State);
        Assert.Equal(1, finalSnapshot.Nodes["Action1"].ExecutionSequence);
        Assert.Equal(2, finalSnapshot.Nodes["Action2"].ExecutionSequence);
        Assert.True(finalSnapshot.Nodes["Action1"].Elapsed >= TimeSpan.Zero);
        Assert.NotEmpty(traceBatch.Entries);
        Assert.True(traceBatch.Entries.Zip(traceBatch.Entries.Skip(1), (left, right) => left.Sequence < right.Sequence).All(value => value));
    }

    [Fact]
    public async Task SlowSnapshotObserver_IsExcludedFromSingleNodeExecutionTime()
    {
        var (definition, handlers, context) = CreateTwoActionWorkflow();
        var engine = new WorkflowEngine(definition, handlers, context);
        engine.SnapshotChanged += _ => Thread.Sleep(15);

        var result = await engine.RunAsync();
        var snapshot = engine.GetRuntimeSnapshot();

        Assert.True(result.Success, result.Message);
        Assert.True(result.Elapsed >= TimeSpan.FromMilliseconds(60));
        Assert.True(snapshot.Nodes["Action1"].Elapsed < TimeSpan.FromMilliseconds(10),
            $"节点执行时间不应包含监视器耗时，实际为 {snapshot.Nodes["Action1"].Elapsed.TotalMilliseconds:F3} ms。");
    }

    private static (WorkflowExecutionPlan Definition, WorkflowNodeHandlerCatalog Handlers, WorkflowContext Context)
        CreateTwoActionWorkflow()
    {
        var first = new ActionNodeModel { Id = "Action1", Title = "动作1", FunctionKey = "First" };
        var second = new ActionNodeModel { Id = "Action2", Title = "动作2", FunctionKey = "Second" };
        var canvasDocument = new WorkflowDocument { Name = "运行监视" };
        var canvas = canvasDocument.CanvasProjection;
        canvas.Nodes.Add(new WorkflowCanvasNode { Node = first });
        canvas.Nodes.Add(new WorkflowCanvasNode { Node = second });
        canvas.Connections.Add(new WorkflowConnectionModel { FromNodeId = first.Id, ToNodeId = second.Id });
        var actions = new WorkflowActionRegistry()
            .Register("First", (_, _) => ValueTask.FromResult<object?>(1))
            .Register("Second", (_, _) => ValueTask.FromResult<object?>(2));
        var services = new WorkflowServiceProvider().Add<IWorkflowActionRegistry>(actions);
        canvasDocument.EntryNodeId = first.Id;
        return (
            new WorkflowCompiler().Compile(canvasDocument),
            new WorkflowNodeHandlerCatalog().Register(new ActionNodeHandler()),
            new WorkflowContext(services));
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        while (!condition())
            await Task.Delay(5, timeout.Token);
    }
}
