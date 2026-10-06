using DP.Vision;
using DP.Vision.Algorithms;
using DP.WorkFlow.Vision.UI;

namespace DP.WorkFlow.Tests;

public sealed class VisionCaliperGizmoTests
{
    [Fact]
    public void Visuals_ShowBandDirectionSamplingLinesAndHandles()
    {
        var gizmo = new VisionCaliperGizmo(new MeasureVisionCaliperNodeModel { StartX = 10, StartY = 50, EndX = 110, EndY = 50, HalfWidth = 3, BandSampleStep = 2 });

        var visuals = gizmo.Visuals(0.1);

        Assert.Equal(6, gizmo.HalfBand);
        Assert.Contains(visuals, visual => visual.Id == "caliper-band" && visual.Caption!.Contains("±6px（7 点 × 间隔 2px）"));
        Assert.Contains(visuals, visual => visual.Id == "caliper-arrow");
        Assert.Contains(visuals, visual => visual.Id == "caliper-axis");
        // 投影线垂直于扫描方向横跨整条带宽：0.1 原图像素/屏幕像素时间距 1px，长度 100px 内部 99 条。
        var samples = visuals.Where(visual => visual.Id.StartsWith("caliper-sample", StringComparison.Ordinal)).ToArray();
        Assert.Equal(99, samples.Length);
        var first = Assert.IsType<ContourGeometry>(samples[0].Geometry).Points;
        Assert.Equal((11d, 44d, 11d, 56d), (first[0].X, first[0].Y, first[1].X, first[1].Y));
        // 只有带宽外框带标注，控制点不再各自挂说明文字。
        Assert.Single(visuals, visual => visual.Caption is not null);
        Assert.Contains(visuals, visual => visual.Id == "caliper-start");
        Assert.Contains(visuals, visual => visual.Id == "caliper-end");
        Assert.Equal(2, visuals.Count(visual => visual.Id.StartsWith("caliper-width", StringComparison.Ordinal)));
    }

    [Fact]
    public void DraggingHandles_WritesStartEndHalfWidthAndTranslation()
    {
        var node = new MeasureVisionCaliperNodeModel { StartX = 10, StartY = 50, EndX = 110, EndY = 50, HalfWidth = 2, BandSampleStep = 1 };
        var gizmo = new VisionCaliperGizmo(node);

        Assert.Equal(EVisionCaliperHandle.End, gizmo.Hit(new PointD(111, 50), 1));
        gizmo.BeginDrag(EVisionCaliperHandle.End, new PointD(110, 50));
        Assert.True(gizmo.Drag(new PointD(10, 150)));
        Assert.True(gizmo.EndDrag());
        Assert.Equal((10d, 150d), (node.EndX, node.EndY));

        Assert.Equal(EVisionCaliperHandle.Start, gizmo.Hit(new PointD(10, 51), 1));
        gizmo.BeginDrag(EVisionCaliperHandle.Start, new PointD(10, 50));
        gizmo.Drag(new PointD(10, 40));
        gizmo.EndDrag();
        Assert.Equal((10d, 40d), (node.StartX, node.StartY));

        // 扫描线现在竖直向下；宽度把手在中点两侧，拖到距离 7.4px 处取整为半宽 7。
        Assert.Equal(EVisionCaliperHandle.Width, gizmo.Hit(new PointD(10 - 7.5, 95), 1));
        gizmo.BeginDrag(EVisionCaliperHandle.Width, new PointD(10 - 7.5, 95));
        gizmo.Drag(new PointD(10 + 7.4, 95));
        gizmo.EndDrag();
        Assert.Equal(7, node.HalfWidth);

        Assert.Equal(EVisionCaliperHandle.Body, gizmo.Hit(new PointD(12, 80), 1));
        gizmo.BeginDrag(EVisionCaliperHandle.Body, new PointD(12, 80));
        gizmo.Drag(new PointD(22, 85));
        Assert.True(gizmo.EndDrag());
        Assert.Equal((20d, 45d, 20d, 155d), (node.StartX, node.StartY, node.EndX, node.EndY));

        gizmo.BeginDrag(EVisionCaliperHandle.Width, new PointD(0, 0));
        Assert.False(gizmo.EndDrag());

        // 超过 63 步时放大垂直采样间隔，带宽可继续拖大；缩回时恢复原间隔。
        gizmo.BeginDrag(EVisionCaliperHandle.Width, new PointD(27, 100));
        gizmo.Drag(new PointD(20 + 200, 100));
        Assert.Equal((63, 3.18), (node.HalfWidth, node.BandSampleStep));
        Assert.InRange(gizmo.HalfBand, 199, 202);
        gizmo.Drag(new PointD(20 + 10, 100));
        Assert.True(gizmo.EndDrag());
        Assert.Equal((10, 1d), (node.HalfWidth, node.BandSampleStep));
        Assert.Null(gizmo.Hit(new PointD(500, 500), 1));
    }

