namespace DP.WorkFlow.Tests;

public sealed class WaitFunctionNodeTests
{
    [Fact]
    public async Task RunAsync_WhenConditionTimesOut_SelectsTimeoutPort()
    {
        var wait = new WaitFunctionNodeModel
        {
            Id = "Wait",
            Title = "等待",
            FunctionKey = "Never",
            TimeoutMs = 30,
            PollIntervalMs = 5,
            TimeoutAsFalseBranch = true
        };
        var timeoutAction = new ActionNodeModel { Id = "TimeoutAction", Title = "超时", FunctionKey = "OnTimeout" };
        var document = CreateDocument(wait, timeoutAction, WorkflowPorts.Timeout);
        var timeoutVisited = false;
        var conditions = new WorkflowConditionRegistry()
            .Register("Never", (_, _) => ValueTask.FromResult(false));
        var actions = new WorkflowActionRegistry()
            .Register("OnTimeout", (_, _) => { timeoutVisited = true; return ValueTask.FromResult<object?>(null); });
        var services = new WorkflowServiceProvider()
            .Add<IWorkflowConditionRegistry>(conditions)
            .Add<IWorkflowActionRegistry>(actions);
        var handlers = new WorkflowNodeHandlerCatalog()
            .Register(new WaitFunctionNodeHandler())
            .Register(new ActionNodeHandler());
        document.EntryNodeId = wait.Id;
        var definition = new WorkflowCompiler().Compile(document);
        var context = new WorkflowContext(services);

        var result = await new WorkflowEngine(definition, handlers, context).RunAsync();

        Assert.True(result.Success, result.Message);
        Assert.True(timeoutVisited);
        Assert.True(context.TryGetNodeOutput("Wait", out var output));
        Assert.True(Assert.IsType<WaitFunctionNodeResult>(output!.Value).TimedOut);
    }

    [Fact]
    public async Task RunAsync_WhenConditionEventuallyMatches_SelectsSuccessPort()
    {
        var wait = new WaitFunctionNodeModel
        {
            Id = "Wait",
            Title = "等待",
            FunctionKey = "Eventually",
            TimeoutMs = 500,
            PollIntervalMs = 5
        };
        var successAction = new ActionNodeModel { Id = "SuccessAction", Title = "成功", FunctionKey = "OnSuccess" };
        var document = CreateDocument(wait, successAction, WorkflowPorts.Success);
        var pollCount = 0;
        var successVisited = false;
        var conditions = new WorkflowConditionRegistry()
            .Register("Eventually", (_, _) => ValueTask.FromResult(Interlocked.Increment(ref pollCount) >= 2));
        var actions = new WorkflowActionRegistry()
            .Register("OnSuccess", (_, _) => { successVisited = true; return ValueTask.FromResult<object?>(null); });
        var services = new WorkflowServiceProvider()
            .Add<IWorkflowConditionRegistry>(conditions)
            .Add<IWorkflowActionRegistry>(actions);
        var handlers = new WorkflowNodeHandlerCatalog()
            .Register(new WaitFunctionNodeHandler())
            .Register(new ActionNodeHandler());
        document.EntryNodeId = wait.Id;
        var definition = new WorkflowCompiler().Compile(document);

        var result = await new WorkflowEngine(definition, handlers, new WorkflowContext(services)).RunAsync();

        Assert.True(result.Success, result.Message);
        Assert.True(successVisited);
        Assert.True(pollCount >= 2);
    }

    private static WorkflowDocument CreateDocument(
        WaitFunctionNodeModel wait,
        ActionNodeModel action,
        string fromPort)
    {
        var document = new WorkflowDocument { Name = "等待测试" };
        var canvas = document.CanvasProjection;
        canvas.Nodes.Add(new WorkflowCanvasNode { Node = wait });
        canvas.Nodes.Add(new WorkflowCanvasNode { Node = action });
        canvas.Connections.Add(new WorkflowConnectionModel
        {
            FromNodeId = wait.Id,
            FromPort = fromPort,
            ToNodeId = action.Id
        });
        return document;
    }
}
