using DP.Vision;
using DP.Vision.Algorithms;

namespace DP.WorkFlow.Tests;

public sealed class VisionArcCaliperTests
{
    // 左半暗(0)右半亮(200)：以 (50,50) 为圆心、半径 30，从 180°（左）顺时针扫 180°（经过顶部到右），
    // 在顶部 x=50 处（角度 270°）穿过暗→亮边缘。
    private static ImageFrame HalfBright()
    {
        var pixels = new byte[100 * 100];
        for (var y = 0; y < 100; y++)
            for (var x = 50; x < 100; x++) pixels[y * 100 + x] = 200;
        using var image = VisionImage.CopyFrom(new ImageInfo(100, 100, EPixelLayout.Gray8), pixels);
        return new ImageFrame("arc-frame", image);
    }

    [Fact]
    public void ArcCaliper_FindsEdgeAlongArc_WithAngleAndArcLength()
    {
        using var frame = HalfBright();
        var result = VisionArcCaliper.Measure(frame, new VisionArcCaliperOptions(new PointD(50, 50), 30, 180, 180, halfWidth: 3, polarity: ECaliperPolarity.Rising));

        Assert.Equal(EVisionCaliperShape.Arc, result.Shape);
        Assert.Equal("arc-frame", result.FrameId);
        var edge = Assert.Single(result.Edges);
        Assert.True(edge.Gradient > 0);
        Assert.InRange(edge.Position.X, 49.4, 50.6);
        Assert.InRange(edge.Position.Y, 19, 21);
        Assert.InRange(edge.AngleDegrees!.Value, 268.5, 271.5);
        Assert.InRange(edge.Distance, Math.PI * 30 / 2 - 1, Math.PI * 30 / 2 + 1);
        Assert.Equal(30d, result.Radius);
        Assert.InRange(result.Start.X, 19.99, 20.01);
        Assert.InRange(result.End.X, 79.99, 80.01);

        // 从 0°（右）顺时针经过底部到左：从亮扫到暗，底部 x=50 处为亮→暗边缘。
        var reverse = VisionArcCaliper.Measure(frame, new VisionArcCaliperOptions(new PointD(50, 50), 30, 0, 180, polarity: ECaliperPolarity.Falling));
        Assert.InRange(Assert.Single(reverse.Edges).Position.Y, 79, 81);
    }

    // 暗背景(20)上半径 20 的亮圆盘(220)，圆心 (50,50)。
    private static ImageFrame BrightDisc()
    {
        var pixels = new byte[100 * 100];
        for (var y = 0; y < 100; y++)
            for (var x = 0; x < 100; x++)
                pixels[y * 100 + x] = (byte)((x + .5 - 50) * (x + .5 - 50) + (y + .5 - 50) * (y + .5 - 50) <= 400 ? 220 : 20);
        using var image = VisionImage.CopyFrom(new ImageInfo(100, 100, EPixelLayout.Gray8), pixels);
        return new ImageFrame("disc-frame", image);
    }