    [Fact]
    public void CoordinateBoundCaliper_IsNotEditableOnImage()
    {
        var gizmo = new VisionCaliperGizmo(new MeasureVisionCaliperNodeModel
        {
            Coordinates = new WorkflowVisionCoordinateBinding { CoordinateSystemId = "work" }
        });

        Assert.False(gizmo.IsEditable);
        Assert.Empty(gizmo.Visuals(1));
        Assert.Null(gizmo.Hit(new PointD(1, 2.5), 1));
    }

    [Fact]
    public void ArcVisuals_ShowAnnularBandRadialProjectionsAndHandles()
    {
        var gizmo = new VisionCaliperGizmo(new MeasureVisionCaliperNodeModel
        {
            Shape = EVisionCaliperShape.Arc, CenterX = 100, CenterY = 100, Radius = 50, StartAngle = 0, SweepAngle = 90, HalfWidth = 5, BandSampleStep = 1
        });

        var visuals = gizmo.Visuals(1);

        var band = Assert.IsType<ContourGeometry>(Assert.Single(visuals, v => v.Id == "caliper-band").Geometry);
        Assert.True(band.Closed);
        Assert.Contains(band.Points, p => Math.Abs(p.X - 155) < 1e-6 && Math.Abs(p.Y - 100) < 1e-6);
        Assert.Contains(band.Points, p => Math.Abs(p.X - 100) < 1e-6 && Math.Abs(p.Y - 145) < 1e-6);
        Assert.Contains("圆弧卡尺 半径 50px", Assert.Single(visuals, v => v.Caption is not null).Caption);
        // 投影线沿半径方向：两端到圆心的距离分别是内外半径。
        var sample = Assert.IsType<ContourGeometry>(visuals.First(v => v.Id.StartsWith("caliper-sample", StringComparison.Ordinal)).Geometry).Points;
        Assert.Equal(45, Distance(sample[0], new PointD(100, 100)), 6);
        Assert.Equal(55, Distance(sample[1], new PointD(100, 100)), 6);
        foreach (var id in new[] { "caliper-axis", "caliper-arrow", "caliper-start", "caliper-end", "caliper-center", "caliper-radius" })
            Assert.Contains(visuals, v => v.Id == id);
        Assert.Equal((150d, 100d), (gizmo.Start.X, Math.Round(gizmo.Start.Y, 6)));
        Assert.Equal((100d, 150d), (Math.Round(gizmo.End.X, 6), gizmo.End.Y));
    }

    [Fact]
    public void ArcDragging_ChangesAnglesRadiusWidthAndCenter()
    {
        var node = new MeasureVisionCaliperNodeModel
        {
            Shape = EVisionCaliperShape.Arc, CenterX = 100, CenterY = 100, Radius = 50, StartAngle = 0, SweepAngle = 90, HalfWidth = 2, BandSampleStep = 1
        };
        var gizmo = new VisionCaliperGizmo(node);

        // 终点（90°，正下方）拖到 180°（左侧）：扫描角度 180°。
        Assert.Equal(EVisionCaliperHandle.End, gizmo.Hit(new PointD(100, 151), 1));
        gizmo.BeginDrag(EVisionCaliperHandle.End, new PointD(100, 150));
        gizmo.Drag(new PointD(40, 100));
        Assert.True(gizmo.EndDrag());
        Assert.Equal(180d, node.SweepAngle);

        // 起点拖到 -90°（正上方），终止角 180° 不动：起始角 -90°、扫描 270°。
        gizmo.BeginDrag(EVisionCaliperHandle.Start, new PointD(150, 100));
        gizmo.Drag(new PointD(100, 30));
        gizmo.EndDrag();
        Assert.Equal((-90d, 270d), (node.StartAngle, node.SweepAngle));

        // 半径把手在扫描中点（45°）。
        var middle = new PointD(100 + 50 * Math.Cos(Math.PI / 4), 100 + 50 * Math.Sin(Math.PI / 4));
        Assert.Equal(EVisionCaliperHandle.Radius, gizmo.Hit(middle, 1));
        gizmo.BeginDrag(EVisionCaliperHandle.Radius, middle);
        gizmo.Drag(new PointD(100 + 80 * Math.Cos(Math.PI / 4), 100 + 80 * Math.Sin(Math.PI / 4)));
        gizmo.EndDrag();
        Assert.Equal(80d, node.Radius);

        // 宽度：离中心线 12px → 半宽 12；超过 63 步时放大间隔。
        gizmo.BeginDrag(EVisionCaliperHandle.Width, middle);
        gizmo.Drag(new PointD(100 + 92 * Math.Cos(Math.PI / 4), 100 + 92 * Math.Sin(Math.PI / 4)));
        Assert.Equal((12, 1d), (node.HalfWidth, node.BandSampleStep));
        gizmo.Drag(new PointD(100 + 180 * Math.Cos(Math.PI / 4), 100 + 180 * Math.Sin(Math.PI / 4)));
        gizmo.EndDrag();
        // 内侧不能越过圆心：带宽贴近但不超过半径 80（间隔放大到 1.27，63 步会到 80.01，退为 62 步）。
        Assert.InRange(gizmo.HalfBand, 78, 80);
        Assert.Equal(1.27, node.BandSampleStep);

        Assert.Equal(EVisionCaliperHandle.Center, gizmo.Hit(new PointD(101, 100), 1));
        gizmo.BeginDrag(EVisionCaliperHandle.Center, new PointD(100, 100));
        gizmo.Drag(new PointD(110, 95));
        Assert.True(gizmo.EndDrag());
        Assert.Equal((110d, 95d, 80d), (node.CenterX, node.CenterY, node.Radius));
    }

