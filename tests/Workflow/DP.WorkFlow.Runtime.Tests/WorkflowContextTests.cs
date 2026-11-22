namespace DP.WorkFlow.Tests;

public sealed class WorkflowContextTests
{
    [Fact]
    public void SetVariable_WhenValueIsNull_ThrowsAndDoesNotCreateVariable()
    {
        var context = new WorkflowContext();

        Assert.Throws<ArgumentNullException>(() => context.SetVariable("Value", null!));
        Assert.False(context.ContainsVariable("Value"));
    }

    [Fact]
    public void RemoveVariable_UsesExplicitDeletionSemantics()
    {
        var context = new WorkflowContext();
        context.SetVariable("Value", 42);

        var removed = context.RemoveVariable("Value");

        Assert.True(removed);
        Assert.False(context.ContainsVariable("Value"));
    }

    [Fact]
    public void PublicData_IsSharedWithChildScopeButLocalVariablesAreIsolated()
    {
        var parent = new WorkflowContext();
        parent.SetVariable("Local", 1);
        var child = parent.CreateChildScope();
        parent.PublicDataCenter.Values.WriteValue("Shared", 2);

        Assert.False(child.TryGetVariable<int>("Local", out _));
        Assert.True(child.TryReadPublicData("Shared", out var shared));
        Assert.Equal(2, shared);
    }

    [Fact]
    public async Task RunAsync_WhenCancellationIsRequested_ReturnsCanceledResult()
    {
        var canvasDocument = new WorkflowDocument { Name = "取消测试" };
        var canvas = canvasDocument;
        canvas.Nodes.Add(new WorkflowCanvasNode { Node = new BlockingNode { Id = "N1", Title = "等待" } });
        canvasDocument.EntryNodeId = "N1";
        var definition = new WorkflowCompiler().Compile(canvasDocument);
        var handlers = new WorkflowNodeHandlerCatalog().Register(new BlockingNodeHandler());
        var engine = new WorkflowEngine(definition, handlers);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        var result = await engine.RunAsync(cancellation.Token);

        Assert.False(result.Success);
        Assert.Equal(E_WorkflowExecutionState.Canceled, result.State);
    }

    private sealed class BlockingNode : WorkflowNodeModel
    {
        public override string NodeType => "Blocking";
    }

    private sealed class BlockingNodeHandler : WorkflowNodeHandler<BlockingNode>
    {
        protected override async ValueTask<NodeExecutionResult> ExecuteAsync(
            BlockingNode node,
            IWorkflowNodeExecutionContext context,
            CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return NodeExecutionResult.Complete();
        }
    }
}
