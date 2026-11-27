using DP.WorkFlow.UI;

namespace DP.WorkFlow.Tests;

public sealed class WorkflowLayoutTests
{
    [Fact]
    public void MultiSelection_AlignDistributeAndBatchMoveAreUndoable()
    {
        var session = CreateSession();
        var first = session.AddNode("Action", 10, 20);
        var middle = session.AddNode("Action", 260, 90);
        var last = session.AddNode("Action", 900, 180);
        session.SelectNodes(new[] { first.Node.Id, middle.Node.Id, last.Node.Id });
        var initialMiddleX = middle.X;
        var initialMiddleY = middle.Y;

        Assert.True(session.AlignSelectedNodes(WorkflowNodeAlignment.Top));
        Assert.All(new[] { first, middle, last }, node => Assert.Equal(first.Y, node.Y));
        Assert.True(session.DistributeSelectedNodes(WorkflowNodeDistribution.Horizontal));
        var firstCenter = first.X + first.Width / 2;
        var middleCenter = middle.X + middle.Width / 2;
        var lastCenter = last.X + last.Width / 2;
        // 中间节点吸附栅格，中心与首尾中点相差不超过半格。
        Assert.True(Math.Abs((firstCenter + lastCenter) / 2 - middleCenter) <= WorkflowDesignerGeometry.GridSize / 2);

        Assert.True(session.Undo());
        Assert.Equal(initialMiddleX, middle.X);
        Assert.True(session.Undo());
        Assert.Equal(initialMiddleY, middle.Y);
    }

    [Fact]
    public void CenterAlign_SpreadsNodesThatWouldOverlapAndDistributeSnapsToGrid()
    {
        var session = CreateSession();
        var nodes = new[] { session.AddNode("Action", 0, 0), session.AddNode("Action", 240, 10), session.AddNode("Action", 600, 30) };
        session.SelectNodes(nodes.Select(node => node.Node.Id));

        // 同一行的节点做水平居中：中心对齐，按原来的左右顺序自上而下让开，不叠在一起，位置在栅格上。
        Assert.True(session.AlignSelectedNodes(WorkflowNodeAlignment.HorizontalCenter));
        Assert.Single(nodes.Select(node => node.X + node.Width / 2).Distinct());
        for (var index = 1; index < nodes.Length; index++)
            Assert.True(nodes[index].Y >= nodes[index - 1].Y + nodes[index - 1].Height + WorkflowDesignerGeometry.GridSize - 0.01);
        Assert.All(nodes.Skip(1), node => Assert.Equal(0, node.Y % WorkflowDesignerGeometry.GridSize, 6));

        session.MoveNodes(new Dictionary<string, WorkflowPoint>
        {
            [nodes[0].Node.Id] = new(0, 0), [nodes[1].Node.Id] = new(100, 0), [nodes[2].Node.Id] = new(700, 0)
        });
        Assert.True(session.DistributeSelectedNodes(WorkflowNodeDistribution.Horizontal));
        Assert.Equal(0, nodes[1].X % WorkflowDesignerGeometry.GridSize, 6);
    }

    [Fact]
    public void FitToView_KeepsLoopBackLinesInsideTheViewport()
    {
        var (session, _, _, _, _, _) = CreateLoop(vertical: true);
        session.AutoLayout();

        session.FitToView(800, 600);

        foreach (var connection in session.Document.Connections)
            Assert.All(WorkflowDesignerInteraction.GetConnectionPath(session, connection),
                point => Assert.True(point.X >= 0 && point.X <= 800 && point.Y >= 0 && point.Y <= 600));
    }

    [Fact]
    public void AutoLayout_PlacesConnectedNodesInIncreasingLayersWithStraightLines()
    {
        var session = CreateSession();
        var start = session.AddNode("Start", 800, 500);
        var action = session.AddNode("Action", 10, 400);
        var end = session.AddNode("End", 20, 10);
        SetSides(session, false, start, action, end);
        session.Connect(start.Node.Id, WorkflowPorts.Success, action.Node.Id);
        session.Connect(action.Node.Id, WorkflowPorts.Success, end.Node.Id);

        Assert.True(session.AutoLayout());

        Assert.True(start.X < action.X);
        Assert.True(action.X < end.X);
        Assert.All(session.Document.Connections, connection => AssertStraight(session, connection, vertical: false));
    }

