using DP.Vision;
using DP.Vision.Algorithms;

namespace DP.WorkFlow;

/// <summary>一次面积范围解析的只读结果；矩形仅限定计算域，非空Region才是精确范围。没有图像所有权。</summary>
public sealed class WorkflowVisionResolvedRange
{
    internal WorkflowVisionResolvedRange(PixelBounds bounds, RegionGeometry? region, VisionCoordinateSystem? coordinates)
    { Bounds = bounds; Region = region; Coordinates = coordinates; }
    /// <summary>原图计算矩形，不能代替Region。</summary>
    public PixelBounds Bounds { get; }
    /// <summary>可空精确原图掩码，已完成局部ROI变换、排除和输入Mask交集。</summary>
    public RegionGeometry? Region { get; }
    /// <summary>来自当前绑定来源、已验证本帧身份和图像尺寸的坐标映射。</summary>
    public VisionCoordinateSystem? Coordinates { get; }
    /// <summary>取得真正参与计算的原图区域，包含矩形边界与精确掩膜的交集；空区域保持为空。</summary>
    /// <param name="token">取消令牌。</param>
    /// <returns>独立区域事实，不回退全图。</returns>
    public RegionGeometry ToRegion(CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        var rectangle = new RegionGeometry(Enumerable.Range(Bounds.Y, Bounds.Height)
            .Select(y => new RegionRun(y, Bounds.X, Bounds.X + Bounds.Width)));
        return Region is null ? rectangle : rectangle.Intersect(Region, token);
    }
}
