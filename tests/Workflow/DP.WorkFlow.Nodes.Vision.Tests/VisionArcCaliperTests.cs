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

    // 暗背景(20)上一条亮竖条(220)：x∈[40, 52)，宽 12px。
    private static ImageFrame BrightStripe()
    {
        var pixels = new byte[100 * 100];
        for (var y = 0; y < 100; y++)
            for (var x = 0; x < 100; x++) pixels[y * 100 + x] = (byte)(x is >= 40 and < 52 ? 220 : 20);
        using var image = VisionImage.CopyFrom(new ImageInfo(100, 100, EPixelLayout.Gray8), pixels);
        return new ImageFrame("stripe-frame", image);
    }

    [Fact]
    public void EdgePairing_PairsAdjacentOppositeEdgesWithinWidthRange()
    {
        // 距离 10(+) 20(−) 30(+) 31(+) 45(−) 60(−)
        double[] distances = { 10, 20, 30, 31, 45, 60 }, gradients = { 5, -5, 5, 6, -6, -5 };
        Assert.Equal(new[] { (0, 1), (2, 4) }, VisionEdgePairing.Pair(distances, gradients, ECaliperPolarity.Rising, 0, 100));
        // 宽度至少 12：10→20 太窄，跳到下一个相反边 45（宽 35）。
        Assert.Equal(new[] { (0, 4) }, VisionEdgePairing.Pair(distances, gradients, ECaliperPolarity.Rising, 12, 100));
        // 第一个边缘为亮→暗：20→30。
        Assert.Equal((1, 2), VisionEdgePairing.Pair(distances, gradients, ECaliperPolarity.Falling, 0, 100)[0]);
        Assert.Empty(VisionEdgePairing.Pair(distances, gradients, ECaliperPolarity.Rising, 0, 5));
    }

    [Fact]
    public void LineCaliper_PairModeMeasuresStripeWidth()
    {
        using var frame = BrightStripe();
        var raw = VisionLineCaliper.Measure(frame, new CaliperOptions(new PointD(20, 50.5), new PointD(80, 50.5), 3), 1,
            options => new CaliperMeasurer().Measure(frame, options));
        var result = raw.WithEdgeMode(EVisionCaliperEdgeMode.Pair, ECaliperPolarity.Rising, 5, 30);

        var pair = Assert.Single(result.Pairs);
        Assert.InRange(pair.Width, 11.5, 12.5);
        Assert.InRange(pair.Midpoint.X, 45.5, 46.5);
        Assert.Equal(50.5, pair.Midpoint.Y, 6);
        Assert.True(pair.First.Gradient > 0 && pair.Second.Gradient < 0);
        Assert.Equal(pair.Width, result.Width);
        Assert.Contains("边缘对 1，宽度", result.Summary);
        // 宽度上限 10 时这一对被排除。
        Assert.Empty(raw.WithEdgeMode(EVisionCaliperEdgeMode.Pair, ECaliperPolarity.Rising, 0, 10).Pairs);
        // 单边缘模式不配对。
        Assert.Empty(raw.WithEdgeMode(EVisionCaliperEdgeMode.Single, ECaliperPolarity.Rising, 0, 100).Pairs);
    }

    [Fact]
    public void LineCaliper_ScanStepChangesProfileSampling()
    {
        using var frame = BrightStripe();
        var result = VisionLineCaliper.Measure(frame, new CaliperOptions(new PointD(20, 50.5), new PointD(80, 50.5), 2, polarity: ECaliperPolarity.Rising), 2,
            options => new CaliperMeasurer().Measure(frame, options));

        Assert.Equal(31, result.Profile.Count);       // 60px ÷ 2px + 1。
        Assert.Equal(2, result.SampleStep, 9);
        Assert.Null(result.Line);                     // 非 1px 间隔走托管采样。
        Assert.InRange(Assert.Single(result.Edges).Position.X, 39, 41);
    }

    [Fact]
    public void ArcCaliper_PairModeMeasuresRingWidthAlongArc()
    {
        using var frame = BrightStripe();
        // 圆心 (46,90)，半径 40：沿圆弧从 225° 顺时针扫到 315°（经过顶部），穿过亮竖条。
        var raw = VisionArcCaliper.Measure(frame, new VisionArcCaliperOptions(new PointD(46, 90), 40, 225, 90, halfWidth: 2));
        var result = raw.WithEdgeMode(EVisionCaliperEdgeMode.Pair, ECaliperPolarity.Rising, 0, 100);
        var pair = Assert.Single(result.Pairs);
        Assert.InRange(pair.Width, 11, 13.5);                  // 弧长约等于条宽（顶部附近近似水平）。
        Assert.InRange(pair.Midpoint.X, 45, 47);
        Assert.InRange(Math.Sqrt(Math.Pow(pair.Midpoint.X - 46, 2) + Math.Pow(pair.Midpoint.Y - 90, 2)), 39.99, 40.01);
    }

    [Fact]
    public void FindLine_CaliperScansFollowSearchBox()
    {
        var node = new FindVisionLineNodeModel
        {
            CaliperCount = 4, HalfWidth = 2, BandSampleStep = 1,
            Regions = new() { new() { Id = "box", CenterX = 50, CenterY = 50, Width = 80, Height = 20, Angle = 0 } }
        };
        var scans = node.CaliperScans();
        Assert.Equal(4, scans.Count);
        // 沿框宽度（X）均匀排布，每把沿高度方向（Y，从上到下）扫描。
        Assert.Equal(new[] { 20d, 40, 60, 80 }, scans.Select(s => Math.Round(s.Start.X, 6)));
        Assert.All(scans, s => Assert.Equal((40d, 60d, 2d), (Math.Round(s.Start.Y, 6), Math.Round(s.End.Y, 6), s.HalfBand)));
        node.ReverseScan = true;
        Assert.All(node.CaliperScans(), s => Assert.Equal(60, s.Start.Y, 6));
        node.EdgeMode = EVisionCaliperEdgeMode.Pair; node.MinimumPairWidth = 5; node.MaximumPairWidth = 2;
        Assert.Contains(node.ValidateConfiguration(), e => e.Contains("边缘对宽度", StringComparison.Ordinal));
    }

    [Fact]
    public void ArcCaliperOptions_RejectInvalidBands()
    {
        Assert.Throws<ArgumentException>(() => new VisionArcCaliperOptions(new PointD(50, 50), 0, 0, 90));
        Assert.Throws<ArgumentException>(() => new VisionArcCaliperOptions(new PointD(50, 50), 30, 0, 0));
        Assert.Throws<ArgumentException>(() => new VisionArcCaliperOptions(new PointD(50, 50), 30, 0, 400));
        Assert.Throws<ArgumentException>(() => new VisionArcCaliperOptions(new PointD(50, 50), 5, 0, 90, halfWidth: 10));
        Assert.Throws<ArgumentException>(() => new VisionArcCaliperOptions(new PointD(50, 50), 30, 0, 90, scanStep: 0));
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
