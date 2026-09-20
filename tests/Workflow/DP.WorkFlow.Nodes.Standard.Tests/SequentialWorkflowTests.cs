namespace DP.WorkFlow.Tests;

public sealed class SequentialWorkflowTests
{
    [Fact]
    public async Task RunAsync_StartActionEnd_CompletesAndStoresActionOutput()
    {
        var start = new StartNodeModel { Id = "Start1", Title = "启动" };
        var action = new ActionNodeModel { Id = "Action1", Title = "动作", FunctionKey = "Increment" };
        var end = new EndNodeModel { Id = "End1", Title = "结束" };
        var canvasDocument = new WorkflowDocument { Name = "最小闭环" };
        var canvas = canvasDocument.CanvasProjection;
        canvas.Nodes.Add(new WorkflowCanvasNode { Node = start, X = 0, Y = 0 });
        canvas.Nodes.Add(new WorkflowCanvasNode { Node = action, X = 240, Y = 0 });
        canvas.Nodes.Add(new WorkflowCanvasNode { Node = end, X = 480, Y = 0 });
        canvas.Connections.Add(Connect("Start1", "Action1"));
        canvas.Connections.Add(Connect("Action1", "End1"));

        var actionRegistry = new WorkflowActionRegistry().Register(
            "Increment",
            (context, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                context.SetVariable("Counter", 1);
                return ValueTask.FromResult<object?>(new ActionOutput(1));
            });
        var services = new WorkflowServiceProvider().Add<IWorkflowActionRegistry>(actionRegistry);
        var context = new WorkflowContext(services);
        var handlers = new WorkflowNodeHandlerCatalog()
            .Register(new StartNodeHandler())
            .Register(new ActionNodeHandler())
            .Register(new EndNodeHandler());
        canvasDocument.EntryNodeId = start.Id;
        var definition = new WorkflowCompiler().Compile(canvasDocument);
        var engine = new WorkflowEngine(definition, handlers, context);
        var startedNodes = new List<string>();
        engine.NodeStarted += node => startedNodes.Add(node.Id);

        var result = await engine.RunAsync();

        Assert.True(result.Success, result.Message);
        Assert.Equal(E_WorkflowExecutionState.Completed, result.State);
        Assert.Equal(new[] { "Start1", "Action1", "End1" }, startedNodes);
        Assert.True(context.TryGetVariable<int>("Counter", out var counter));
        Assert.Equal(1, counter);
        Assert.True(context.TryGetNodeOutput("Action1", out var output));
        Assert.Equal(new ActionOutput(1), output!.Value);
    }

    private static WorkflowConnectionModel Connect(string from, string to) => new()
    {
        FromNodeId = from,
        FromPort = WorkflowPorts.Success,
        ToNodeId = to,
        ToPort = WorkflowPorts.Input
    };

    private sealed record ActionOutput(int Value);
}
