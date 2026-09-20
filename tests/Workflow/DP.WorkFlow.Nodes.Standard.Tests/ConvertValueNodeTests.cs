namespace DP.WorkFlow.Tests;

public sealed class ConvertValueNodeTests
{
    [Fact]
    public async Task ConvertValue_UsesInvariantCulture()
    {
        var node = new ConvertValueNodeModel
        {
            Id = "Convert",
            Title = "转换",
            LiteralValue = "12.5",
            TargetType = E_SignalValueType.Double
        };
        var canvasDocument = new WorkflowDocument { Name = "转换" };
        var canvas = canvasDocument.CanvasProjection;
        canvas.Nodes.Add(new WorkflowCanvasNode { Node = node });
        var catalog = new WorkflowNodeCatalog().RegisterStandardNodes();
        canvasDocument.EntryNodeId = node.Id;
        var definition = new WorkflowCompiler(catalog).Compile(canvasDocument);
        var context = new WorkflowContext();
        var result = await new WorkflowEngine(
            definition,
            new WorkflowNodeHandlerCatalog().Register(new ConvertValueNodeHandler()),
            context).RunAsync();

        Assert.True(result.Success);
        Assert.True(context.TryGetNodeOutput(node.Id, out var nodeOutput));
        var output = Assert.IsType<ConvertValueNodeResult>(nodeOutput!.Value);
        Assert.Equal(12.5, output.DoubleValue);
        Assert.Equal("12.5", output.OriginalText);
    }
}
