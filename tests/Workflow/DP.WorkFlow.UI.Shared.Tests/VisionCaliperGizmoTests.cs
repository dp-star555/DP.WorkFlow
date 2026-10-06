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
}
