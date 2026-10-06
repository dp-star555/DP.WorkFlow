using DP.Vision;
using DP.Vision.Algorithms;
using DP.WorkFlow.Vision.UI;

namespace DP.WorkFlow.Tests;

public sealed class VisionFindShapeGizmoTests
{
    private static FindVisionLineNodeModel Line() => new()
    {
        CaliperCount = 5, HalfWidth = 2, BandSampleStep = 1,
        Regions = new() { new() { Id = "search", Shape = EWorkflowVisionRoiShape.Rectangle, CenterX = 100, CenterY = 100, Width = 100, Height = 40 } }
    };

    [Fact]
    public void FindLine_ShowsExpectedLineCalipersAndArrows_WithoutLabels()
    {
        var gizmo = new VisionFindShapeGizmo(Line());
        var visuals = gizmo.Visuals(1);

        var expected = Assert.IsType<ContourGeometry>(Assert.Single(visuals, v => v.Id == "find-expected").Geometry).Points;
        Assert.Equal((50d, 100d, 150d, 100d), (expected[0].X, expected[0].Y, expected[1].X, expected[1].Y));
        Assert.Equal(5, visuals.Count(v => v.Id.StartsWith("find-caliper", StringComparison.Ordinal)));
        Assert.Equal(5, visuals.Count(v => v.Id.StartsWith("find-arrow", StringComparison.Ordinal)));
        // 箭头尖在每把卡尺的扫描终点（框下边，从上到下扫描）。
        Assert.All(visuals.Where(v => v.Id.StartsWith("find-arrow", StringComparison.Ordinal)),
            v => Assert.Equal(120, Assert.IsType<ContourGeometry>(v.Geometry).Points[1].Y, 6));
        Assert.DoesNotContain(visuals, v => v.Caption is not null);
        Assert.Contains("5 把卡尺", gizmo.Caption);
    }

    [Fact]
    public void FindLine_DraggingWritesSearchBox()
    {
        var node = Line();
        var gizmo = new VisionFindShapeGizmo(node);

        // 终点拖到 (100,150)：期望线变为 (50,100)→(100,150)。
        Assert.Equal(EVisionCaliperHandle.End, gizmo.Hit(new PointD(151, 100), 1));
        gizmo.BeginDrag(EVisionCaliperHandle.End, new PointD(150, 100), 1);
        gizmo.Drag(new PointD(100, 150));
        Assert.True(gizmo.EndDrag());
        var box = node.Regions[0];
        Assert.Equal((75d, 125d), (box.CenterX, box.CenterY));
        Assert.Equal(Math.Round(Math.Sqrt(2) * 50, 2), box.Width);
        Assert.Equal(Math.PI / 4, box.Angle, 6);

        // 搜索长度：拖到离期望线 30px → 高 60。
        var v = (-Math.Sin(Math.PI / 4), Math.Cos(Math.PI / 4));
        var lengthHandle = new PointD(75 + v.Item1 * 20, 125 + v.Item2 * 20);
        Assert.Equal(EVisionCaliperHandle.Length, gizmo.Hit(lengthHandle, 1));
        gizmo.BeginDrag(EVisionCaliperHandle.Length, lengthHandle, 1);
        gizmo.Drag(new PointD(75 + v.Item1 * 30, 125 + v.Item2 * 30));
        gizmo.EndDrag();
        Assert.Equal(60, box.Height, 6);

        // 整体平移。
        gizmo.BeginDrag(EVisionCaliperHandle.Body, new PointD(75, 125), 1);
        gizmo.Drag(new PointD(85, 120));
        gizmo.EndDrag();
        Assert.Equal((85d, 120d), (box.CenterX, box.CenterY));

        // 卡尺宽度：拖到离中间卡尺中心线 6px → 半宽 6。
        gizmo.BeginDrag(EVisionCaliperHandle.Width, new PointD(85, 120), 1);
        var u = (Math.Cos(Math.PI / 4), Math.Sin(Math.PI / 4));
        gizmo.Drag(new PointD(85 + u.Item1 * 6, 120 + u.Item2 * 6));
        gizmo.EndDrag();
        Assert.Equal((6, 1d), (node.HalfWidth, node.BandSampleStep));
    }

