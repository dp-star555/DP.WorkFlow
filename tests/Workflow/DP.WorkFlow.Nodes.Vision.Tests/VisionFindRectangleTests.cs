using DP.Vision;
using DP.Vision.Algorithms;

namespace DP.WorkFlow.Tests;

/// <summary>找矩形：四边卡尺分别拟合直线，相邻边求交得到角点、中心、宽高与角度。</summary>
public sealed class VisionFindRectangleTests
{
    // 亮背景上的暗矩形：中心 (150,120)，宽 140、高 90，宽度方向顺时针 10°。
    private static ImageFrame DarkRectangle()
    {
        const int width = 300, height = 240;
        double angle = 10 * Math.PI / 180, cos = Math.Cos(angle), sin = Math.Sin(angle);
        var pixels = new byte[width * height];
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                double dx = x + .5 - 150, dy = y + .5 - 120, u = dx * cos + dy * sin, v = -dx * sin + dy * cos;
                pixels[y * width + x] = Math.Abs(u) <= 70 && Math.Abs(v) <= 45 ? (byte)20 : (byte)220;
            }
        using var image = VisionImage.CopyFrom(new ImageInfo(width, height, EPixelLayout.Gray8), pixels);
        return new ImageFrame("rectangle", image);
    }

    private static FindVisionRectangleNodeModel Node() => new()
    {
        Id = "rect", Frame = WorkflowInput<ImageFrame>.FromBinding(new WorkflowBindingKey("file", "$")), CaliperCount = 8, HalfWidth = 3, SearchLength = 24, Polarity = ECaliperPolarity.Rising,
        // 期望矩形故意偏一点：中心、尺寸和角度都与实际不同。
        Regions = new() { new() { Id = "search", Shape = EWorkflowVisionRoiShape.Rectangle, CenterX = 152, CenterY = 118, Width = 136, Height = 94, Angle = 8 * Math.PI / 180 } }
    };

    [Fact]
    public async Task FindRectangle_FitsFourSidesAndCorners()
    {
        using var frame = DarkRectangle();
        var node = Node();
        Assert.Empty(node.ValidateConfiguration());
        var services = new WorkflowServiceProvider().Add<ICaliperMeasurer>(new CaliperMeasurer()).Add<IRobustLineFitter>(new RobustLineFitter());
        var runner = new WorkflowNodeTrialRunner(new WorkflowNodeHandlerCatalog().Register(new FindVisionRectangleNodeHandler()), new WorkflowContext(services));

        var output = await runner.RunAsync(node, new Dictionary<object, object?>(ReferenceEqualityComparer.Instance) { [node.Frame] = frame });

        Assert.True(output.Success, output.Message);
        var result = Assert.IsType<VisionFindRectangleResult>(output.Output);
        Assert.Equal(32, result.CaliperCount);
        Assert.Equal(32, result.FoundCount);
        Assert.InRange(result.Width, 139.5, 140.5);
        Assert.InRange(result.Height, 89.5, 90.5);
        Assert.InRange(result.AngleDegrees, 9.7, 10.3);
        Assert.InRange(result.CenterX, 149.5, 150.5);
        Assert.InRange(result.CenterY, 119.5, 120.5);
        Assert.InRange(result.MaximumCornerError, 0, .5);
        Assert.Equal(4, result.Corners.Count);
        Assert.Equal(4, result.SideLines.Count);
        // 左上角点：中心 + R(10°)·(-70,-45)。
        double a = 10 * Math.PI / 180;
        Assert.InRange(result.Corners[0].ImagePosition.X, 150 - 70 * Math.Cos(a) + 45 * Math.Sin(a) - .7, 150 - 70 * Math.Cos(a) + 45 * Math.Sin(a) + .7);
        Assert.InRange(result.Corners[0].ImagePosition.Y, 120 - 70 * Math.Sin(a) - 45 * Math.Cos(a) - .7, 120 - 70 * Math.Sin(a) - 45 * Math.Cos(a) + .7);
        Assert.Contains(result.DisplayItems, i => i.Role == EVisionDisplayRole.Result && i.Label == FormattableString.Invariant($"{result.Width:F2}×{result.Height:F2}"));
        Assert.StartsWith("找矩形：32/32", result.Summary);
    }

    [Fact]
    public void FindRectangle_CalipersSitOnEachSideAndScanAcrossIt()
    {
        var node = new FindVisionRectangleNodeModel
        {
            CaliperCount = 4, SearchLength = 10,
            Regions = new() { new() { Id = "search", Shape = EWorkflowVisionRoiShape.Rectangle, CenterX = 100, CenterY = 100, Width = 80, Height = 40 } }
        };
        var scans = node.CaliperScans();
        Assert.Equal(16, scans.Count);
        // 上边（y=80）第一把：在 x=60+10 处，由内向外从 y=85 扫到 y=75。
        Near(new PointD(70, 85), scans[0].Start);
        Near(new PointD(70, 75), scans[0].End);
        // 右边第一把：在 y=80+5 处，从 x=135 扫到 x=145。
        Near(new PointD(135, 85), scans[4].Start);
        Near(new PointD(145, 85), scans[4].End);
        node.Direction = EVisionFindCircleDirection.Inward;
        Near(new PointD(70, 75), node.CaliperScans()[0].Start);

        node.SearchLength = 40;
        Assert.Contains(node.ValidateConfiguration(), e => e.Contains("短边", StringComparison.Ordinal));
        var fresh = new FindVisionRectangleNodeModel();
        Assert.DoesNotContain(fresh.ValidateConfiguration(), e => e.Contains("期望矩形", StringComparison.Ordinal) || e.Contains("短边", StringComparison.Ordinal));
        Assert.True(fresh.FitPlaceholderSearchRoi(1000, 800));
        Assert.Equal((500d, 400d, 400d, 320d), (fresh.Regions[0].CenterX, fresh.Regions[0].CenterY, fresh.Regions[0].Width, fresh.Regions[0].Height));
    }

    [Fact]
    public async Task FindRectangle_MissingSideFailsWithItsName()
    {
        using var frame = DarkRectangle();
        var node = Node();
        node.Regions[0].CenterY = 40; // 上边的卡尺全落在亮背景里，找不到边缘。
        node.Regions[0].Height = 60;
        var services = new WorkflowServiceProvider().Add<ICaliperMeasurer>(new CaliperMeasurer()).Add<IRobustLineFitter>(new RobustLineFitter());
        var runner = new WorkflowNodeTrialRunner(new WorkflowNodeHandlerCatalog().Register(new FindVisionRectangleNodeHandler()), new WorkflowContext(services));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            runner.RunAsync(node, new Dictionary<object, object?>(ReferenceEqualityComparer.Instance) { [node.Frame] = frame }));
        Assert.Contains("上边", error.Message);
    }

    private static void Near(PointD expected, PointD actual)
    {
        Assert.Equal(expected.X, actual.X, 9);
        Assert.Equal(expected.Y, actual.Y, 9);
    }
}
