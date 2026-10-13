namespace DP.WorkFlow.Tests;

public sealed class LoopBlockNodeTests
{
    [Fact]
    public async Task RunAsync_NodesBeforeLoopRunOnce_AndEachCycleIsAnIndependentChildRun()
    {
        var initCount = 0;
        var childRuns = new List<Guid>();
        var actions = new WorkflowActionRegistry()
            .Register("Init", (_, _) =>
            {
                initCount++;
                return ValueTask.FromResult<object?>(null);
            })
            .Register("Work", (context, _) =>
            {
                childRuns.Add(context.ExecutionIdentity.RunId);
                return ValueTask.FromResult<object?>(null);
            });
        var context = new WorkflowContext(new WorkflowServiceProvider().Add<IWorkflowActionRegistry>(actions));
        var (engine, loop) = CreateEngine(ChildDocument(withEnd: true), context, configured => configured.Iterations = 5);

        var result = await engine.RunAsync();

        Assert.True(result.Success, result.Message);
        Assert.Equal(1, initCount);
        Assert.Equal(5, childRuns.Count);
        Assert.Equal(5, childRuns.Distinct().Count());
        Assert.DoesNotContain(engine.RunState.RunId, childRuns);
        Assert.True(context.TryGetNodeOutput(loop.Id, out var output));
        var loopOutput = Assert.IsType<LoopBlockNodeOutput>(output!.Value);
        Assert.Equal(5, loopOutput.CompletedCycles);
        Assert.Equal("达到循环次数", loopOutput.ExitReason);
        Assert.Equal(childRuns[^1], loopOutput.LastChildRunId);
    }

    [Fact]
    public async Task RunAsync_ExitsWhenExitVariableIsTrue_AndVariablesDoNotCarryOverBetweenCycles()
    {
        var cycles = 0;
        var actions = new WorkflowActionRegistry()
            .Register("Init", (_, _) => ValueTask.FromResult<object?>(null))
            .Register("Work", (context, _) =>
            {
                // 每圈收到相同的输入，看不到上一圈写入的变量。
                var limit = Assert.IsType<int>(context.SubflowInputs["Limit"]);
                Assert.False(context.TryGetVariable<int>("Previous", out int _));
                cycles++;
                context.SetVariable("Previous", cycles);
                context.SetVariable("Stop", cycles >= limit);
                return ValueTask.FromResult<object?>(null);
            });
        var context = new WorkflowContext(new WorkflowServiceProvider().Add<IWorkflowActionRegistry>(actions));
        var (engine, loop) = CreateEngine(ChildDocument(withEnd: true), context, configured =>
        {
            configured.Iterations = 0;
            configured.ExitVariableName = "Stop";
            configured.Inputs.Add(new WorkflowSubflowInput
            {
                Name = "Limit",
                ValueType = E_WorkflowSubflowValueType.Integer,
                Value = 3
            });
        });

        var result = await engine.RunAsync();

        Assert.True(result.Success, result.Message);
        Assert.Equal(3, cycles);
        Assert.True(context.TryGetNodeOutput(loop.Id, out var output));
        var loopOutput = Assert.IsType<LoopBlockNodeOutput>(output!.Value);
        Assert.Equal(3, loopOutput.CompletedCycles);
        Assert.Equal("退出变量为真", loopOutput.ExitReason);
    }

    [Fact]
    public async Task Pause_BetweenCycles_IsKeptByTheNextCycle_AndStopCancelsImmediately()
    {
        var cycles = 0;
        WorkflowEngine? engine = null;
        var actions = new WorkflowActionRegistry()
            .Register("Init", (_, _) => ValueTask.FromResult<object?>(null))
            .Register("Work", (_, _) =>
            {
                // Work 是子画布最后一个节点：第一圈在这里请求暂停，暂停正好落在两圈之间。
                if (Interlocked.Increment(ref cycles) == 1)
                    engine!.Pause();
                return ValueTask.FromResult<object?>(null);
            });
        var context = new WorkflowContext(new WorkflowServiceProvider().Add<IWorkflowActionRegistry>(actions));
        var (created, _) = CreateEngine(ChildDocument(withEnd: false), context, configured => configured.Iterations = 0);
        engine = created;
        using var cancellation = new CancellationTokenSource();

        var running = created.RunAsync(cancellation.Token);
        await WaitUntilAsync(() => created.GetRuntimeSnapshot().EnumerateChildWorkflows()
            .Any(info => info.Snapshot.ExecutionState == E_WorkflowExecutionState.Paused));
        await Task.Delay(50);

        Assert.Equal(1, Volatile.Read(ref cycles));
        created.Resume();
        await WaitUntilAsync(() => Volatile.Read(ref cycles) >= 3);
        cancellation.Cancel();
        var result = await running.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(E_WorkflowExecutionState.Canceled, result.State);
        Assert.Empty(created.RunState.Faults);
    }

