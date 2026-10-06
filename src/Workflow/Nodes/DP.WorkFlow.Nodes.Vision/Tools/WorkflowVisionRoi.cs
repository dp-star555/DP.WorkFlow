using System.ComponentModel;
using DP.Vision;

namespace DP.WorkFlow;

/// <summary>后台中立范围形状；不包含UI编辑器对象。</summary>
public enum EWorkflowVisionRoiShape
{
    /// <summary>矩形，可以旋转。</summary>
    [Description("矩形")]
    Rectangle,
    /// <summary>椭圆，可以旋转。</summary>
    [Description("椭圆")]
    Ellipse,
    /// <summary>闭合填充多边形。</summary>
    [Description("多边形")]
    Polygon
}

/// <summary>持久化ROI顶点。</summary>
public sealed class WorkflowVisionRoiPoint
{
    /// <summary>原图像素边界X。</summary>
    public double X { get; set; }
    /// <summary>原图像素边界Y。</summary>
    public double Y { get; set; }
}

/// <summary>可持久化的形状配置；执行时栅格化为独立Region，保留排除区域。</summary>
public sealed class WorkflowVisionRoi
{
    /// <summary>稳定标识。</summary>
    public string Id { get; set; } = "roi";
    /// <summary>形状。</summary>
    public EWorkflowVisionRoiShape Shape { get; set; }
    /// <summary>是否启用。</summary>
    public bool Enabled { get; set; } = true;
    /// <summary>是否排除；包含集合并集减排除集合并集。</summary>
    public bool Exclude { get; set; }
    /// <summary>矩形/椭圆中心X。</summary>
    public double CenterX { get; set; }
    /// <summary>矩形/椭圆中心Y。</summary>
    public double CenterY { get; set; }
    /// <summary>局部宽度。</summary>
    public double Width { get; set; } = 1;
    /// <summary>局部高度。</summary>
    public double Height { get; set; } = 1;
    /// <summary>顺时针弧度。</summary>
    public double Angle { get; set; }
    /// <summary>多边形原图顶点，不用于模拟圆弧。</summary>
    public List<WorkflowVisionRoiPoint> Points { get; set; } = new();

    /// <summary>从配置建立不可变几何；不自动裁剪越界，不将开放曲线填充。</summary>
    /// <returns>独立形状快照。</returns>
    public Geometry ToGeometry() => Shape switch
    {
        EWorkflowVisionRoiShape.Rectangle => new RectangleGeometry(new PointD(CenterX, CenterY), Width, Height, Angle),
        EWorkflowVisionRoiShape.Ellipse => new EllipseGeometry(new PointD(CenterX, CenterY), Width / 2, Height / 2, Angle),
        EWorkflowVisionRoiShape.Polygon when Points is { Count: >= 3 and <= 4096 } && Points.All(p => p is not null) =>
            new ContourGeometry(Points.Select(p => new PointD(p.X, p.Y)), true, true),
        _ => throw new ArgumentException("无效ROI形状或顶点数量。")
    };

    internal static RegionGeometry Compose(IReadOnlyList<WorkflowVisionRoi> rois, IImageSource image, CancellationToken token)
    {
        var enabled = rois.Where(r => r.Enabled).ToArray();
        if (enabled.Length == 0) throw new InvalidOperationException("全部ROI已禁用，不能默认检查全图。");
        return DP.Vision.Algorithms.InspectionMask.Compose(image, enabled.Where(r => !r.Exclude).Select(r => r.ToGeometry()),
            enabled.Where(r => r.Exclude).Select(r => r.ToGeometry()), token);
    }
}
