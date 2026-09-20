using DP.Vision;
using DP.Vision.Algorithms;

namespace DP.WorkFlow;

/// <summary>一次面积范围解析的只读结果；矩形仅限定计算域，非空Region才是精确范围。没有图像所有权。</summary>
public sealed class WorkflowVisionResolvedRange
{
    internal WorkflowVisionResolvedRange(PixelBounds bounds, RegionGeometry? region, LocatedCoordinateSystem? coordinates)
    { Bounds = bounds; Region = region; Coordinates = coordinates; }
    /// <summary>原图计算矩形，不能代替Region。</summary>
    public PixelBounds Bounds { get; }
    /// <summary>可空精确原图掩码，已完成局部ROI变换、排除和输入Mask交集。</summary>
    public RegionGeometry? Region { get; }
    /// <summary>已按本帧和制作身份验证的定位。</summary>
    public LocatedCoordinateSystem? Coordinates { get; }
}
