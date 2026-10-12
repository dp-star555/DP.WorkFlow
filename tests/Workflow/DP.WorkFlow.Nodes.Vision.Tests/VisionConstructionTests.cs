using DP.Vision;
using DP.Vision.Algorithms;

namespace DP.WorkFlow.Tests;

public sealed class VisionConstructionTests
{
    private const string Frame = "construction";

    private static VisionLine Line(double x1, double y1, double x2, double y2) =>
        new(new VisionPoint(Frame, new PointD(x1, y1)), new VisionPoint(Frame, new PointD(x2, y2)));

    // 用圆上 36 个精确点拟合出圆，得到与找圆输出相同类型的圆。
    private static RobustCircleResult Circle(double cx, double cy, double r) => new RobustCircleFitter().Fit(Frame,
        Enumerable.Range(0, 36).Select(i => new PointD(cx + r * Math.Cos(i * Math.PI / 18), cy + r * Math.Sin(i * Math.PI / 18))).ToArray(), .01);

    [Fact]
    public void IntersectLines_UsesExtensions_AndRejectsNearlyParallel()
    {
        // 两条线段本身不相交，延长线交于 (5, 0)。
        var result = VisionConstruction.IntersectLines(Line(0, 0, 3, 0), Line(5, 5, 5, 10));
        Assert.Equal(5, result.X, 9); Assert.Equal(0, result.Y, 9);
        Assert.Equal(90, result.AngleDegrees, 9);
        Assert.Equal(Frame, result.Point.FrameId);
        var error = Assert.Throws<InvalidOperationException>(() => VisionConstruction.IntersectLines(Line(0, 0, 100, 0), Line(0, 5, 100, 5.05)));
        Assert.Contains("平行", error.Message);
        Assert.Throws<InvalidOperationException>(() => VisionConstruction.IntersectLines(Line(0, 0, 100, 0), new VisionLine(
            new VisionPoint("other", new PointD(0, 0)), new VisionPoint("other", new PointD(0, 9)))));
    }

    [Fact]
    public void MeasureAngle_ReportsAcuteDirectionAndSignedAngles()
    {
        // 图像 Y 向下：(1,1) 方向在 X 轴顺时针 45° 处。
        var down = VisionConstruction.MeasureAngle(Line(0, 0, 10, 0), Line(0, 0, 10, 10), EVisionLineAngleMode.Signed);
        Assert.Equal(45, down.SignedDegrees, 9); Assert.Equal(45, down.DirectionDegrees, 9); Assert.Equal(45, down.AcuteDegrees, 9);
        Assert.Equal(45, down.AngleDegrees, 9);
        var reversed = VisionConstruction.MeasureAngle(Line(0, 0, 10, 0), Line(10, 10, 0, 0), EVisionLineAngleMode.Direction);
        Assert.Equal(-135, reversed.SignedDegrees, 9); Assert.Equal(135, reversed.AngleDegrees, 9); Assert.Equal(45, reversed.AcuteDegrees, 9);
        Assert.NotNull(reversed.Intersection);
        var parallel = VisionConstruction.MeasureAngle(Line(0, 0, 10, 0), Line(0, 5, 10, 5), EVisionLineAngleMode.Acute);
        Assert.Equal(0, parallel.AngleDegrees, 9);
        Assert.Null(parallel.Intersection);
    }

    [Fact]
    public void IntersectLineCircle_OrdersAlongLine_DetectsTangentAndMiss()
    {
        var circle = Circle(50, 50, 10);
        var cut = VisionConstruction.IntersectLineCircle(Line(0, 50, 20, 50), circle);
        Assert.Equal(2, cut.Count);
        Assert.Equal(40, cut.First.ImagePosition.X, 6); Assert.Equal(60, cut.Second.ImagePosition.X, 6);
        // 反向直线：顺序随直线方向反转。
        Assert.Equal(60, VisionConstruction.IntersectLineCircle(Line(100, 50, 80, 50), circle).First.ImagePosition.X, 6);
        var tangent = VisionConstruction.IntersectLineCircle(Line(0, 40, 10, 40), circle);
        Assert.True(tangent.Tangent);
        Assert.Equal(50, tangent.First.ImagePosition.X, 6); Assert.Equal(40, tangent.First.ImagePosition.Y, 6);
        Assert.Contains("不相交", Assert.Throws<InvalidOperationException>(() => VisionConstruction.IntersectLineCircle(Line(0, 30, 10, 30), circle)).Message);
    }

    [Fact]
    public void IntersectCircles_FirstPointIsOnTheRightLookingFromFirstCenter()
    {
        // 圆心 (0,0) 与 (8,0)，半径都为 5：交点 (4, ±3)；从圆1看向圆2（向右），右侧在图像下方（Y 正）。
        var result = VisionConstruction.IntersectCircles(Circle(0, 0, 5), Circle(8, 0, 5));
        Assert.Equal(2, result.Count);
        Assert.Equal(4, result.First.ImagePosition.X, 6); Assert.Equal(3, result.First.ImagePosition.Y, 6);
        Assert.Equal(4, result.Second.ImagePosition.X, 6); Assert.Equal(-3, result.Second.ImagePosition.Y, 6);
        Assert.True(VisionConstruction.IntersectCircles(Circle(0, 0, 5), Circle(10, 0, 5)).Tangent);
        Assert.Contains("分离", Assert.Throws<InvalidOperationException>(() => VisionConstruction.IntersectCircles(Circle(0, 0, 5), Circle(20, 0, 5))).Message);
        Assert.Contains("圆内", Assert.Throws<InvalidOperationException>(() => VisionConstruction.IntersectCircles(Circle(0, 0, 10), Circle(1, 0, 2))).Message);
        Assert.Contains("同心", Assert.Throws<InvalidOperationException>(() => VisionConstruction.IntersectCircles(Circle(3, 3, 10), Circle(3, 3, 2))).Message);
    }

    [Fact]
    public void Project_ReturnsFootOnLineOrClampedToSegment()
    {
        var infinite = VisionConstruction.Project(new VisionPoint(Frame, new PointD(15, 7)), Line(0, 0, 10, 0), EVisionLineDistanceMode.InfiniteLines);
        Assert.Equal(15, infinite.X, 9); Assert.Equal(0, infinite.Y, 9); Assert.Equal(7, infinite.Distance, 9);
        var segment = VisionConstruction.Project(new VisionPoint(Frame, new PointD(15, 7)), Line(0, 0, 10, 0), EVisionLineDistanceMode.Segments);
        Assert.Equal(10, segment.X, 9); Assert.Equal(Math.Sqrt(25 + 49), segment.Distance, 9);
    }
}
