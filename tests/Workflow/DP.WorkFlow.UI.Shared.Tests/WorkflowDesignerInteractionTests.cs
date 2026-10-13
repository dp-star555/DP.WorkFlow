using DP.WorkFlow.UI;

namespace DP.WorkFlow.Tests;

public sealed class WorkflowDesignerInteractionTests
{
    [Theory]
    [InlineData(1500, "1.50s")]
    [InlineData(25, "25ms")]
    [InlineData(2.5, "2.5ms")]
    [InlineData(0.25, "0.250ms")]
    public void FormatElapsed_ChoosesPrecisionByMagnitude(double milliseconds, string expected) =>
        Assert.Equal(expected, WorkflowDesignerInteraction.FormatElapsed(TimeSpan.FromMilliseconds(milliseconds)));

    [Fact]
    public void DistanceToSegment_ClampsToEndpointsAndHandlesDegenerateSegment()
    {
        var start = new WorkflowPoint(0, 0);
        var end = new WorkflowPoint(10, 0);

        Assert.Equal(3, WorkflowDesignerInteraction.DistanceToSegment(5, 3, start, end), 9);
        Assert.Equal(5, WorkflowDesignerInteraction.DistanceToSegment(13, 4, start, end), 9);
        Assert.Equal(5, WorkflowDesignerInteraction.DistanceToSegment(3, 4, start, start), 9);
    }

    [Fact]
    public void PointAlongPath_AndProjectPathPosition_AreConsistent()
    {
        var path = new[] { new WorkflowPoint(0, 0), new WorkflowPoint(10, 0), new WorkflowPoint(10, 10) };

        Assert.Equal(new WorkflowPoint(10, 0), WorkflowDesignerInteraction.PointAlongPath(path, 0.5));
        Assert.Equal(new WorkflowPoint(10, 10), WorkflowDesignerInteraction.PointAlongPath(path, 2));
        Assert.Equal(default, WorkflowDesignerInteraction.PointAlongPath(Array.Empty<WorkflowPoint>(), 0.5));
        Assert.Equal(0.25, WorkflowDesignerInteraction.ProjectPathPosition(path, 5, 3), 9);
        Assert.Equal(0.95, WorkflowDesignerInteraction.ProjectPathPosition(path, 10, 50), 9);
        Assert.Equal(0.5, WorkflowDesignerInteraction.ProjectPathPosition(new[] { path[0] }, 0, 0));
    }

    [Fact]
    public void BuildOrthogonalPath_RoutesThroughLeadsAndWaypoints()
    {
        var start = new WorkflowPoint(0, 0);
        var end = new WorkflowPoint(100, 40);

        Assert.Equal(
            new[]
            {
                start, new WorkflowPoint(28, 0), new WorkflowPoint(50, 0),
                new WorkflowPoint(50, 40), new WorkflowPoint(72, 40), end
            },
            WorkflowDesignerInteraction.BuildOrthogonalPath(start, end, Array.Empty<WorkflowPoint>()));
        Assert.Equal(
            new[] { start, new WorkflowPoint(60, 0), new WorkflowPoint(60, 20), new WorkflowPoint(100, 20), end },
            WorkflowDesignerInteraction.BuildOrthogonalPath(start, end, new[] { new WorkflowPoint(60, 20) }));
    }

    [Fact]
    public void NearestSideAndIsNearRect_UseRectangleEdges()
    {
        var rect = new WorkflowDesignerRect(0, 0, 100, 50);

        Assert.Equal(WorkflowPortSide.Left, WorkflowDesignerInteraction.NearestSide(rect, 2, 25));
        Assert.Equal(WorkflowPortSide.Bottom, WorkflowDesignerInteraction.NearestSide(rect, 50, 49));
        Assert.True(WorkflowDesignerInteraction.IsNearRect(rect, 140, 25, 42));
        Assert.False(WorkflowDesignerInteraction.IsNearRect(rect, 143, 25, 42));
        // 没有侧边端口行时左右锚点取节点竖直中点；有侧边行时取这些行的中点。
        Assert.Equal(new WorkflowPoint(100, 25), WorkflowDesignerInteraction.GetSideCenter(rect, WorkflowPortSide.Right, 1));
        var tall = new WorkflowDesignerRect(0, 0, 96, 120);
        Assert.Equal(new WorkflowPoint(0, 48 + 24 + 24), WorkflowDesignerInteraction.GetSideCenter(tall, WorkflowPortSide.Left, 1, new WorkflowNodeRows(1, 2, 0, 0)));
        Assert.Equal(new WorkflowPoint(50, 0), WorkflowDesignerInteraction.GetSideCenter(rect, WorkflowPortSide.Top, 1));
    }

