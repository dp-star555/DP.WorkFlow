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
    public async Task RunAsync_InfiniteJumpCycle_IsStoppedByExecutionSafetyLimit()
    {
        var jump = new JumpNodeModel { Id = "Jump", Title = "循环跳转" };
        var canvasDocument = new WorkflowDocument { Name = "无限循环保护" };
        var canvas = canvasDocument.CanvasProjection;
        canvas.Nodes.Add(new WorkflowCanvasNode { Node = jump });
        canvas.Connections.Add(Connect("Jump", WorkflowPorts.Success, "Jump"));
        canvasDocument.EntryNodeId = jump.Id;
        var definition = new WorkflowCompiler(new WorkflowNodeCatalog().RegisterStandardNodes())
            .Compile(canvasDocument);
        var handlers = new WorkflowNodeHandlerCatalog().Register(new JumpNodeHandler());
        var options = new WorkflowExecutionOptions
        {
            MaxNodeExecutions = 3,
            MaxTotalNodeExecutions = 10
        };

        var result = await new WorkflowEngine(definition, handlers, options: options).RunAsync();

        Assert.False(result.Success);
        Assert.Equal(E_WorkflowExecutionState.Faulted, result.State);
        Assert.Contains("无限循环", result.Message);
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