    [Theory]
    [InlineData(EVisionArcScanDirection.InnerToOuter, ECaliperPolarity.Falling)]
    [InlineData(EVisionArcScanDirection.OuterToInner, ECaliperPolarity.Rising)]
    public void RadialArcCaliper_FindsCircleEdgeOnEveryCaliper(EVisionArcScanDirection direction, ECaliperPolarity polarity)
    {
        using var frame = BrightDisc();
        var result = VisionArcCaliper.Measure(frame, new VisionArcCaliperOptions(new PointD(50, 50), 20, 0, 360, halfWidth: 8,
            polarity: polarity, direction: direction, caliperCount: 6));

        Assert.Equal(direction, result.ArcDirection);
        Assert.Equal(6, result.Profiles.Count);
        Assert.Equal(6, result.Count);
        Assert.Equal(Enumerable.Range(0, 6), result.Edges.Select(e => e.CaliperIndex));
        foreach (var edge in result.Edges)
        {
            var radius = Math.Sqrt(Math.Pow(edge.Position.X - 50, 2) + Math.Pow(edge.Position.Y - 50, 2));
            Assert.InRange(radius, 19, 21);
            // 距离从搜索起点算：由内到外从半径 12 起，由外到内从半径 28 起，都约 8px。
            Assert.InRange(edge.Distance, 7, 9);
        }
        Assert.Equal(30, result.Edges[0].AngleDegrees!.Value, 6);
        Assert.Contains(direction == EVisionArcScanDirection.InnerToOuter ? "由内到外" : "由外到内", result.Summary);

        // 极性相对搜索方向：方向反了就找不到。
        var wrong = VisionArcCaliper.Measure(frame, new VisionArcCaliperOptions(new PointD(50, 50), 20, 0, 360, halfWidth: 8,
            polarity: polarity == ECaliperPolarity.Rising ? ECaliperPolarity.Falling : ECaliperPolarity.Rising, direction: direction, caliperCount: 6));
        Assert.Empty(wrong.Edges);
    }

    [Fact]
    public void ArcCaliperOptions_RejectInvalidBands()
    {
        Assert.Throws<ArgumentException>(() => new VisionArcCaliperOptions(new PointD(50, 50), 0, 0, 90));
        Assert.Throws<ArgumentException>(() => new VisionArcCaliperOptions(new PointD(50, 50), 30, 0, 0));
        Assert.Throws<ArgumentException>(() => new VisionArcCaliperOptions(new PointD(50, 50), 30, 0, 400));
        Assert.Throws<ArgumentException>(() => new VisionArcCaliperOptions(new PointD(50, 50), 5, 0, 90, halfWidth: 10));
        Assert.Throws<ArgumentException>(() => new VisionArcCaliperOptions(new PointD(50, 50), 30, 0, 90, halfWidth: 1, direction: EVisionArcScanDirection.InnerToOuter));
        Assert.Throws<ArgumentException>(() => new VisionArcCaliperOptions(new PointD(50, 50), 30, 0, 90, halfWidth: 4, direction: EVisionArcScanDirection.InnerToOuter, caliperCount: 0));
        using var frame = HalfBright();
        Assert.Throws<ArgumentException>(() => VisionArcCaliper.Measure(frame, new VisionArcCaliperOptions(new PointD(50, 50), 60, 0, 90)));
    }

    [Fact]
    public void CaliperNode_ValidatesShapeSpecificParameters()
    {
        var node = new MeasureVisionCaliperNodeModel { Shape = EVisionCaliperShape.Arc, CenterX = 50, CenterY = 50, Radius = 30, StartAngle = 0, SweepAngle = 90 };
        Assert.DoesNotContain(node.ValidateConfiguration(), e => e.Contains("圆弧", StringComparison.Ordinal));
        node.SweepAngle = 0;
        Assert.Contains(node.ValidateConfiguration(), e => e.Contains("圆弧", StringComparison.Ordinal));
    }

    [Fact]
    public void LineMeasurement_KeepsCaliperMemberNames()
    {
        using var frame = HalfBright();
        var line = new CaliperMeasurer().Measure(frame, new CaliperOptions(new PointD(30, 50.5), new PointD(70, 50.5), polarity: ECaliperPolarity.Rising));
        var measurement = VisionCaliperMeasurement.FromLine(line);

        Assert.Equal(EVisionCaliperShape.Line, measurement.Shape);
        Assert.Equal(line.Count, measurement.Count);
        Assert.Equal(line.Edges[0].Position, measurement.Edges[0].Position);
        Assert.Equal(line.Profile, measurement.Profile);
        Assert.Null(measurement.Radius);
        foreach (var member in new[] { "Start", "End", "Profile", "Edges", "Count", "MeasuredEdges", "LocatedEdges", "SampleStep", "FrameId" })
            Assert.NotNull(typeof(VisionCaliperMeasurement).GetProperty(member));
    }
}