    [Fact]
    public async Task GetTraceBatch_IncludesEachCycleOfTheLoop_TaggedWithLoopNodeAndCycle()
    {
        var actions = new WorkflowActionRegistry()
            .Register("Init", (_, _) => ValueTask.FromResult<object?>(null))
            .Register("Work", (_, _) => ValueTask.FromResult<object?>(null));
        var context = new WorkflowContext(new WorkflowServiceProvider().Add<IWorkflowActionRegistry>(actions));
        var (engine, loop) = CreateEngine(ChildDocument(withEnd: true), context, configured => configured.Iterations = 3);

        var result = await engine.RunAsync();

        Assert.True(result.Success, result.Message);
        Assert.DoesNotContain(engine.GetTraceBatch().Entries, entry => entry.Parents is not null);
        var merged = engine.GetTraceBatch(includeChildRuns: true).Entries;
        var completedWork = merged
            .Where(entry => entry.NodeId == "Work" && entry.Step == "NodeCompleted")
            .Select(entry => Assert.Single(entry.Parents!))
            .ToArray();
        Assert.Equal(new[] { 1, 2, 3 }, completedWork.Select(parent => parent.Cycle));
        Assert.All(completedWork, parent => Assert.Equal(loop.Id, parent.NodeId));
        Assert.Equal(merged.OrderBy(entry => entry.Timestamp), merged);
        // 父层自身的记录仍在，且不带子运行标记。
        Assert.Contains(merged, entry => entry.NodeId == loop.Id && entry.Step == "NodeStarted" && entry.Parents is null);
    }

