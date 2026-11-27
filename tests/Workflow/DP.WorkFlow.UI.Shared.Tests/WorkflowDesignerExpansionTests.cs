using DP.WorkFlow.UI;

namespace DP.WorkFlow.Tests;

public sealed class WorkflowDesignerExpansionTests
{
    public WorkflowDesignerExpansionTests() => WorkflowCanvasExpansionMemory.FilePath = null;

    [Fact]
    public void Expand_GrowsNodeAndMovesRightAndBelowNodesWithoutTouchingDocument()
    {
        var (session, block, right, below, left) = CreateRoot("展开让位");
        var changes = new List<WorkflowDesignerChangeKind>();
        session.Changed += (_, e) => changes.Add(e.Kind);
        var before = (block.X, block.Y, block.Width, block.Height, RightX: right.X, BelowY: below.Y);

        Assert.True(session.SetExpanded(block.Node.Id, true));

        var frame = session.GetNodeDisplayRect(block);
        Assert.Equal(block.X, frame.X);
        Assert.True(frame.Width > block.Width && frame.Height > block.Height);
        var growX = frame.Width - block.Width;
        var growY = frame.Height - block.Height;
        Assert.Equal(right.X + growX, session.GetNodeDisplayRect(right).X);
        Assert.Equal(right.Y, session.GetNodeDisplayRect(right).Y);
        Assert.Equal(below.Y + growY, session.GetNodeDisplayRect(below).Y);
        Assert.Equal(left.X, session.GetNodeDisplayRect(left).X);
        Assert.Equal(left.Y, session.GetNodeDisplayRect(left).Y);
        // 只是显示：保存的坐标不变，也不算文档修改。
        Assert.Equal(before, (block.X, block.Y, block.Width, block.Height, right.X, below.Y));
        Assert.DoesNotContain(WorkflowDesignerChangeKind.Document, changes);
        Assert.False(session.CanUndo);

        var stored = new WorkflowPoint(right.X + 10, right.Y + 5);
        Assert.Equal(stored, session.ToStoredPoint(session.ToDisplayPoint(stored)));

        Assert.True(session.SetExpanded(block.Node.Id, false));
        Assert.Equal(right.X, session.GetNodeDisplayRect(right).X);
    }

    [Fact]
    public void ExpandedSession_DrawsInsideFrameAndFollowsViewport()
    {
        var (session, block, _, _, _) = CreateRoot("展开同步");
        session.SetExpanded(block.Node.Id, true);
        session.SetViewport(1.5, 40, 30);

        var child = session.GetExpandedSession(block.Node.Id)!;
        Assert.Same(session, child.Owner);
        Assert.Equal(session.Zoom, child.Zoom);
        var content = session.GetExpandedContentRect(block.Node.Id)!.Value;
        var first = child.Document.Nodes[0];
        var screen = WorkflowDesignerGeometry.GetNodeScreenRect(child, first);
        var expectedLeft = (content.X + WorkflowDesignerSession.ExpandedPadding) * session.Zoom + session.PanX;
        Assert.Equal(expectedLeft, screen.X, 6);
        Assert.True(screen.Y >= (block.Y + block.Height) * session.Zoom + session.PanY);
    }

    [Fact]
    public void EditInExpandedArea_SharesUndoWithRootAndMarksDocumentChanged()
    {
        var (session, block, _, _, _) = CreateRoot("展开编辑");
        session.SetExpanded(block.Node.Id, true);
        var child = session.GetExpandedSession(block.Node.Id)!;
        var documentChanges = 0;
        session.Changed += (_, e) => { if (e.Kind == WorkflowDesignerChangeKind.Document) documentChanges++; };
        var node = child.Document.Nodes[1];
        var origin = (node.X, node.Y);

        Assert.True(child.MoveNode(node.Node.Id, node.X + 48, node.Y));

        Assert.True(documentChanges > 0);
        Assert.True(session.CanUndo);
        Assert.True(child.CanUndo);
        Assert.True(session.Undo());
        Assert.Equal(origin, (node.X, node.Y));
        Assert.True(child.Redo());
        Assert.Equal(origin.X + 48, node.X);
    }

