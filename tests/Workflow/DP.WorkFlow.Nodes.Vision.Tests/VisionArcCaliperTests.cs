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

    [Fact]
    public void ArcCaliperOptions_RejectInvalidBands()
    {
        Assert.Throws<ArgumentException>(() => new VisionArcCaliperOptions(new PointD(50, 50), 0, 0, 90));
        Assert.Throws<ArgumentException>(() => new VisionArcCaliperOptions(new PointD(50, 50), 30, 0, 0));
        Assert.Throws<ArgumentException>(() => new VisionArcCaliperOptions(new PointD(50, 50), 30, 0, 400));
        Assert.Throws<ArgumentException>(() => new VisionArcCaliperOptions(new PointD(50, 50), 5, 0, 90, halfWidth: 10));
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
