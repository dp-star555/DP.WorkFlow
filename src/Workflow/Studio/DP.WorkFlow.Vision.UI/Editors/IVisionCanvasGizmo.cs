using DP.Vision;
using DP.Vision.Algorithms;

namespace DP.WorkFlow.Vision.UI;

/// <summary>
/// 图像页上可拖动编辑节点几何参数的叠加控件（卡尺、找线、找圆）。坐标均为原图像素；
/// 节点绑定坐标系时由页面提供本帧坐标系，控件负责局部 ↔ 原图换算。
/// </summary>
public interface IVisionCanvasGizmo
{
    /// <summary>当前能否在图上显示和拖动。</summary>
    bool IsEditable { get; }
    /// <summary>是否正在拖动。</summary>
    bool IsDragging { get; }
    /// <summary>几何参数签名；变化时预览需要重绘。</summary>
    string Key { get; }
    /// <summary>状态栏中的尺寸说明。</summary>
    string Caption { get; }
    /// <summary>状态栏中的操作提示。</summary>
    string Hint { get; }
    /// <summary>本帧坐标系（节点绑定坐标系时由页面设置）。</summary>
    VisionCoordinateSystem? Coordinates { get; set; }
    /// <summary>生成叠加图形；控制点按屏幕像素固定大小。</summary>
    /// <param name="imagePixelsPerScreenPixel">当前缩放下 1 个屏幕像素对应的原图像素。</param>
    IReadOnlyList<Visual> Visuals(double imagePixelsPerScreenPixel);
    /// <summary>命中测试。</summary>
    /// <param name="point">原图坐标。</param><param name="imagePixelsPerScreenPixel">每屏幕像素对应的原图像素。</param>
    EVisionCaliperHandle? Hit(PointD point, double imagePixelsPerScreenPixel);
    /// <summary>开始拖动。</summary>
    /// <param name="handle">控制点。</param><param name="point">按下位置。</param><param name="imagePixelsPerScreenPixel">每屏幕像素对应的原图像素。</param>
    void BeginDrag(EVisionCaliperHandle handle, PointD point, double imagePixelsPerScreenPixel = 1);
    /// <summary>拖动并写回节点参数。</summary>
    /// <param name="point">当前指针位置。</param><returns>参数发生变化时返回真。</returns>
    bool Drag(PointD point);
    /// <summary>结束拖动。</summary><returns>本次拖动是否修改了参数。</returns>
    bool EndDrag();
}
