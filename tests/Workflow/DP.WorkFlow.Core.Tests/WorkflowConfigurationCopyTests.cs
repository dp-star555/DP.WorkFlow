namespace DP.WorkFlow.Tests;

/// <summary>R7-c：运行开始时按保存格式复制配置。</summary>
public sealed class WorkflowConfigurationCopyTests
{
    [Fact]
    public void CopyDocument_IsDetachedIncludingSubDocuments()
    {
        var inner = new WorkflowDocument { Name = "子", EntryNodeId = "I" };
        inner.Nodes.Add(new WorkflowCanvasNode { Node = new PlainNode { Id = "I", Value = 1 } });
        var outer = new WorkflowDocument { Name = "主", EntryNodeId = "C" };
        outer.Nodes.Add(new WorkflowCanvasNode { Node = new CompositeNode { Id = "C", SubDocument = inner }, X = 5 });

        var copy = WorkflowConfigurationCopy.CopyDocument(outer);

        var composite = Assert.IsType<CompositeNode>(Assert.Single(copy.Nodes).Node);
        Assert.NotSame(inner, composite.SubDocument);
        var plain = Assert.IsType<PlainNode>(Assert.Single(composite.SubDocument.Nodes).Node);
        ((PlainNode)inner.Nodes[0].Node).Value = 2;
        Assert.Equal(1, plain.Value);
        Assert.Equal(5, copy.Nodes[0].X);
    }

    [Fact]
    public void CopyNode_RejectsConfigurationThatCannotBeSaved()
    {
        var error = Assert.Throws<InvalidOperationException>(() => WorkflowConfigurationCopy.CopyNode(new UnsavableNode { Id = "U" }));

        Assert.Contains("不能按保存格式复制", error.Message);
    }

    [Fact]
    public void CopyDocument_DetectsSubDocumentCycle()
    {
        var document = new WorkflowDocument { EntryNodeId = "C" };
        document.Nodes.Add(new WorkflowCanvasNode { Node = new CompositeNode { Id = "C", SubDocument = document } });

        Assert.Throws<InvalidOperationException>(() => WorkflowConfigurationCopy.CopyDocument(document));
    }

    private sealed class PlainNode : WorkflowNodeModel
    {
        public override string NodeType => "Plain";
        public int Value { get; set; }
    }

    private sealed class CompositeNode : WorkflowNodeModel, IWorkflowSubDocumentNode
    {
        public override string NodeType => "Composite";
        [System.Text.Json.Serialization.JsonIgnore]
        public WorkflowDocument SubDocument { get; set; } = new();
    }

    private sealed class UnsavableNode : WorkflowNodeModel
    {
        public override string NodeType => "Unsavable";
        public Action Callback { get; set; } = () => { };
    }
}
