using DP.WorkFlow.UI;

namespace DP.WorkFlow.Tests;

public sealed class WorkflowOrthogonalRouterTests
{
    [Fact]
    public void Route_AvoidsObstacleUsingNearbyShortestChannel()
    {
        var obstacle = new WorkflowDesignerRect(80, -20, 40, 40);

        var route = WorkflowOrthogonalRouter.Route(
            new WorkflowPoint(0, 0),
            new WorkflowPoint(200, 0),
            WorkflowPortSide.Right,
            WorkflowPortSide.Left,
            Array.Empty<WorkflowPoint>(),
            new[] { obstacle });

        Assert.DoesNotContain(route.Zip(route.Skip(1)), pair => CrossesInterior(pair.First, pair.Second, obstacle));
        Assert.True(route.Max(point => Math.Abs(point.Y)) <= 20.01);
        Assert.True(route.Count <= 6);
    }

    [Fact]
    public void Route_RemovesRedundantCollinearPoints()
    {
        var route = WorkflowOrthogonalRouter.Route(
            new WorkflowPoint(0, 0),
            new WorkflowPoint(200, 0),
            WorkflowPortSide.Right,
            WorkflowPortSide.Left,
            Array.Empty<WorkflowPoint>(),
            Array.Empty<WorkflowDesignerRect>());

        Assert.Equal(new[] { new WorkflowPoint(0, 0), new WorkflowPoint(200, 0) }, route);
    }

    [Fact]
    public void Route_LargeGraphObstacleField_RemainsOrthogonalAndAvoidsEveryNode()
    {
        var obstacles = Enumerable.Range(0, 80)
            .Select(index => new WorkflowDesignerRect(60 + index % 10 * 72, -180 + index / 10 * 52, 44, 30))
            .ToArray();

        var route = WorkflowOrthogonalRouter.Route(
            new WorkflowPoint(0, 0),
            new WorkflowPoint(840, 220),
            WorkflowPortSide.Bottom,
            WorkflowPortSide.Top,
            Array.Empty<WorkflowPoint>(),
            obstacles);

        Assert.All(route.Zip(route.Skip(1)), pair => Assert.True(
            Math.Abs(pair.First.X - pair.Second.X) < 0.01 || Math.Abs(pair.First.Y - pair.Second.Y) < 0.01));
        foreach (var obstacle in obstacles)
            Assert.DoesNotContain(route.Zip(route.Skip(1)), pair => CrossesInterior(pair.First, pair.Second, obstacle));
    }

    [Fact]
    public void Route_FeedbackLoop_UsesManualWaypointWithoutDiagonalSegments()
    {
        var waypoint = new WorkflowPoint(-80, 140);
        var obstacle = new WorkflowDesignerRect(0, 0, 180, 84);
        var route = WorkflowOrthogonalRouter.Route(
            new WorkflowPoint(90, 84),
            new WorkflowPoint(90, 0),
            WorkflowPortSide.Bottom,
            WorkflowPortSide.Top,
            new[] { waypoint },
            new[] { obstacle });

        Assert.Contains(waypoint, route);
        Assert.All(route.Zip(route.Skip(1)), pair => Assert.True(
            Math.Abs(pair.First.X - pair.Second.X) < 0.01 || Math.Abs(pair.First.Y - pair.Second.Y) < 0.01));
    }

    private static bool CrossesInterior(WorkflowPoint first, WorkflowPoint second, WorkflowDesignerRect rect)
    {
        if (Math.Abs(first.Y - second.Y) < 0.01)
            return first.Y > rect.Y && first.Y < rect.Y + rect.Height
                && Math.Max(first.X, second.X) > rect.X
                && Math.Min(first.X, second.X) < rect.X + rect.Width;
        return first.X > rect.X && first.X < rect.X + rect.Width
            && Math.Max(first.Y, second.Y) > rect.Y
            && Math.Min(first.Y, second.Y) < rect.Y + rect.Height;
    }
}
