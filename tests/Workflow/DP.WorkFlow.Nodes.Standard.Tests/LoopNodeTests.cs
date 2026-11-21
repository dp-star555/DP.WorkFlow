namespace DP.WorkFlow.Tests;

public sealed class LoopNodeTests
{
    [Fact]
    public async Task RunAsync_FixedLoop_ExecutesBodyConfiguredNumberOfTimes()
    {
        var loop = new LoopNodeModel { Id = "Loop", Title = "循环", Iterations = 3 };
        var body = new ActionNodeModel { Id = "Body", Title = "循环体", FunctionKey = "Body" };
        var end = new EndNodeModel { Id = "End", Title = "结束" };
        var canvasDocument = new WorkflowDocument { Name = "固定循环" };
        var canvas = canvasDocument.CanvasProjection;
        foreach (var node in new IWorkflowNodeModel[] { loop, body, end })
            canvas.Nodes.Add(new WorkflowCanvasNode { Node = node });
        canvas.Connections.Add(Connect("Loop", WorkflowPorts.Loop, "Body"));
        canvas.Connections.Add(Connect("Body", WorkflowPorts.Success, "Loop"));
        canvas.Connections.Add(Connect("Loop", WorkflowPorts.Completed, "End"));

        var bodyExecutions = 0;
        var actions = new WorkflowActionRegistry().Register(
            "Body",
            (_, _) =>
            {
                Interlocked.Increment(ref bodyExecutions);
                return ValueTask.FromResult<object?>(null);
            });
        var services = new WorkflowServiceProvider().Add<IWorkflowActionRegistry>(actions);
        var handlers = new WorkflowNodeHandlerCatalog()
            .Register(new LoopNodeHandler())
            .Register(new ActionNodeHandler())
            .Register(new EndNodeHandler());
        canvasDocument.EntryNodeId = loop.Id;
        var definition = new WorkflowCompiler(new WorkflowNodeCatalog().RegisterStandardNodes())
            .Compile(canvasDocument);
        var context = new WorkflowContext(services);

        var result = await new WorkflowEngine(definition, handlers, context).RunAsync();

        Assert.True(result.Success, result.Message);
        Assert.Equal(3, bodyExecutions);
        Assert.True(context.TryGetNodeOutput("Loop", out var output));
        var loopResult = Assert.IsType<LoopNodeResult>(output!.Value);
        Assert.True(loopResult.Completed);
        Assert.Equal(3, loopResult.TotalIterations);
    }

    [Fact]
    public async Task RunAsync_WhenCompletedLoopIsEnteredAgain_StartsANewLoopFrame()
    {
        var loop = new LoopNodeModel { Id = "Loop", Title = "循环", Iterations = 2 };
        var body = new ActionNodeModel { Id = "Body", Title = "循环体", FunctionKey = "Body" };
        var reentry = new ReentryNode { Id = "Reentry", Title = "再次进入" };
        var canvasDocument = new WorkflowDocument { Name = "循环帧重入" };
        var canvas = canvasDocument.CanvasProjection;
        foreach (var node in new IWorkflowNodeModel[] { loop, body, reentry })
            canvas.Nodes.Add(new WorkflowCanvasNode { Node = node });
        canvas.Connections.Add(Connect(loop.Id, WorkflowPorts.Loop, body.Id));
        canvas.Connections.Add(Connect(body.Id, WorkflowPorts.Success, loop.Id));
        canvas.Connections.Add(Connect(loop.Id, WorkflowPorts.Completed, reentry.Id));
        canvas.Connections.Add(Connect(reentry.Id, "Reenter", loop.Id));

        var bodyExecutions = 0;
        var actions = new WorkflowActionRegistry().Register(
            "Body",
            (_, _) =>
            {
                bodyExecutions++;
                return ValueTask.FromResult<object?>(null);
            });
        var reentryHandler = new ReentryNodeHandler();
        var handlers = new WorkflowNodeHandlerCatalog()
            .Register(new LoopNodeHandler())
            .Register(new ActionNodeHandler())
            .Register(reentryHandler);
        canvasDocument.EntryNodeId = loop.Id;
        var definition = new WorkflowCompiler(new WorkflowNodeCatalog().RegisterStandardNodes())
            .Compile(canvasDocument);

        var result = await new WorkflowEngine(
            definition,
            handlers,
            new WorkflowContext(new WorkflowServiceProvider().Add<IWorkflowActionRegistry>(actions))).RunAsync();

        Assert.True(result.Success, result.Message);
        Assert.Equal(4, bodyExecutions);
        Assert.Equal(2, reentryHandler.ExecutionCount);
    }

