namespace DP.WorkFlow.Tests;

public sealed class WorkflowDocumentTests
{
    [Fact]
    public void Construction_HasSingleDocumentRootAndOneWayCanvasProjection()
    {
        var documentConstructors = typeof(WorkflowDocument).GetConstructors();
        var canvasConstructors = typeof(WorkflowCanvasModel).GetConstructors();

        Assert.Single(documentConstructors);
        Assert.Empty(documentConstructors[0].GetParameters());
        Assert.Empty(canvasConstructors);
        Assert.DoesNotContain(
            typeof(WorkflowGraph).GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic),
            field => field.FieldType == typeof(WorkflowCanvasModel));
        Assert.DoesNotContain(
            typeof(WorkflowLayout).GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic),
            field => field.FieldType == typeof(WorkflowCanvasModel));
    }

    [Fact]
    public void CanvasProjection_MutatesStateOwnedAndExposedByDocument()
    {
        var document = new WorkflowDocument { Name = "Owned" };
        var canvas = document.CanvasProjection;
        var node = new TestNode { Id = "Node", Title = "Node" };

        canvas.Nodes.Add(new WorkflowCanvasNode { Node = node, X = 12, Y = 34 });
        canvas.Name = "Renamed";

        Assert.DoesNotContain(
            typeof(WorkflowCanvasModel).GetProperties(),
            property => property.PropertyType == typeof(WorkflowDocument));
        Assert.Equal("Renamed", document.Name);
        Assert.Same(node, Assert.Single(document.Graph.Nodes));
        var layout = Assert.Single(document.Layout.Nodes);
        Assert.Equal(node.Id, layout.NodeId);
        Assert.Equal(12, layout.X);
        Assert.Equal(34, layout.Y);
    }

    [Fact]
    public void CoreAnalysisAndCompilation_DoNotExposeCanvasEntryPoints()
    {
        Assert.DoesNotContain(
            typeof(WorkflowCompiler).GetMethods(),
            method => method.GetParameters().Any(parameter => parameter.ParameterType == typeof(WorkflowCanvasModel)));
        Assert.DoesNotContain(
            typeof(WorkflowGraphValidator).GetMethods(),
            method => method.GetParameters().Any(parameter => parameter.ParameterType == typeof(WorkflowCanvasModel)));
        Assert.DoesNotContain(
            typeof(WorkflowBindingAnalyzer).GetMethods(),
            method => method.GetParameters().Any(parameter => parameter.ParameterType == typeof(WorkflowCanvasModel)));
    }

    [Fact]
    public void Compile_UsesPersistedEntryAndIgnoresDesignerLayout()
    {
        var document = new WorkflowDocument { Name = "Document", EntryNodeId = "Start" };
        var start = new TestNode { Id = "Start", Title = "Start" };
        var end = new TestNode { Id = "End", Title = "End" };
        document.CanvasProjection.Nodes.Add(new WorkflowCanvasNode
        {
            Node = start,
            X = 20,
            Y = 30,
            Width = 180,
            Height = 60
        });
        document.CanvasProjection.Nodes.Add(new WorkflowCanvasNode { Node = end, X = 300, Y = 30 });
        document.CanvasProjection.Connections.Add(new WorkflowConnectionModel
        {
            FromNodeId = start.Id,
            FromPort = WorkflowPorts.Success,
            ToNodeId = end.Id,
            ToPort = WorkflowPorts.Input,
            Waypoints = { new WorkflowPoint(200, 60) }
        });

        var first = new WorkflowCompiler().Compile(document);
        document.CanvasProjection.Nodes[0].X = 900;
        document.CanvasProjection.Nodes[0].Width = 420;
        document.CanvasProjection.Connections[0].Waypoints.Clear();
        var second = new WorkflowCompiler().Compile(document);

        Assert.Equal("Start", first.EntryNodeId);
        Assert.Equal(new[] { "End" }, first.GetNextNodeIds("Start", WorkflowPorts.Success));
        Assert.Equal(first.NodeIds.OrderBy(id => id), second.NodeIds.OrderBy(id => id));
        Assert.Equal(first.GetNextNodeIds("Start", WorkflowPorts.Success), second.GetNextNodeIds("Start", WorkflowPorts.Success));
    }

    [Fact]
    public void Compile_RejectsDocumentWithoutExplicitEntry()
    {
        var document = new WorkflowDocument();
        document.CanvasProjection.Nodes.Add(new WorkflowCanvasNode
        {
            Node = new TestNode { Id = "Node", Title = "Node" }
        });

        var exception = Assert.Throws<WorkflowCompilationException>(() => new WorkflowCompiler().Compile(document));

        Assert.Contains(exception.Errors, error => error.Code == "WF013");
    }

    [Fact]
    public void Compile_RejectsNestedDocumentWithoutExplicitEntry()
    {
        var child = new WorkflowDocument();
        child.CanvasProjection.Nodes.Add(new WorkflowCanvasNode
        {
            Node = new TestNode { Id = "ChildStart", Title = "Child" }
        });
        var block = new TestSubDocumentNode { Id = "Block", Title = "Block", SubDocument = child };
        var parent = new WorkflowDocument { EntryNodeId = block.Id };
        parent.CanvasProjection.Nodes.Add(new WorkflowCanvasNode { Node = block });

        var exception = Assert.Throws<WorkflowCompilationException>(() => new WorkflowCompiler().Compile(parent));

        Assert.Contains(exception.Errors, error => error.Code == "WF013");
    }

    private sealed class TestSubDocumentNode : WorkflowNodeModel, IWorkflowSubDocumentNode
    {
        public override string NodeType => "DocumentTest.Block";

        public WorkflowDocument SubDocument { get; set; } = new();
    }

    private sealed class TestNode : WorkflowNodeModel
    {
        public override string NodeType => "DocumentTest";
    }
}
