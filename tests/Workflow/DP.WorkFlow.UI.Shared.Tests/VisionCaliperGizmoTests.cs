using DP.Vision;
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

    private static double Distance(PointD a, PointD b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
}
