namespace DP.WorkFlow.Tests;

public sealed class DecisionNodeTests
{
    [Fact]
    public async Task RunAsync_BoundTrueCondition_SelectsTruePortOnly()
    {
        var producer = new ActionNodeModel { Id = "Read", Title = "读取", FunctionKey = "Read" };
        var decision = new DecisionNodeModel
        {
            Id = "Decision",
            Title = "判断",
            ConditionSource = E_DecisionConditionSource.Binding,
            Condition = WorkflowInput<bool>.FromBinding(new WorkflowBindingKey("Read", "Value"))
        };
        var trueAction = new ActionNodeModel { Id = "TrueAction", Title = "真分支", FunctionKey = "TrueAction" };
        var falseAction = new ActionNodeModel { Id = "FalseAction", Title = "假分支", FunctionKey = "FalseAction" };
        var canvasDocument = new WorkflowDocument { Name = "判断测试" };
        var canvas = canvasDocument.CanvasProjection;
        foreach (var node in new IWorkflowNodeModel[] { producer, decision, trueAction, falseAction })
            canvas.Nodes.Add(new WorkflowCanvasNode { Node = node });
        canvas.Connections.Add(Connect("Read", WorkflowPorts.Success, "Decision"));
        canvas.Connections.Add(Connect("Decision", WorkflowPorts.True, "TrueAction"));
        canvas.Connections.Add(Connect("Decision", WorkflowPorts.False, "FalseAction"));

        var actions = new List<string>();
        var actionRegistry = new WorkflowActionRegistry()
            .Register("Read", (_, _) => ValueTask.FromResult<object?>(new BooleanOutput(true)))
            .Register("TrueAction", (_, _) => { actions.Add("True"); return ValueTask.FromResult<object?>(null); })
            .Register("FalseAction", (_, _) => { actions.Add("False"); return ValueTask.FromResult<object?>(null); });
        var services = new WorkflowServiceProvider().Add<IWorkflowActionRegistry>(actionRegistry);
        var handlers = new WorkflowNodeHandlerCatalog()
            .Register(new ActionNodeHandler())
            .Register(new DecisionNodeHandler());
        canvasDocument.EntryNodeId = producer.Id;
        var definition = new WorkflowCompiler().Compile(canvasDocument);

        var result = await new WorkflowEngine(definition, handlers, new WorkflowContext(services)).RunAsync();

        Assert.True(result.Success, result.Message);
        Assert.Equal(new[] { "True" }, actions);
    }

    private static WorkflowConnectionModel Connect(string from, string port, string to) => new()
    {
        FromNodeId = from,
        FromPort = port,
        ToNodeId = to
    };

    private sealed record BooleanOutput(bool Value);
}