    [Fact]
    public void SwitchingShape_KeepsPositionOnImage()
    {
        var node = new MeasureVisionCaliperNodeModel { StartX = 10, StartY = 50, EndX = 110, EndY = 50, HalfWidth = 3, BandSampleStep = 1 };
        var gizmo = new VisionCaliperGizmo(node);

        Assert.True(gizmo.SetShape(EVisionCaliperShape.Arc));
        // 直线变成经过原起点和终点的半圆。
        Assert.Equal((60d, 50d, 50d, 180d, 180d), (node.CenterX, node.CenterY, node.Radius, node.StartAngle, node.SweepAngle));
        Assert.Equal(10, gizmo.Start.X, 6);
        Assert.Equal(110, gizmo.End.X, 6);
        Assert.False(gizmo.SetShape(EVisionCaliperShape.Arc));

        node.SweepAngle = 90;
        Assert.True(gizmo.SetShape(EVisionCaliperShape.Line));
        Assert.Equal(EVisionCaliperShape.Line, node.Shape);
        Assert.Equal((10d, 50d, 60d, 0d), (node.StartX, node.StartY, node.EndX, node.EndY));
    }

    [Fact]
    public void CoordinateBoundCaliper_IsDisplayedAndDraggedThroughFrameCoordinates()
    {
        // 局部→原图：尺度 2、旋转 90°、平移 (100, 50)：x' = -2y + 100，y' = 2x + 50。
        var system = new VisionCoordinateSystem(new VisionCoordinateDefinition("work", "工件"), "frame", 400, 400,
            CoordinateMatrix2D.FromAffine(0, -2, 100, 2, 0, 50));
        var node = new MeasureVisionCaliperNodeModel
        {
            Shape = EVisionCaliperShape.Arc, CenterX = 10, CenterY = 10, Radius = 5, StartAngle = 0, SweepAngle = 90, HalfWidth = 2, BandSampleStep = 1,
            Coordinates = new WorkflowVisionCoordinateBinding { CoordinateSystemId = "work" }
        };
        var gizmo = new VisionCaliperGizmo(node);
        Assert.False(gizmo.IsEditable);
        Assert.Contains("请先运行流程", gizmo.Hint);

        gizmo.Coordinates = system;
        Assert.True(gizmo.IsEditable);
        Assert.NotEmpty(gizmo.Visuals(1));
        Assert.Equal((80d, 70d), (gizmo.Center.X, gizmo.Center.Y));
        Assert.Equal(80, gizmo.Start.X, 6); Assert.Equal(80, gizmo.Start.Y, 6);
        Assert.Equal(4d, gizmo.HalfBand);

        // 圆心在原图拖到 (90, 70) → 局部 (10, 5)。
        gizmo.BeginDrag(EVisionCaliperHandle.Center, new PointD(80, 70));
        gizmo.Drag(new PointD(90, 70));
        gizmo.EndDrag();
        Assert.Equal((10d, 5d), (node.CenterX, node.CenterY));

        // 原图半径 16 → 局部 8。
        var middle = new PointD(90 + 10 * Math.Cos(135 * Math.PI / 180), 70 + 10 * Math.Sin(135 * Math.PI / 180));
        // 这段圆弧在原图只有约 16px，放大后（0.1 原图像素/屏幕像素）才能分开抓取中点与终点。
        Assert.Equal(EVisionCaliperHandle.Radius, gizmo.Hit(middle, .1));
        gizmo.BeginDrag(EVisionCaliperHandle.Radius, middle);
        gizmo.Drag(new PointD(90 + 16 * Math.Cos(135 * Math.PI / 180), 70 + 16 * Math.Sin(135 * Math.PI / 180)));
        gizmo.EndDrag();
        Assert.Equal(8, node.Radius, 6);

        // 起点拖到原图 120°：终止角（原图 180°）不动 → 局部起始角 30°、扫描 60°。
        gizmo.BeginDrag(EVisionCaliperHandle.Start, gizmo.Start);
        gizmo.Drag(new PointD(90 + 16 * Math.Cos(120 * Math.PI / 180), 70 + 16 * Math.Sin(120 * Math.PI / 180)));
        gizmo.EndDrag();
        Assert.Equal(30, node.StartAngle, 6); Assert.Equal(60, node.SweepAngle, 6);

        // 原图带宽 6px、原图间隔 2px → 半宽 3，局部间隔仍为 1。
        var widthPoint = new PointD(90 + 22 * Math.Cos(150 * Math.PI / 180), 70 + 22 * Math.Sin(150 * Math.PI / 180));
        gizmo.BeginDrag(EVisionCaliperHandle.Width, widthPoint);
        gizmo.Drag(widthPoint);
        gizmo.EndDrag();
        Assert.Equal((3, 1d), (node.HalfWidth, node.BandSampleStep));

        // 直线：局部 (0,0)→(10,0) 在原图为 (100,50)→(100,70)；终点拖到原图 (120,50) → 局部 (0,-10)。
        Assert.True(gizmo.SetShape(EVisionCaliperShape.Line));
        node.StartX = 0; node.StartY = 0; node.EndX = 10; node.EndY = 0;
        Assert.Equal((100d, 70d), (gizmo.End.X, gizmo.End.Y));
        gizmo.BeginDrag(EVisionCaliperHandle.End, gizmo.End);
        gizmo.Drag(new PointD(120, 50));
        gizmo.EndDrag();
        Assert.Equal((0d, -10d), (node.EndX, node.EndY));
    }