    [Fact]
    public void HitNodeAndMarquee_UseScreenRectangles()
    {
        var (session, first, second, _) = CreateConnectedSession();
        var firstRect = WorkflowDesignerGeometry.GetNodeScreenRect(session, first);

        Assert.Same(first, WorkflowDesignerInteraction.HitNode(session, firstRect.X + 5, firstRect.Y + 5));
        Assert.Null(WorkflowDesignerInteraction.HitNode(session, -500, -500));
        Assert.Equal(
            new[] { first.Node.Id },
            WorkflowDesignerInteraction.GetNodeIdsInScreenRect(session, new WorkflowDesignerRect(firstRect.X, firstRect.Y, 10, 10)));
        Assert.Equal(
            new[] { first.Node.Id, second.Node.Id },
            WorkflowDesignerInteraction.GetNodeIdsInScreenRect(session, new WorkflowDesignerRect(-1000, -1000, 5000, 5000)));
    }

    [Fact]
    public void HitPort_FindsDefaultPortAndAlternateInputSidesOnlyWhileConnecting()
    {
        var (session, first, second, _) = CreateConnectedSession();
        var inputs = session.GetPorts(second.Node.Id, WorkflowPortDirection.Input);
        var input = inputs.Single();
        var defaultPoint = WorkflowDesignerGeometry.GetPortScreenPoint(session, second, input, inputs);
        var alternateSide = Enum.GetValues<WorkflowPortSide>().First(side => side != second.GetPortSide(input));
        var alternatePoint = WorkflowDesignerGeometry.GetPortScreenPoint(session, second, input, inputs, alternateSide);

        var hit = WorkflowDesignerInteraction.HitPort(session, defaultPoint.X, defaultPoint.Y, WorkflowPortDirection.Input);
        Assert.NotNull(hit);
        Assert.Same(second, hit.Value.Node);
        Assert.Equal(second.GetPortSide(input), hit.Value.Side);

        Assert.Null(WorkflowDesignerInteraction.HitPort(session, alternatePoint.X, alternatePoint.Y, WorkflowPortDirection.Input));
        Assert.Null(WorkflowDesignerInteraction.HitPort(
            session, alternatePoint.X, alternatePoint.Y, WorkflowPortDirection.Input, connectingFrom: second));
        var alternateHit = WorkflowDesignerInteraction.HitPort(
            session, alternatePoint.X, alternatePoint.Y, WorkflowPortDirection.Input, connectingFrom: first);
        Assert.Equal(alternateSide, alternateHit?.Side);
    }

