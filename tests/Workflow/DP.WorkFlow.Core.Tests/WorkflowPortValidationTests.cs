namespace DP.WorkFlow.Tests;

public sealed class WorkflowPortValidationTests
{
    [Fact]
    public void PortDescriptor_AllowsExplicitNodeEdge()
    {
        var input = WorkflowPortDescriptor.Input(side: WorkflowPortSide.Top);
        var output = WorkflowPortDescriptor.Output("Alarm", 2, WorkflowPortSide.Bottom);

        Assert.Equal(WorkflowPortSide.Top, input.Side);
        Assert.Equal(WorkflowPortSide.Bottom, output.Side);
        Assert.Equal(WorkflowPortDirection.Output, output.Direction);
    }

    [Fact]
    public void Compile_WhenNodeUsesUndeclaredOutputPort_RejectsConnection()
    {
        var catalog = CreateCatalog();
        var canvasDocument = new WorkflowDocument { Name = "非法输出" };
        var canvas = canvasDocument.CanvasProjection;
        canvas.Nodes.Add(new WorkflowCanvasNode { Node = new SinkNode { Id = "Sink", Title = "终点" } });
        canvas.Nodes.Add(new WorkflowCanvasNode { Node = new SourceNode { Id = "Source", Title = "来源" } });
        canvas.Connections.Add(new WorkflowConnectionModel { FromNodeId = "Sink", ToNodeId = "Source" });

        canvasDocument.EntryNodeId = "Sink";
        var exception = Assert.Throws<WorkflowCompilationException>(
            () => new WorkflowCompiler(catalog).Compile(canvasDocument));

        Assert.Contains(exception.Errors, error => error.Code == "WF010");
    }

    [Fact]
    public void Compile_WhenSingleOutputExceedsCardinality_RejectsConnections()
    {
        var catalog = CreateCatalog();
        var canvasDocument = new WorkflowDocument { Name = "端口基数" };
        var canvas = canvasDocument.CanvasProjection;
        canvas.Nodes.Add(new WorkflowCanvasNode { Node = new SourceNode { Id = "Source", Title = "来源" } });
        canvas.Nodes.Add(new WorkflowCanvasNode { Node = new SinkNode { Id = "Sink1", Title = "终点1" } });
        canvas.Nodes.Add(new WorkflowCanvasNode { Node = new SinkNode { Id = "Sink2", Title = "终点2" } });
        canvas.Connections.Add(new WorkflowConnectionModel { FromNodeId = "Source", ToNodeId = "Sink1" });
        canvas.Connections.Add(new WorkflowConnectionModel { FromNodeId = "Source", ToNodeId = "Sink2" });

        canvasDocument.EntryNodeId = "Source";
        var exception = Assert.Throws<WorkflowCompilationException>(
            () => new WorkflowCompiler(catalog).Compile(canvasDocument));

        Assert.Contains(exception.Errors, error => error.Code == "WF012");
    }

    private static WorkflowNodeCatalog CreateCatalog() => new WorkflowNodeCatalog()
        .Register(WorkflowNodeDescriptor.Create<SourceNode>(
            ports: new[] { WorkflowPortDescriptor.Output() }))
        .Register(WorkflowNodeDescriptor.Create<SinkNode>(
            ports: new[] { WorkflowPortDescriptor.Input() }));

    [WorkflowNode("Source")]
    private sealed class SourceNode : WorkflowNodeModel
    {
        public override string NodeType => "Source";
    }

    [WorkflowNode("Sink")]
    private sealed class SinkNode : WorkflowNodeModel
    {
        public override string NodeType => "Sink";
    }
}