    [Fact]
    [DP.WorkFlow.Testing.SkipWorkflowEventStreamCheck("测试核对器会把每条运行事件全部留在内存里，长时间无限循环会撑爆测试进程；引擎自身只保留固定窗口。")]
    public async Task RunAsync_InfiniteJumpCycle_RunsUntilCanceledWithBoundedState()
    {
        var jump = new JumpNodeModel { Id = "Jump", Title = "循环跳转" };
        var canvasDocument = new WorkflowDocument { Name = "无限循环" };
        var canvas = canvasDocument.CanvasProjection;
        canvas.Nodes.Add(new WorkflowCanvasNode { Node = jump });
        canvas.Connections.Add(Connect("Jump", WorkflowPorts.Success, "Jump"));
        canvasDocument.EntryNodeId = jump.Id;
        var definition = new WorkflowCompiler(new WorkflowNodeCatalog().RegisterStandardNodes())
            .Compile(canvasDocument);
        var handlers = new WorkflowNodeHandlerCatalog().Register(new JumpNodeHandler());
        var engine = new WorkflowEngine(definition, handlers);
        using var cancellation = new CancellationTokenSource();
        // 节点全部同步完成时 RunAsync 不会让出线程，放到线程池上跑，测试线程才能观察并取消。
        var run = Task.Run(() => engine.RunAsync(cancellation.Token));
        var spin = System.Diagnostics.Stopwatch.StartNew();
        // 超过原先的单节点 1 万次上限仍继续运行。
        while (ExecutionCount(engine) <= 20_000 && spin.Elapsed < TimeSpan.FromSeconds(30) && !run.IsCompleted)
            await Task.Delay(10);
        cancellation.Cancel();
        var result = await run;

        Assert.Equal(E_WorkflowExecutionState.Canceled, result.State);
        Assert.True(ExecutionCount(engine) > 20_000, "无限循环应一直跑到被停止，不受执行次数限制。");
        Assert.True(engine.RunState.NodeOutputs.Count <= 1, "环形执行只保留最新一次输出。");

        static int ExecutionCount(WorkflowEngine engine) =>
            engine.GetRuntimeSnapshot().Nodes.TryGetValue("Jump", out var info) ? info.ExecutionCount : 0;
    }

    [Fact]
    public async Task RunAsync_DelayCanBeCanceled()
    {
        var delay = new DelayNodeModel { Id = "Delay", Title = "延时", DelayMs = 10_000 };
        var canvasDocument = new WorkflowDocument { Name = "延时取消" };
        var canvas = canvasDocument.CanvasProjection;
        canvas.Nodes.Add(new WorkflowCanvasNode { Node = delay });
        canvasDocument.EntryNodeId = delay.Id;
        var definition = new WorkflowCompiler().Compile(canvasDocument);
        var handlers = new WorkflowNodeHandlerCatalog().Register(new DelayNodeHandler());
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(30));

        var result = await new WorkflowEngine(definition, handlers).RunAsync(cancellation.Token);

