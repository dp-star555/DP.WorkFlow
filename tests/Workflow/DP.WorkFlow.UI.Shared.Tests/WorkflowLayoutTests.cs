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

    [Fact]
    public void AutoLayout_KeepsLoopBodyOnTheMainRowAndPutsCompletedBranchBelow()
    {
        // 开始 → 循环 →（循环体）A → B → 回到循环；循环“完成” → 结束。
        var session = CreateSession();
        var start = session.AddNode("Start", 900, 900);
        var loop = session.AddNode("Loop", 0, 0);
        var end = session.AddNode("End", 300, 0);
        var a = session.AddNode("Action", 600, 0);
        var b = session.AddNode("Action", 900, 0);
        session.Document.EntryNodeId = start.Node.Id;
        session.Connect(start.Node.Id, WorkflowPorts.Success, loop.Node.Id);
        session.Connect(loop.Node.Id, WorkflowPorts.Loop, a.Node.Id);
        session.Connect(a.Node.Id, WorkflowPorts.Success, b.Node.Id);
        session.Connect(b.Node.Id, WorkflowPorts.Success, loop.Node.Id);
        session.Connect(loop.Node.Id, WorkflowPorts.Completed, end.Node.Id);

        Assert.True(session.AutoLayout());

        // 主线一行：开始、循环、A、B 从左到右，间距紧凑（不超过 3 格）。
        Assert.All(new[] { loop, a, b }, node => Assert.Equal(start.Y, node.Y));
        Assert.True(start.X < loop.X && loop.X < a.X && a.X < b.X);
        Assert.True(a.X - (loop.X + loop.Width) <= WorkflowDesignerGeometry.GridSize * 3 + 0.01);
        // “完成”分支和循环体第一个节点同一层，放在下一行。
        Assert.Equal(a.X, end.X);
        Assert.True(end.Y > a.Y);
        var rowHeight = new[] { start, loop, a, b }.Max(node => node.Height);
        Assert.True(end.Y - (a.Y + rowHeight) <= WorkflowDesignerGeometry.GridSize * 2 + 0.01);
    }

    private static WorkflowDesignerSession CreateSession() => new(
        new WorkflowDocument { Name = "Layout" },
        new WorkflowNodeCatalog().RegisterStandardNodes());
}