    [Fact]
    public void ConnectionPath_IsRoutedOnGrid_AndKeepsItsShapeAtEveryZoom()
    {
        var session = new WorkflowDesignerSession(new WorkflowDocument(), new WorkflowNodeCatalog().RegisterStandardNodes());
        // 与截图相同的结构：并行节点两个分支，分支目标左右错开，中间有一个不相关节点挡路。
        var source = session.AddNode("ParallelAll", 240, 0);
        var left = session.AddNode("Delay", 0, 192);
        var right = session.AddNode("Delay", 480, 192);
        var blocker = session.AddNode("Delay", 240, 192);
        var toLeft = session.Connect(source.Node.Id, WorkflowPorts.Branch, left.Node.Id);
        var toRight = session.Connect(source.Node.Id, WorkflowPorts.Branch, right.Node.Id);
        IReadOnlyList<WorkflowPoint> CanvasPath(WorkflowConnectionModel connection) => WorkflowDesignerInteraction.GetConnectionPath(session, connection)
            .Select(point => WorkflowDesignerGeometry.ScreenToCanvas(session, point.X, point.Y)).ToArray();
        session.SetViewport(1, 24, 24);
        var normal = new[] { CanvasPath(toLeft), CanvasPath(toRight) };
        // 端点（端口）与所有拐点都在栅格线上。
        foreach (var path in normal)
            foreach (var point in path)
            {
                Assert.Equal(0, Math.Abs(point.X % WorkflowDesignerGeometry.GridSize), 6);
                Assert.Equal(0, Math.Abs(point.Y % WorkflowDesignerGeometry.GridSize), 6);
            }
        // 从同一出口分出的两条连线在同一条栅格线上拐弯。
        Assert.Equal(normal[0][1].Y, normal[1][1].Y, 6);
        foreach (var zoom in new[] { .25, .5, 2.0 })
        {
            session.SetViewport(zoom, 37, -11);
            var scaled = new[] { CanvasPath(toLeft), CanvasPath(toRight) };
            for (int i = 0; i < 2; i++)
            {
                Assert.Equal(normal[i].Count, scaled[i].Count);
                for (int k = 0; k < normal[i].Count; k++)
                {
                    Assert.Equal(normal[i][k].X, scaled[i][k].X, 6);
                    Assert.Equal(normal[i][k].Y, scaled[i][k].Y, 6);
                }
            }
        }
        _ = blocker;
    }

    [Fact]
    public void ZoomedOutDrawing_KeepsGridAndShapesProportional()
    {
        // 细线每格一条、主线每 5 格一条；缩小后先隐藏细线，主线再按 2 的幂合并，间距仍是设计栅格的整数倍。
        Assert.Equal((24d, 120d), WorkflowDesignerGeometry.GridLineSpacing(1));
        Assert.Equal(0, WorkflowDesignerGeometry.GridLineSpacing(.3).Minor);
        foreach (var zoom in new[] { 1.0, .3, .1, .05, .01 })
        {
            var (minor, major) = WorkflowDesignerGeometry.GridLineSpacing(zoom);
            Assert.True(minor == 0 || minor >= 8);
            Assert.True(major >= 8);
            var cells = major / (WorkflowDesignerGeometry.GridSize * zoom);
            Assert.Equal(0, cells % WorkflowDesignerGeometry.MajorGridCells, 6);
        }
        // 圆角随缩放等比，任何缩放下都小于节点高度的一半（不会变成胶囊）。
        foreach (var zoom in new[] { .2, .5, 1.0, 2.0 })
            Assert.True(WorkflowDesignerGeometry.NodeCornerRadius(zoom) * 2 < WorkflowDesignerGeometry.QuantizeNodeHeight(WorkflowDesignerGeometry.MinimumNodeHeight) * zoom);
        Assert.True(WorkflowDesignerGeometry.ConnectionArrow(.25).Length < WorkflowDesignerGeometry.ConnectionArrow(1).Length);
    }

    [Fact]
    public void HitConnection_InsertionAndWaypoint_FollowTheRoutedPath()
    {
        var (session, _, _, connection) = CreateConnectedSession();
        var path = WorkflowDesignerInteraction.GetConnectionPath(session, connection);
        Assert.True(path.Count >= 2);
        var middle = WorkflowDesignerInteraction.PointAlongPath(path, 0.5);

        Assert.Same(connection, WorkflowDesignerInteraction.HitConnection(session, middle.X, middle.Y));
        Assert.Null(WorkflowDesignerInteraction.HitConnection(session, middle.X, middle.Y + 500));
        var insertion = WorkflowDesignerInteraction.HitConnectionInsertion(session, middle.X, middle.Y, 14);
        Assert.Same(connection, insertion?.Connection);

        Assert.Null(WorkflowDesignerInteraction.HitWaypoint(session, middle.X, middle.Y));
        connection.Waypoints.Add(new WorkflowPoint(300, 300));
        session.SelectConnection(connection);
        var waypoint = WorkflowDesignerGeometry.CanvasToScreen(session, 300, 300);
        Assert.Equal((connection, 0), WorkflowDesignerInteraction.HitWaypoint(session, waypoint.X + 9, waypoint.Y - 9)!.Value);
        Assert.Null(WorkflowDesignerInteraction.HitWaypoint(session, waypoint.X + 10, waypoint.Y));
    }