    [Fact]
    public void AutoLayout_KeepsLoopBodyOnTheMainLineAndPutsCompletedBranchBeside()
    {
        var (session, start, loop, a, b, end) = CreateLoop(vertical: false);

        Assert.True(session.AutoLayout());

        // 主线：开始、循环、A、B 从左到右，间距紧凑（不超过 3 格），连线都是直线。
        Assert.True(start.X < loop.X && loop.X < a.X && a.X < b.X);
        Assert.True(a.X - (loop.X + loop.Width) <= WorkflowDesignerGeometry.GridSize * 3 + 0.01);
        Assert.All(session.Document.Connections.Where(connection => connection.ToNodeId != loop.Node.Id || connection.FromNodeId == start.Node.Id),
            connection => { if (connection.ToNodeId != end.Node.Id) AssertStraight(session, connection, vertical: false); });
        // “完成”分支和循环体第一个节点同一层，排在它下面，不重叠。
        Assert.Equal(a.X, end.X);
        Assert.True(end.Y >= a.Y + a.Height + WorkflowDesignerGeometry.GridSize * 2 - 0.01);
    }

    [Fact]
    public void AutoLayout_FollowsBottomPortsTopToBottom()
    {
        var (session, start, loop, a, b, end) = CreateLoop(vertical: true);

        Assert.True(session.AutoLayout());

        // 下边出线：自上而下分层，层的位置在栅格上；主线连线是竖直的直线。
        Assert.True(start.Y < loop.Y && loop.Y < a.Y && a.Y < b.Y);
        Assert.All(new[] { start, loop, a, b, end }, node => Assert.Equal(0, node.Y % WorkflowDesignerGeometry.GridSize, 6));
        foreach (var connection in session.Document.Connections.Where(connection =>
                     connection.ToNodeId != end.Node.Id && !(connection.FromNodeId == b.Node.Id && connection.ToNodeId == loop.Node.Id)))
            AssertStraight(session, connection, vertical: true);
        // “完成”分支和 A 同一层，排在 A 右边。
        Assert.Equal(a.Y, end.Y);
        Assert.True(end.X >= a.X + a.Width + WorkflowDesignerGeometry.GridSize * 2 - 0.01);
    }

    private static (WorkflowDesignerSession Session, WorkflowCanvasNode Start, WorkflowCanvasNode Loop, WorkflowCanvasNode A,
        WorkflowCanvasNode B, WorkflowCanvasNode End) CreateLoop(bool vertical)
    {
        // 开始 → 循环 →（循环体）A → B → 回到循环；循环“完成” → 结束。
        var session = CreateSession();
        var start = session.AddNode("Start", 900, 900);
        var loop = session.AddNode("Loop", 0, 0);
        var end = session.AddNode("End", 300, 0);
        var a = session.AddNode("Action", 600, 0);
        var b = session.AddNode("Action", 900, 0);
        session.Document.EntryNodeId = start.Node.Id;
        SetSides(session, vertical, start, loop, end, a, b);
        session.Connect(start.Node.Id, WorkflowPorts.Success, loop.Node.Id);
        session.Connect(loop.Node.Id, WorkflowPorts.Loop, a.Node.Id);
        session.Connect(a.Node.Id, WorkflowPorts.Success, b.Node.Id);
        session.Connect(b.Node.Id, WorkflowPorts.Success, loop.Node.Id);
        session.Connect(loop.Node.Id, WorkflowPorts.Completed, end.Node.Id);
        return (session, start, loop, a, b, end);
    }

    private static void SetSides(WorkflowDesignerSession session, bool vertical, params WorkflowCanvasNode[] nodes)
    {
        foreach (var node in nodes)
        {
            foreach (var port in session.GetDeclaredPorts(node.Node.Id, WorkflowPortDirection.Output))
                session.SetPortSide(node.Node.Id, WorkflowPortDirection.Output, port.Key, vertical ? WorkflowPortSide.Bottom : WorkflowPortSide.Right);
            foreach (var port in session.GetDeclaredPorts(node.Node.Id, WorkflowPortDirection.Input))
                session.SetPortSide(node.Node.Id, WorkflowPortDirection.Input, port.Key, vertical ? WorkflowPortSide.Top : WorkflowPortSide.Left);
        }
    }

    private static void AssertStraight(WorkflowDesignerSession session, WorkflowConnectionModel connection, bool vertical)
    {
        var path = WorkflowDesignerInteraction.GetConnectionPath(session, connection);
        Assert.NotEmpty(path);
        Assert.All(path, point => Assert.Equal(vertical ? path[0].X : path[0].Y, vertical ? point.X : point.Y, 3));
    }

    private static WorkflowDesignerSession CreateSession() => new(
        new WorkflowDocument { Name = "Layout" },
        new WorkflowNodeCatalog().RegisterStandardNodes());
}
