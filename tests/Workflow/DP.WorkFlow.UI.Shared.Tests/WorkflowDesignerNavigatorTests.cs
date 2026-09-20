using DP.WorkFlow.UI;

namespace DP.WorkFlow.Tests;

public sealed class WorkflowDesignerNavigatorTests
{
    [Fact]
    public void EnterAndNavigateUp_SwitchesBetweenRootAndBlockCanvas()
    {
        var childStart = new StartNodeModel { Id = "ChildStart", Title = "子开始" };
        var childDocument = new WorkflowDocument { Name = "Child" };
        var child = childDocument.CanvasProjection;
        child.Nodes.Add(new WorkflowCanvasNode { Node = childStart });
        childDocument.EntryNodeId = childStart.Id;
        var block = new BlockNodeModel
        {
            Id = "Block",
            Title = "工位子流程",
            SubDocument = childDocument
        };
        var rootDocument = new WorkflowDocument { Name = "Root" };
        var root = rootDocument.CanvasProjection;
        root.Nodes.Add(new WorkflowCanvasNode { Node = block });
        rootDocument.EntryNodeId = block.Id;
        var catalog = new WorkflowNodeCatalog().RegisterStandardNodes().RegisterCompositeNodes();
        var navigator = new WorkflowDesignerNavigator(rootDocument, catalog);

        Assert.True(navigator.EnterSubCanvas(block.Id));
        Assert.Equal(1, navigator.Depth);
        Assert.Same(child, navigator.CurrentSession.Canvas);
        Assert.Equal(childStart.Id, navigator.CurrentEntryNodeId);
        Assert.Equal(new[] { "Root", "工位子流程" }, navigator.Breadcrumbs.Select(item => item.Title));

        Assert.True(navigator.NavigateUp());
        Assert.Equal(0, navigator.Depth);
        Assert.Same(root, navigator.CurrentSession.Canvas);
    }

    [Fact]
    public void NestedBlocks_CanEnterTwoLevelsAndReturnToRootWithoutLosingSessions()
    {
        var nestedStart = new StartNodeModel { Id = "NestedStart", Title = "嵌套开始" };
        var nestedCanvasDocument = new WorkflowDocument { Name = "Nested" };
        var nestedCanvas = nestedCanvasDocument.CanvasProjection;
        nestedCanvas.Nodes.Add(new WorkflowCanvasNode { Node = nestedStart });
        nestedCanvasDocument.EntryNodeId = nestedStart.Id;
        var nestedBlock = new BlockNodeModel { Id = "NestedBlock", Title = "嵌套块", SubDocument = nestedCanvasDocument };
        var childCanvasDocument = new WorkflowDocument { Name = "Child" };
        var childCanvas = childCanvasDocument.CanvasProjection;
        childCanvas.Nodes.Add(new WorkflowCanvasNode { Node = nestedBlock });
        childCanvasDocument.EntryNodeId = nestedBlock.Id;
        var parentBlock = new BlockNodeModel { Id = "ParentBlock", Title = "父块", SubDocument = childCanvasDocument };
        var rootDocument = new WorkflowDocument { Name = "Root" };
        var root = rootDocument.CanvasProjection;
        root.Nodes.Add(new WorkflowCanvasNode { Node = parentBlock });
        rootDocument.EntryNodeId = parentBlock.Id;
        var navigator = new WorkflowDesignerNavigator(
            rootDocument,
            new WorkflowNodeCatalog().RegisterStandardNodes().RegisterCompositeNodes());

        Assert.True(navigator.EnterSubCanvas(parentBlock.Id));
        var childSession = navigator.CurrentSession;
        Assert.True(navigator.EnterSubCanvas(nestedBlock.Id));
        Assert.Equal(2, navigator.Depth);
        Assert.Same(nestedCanvas, navigator.CurrentSession.Canvas);
        Assert.True(navigator.NavigateUp());
        Assert.Same(childSession, navigator.CurrentSession);
        Assert.True(navigator.NavigateUp());
        Assert.Equal(0, navigator.Depth);
        Assert.Same(root, navigator.CurrentSession.Canvas);
    }

    [Fact]
    public void EnterEmptyBlock_DoesNotMutateDocumentAndReturnsFalse()
    {
        var block = new BlockNodeModel { Id = "Block", Title = "空子流程" };
        var rootDocument = new WorkflowDocument { Name = "Root" };
        var root = rootDocument.CanvasProjection;
        root.Nodes.Add(new WorkflowCanvasNode { Node = block });
        rootDocument.EntryNodeId = block.Id;
        var catalog = new WorkflowNodeCatalog().RegisterStandardNodes().RegisterCompositeNodes();
        var navigator = new WorkflowDesignerNavigator(rootDocument, catalog);

        Assert.False(navigator.EnterSubCanvas(block.Id));
        Assert.Empty(block.SubDocument.Graph.Nodes);
        Assert.Empty(block.SubDocument.EntryNodeId);
        Assert.Equal(0, navigator.Depth);
        Assert.False(navigator.RootSession.CanUndo);
    }
}