    [Fact]
    public void FindCircle_ShowsRadialCalipers_AndDragsRadiusLengthAndAngles()
    {
        var node = new FindVisionCircleNodeModel
        {
            CaliperCount = 4, SearchLength = 20, StartAngle = 0, SweepAngle = 180,
            Regions = new() { new() { Id = "search", Shape = EWorkflowVisionRoiShape.Ellipse, CenterX = 100, CenterY = 100, Width = 80, Height = 80 } }
        };
        var gizmo = new VisionFindShapeGizmo(node);
        var visuals = gizmo.Visuals(1);
        Assert.Equal(4, visuals.Count(v => v.Id.StartsWith("find-caliper", StringComparison.Ordinal)));
        // 由内向外：箭头尖在外圈（半径 50）。
        Assert.All(visuals.Where(v => v.Id.StartsWith("find-arrow", StringComparison.Ordinal)), v =>
        {
            var tip = Assert.IsType<ContourGeometry>(v.Geometry).Points[1];
            Assert.Equal(50, Math.Sqrt(Math.Pow(tip.X - 100, 2) + Math.Pow(tip.Y - 100, 2)), 6);
        });
        Assert.Contains(visuals, v => v.Id == "find-end");

        // 半径把手在扫描中点（90°，正下方）。
        Assert.Equal(EVisionCaliperHandle.Radius, gizmo.Hit(new PointD(100, 140), 1));
        gizmo.BeginDrag(EVisionCaliperHandle.Radius, new PointD(100, 140), 1);
        gizmo.Drag(new PointD(100, 160));
        gizmo.EndDrag();
        Assert.Equal((120d, 120d), (node.Regions[0].Width, node.Regions[0].Height));

        Assert.Equal(EVisionCaliperHandle.Length, gizmo.Hit(new PointD(100, 170), 1));
        gizmo.BeginDrag(EVisionCaliperHandle.Length, new PointD(100, 170), 1);
        gizmo.Drag(new PointD(100, 175));
        gizmo.EndDrag();
        Assert.Equal(30, node.SearchLength, 6);

        // 终点（180°，左侧）拖到 270°（正上方）：扫描 270°。
        gizmo.BeginDrag(EVisionCaliperHandle.End, new PointD(40, 100), 1);
        gizmo.Drag(new PointD(100, 30));
        gizmo.EndDrag();
        Assert.Equal(270, node.SweepAngle, 6);
    }

    [Fact]
    public void FindLine_WithCoordinates_DisplaysAndWritesLocalValues()
    {
        // 局部→原图：尺度 2、平移 (10, 20)。
        var system = new VisionCoordinateSystem(new VisionCoordinateDefinition("work", "工件"), "frame", 400, 400,
            CoordinateMatrix2D.FromAffine(2, 0, 10, 0, 2, 20));
        var node = Line();
        node.Coordinates = new WorkflowVisionCoordinateBinding { CoordinateSystemId = "work" };
        var gizmo = new VisionFindShapeGizmo(node);
        Assert.False(gizmo.IsEditable);
        gizmo.Coordinates = system;
        Assert.True(gizmo.IsEditable);
        var expected = Assert.IsType<ContourGeometry>(Assert.Single(gizmo.Visuals(1), v => v.Id == "find-expected").Geometry).Points;
        Assert.Equal((110d, 220d, 310d, 220d), (expected[0].X, expected[0].Y, expected[1].X, expected[1].Y));
        gizmo.BeginDrag(EVisionCaliperHandle.Body, new PointD(210, 220), 1);
        gizmo.Drag(new PointD(230, 220));
        gizmo.EndDrag();
        Assert.Equal((110d, 100d), (node.Regions[0].CenterX, node.Regions[0].CenterY));
    }

    [Fact]
    public void FindLine_WithoutSearchBox_IsNotEditable()
    {
        var node = Line();
        node.Regions.Clear();
        var gizmo = new VisionFindShapeGizmo(node);
        Assert.False(gizmo.IsEditable);
        Assert.Empty(gizmo.Visuals(1));
        Assert.Contains("先运行一次流程", gizmo.Hint);
        Assert.True(node.FitPlaceholderSearchRoi(640, 480));
        Assert.True(gizmo.IsEditable);
    }
}