        Assert.Equal(E_WorkflowExecutionState.Canceled, result.State);
    }

    [Fact]
    public async Task RunAsync_UnlimitedLoop_ExitsAtExitLoopNodeAfterCurrentCycle()
    {
        var loop = new LoopNodeModel { Id = "Loop", Title = "循环", Iterations = 0 };
        var body = new ActionNodeModel { Id = "Body", Title = "循环体", FunctionKey = "Body" };
        var gate = new DecisionNodeModel
        {
            Id = "Gate", Title = "够了吗", ConditionSource = E_DecisionConditionSource.Binding,
            Condition = WorkflowInput<bool>.FromBinding(new WorkflowBindingKey(body.Id, "$"))
        };
        var exit = new ExitLoopNodeModel { Id = "Exit", Title = "退出循环", LoopNodeId = loop.Id };
        var end = new EndNodeModel { Id = "End", Title = "结束" };
        var document = new WorkflowDocument { Name = "不限次数循环", EntryNodeId = loop.Id };
        foreach (var node in new IWorkflowNodeModel[] { loop, body, gate, exit, end })
            document.CanvasProjection.Nodes.Add(new WorkflowCanvasNode { Node = node });
        document.CanvasProjection.Connections.Add(Connect(loop.Id, WorkflowPorts.Loop, body.Id));
        document.CanvasProjection.Connections.Add(Connect(body.Id, WorkflowPorts.Success, gate.Id));
        document.CanvasProjection.Connections.Add(Connect(gate.Id, WorkflowPorts.False, loop.Id));
        document.CanvasProjection.Connections.Add(Connect(gate.Id, WorkflowPorts.True, exit.Id));
        document.CanvasProjection.Connections.Add(Connect(exit.Id, WorkflowPorts.Success, loop.Id));
        document.CanvasProjection.Connections.Add(Connect(loop.Id, WorkflowPorts.Completed, end.Id));
        var executions = 0;
        var actions = new WorkflowActionRegistry().Register("Body", (_, _) => ValueTask.FromResult<object?>(++executions >= 4));
        var context = new WorkflowContext(new WorkflowServiceProvider().Add<IWorkflowActionRegistry>(actions));

        var result = await new WorkflowEngine(
            new WorkflowCompiler(new WorkflowNodeCatalog().RegisterStandardNodes()).Compile(document),
            new WorkflowNodeHandlerCatalog().RegisterStandardNodeHandlers(), context).RunAsync();

        Assert.True(result.Success, result.Message);
        Assert.Equal(4, executions);
        Assert.True(context.TryGetNodeOutput(loop.Id, out var output));
        var loopResult = Assert.IsType<LoopNodeResult>(output!.Value);
        Assert.Equal(4, loopResult.CurrentIteration);
        Assert.Equal("退出循环节点", loopResult.ExitReason);
    }

    [Fact]
    public async Task RunAsync_ExitSignal_ExitsAtEndOfCycleAndResets()
    {
        var loop = new LoopNodeModel { Id = "Loop", Title = "循环", Iterations = 10, ExitSignalKey = "Stop" };
        var body = new ActionNodeModel { Id = "Body", Title = "循环体", FunctionKey = "Body" };
        var end = new EndNodeModel { Id = "End", Title = "结束" };
        var document = new WorkflowDocument { Name = "外部退出", EntryNodeId = loop.Id };
        foreach (var node in new IWorkflowNodeModel[] { loop, body, end })
            document.CanvasProjection.Nodes.Add(new WorkflowCanvasNode { Node = node });
        document.CanvasProjection.Connections.Add(Connect(loop.Id, WorkflowPorts.Loop, body.Id));
        document.CanvasProjection.Connections.Add(Connect(body.Id, WorkflowPorts.Success, loop.Id));
        document.CanvasProjection.Connections.Add(Connect(loop.Id, WorkflowPorts.Completed, end.Id));
        var executions = 0;
        WorkflowContext? context = null;
        var actions = new WorkflowActionRegistry().Register("Body", (_, _) =>
        {
            if (++executions == 2)
                context!.PublicDataCenter.Signals.Write("Stop", true);
            return ValueTask.FromResult<object?>(null);
        });
        context = new WorkflowContext(new WorkflowServiceProvider().Add<IWorkflowActionRegistry>(actions));

        var result = await new WorkflowEngine(
            new WorkflowCompiler(new WorkflowNodeCatalog().RegisterStandardNodes()).Compile(document),
            new WorkflowNodeHandlerCatalog().RegisterStandardNodeHandlers(), context).RunAsync();

        Assert.True(result.Success, result.Message);
        Assert.Equal(2, executions);
        Assert.True(context.TryGetNodeOutput(loop.Id, out var output));
        Assert.Equal("外部退出信号", Assert.IsType<LoopNodeResult>(output!.Value).ExitReason);
        Assert.False(context.PublicDataCenter.Signals.Read("Stop"));
    }

    private static WorkflowConnectionModel Connect(string from, string port, string to) => new()
    {
        FromNodeId = from,
        FromPort = port,
        ToNodeId = to
    };

    private sealed class ReentryNode : WorkflowNodeModel
    {
        public override string NodeType => "Test.Reentry";
    }

    private sealed class ReentryNodeHandler : WorkflowNodeHandler<ReentryNode>
    {
        public int ExecutionCount { get; private set; }

        protected override ValueTask<NodeExecutionResult> ExecuteAsync(
            ReentryNode node,
            IWorkflowNodeExecutionContext context,
            CancellationToken cancellationToken)
        {
            ExecutionCount++;
            return ValueTask.FromResult(ExecutionCount == 1
                ? NodeExecutionResult.Continue("Reenter")
                : NodeExecutionResult.Complete());
        }
    }
}
