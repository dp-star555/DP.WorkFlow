namespace DP.WorkFlow.Tests;

public sealed class ActionNodeHandlerTests
{
    [Fact]
    public async Task RunAsync_WhenActionIsNotRegistered_ReturnsFaultedResult()
    {
        var node = new ActionNodeModel
        {
            Id = "Action1",
            Title = "不存在的动作",
            FunctionKey = "Missing"
        };
        var canvasDocument = new WorkflowDocument { Name = "动作错误" };
        var canvas = canvasDocument.CanvasProjection;
        canvas.Nodes.Add(new WorkflowCanvasNode { Node = node });
        canvasDocument.EntryNodeId = node.Id;
        var definition = new WorkflowCompiler().Compile(canvasDocument);
        var services = new WorkflowServiceProvider()
            .Add<IWorkflowActionRegistry>(new WorkflowActionRegistry());
        var context = new WorkflowContext(services);
        var handlers = new WorkflowNodeHandlerCatalog().Register(new ActionNodeHandler());

        var result = await new WorkflowEngine(definition, handlers, context).RunAsync();

        Assert.False(result.Success);
        Assert.Equal(E_WorkflowExecutionState.Faulted, result.State);
        Assert.Equal("Action1", result.FailedNodeId);
        Assert.Contains("Missing", result.Message);
    }
}
