namespace DP.WorkFlow.Tests;

public sealed class WorkflowCompilerTests
{
    [Fact]
    public void Compile_InvalidWorkflowInputConfigurationFailsBeforePlanCreation()
    {
        var node = new DecisionNodeModel
        {
            Id = "Decision",
            Title = "判断",
            Condition = new WorkflowInput<bool> { Source = WorkflowValueSource.Binding }
        };
        var canvasDocument = new WorkflowDocument();
        var canvas = canvasDocument.CanvasProjection;
        canvas.Nodes.Add(new WorkflowCanvasNode { Node = node });

        canvasDocument.EntryNodeId = node.Id;
        var exception = Assert.Throws<WorkflowCompilationException>(() =>
            new WorkflowCompiler(new WorkflowNodeCatalog().RegisterStandardNodes()).Compile(canvasDocument));

        Assert.Contains(exception.Errors, error => error.Code == "WF030");
    }

    [Fact]
    public void Compile_WhenNodeIdIsDuplicated_ReturnsStructuredValidationError()
    {
        var canvasDocument = new WorkflowDocument { Name = "重复节点测试" };
        var canvas = canvasDocument.CanvasProjection;
        canvas.Nodes.Add(new WorkflowCanvasNode { Node = new TestNode("N1") });
        canvas.Nodes.Add(new WorkflowCanvasNode { Node = new TestNode("N1") });

        canvasDocument.EntryNodeId = "N1";
        var exception = Assert.Throws<WorkflowCompilationException>(
            () => new WorkflowCompiler().Compile(canvasDocument));

        Assert.Contains(exception.Errors, error => error.Code == "WF001");
    }

    [Fact]
    public void Compile_MapsExplicitOutputPortToTargetNode()
    {
        var canvasDocument = new WorkflowDocument { Name = "端口测试" };
        var canvas = canvasDocument.CanvasProjection;
        canvas.Nodes.Add(new WorkflowCanvasNode { Node = new TestNode("N1") });
        canvas.Nodes.Add(new WorkflowCanvasNode { Node = new TestNode("N2") });
        canvas.Connections.Add(new WorkflowConnectionModel
        {
            FromNodeId = "N1",
            FromPort = WorkflowPorts.True,
            ToNodeId = "N2"
        });

        canvasDocument.EntryNodeId = "N1";
        var definition = new WorkflowCompiler().Compile(canvasDocument);

        Assert.Equal(new[] { "N2" }, definition.GetNextNodeIds("N1", WorkflowPorts.True));
        Assert.Empty(definition.GetNextNodeIds("N1", WorkflowPorts.False));
    }

    [Fact]
    public void Compile_WhenCanvasOrReturnedNodeChanges_KeepsExecutionSnapshotStable()
    {
        var source = new TestNode("N1") { Value = 10 };
        source.Labels.Add("compiled");
        var canvasDocument = new WorkflowDocument { Name = "配置快照" };
        var canvas = canvasDocument.CanvasProjection;
        canvas.Nodes.Add(new WorkflowCanvasNode { Node = source });

        canvasDocument.EntryNodeId = source.Id;
        var definition = new WorkflowCompiler().Compile(canvasDocument);
        source.Value = 20;
        source.Labels.Add("canvas-change");
        var firstExecutionNode = Assert.IsType<TestNode>(definition.GetNodeOrThrow(source.Id));
        firstExecutionNode.Value = 30;
        firstExecutionNode.Labels.Add("handler-change");
        var secondExecutionNode = Assert.IsType<TestNode>(definition.GetNodeOrThrow(source.Id));

        Assert.Equal(10, secondExecutionNode.Value);
        Assert.Equal(new[] { "compiled" }, secondExecutionNode.Labels);
        Assert.NotSame(source.Labels, secondExecutionNode.Labels);
    }

    private sealed class TestNode : WorkflowNodeModel
    {
        public TestNode(string id)
        {
            Id = id;
            Title = id;
        }

        public override string NodeType => "Test";

        public int Value { get; set; }

        public List<string> Labels { get; } = new();
    }
}