    [Fact]
    public void NestedExpansion_GrowsOuterFrame()
    {
        var catalog = Catalog();
        var inner = Sub("内层", 3);
        var nested = new BlockNodeModel { Id = "Inner", Title = "内层块", SubDocument = inner };
        var middle = Sub("中层", 1);
        middle.Nodes.Add(new WorkflowCanvasNode { Node = nested, X = 240, Y = 0 });
        var outer = new BlockNodeModel { Id = "Outer", Title = "外层块", SubDocument = middle };
        var document = new WorkflowDocument { Name = "嵌套展开", EntryNodeId = "Outer" };
        document.Nodes.Add(new WorkflowCanvasNode { Node = outer, X = 0, Y = 0 });
        var session = new WorkflowDesignerSession(document, catalog);
        session.SetExpanded("Outer", true);
        var collapsedInner = session.GetNodeDisplayRect(document.Nodes[0]);

        var child = session.GetExpandedSession("Outer")!;
        Assert.True(child.SetExpanded("Inner", true));

        var expandedInner = session.GetNodeDisplayRect(document.Nodes[0]);
        Assert.True(expandedInner.Width > collapsedInner.Width);
        Assert.True(expandedInner.Height > collapsedInner.Height);
        Assert.NotNull(child.GetExpandedSession("Inner"));
        Assert.Equal(3, session.SelfAndExpandedSessions().Count());
    }

    [Fact]
    public void Expansion_IsRememberedPerFlowAndRestoredWhenEnabled()
    {
        var (session, block, _, _, _) = CreateRoot("展开记忆");
        session.EnableExpansion();
        session.SetExpanded(block.Node.Id, true);

        var (reopened, _, _, _, _) = CreateRoot("展开记忆");
        Assert.False(reopened.IsExpanded(block.Node.Id));
        reopened.EnableExpansion();
        Assert.True(reopened.IsExpanded(block.Node.Id));

        var (other, _, _, _, _) = CreateRoot("别的流程");
        other.EnableExpansion();
        Assert.False(other.IsExpanded(block.Node.Id));
        session.SetExpanded(block.Node.Id, false);
    }

    private static WorkflowNodeCatalog Catalog() => new WorkflowNodeCatalog().RegisterStandardNodes().RegisterCompositeNodes();

    private static WorkflowDocument Sub(string name, int count)
    {
        var document = new WorkflowDocument { Name = name, EntryNodeId = "S0" };
        document.Nodes.Add(new WorkflowCanvasNode { Node = new StartNodeModel { Id = "S0", Title = "开始" }, X = 0, Y = 0 });
        for (var i = 1; i < count; i++)
            document.Nodes.Add(new WorkflowCanvasNode { Node = new EndNodeModel { Id = "S" + i, Title = "结束" + i }, X = i * 240, Y = 0 });
        return document;
    }

    // 块在 (240, 96)；右边一个、下边一个、左上一个。
    private static (WorkflowDesignerSession Session, WorkflowCanvasNode Block, WorkflowCanvasNode Right, WorkflowCanvasNode Below, WorkflowCanvasNode Left) CreateRoot(string name)
    {
        var block = new BlockNodeModel { Id = "Block", Title = "子流程", SubDocument = Sub("子", 3) };
        var document = new WorkflowDocument { Name = name, EntryNodeId = "Start" };
        document.Nodes.Add(new WorkflowCanvasNode { Node = new StartNodeModel { Id = "Start", Title = "开始" }, X = 0, Y = 0 });
        document.Nodes.Add(new WorkflowCanvasNode { Node = block, X = 240, Y = 96 });
        document.Nodes.Add(new WorkflowCanvasNode { Node = new EndNodeModel { Id = "Right", Title = "右" }, X = 528, Y = 96 });
        document.Nodes.Add(new WorkflowCanvasNode { Node = new EndNodeModel { Id = "Below", Title = "下" }, X = 240, Y = 288 });
        var session = new WorkflowDesignerSession(document, Catalog());
        return (session, document.Nodes[1], document.Nodes[2], document.Nodes[3], document.Nodes[0]);
    }
}
