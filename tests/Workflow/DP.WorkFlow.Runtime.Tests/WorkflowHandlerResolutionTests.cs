namespace DP.WorkFlow.Tests;

public sealed class WorkflowHandlerResolutionTests
{
    [Fact]
    public void Constructor_WhenHandlerIsMissing_FailsBeforeRunStarts()
    {
        var definition = CompileSingleNode();

        var exception = Assert.Throws<InvalidOperationException>(
            () => new WorkflowEngine(definition, new WorkflowNodeHandlerCatalog()));

        Assert.Contains("没有已注册的处理器", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Constructor_WhenHandlersAreAmbiguous_FailsBeforeRunStarts()
    {
        var definition = CompileSingleNode();
        var handlers = new WorkflowNodeHandlerCatalog()
            .Register(new TestNodeHandler())
            .Register(new TestNodeHandler());

        var exception = Assert.Throws<InvalidOperationException>(
            () => new WorkflowEngine(definition, handlers));

        Assert.Contains("匹配到多个处理器", exception.Message, StringComparison.Ordinal);
    }

    private static WorkflowExecutionPlan CompileSingleNode()
    {
        var node = new TestNode { Id = "Node", Title = "节点" };
        var canvasDocument = new WorkflowDocument { Name = "Handler 预解析" };
        var canvas = canvasDocument.CanvasProjection;
        canvas.Nodes.Add(new WorkflowCanvasNode { Node = node });
        canvasDocument.EntryNodeId = node.Id;
        return new WorkflowCompiler().Compile(canvasDocument);
    }

    private sealed class TestNode : WorkflowNodeModel
    {
        public override string NodeType => "Test.HandlerResolution";
    }

    private sealed class TestNodeHandler : WorkflowNodeHandler<TestNode>
    {
        protected override ValueTask<NodeExecutionResult> ExecuteAsync(
            TestNode node,
            IWorkflowNodeExecutionContext context,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(NodeExecutionResult.Complete());
    }
}