    [Fact]
    public void StepHandle_ChangesSamplingIntervalKeepingBandWidth()
    {
        // 水平卡尺 (0,50)→(200,50)，带宽 ±20 = 20 点 × 1px。
        var node = new MeasureVisionCaliperNodeModel { StartX = 0, StartY = 50, EndX = 200, EndY = 50, HalfWidth = 20, BandSampleStep = 1 };
        var gizmo = new VisionCaliperGizmo(node);
        var visuals = gizmo.Visuals(1);
        // 标尺在 1/4 处（x=50）；间隔 1px 在 1 屏幕像素/原图像素下按 4px 抽稀。
        Assert.Contains(visuals, v => v.Id == "caliper-step-ruler");
        Assert.Equal(11, visuals.Count(v => v.Id.StartsWith("caliper-step-dot", StringComparison.Ordinal)));
        // 把手在第 14 个采样点（离扫描线 14px）。
        var handle = Assert.IsType<RectangleGeometry>(Assert.Single(visuals, v => v.Id == "caliper-step").Geometry);
        Assert.Equal((50d, 64d), (Math.Round(handle.Center.X, 6), Math.Round(handle.Center.Y, 6)));
        Assert.Equal(EVisionCaliperHandle.Step, gizmo.Hit(new PointD(51, 64), 1));

        // 拖到离扫描线 28px：间隔 2px，带宽仍 ±20 → 半宽 10。
        gizmo.BeginDrag(EVisionCaliperHandle.Step, new PointD(50, 64), 1);
        gizmo.Drag(new PointD(50, 78));
        Assert.Equal((2d, 10), (node.BandSampleStep, node.HalfWidth));
        Assert.Equal(20d, gizmo.HalfBand);
        // 拖到 3.5px：间隔 0.25px → 需要 80 步，最多 63 步，带宽缩为 15.75。
        gizmo.Drag(new PointD(50, 53.5));
        Assert.True(gizmo.EndDrag());
        Assert.Equal((.25, 63), (node.BandSampleStep, node.HalfWidth));
        Assert.Contains("127 点 × 间隔 0.25px", gizmo.Caption);
    }

    private static double Distance(PointD a, PointD b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
}
