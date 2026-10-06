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
        Assert.Contains(visuals, visual => visual.Id == "caliper-band" && visual.Caption!.Contains("±3×2=6px"));
        Assert.Contains(visuals, visual => visual.Id == "caliper-arrow");
        Assert.Contains(visuals, visual => visual.Id == "caliper-axis");
        // 中心线两侧各 3 条垂直采样线。
        Assert.Equal(6, visuals.Count(visual => visual.Id.StartsWith("caliper-sample", StringComparison.Ordinal)));
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