    [Fact]
    public async Task RunningCycle_IsVisibleLive_InSnapshotTraceAndOutputs()
    {
        var cycles = 0;
        WorkflowEngine? engine = null;
        var actions = new WorkflowActionRegistry()
            .Register("Init", (_, _) => ValueTask.FromResult<object?>(null))
            .Register("Work", (_, _) =>
            {
                var cycle = Interlocked.Increment(ref cycles);
                if (cycle == 2)
                    engine!.Pause();
                return ValueTask.FromResult<object?>(cycle);
            });
        var context = new WorkflowContext(new WorkflowServiceProvider().Add<IWorkflowActionRegistry>(actions));
        var (created, loop) = CreateEngine(ChildDocument(withEnd: true), context, configured => configured.Iterations = 0);
        engine = created;
        using var cancellation = new CancellationTokenSource();

        var running = created.RunAsync(cancellation.Token);
        await WaitUntilAsync(() => created.GetRuntimeSnapshot().EnumerateChildWorkflows()
            .Any(info => info.Snapshot.ExecutionState == E_WorkflowExecutionState.Paused));

        var child = Assert.Single(created.GetRuntimeSnapshot().ActiveChildWorkflows.Values);
        Assert.Equal(loop.Id, child.ParentNodeId);
        Assert.Equal(2, child.Cycle);
        // 第2圈尚未结束：它的节点记录实时出现在父轨迹中，第1圈来自已归档历史。
        var work = created.GetTraceBatch(includeChildRuns: true).Entries
            .Where(entry => entry.NodeId == "Work" && entry.Step == "NodeCompleted")
            .Select(entry => Assert.Single(entry.Parents!).Cycle)
            .ToArray();
        Assert.Equal(new[] { 1, 2 }, work);
        // 子画布按子快照的运行标识读取内部节点输出。
        var output = created.FindLatestNodeOutput(child.Snapshot.RunId, "Work");
        Assert.NotNull(output);
        Assert.Equal(2, output!.Value);
        Assert.Null(created.FindLatestNodeOutput(Guid.NewGuid(), "Work"));

        cancellation.Cancel();
        created.Resume();
        await running.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task RunAsync_ChildFailureFailsTheLoopBlock()
    {
        var actions = new WorkflowActionRegistry()
            .Register("Init", (_, _) => ValueTask.FromResult<object?>(null))
            .Register("Work", (_, _) => throw new InvalidOperationException("扫描失败"));
        var context = new WorkflowContext(new WorkflowServiceProvider().Add<IWorkflowActionRegistry>(actions));
        var (engine, loop) = CreateEngine(ChildDocument(withEnd: true), context, configured => configured.Iterations = 0);

        var result = await engine.RunAsync();

        Assert.False(result.Success);
        Assert.Equal(loop.Id, result.FailedNodeId);
        Assert.Contains("第 1 圈", result.Message);
    }

    [Fact]
    public async Task RunNodeAsync_RerunsOnlyTheLoopInTheFinishedRun_WithEditedConfiguration()
    {
        var initCount = 0;
        var workCount = 0;
        var actions = new WorkflowActionRegistry()
            .Register("Init", (_, _) =>
            {
                initCount++;
                return ValueTask.FromResult<object?>(null);
            })
            .Register("Work", (_, _) =>
            {
                workCount++;
                return ValueTask.FromResult<object?>(null);
            });
        var context = new WorkflowContext(new WorkflowServiceProvider().Add<IWorkflowActionRegistry>(actions));
        var (document, loop) = ParentDocument(ChildDocument(withEnd: true), configured => configured.Iterations = 2);
        using var host = new WorkflowRuntimeHost(Catalog(), Handlers());
        await Assert.ThrowsAsync<InvalidOperationException>(() => host.RunNodeAsync(document, loop.Id));
        host.Configure(document, context);
        Assert.True((await host.RunAsync()).Success);
        var engine = host.Engine!;
        var runId = engine.RunState.RunId;
        var lastSequence = engine.GetTraceBatch().LastSequence;
        var parentEndCount = engine.GetRuntimeSnapshot().Nodes["ParentEnd"].ExecutionCount;

        // 节点窗口里改了循环次数但还没确认：按当前画布重新编译后单独运行循环块。
        loop.Iterations = 3;
        var result = await host.RunNodeAsync(document, loop.Id);

        Assert.True(result.Success, result.Message);
        Assert.Same(engine, host.Engine);
        Assert.Equal(runId, engine.RunState.RunId);
        Assert.Equal(1, initCount);
        Assert.Equal(5, workCount);
        var snapshot = engine.GetRuntimeSnapshot();
        Assert.Equal(E_WorkflowExecutionState.Completed, snapshot.ExecutionState);
        Assert.Equal(2, snapshot.Nodes[loop.Id].ExecutionCount);
        Assert.Equal(parentEndCount, snapshot.Nodes["ParentEnd"].ExecutionCount);
        Assert.True(context.TryGetNodeOutput(loop.Id, out var output));
        Assert.Equal(3, Assert.IsType<LoopBlockNodeOutput>(output!.Value).CompletedCycles);
        var trace = engine.GetTraceBatch(includeChildRuns: true).Entries;
        Assert.Contains(trace, entry => entry.Step == "RunContinued" && entry.Sequence > lastSequence);
        Assert.Equal(new[] { 1, 2, 3 }, trace
            .Where(entry => entry.NodeId == "Work" && entry.Step == "NodeCompleted")
            .Select(entry => Assert.Single(entry.Parents!).Cycle));
    }

    [Fact]
    public async Task RunNodeAsync_IsRejectedWhileTheWorkflowRuns()
    {
        var gate = new TaskCompletionSource();
        var actions = new WorkflowActionRegistry()
            .Register("Init", (_, _) => ValueTask.FromResult<object?>(null))
            .Register("Work", async (_, token) =>
            {
                await gate.Task.WaitAsync(token);
                return null;
            });
        var context = new WorkflowContext(new WorkflowServiceProvider().Add<IWorkflowActionRegistry>(actions));
        var (document, loop) = ParentDocument(ChildDocument(withEnd: true), configured => configured.Iterations = 1);
        using var host = new WorkflowRuntimeHost(Catalog(), Handlers());
        host.Configure(document, context);
        var running = host.RunAsync();
        await WaitUntilAsync(() => host.State == E_WorkflowExecutionState.Running);

        await Assert.ThrowsAsync<InvalidOperationException>(() => host.RunNodeAsync(document, loop.Id));

        gate.SetResult();
        Assert.True((await running).Success);
    }

    /// <summary>父流程：初始化 → 循环块 → 结束。</summary>
    private static (WorkflowEngine Engine, LoopBlockNodeModel Loop) CreateEngine(
        WorkflowDocument childDocument,
        WorkflowContext context,
        Action<LoopBlockNodeModel> configure)
    {
        var (parentDocument, loop) = ParentDocument(childDocument, configure);
        var definition = new WorkflowCompiler(Catalog()).Compile(parentDocument);
        return (new WorkflowEngine(definition, Handlers(), context), loop);
    }

    private static WorkflowNodeCatalog Catalog() => new WorkflowNodeCatalog().RegisterStandardNodes().RegisterCompositeNodes();

    private static WorkflowNodeHandlerCatalog Handlers() => new WorkflowNodeHandlerCatalog()
        .RegisterCompositeNodeHandlers()
        .Register(new StartNodeHandler())
        .Register(new ActionNodeHandler())
        .Register(new EndNodeHandler());

    private static (WorkflowDocument Document, LoopBlockNodeModel Loop) ParentDocument(
        WorkflowDocument childDocument,
        Action<LoopBlockNodeModel> configure)
    {
        var init = new ActionNodeModel { Id = "Init", Title = "初始化", FunctionKey = "Init" };
        var loop = new LoopBlockNodeModel { Id = "Loop", Title = "扫描循环", SubDocument = childDocument };
        configure(loop);
        var end = new EndNodeModel { Id = "ParentEnd", Title = "结束" };
        var parentDocument = new WorkflowDocument { Name = "Parent" };
        var parent = parentDocument.CanvasProjection;
        parent.Nodes.Add(new WorkflowCanvasNode { Node = init });
        parent.Nodes.Add(new WorkflowCanvasNode { Node = loop });
        parent.Nodes.Add(new WorkflowCanvasNode { Node = end });
        parent.Connections.Add(Connect(init.Id, loop.Id));
        parent.Connections.Add(Connect(loop.Id, end.Id));
        parentDocument.EntryNodeId = init.Id;
        return (parentDocument, loop);
    }

    /// <summary>子画布：开始 → Work（→ 结束）。</summary>
    private static WorkflowDocument ChildDocument(bool withEnd)
    {
        var start = new StartNodeModel { Id = "ChildStart", Title = "开始" };
        var work = new ActionNodeModel { Id = "Work", Title = "扫描", FunctionKey = "Work" };
        var document = new WorkflowDocument { Name = "Child", EntryNodeId = start.Id };
        var canvas = document.CanvasProjection;
        canvas.Nodes.Add(new WorkflowCanvasNode { Node = start });
        canvas.Nodes.Add(new WorkflowCanvasNode { Node = work });
        canvas.Connections.Add(Connect(start.Id, work.Id));
        if (withEnd)
        {
            var end = new EndNodeModel { Id = "ChildEnd", Title = "结束" };
            canvas.Nodes.Add(new WorkflowCanvasNode { Node = end });
            canvas.Connections.Add(Connect(work.Id, end.Id));
        }
        return document;
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        while (!condition())
            await Task.Delay(10, timeout.Token);
    }

    private static WorkflowConnectionModel Connect(string from, string to) => new()
    {
        FromNodeId = from,
        FromPort = WorkflowPorts.Success,
        ToNodeId = to,
        ToPort = WorkflowPorts.Input
    };
}
