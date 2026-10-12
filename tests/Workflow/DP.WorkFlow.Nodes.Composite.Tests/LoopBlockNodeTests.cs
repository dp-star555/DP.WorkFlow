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
                // 每圈从输入映射的值开始，看不到上一圈写入的变量。
                Assert.True(context.TryGetVariable<int>("Limit", out var limit));
                Assert.False(context.TryGetVariable<int>("Previous", out _));
                cycles++;
                context.SetVariable("Previous", cycles);
                context.SetVariable("Stop", cycles >= limit);
                return ValueTask.FromResult<object?>(null);
            });
        var context = new WorkflowContext(new WorkflowServiceProvider().Add<IWorkflowActionRegistry>(actions));
        context.SetVariable("Limit", 3);
        var (engine, loop) = CreateEngine(ChildDocument(withEnd: true), context, configured =>
        {
            configured.Iterations = 0;
            configured.ExitVariableName = "Stop";
            configured.InputMappings.Add(new BlockInputMapping
            {
                TargetVariableName = "Limit",
                Source = E_BlockInputSource.ParentVariable,
                ParentVariableName = "Limit"
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

    /// <summary>父流程：初始化 → 循环块 → 结束。</summary>
    private static (WorkflowEngine Engine, LoopBlockNodeModel Loop) CreateEngine(
        WorkflowDocument childDocument,
        WorkflowContext context,
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
        var catalog = new WorkflowNodeCatalog().RegisterStandardNodes().RegisterCompositeNodes();
        var definition = new WorkflowCompiler(catalog).Compile(parentDocument);
        var handlers = new WorkflowNodeHandlerCatalog()
            .RegisterCompositeNodeHandlers()
            .Register(new StartNodeHandler())
            .Register(new ActionNodeHandler())
            .Register(new EndNodeHandler());
        return (new WorkflowEngine(definition, handlers, context), loop);
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
