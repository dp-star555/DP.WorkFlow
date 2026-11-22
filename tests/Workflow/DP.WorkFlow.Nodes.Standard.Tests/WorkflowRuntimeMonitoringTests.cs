namespace DP.WorkFlow.Tests;

public sealed class WorkflowRuntimeMonitoringTests
{
    [Fact]
    public async Task Pause_AfterFirstNode_BlocksNextNodeUntilResume()
    {
        // 回调不再在执行线程上同步执行（R1-04），不能靠 NodeCompleted 回调"卡住"下一个节点；
        // 改为在第一个节点的动作里请求暂停，验证暂停在节点边界生效。
        WorkflowEngine? engine = null;
        var secondStarted = false;
        var (definition, handlers, context) = CreateTwoActionWorkflow(onFirst: () => engine!.Pause(), onSecond: () => secondStarted = true);
        engine = new WorkflowEngine(definition, handlers, context);

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
        await engine.FlushNotificationsAsync();

        Assert.True(result.Success, result.Message);
        Assert.True(anyNodeStarted);
    }

    [Fact]
    public async Task SnapshotsAndTrace_AreOrderedAndContainNodeTiming()
    {
        WorkflowEngine? engine = null;
        var snapshots = new List<WorkflowRuntimeSnapshot>();
        // 在节点执行中途拉取快照，模拟界面按帧拉取。
        var (definition, handlers, context) = CreateTwoActionWorkflow(
            onFirst: () => snapshots.Add(engine!.GetRuntimeSnapshot()),
            onSecond: () => snapshots.Add(engine!.GetRuntimeSnapshot()));
        engine = new WorkflowEngine(definition, handlers, context);

        var result = await engine.RunAsync();
        snapshots.Add(engine.GetRuntimeSnapshot());
        var finalSnapshot = snapshots[^1];
        var traceBatch = engine.GetTraceBatch();

        Assert.True(result.Success, result.Message);
        Assert.Equal(3, snapshots.Count);
        Assert.True(snapshots.Zip(snapshots.Skip(1), (left, right) => left.Sequence < right.Sequence).All(value => value));
        Assert.Equal(E_NodeState.Running, snapshots[0].Nodes["Action1"].State);
        Assert.Equal(E_NodeState.Completed, snapshots[1].Nodes["Action1"].State);
        Assert.Equal(E_WorkflowExecutionState.Completed, finalSnapshot.ExecutionState);
        Assert.Equal(E_NodeState.Completed, finalSnapshot.Nodes["Action1"].State);
        Assert.Equal(1, finalSnapshot.Nodes["Action1"].ExecutionSequence);
        Assert.Equal(2, finalSnapshot.Nodes["Action2"].ExecutionSequence);
        Assert.True(finalSnapshot.Nodes["Action1"].Elapsed >= TimeSpan.Zero);
        Assert.NotEmpty(traceBatch.Entries);
        Assert.True(traceBatch.Entries.Zip(traceBatch.Entries.Skip(1), (left, right) => left.Sequence < right.Sequence).All(value => value));
    }

    [Fact]
    public async Task SlowObserver_IsExcludedFromNodeExecutionTime()
    {
        // 慢的状态订阅方在通知队列里排队，不占用执行线程，也不计入节点耗时。
        // 订阅方一直阻塞到运行结束之后才放行：若回调在执行线程上同步调用，运行无法结束（最多等到 5 秒超时），
        // 用这个因果关系判断，而不是比较毫秒数，避免测试并行时线程池排队造成的计时抖动。
        var (definition, handlers, context) = CreateTwoActionWorkflow();
        var engine = new WorkflowEngine(definition, handlers, context);
        using var release = new ManualResetEventSlim(false);
        var blocked = 0;
        void Block()
        {
            Interlocked.Increment(ref blocked);
            release.Wait(TimeSpan.FromSeconds(5));
        }
        engine.StateChanged += _ => Block();
        engine.NodeStarted += _ => Block();
        engine.NodeCompleted += _ => Block();

        var result = await engine.RunAsync();
        var snapshot = engine.GetRuntimeSnapshot();
        var stillBlocked = !release.IsSet;
        release.Set();
        await engine.FlushNotificationsAsync();

        Assert.True(result.Success, result.Message);
        Assert.True(stillBlocked);
        Assert.True(result.Elapsed < TimeSpan.FromSeconds(4), $"运行等到了订阅方，耗时 {result.Elapsed.TotalMilliseconds:F0} ms。");
        Assert.True(snapshot.Nodes["Action1"].Elapsed < TimeSpan.FromSeconds(4));
        Assert.True(Volatile.Read(ref blocked) > 1);
    }

    private static (WorkflowExecutionPlan Definition, WorkflowNodeHandlerCatalog Handlers, WorkflowContext Context)
        CreateTwoActionWorkflow(Action? onFirst = null, Action? onSecond = null)
    {
        var first = new ActionNodeModel { Id = "Action1", Title = "动作1", FunctionKey = "First" };
        var second = new ActionNodeModel { Id = "Action2", Title = "动作2", FunctionKey = "Second" };
        var canvasDocument = new WorkflowDocument { Name = "运行监视" };
        var canvas = canvasDocument;
        canvas.Nodes.Add(new WorkflowCanvasNode { Node = first });
        canvas.Nodes.Add(new WorkflowCanvasNode { Node = second });
        canvas.Connections.Add(new WorkflowConnectionModel { FromNodeId = first.Id, ToNodeId = second.Id });
        var actions = new WorkflowActionRegistry()
            .Register("First", (_, _) =>
            {
                onFirst?.Invoke();
                return ValueTask.FromResult<object?>(1);
            })
            .Register("Second", (_, _) =>
            {
                onSecond?.Invoke();
                return ValueTask.FromResult<object?>(2);
            });
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