    [Fact]
    public void PortQueries_ReportConnectionsAndDefaultSides()
    {
        var (session, first, second, connection) = CreateConnectedSession();
        var output = session.GetPorts(first.Node.Id, WorkflowPortDirection.Output).First(port => port.Key == connection.FromPort);
        var outputSide = first.GetPortSide(output);

        Assert.True(WorkflowDesignerInteraction.IsPortConnectedAtSide(session, first, output, outputSide));
        Assert.True(WorkflowDesignerInteraction.HasConnectedEndpointAtSide(session, first, outputSide));
        Assert.Equal(outputSide, WorkflowDesignerInteraction.FindPortSide(
            session, first.Node.Id, output.Key, WorkflowPortDirection.Output));
        Assert.Equal(WorkflowPortSide.Left, WorkflowDesignerInteraction.FindPortSide(
            session, "missing", "In", WorkflowPortDirection.Input));
        Assert.Equal(WorkflowPortSide.Right, WorkflowDesignerInteraction.FindPortSide(
            session, second.Node.Id, "missing", WorkflowPortDirection.Output));
        Assert.Null(WorkflowDesignerInteraction.FindPortPoint(session, "missing", "In", WorkflowPortDirection.Input));
        Assert.Empty(WorkflowDesignerInteraction.GetSideOverrideEndpoints(session, first, WorkflowPortDirection.Output));

        var overrideSide = Enum.GetValues<WorkflowPortSide>().First(side => side != outputSide);
        connection.FromSide = overrideSide;
        Assert.Equal(
            new[] { (output.Key, overrideSide) },
            WorkflowDesignerInteraction.GetSideOverrideEndpoints(session, first, WorkflowPortDirection.Output)
                .Select(endpoint => (endpoint.Port.Key, endpoint.Side)));
    }

    [Fact]
    public void NodeDrag_UpdatesLiveThenCommitsOneUndoableMove()
    {
        var (session, first, second, connection) = CreateConnectedSession();
        connection.Waypoints.Add(new WorkflowPoint(260, 30));
        session.SelectNodes(new[] { first.Node.Id, second.Node.Id });
        var origin = new WorkflowPoint(first.X, first.Y);

        var drag = new WorkflowNodeDragOperation(session, new WorkflowPoint(0, 0));
        drag.Update(new WorkflowPoint(48, 24));
        Assert.Equal(origin.X + 48, first.X);
        Assert.Equal(new WorkflowPoint(308, 54), connection.Waypoints.Single());

        drag.Commit();
        Assert.Equal(origin.X + 48, first.X);
        Assert.Equal(origin.Y + 24, first.Y);
        Assert.True(session.Undo());
        Assert.Equal(origin, new WorkflowPoint(first.X, first.Y));
    }

    [Fact]
    public void NodeDrag_CancelRestoresNodesAndClearedWaypoints()
    {
        var (session, first, _, connection) = CreateConnectedSession();
        connection.Waypoints.Add(new WorkflowPoint(260, 30));
        session.SelectNodes(new[] { first.Node.Id });
        var origin = new WorkflowPoint(first.X, first.Y);

        var drag = new WorkflowNodeDragOperation(session, new WorkflowPoint(0, 0));
        drag.Update(new WorkflowPoint(96, 0));
        Assert.Empty(connection.Waypoints);

        drag.Cancel();
        Assert.Equal(origin, new WorkflowPoint(first.X, first.Y));
        Assert.Equal(new WorkflowPoint(260, 30), connection.Waypoints.Single());
    }

    private static (WorkflowDesignerSession Session, WorkflowCanvasNode First, WorkflowCanvasNode Second,
        WorkflowConnectionModel Connection) CreateConnectedSession()
    {
        var session = new WorkflowDesignerSession(
            new WorkflowDocument { Name = "Interaction" },
            new WorkflowNodeCatalog().RegisterStandardNodes());
        session.SnapToGrid = false;
        session.SetViewport(1, 0, 0);
        var first = session.AddNode("Action", 0, 0);
        var second = session.AddNode("Action", 480, 0);
        var connection = session.Connect(first.Node.Id, WorkflowPorts.Success, second.Node.Id);
        return (session, first, second, connection);
    }
}
