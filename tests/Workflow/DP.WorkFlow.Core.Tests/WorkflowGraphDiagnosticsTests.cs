namespace DP.WorkFlow.Tests;

public sealed class WorkflowGraphDiagnosticsTests
{
    [Fact]
    public void Compile_PreservesUnreachableNodeAsWarning()
    {
        var canvasDocument = new WorkflowDocument { Name = "不可达节点" };
        var canvas = canvasDocument.CanvasProjection;
        canvas.Nodes.Add(new WorkflowCanvasNode { Node = new TestNode { Id = "Start", Title = "Start" } });
        canvas.Nodes.Add(new WorkflowCanvasNode { Node = new TestNode { Id = "Unused", Title = "Unused" } });

        canvasDocument.EntryNodeId = "Start";
        var definition = new WorkflowCompiler().Compile(canvasDocument);

        var warning = Assert.Single(definition.Diagnostics, item => item.Code == "WF101");
        Assert.Equal(WorkflowValidationSeverity.Warning, warning.Severity);
        Assert.Equal("Unused", warning.NodeId);
    }

    private sealed class TestNode : WorkflowNodeModel
    {
        public override string NodeType => "Test";
    }
}
