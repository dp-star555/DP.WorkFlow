namespace DP.WorkFlow.Tests;

public sealed class CompareNodeTests
{
    [Theory]
    [InlineData(E_ValueCompareOperator.Equal, true)]
    [InlineData(E_ValueCompareOperator.NotEqual, false)]
    [InlineData(E_ValueCompareOperator.GreaterThan, false)]
    [InlineData(E_ValueCompareOperator.GreaterOrEqual, true)]
    [InlineData(E_ValueCompareOperator.LessThan, false)]
    [InlineData(E_ValueCompareOperator.LessOrEqual, true)]
    public async Task ValueCompare_DoubleToleranceHasConsistentOrdering(
        E_ValueCompareOperator operation,
        bool expected)
    {
        var node = new ValueCompareNodeModel
        {
            Id = "Compare",
            Title = "比较",
            DataType = E_ValueCompareDataType.Double,
            Left = WorkflowInput<string?>.FromLiteral("1.05"),
            Right = WorkflowInput<string?>.FromLiteral("1.0"),
            EqualityTolerance = 0.1,
            Operator = operation
        };

        var output = await ExecuteValueCompareAsync(node);

        Assert.Equal(expected, output.Value);
    }

    [Fact]
    public async Task ValueCompare_BindingConvertsNumericOutputWithInvariantCulture()
    {
        var producer = new ActionNodeModel { Id = "Producer", Title = "生产", FunctionKey = "Produce" };
        var compare = new ValueCompareNodeModel
        {
            Id = "Compare",
            Title = "比较",
            DataType = E_ValueCompareDataType.Int64,
            Left = WorkflowInput<string?>.FromBinding(new WorkflowBindingKey("Producer", "Value")),
            Right = WorkflowInput<string?>.FromLiteral("42"),
            Operator = E_ValueCompareOperator.Equal
        };
        var canvasDocument = new WorkflowDocument { Name = "绑定比较" };
        var canvas = canvasDocument.CanvasProjection;
        canvas.Nodes.Add(new WorkflowCanvasNode { Node = producer });
        canvas.Nodes.Add(new WorkflowCanvasNode { Node = compare });
        canvas.Connections.Add(new WorkflowConnectionModel
        {
            FromNodeId = producer.Id,
            FromPort = WorkflowPorts.Success,
            ToNodeId = compare.Id,
            ToPort = WorkflowPorts.Input
        });
        var actions = new WorkflowActionRegistry()
            .Register("Produce", (_, _) => ValueTask.FromResult<object?>(new NumericOutput(42)));
        var context = new WorkflowContext(
            new WorkflowServiceProvider().Add<IWorkflowActionRegistry>(actions));
        var handlers = new WorkflowNodeHandlerCatalog()
            .Register(new ActionNodeHandler())
            .Register(new ValueCompareNodeHandler());
        canvasDocument.EntryNodeId = producer.Id;
        var definition = new WorkflowCompiler().Compile(canvasDocument);

        var result = await new WorkflowEngine(definition, handlers, context).RunAsync();

        Assert.True(result.Success, result.Message);
        Assert.True(context.TryGetNodeOutput(compare.Id, out var raw));
        Assert.True(Assert.IsType<CompareNodeResult>(raw!.Value).Value);
    }

    [Theory]
    [InlineData(E_StringCompareOperator.Equals, " abc ", "ABC", true, true)]
    [InlineData(E_StringCompareOperator.Contains, "Workflow", "FLOW", true, true)]
    [InlineData(E_StringCompareOperator.StartsWith, "Workflow", "work", true, true)]
    [InlineData(E_StringCompareOperator.EndsWith, "Workflow", "flow", true, true)]
    [InlineData(E_StringCompareOperator.Like, "Axis-123", "Axis-???", false, true)]
    [InlineData(E_StringCompareOperator.NotContains, "Workflow", "axis", true, true)]
    public async Task StringCompare_SelectsExpectedResult(
        E_StringCompareOperator operation,
        string left,
        string right,
        bool trim,
        bool expected)
    {
        var node = new StringCompareNodeModel
        {
            Id = "Compare",
            Title = "字符串比较",
            Left = WorkflowInput<string?>.FromLiteral(left),
            Right = WorkflowInput<string?>.FromLiteral(right),
            Operator = operation,
            IgnoreCase = true,
            TrimBeforeCompare = trim
        };
        var canvasDocument = new WorkflowDocument { Name = "字符串比较" };
        var canvas = canvasDocument.CanvasProjection;
        canvas.Nodes.Add(new WorkflowCanvasNode { Node = node });
        canvasDocument.EntryNodeId = node.Id;
        var definition = new WorkflowCompiler().Compile(canvasDocument);
        var context = new WorkflowContext();
        var engine = new WorkflowEngine(
            definition,
            new WorkflowNodeHandlerCatalog().Register(new StringCompareNodeHandler()),
            context);

        var result = await engine.RunAsync();

        Assert.True(result.Success, result.Message);
        Assert.True(context.TryGetNodeOutput(node.Id, out var raw));
        Assert.Equal(expected, Assert.IsType<CompareNodeResult>(raw!.Value).Value);
    }

    [Fact]
    public async Task ValueCompare_BooleanRangeOperatorFailsClearly()
    {
        var node = new ValueCompareNodeModel
        {
            Id = "Compare",
            Title = "错误布尔比较",
            DataType = E_ValueCompareDataType.Boolean,
            Left = WorkflowInput<string?>.FromLiteral("true"),
            Right = WorkflowInput<string?>.FromLiteral("false"),
            Operator = E_ValueCompareOperator.GreaterThan
        };
        var canvasDocument = new WorkflowDocument { Name = "错误比较" };
        var canvas = canvasDocument.CanvasProjection;
        canvas.Nodes.Add(new WorkflowCanvasNode { Node = node });
        canvasDocument.EntryNodeId = node.Id;
        var definition = new WorkflowCompiler().Compile(canvasDocument);
        var engine = new WorkflowEngine(
            definition,
            new WorkflowNodeHandlerCatalog().Register(new ValueCompareNodeHandler()));

        var result = await engine.RunAsync();

        Assert.False(result.Success);
        Assert.Contains("布尔类型仅支持", result.Message);
    }

    private static async Task<CompareNodeResult> ExecuteValueCompareAsync(ValueCompareNodeModel node)
    {
        var canvasDocument = new WorkflowDocument { Name = "值比较" };
        var canvas = canvasDocument.CanvasProjection;
        canvas.Nodes.Add(new WorkflowCanvasNode { Node = node });
        canvasDocument.EntryNodeId = node.Id;
        var definition = new WorkflowCompiler().Compile(canvasDocument);
        var context = new WorkflowContext();
        var engine = new WorkflowEngine(
            definition,
            new WorkflowNodeHandlerCatalog().Register(new ValueCompareNodeHandler()),
            context);
        var result = await engine.RunAsync();
        Assert.True(result.Success, result.Message);
        Assert.True(context.TryGetNodeOutput(node.Id, out var output));
        return Assert.IsType<CompareNodeResult>(output!.Value);
    }

    private sealed record NumericOutput(long Value);
}
