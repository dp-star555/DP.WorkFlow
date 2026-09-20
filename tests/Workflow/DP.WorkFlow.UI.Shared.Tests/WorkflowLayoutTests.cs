using DP.WorkFlow.UI;

namespace DP.WorkFlow.Tests;

public sealed class WorkflowLayoutTests
{
    [Fact]
    public void MultiSelection_AlignDistributeAndBatchMoveAreUndoable()
    {
        var session = CreateSession();
        var first = session.AddNode("Action", 10, 20);
        var middle = session.AddNode("Action", 160, 90);
        var last = session.AddNode("Action", 500, 180);
        session.SelectNodes(new[] { first.Node.Id, middle.Node.Id, last.Node.Id });
        var initialMiddleX = middle.X;
        var initialMiddleY = middle.Y;

        Assert.True(session.AlignSelectedNodes(WorkflowNodeAlignment.Top));
        Assert.All(new[] { first, middle, last }, node => Assert.Equal(first.Y, node.Y));
        Assert.True(session.DistributeSelectedNodes(WorkflowNodeDistribution.Horizontal));
        var firstCenter = first.X + first.Width / 2;
        var middleCenter = middle.X + middle.Width / 2;
        var lastCenter = last.X + last.Width / 2;
        Assert.Equal((firstCenter + lastCenter) / 2, middleCenter, 6);

        Assert.True(session.Undo());
        Assert.Equal(initialMiddleX, middle.X);
        Assert.True(session.Undo());
        Assert.Equal(initialMiddleY, middle.Y);
    }

    [Fact]
    public void AutoLayout_PlacesConnectedNodesInIncreasingLayers()
    {
        var session = CreateSession();
        var start = session.AddNode("Start", 800, 500);
        var action = session.AddNode("Action", 10, 400);
        var end = session.AddNode("End", 20, 10);
        session.Connect(start.Node.Id, WorkflowPorts.Success, action.Node.Id);
        session.Connect(action.Node.Id, WorkflowPorts.Success, end.Node.Id);

        Assert.True(session.AutoLayout());

        Assert.True(start.X < action.X);
        Assert.True(action.X < end.X);
        Assert.Equal(0, start.Y);
        Assert.Equal(0, action.Y);
        Assert.Equal(0, end.Y);
    }

    private static WorkflowDesignerSession CreateSession() => new(
        new WorkflowDocument { Name = "Layout" },
        new WorkflowNodeCatalog().RegisterStandardNodes());
}
