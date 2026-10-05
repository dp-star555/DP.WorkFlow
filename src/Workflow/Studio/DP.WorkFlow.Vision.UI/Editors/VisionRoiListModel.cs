using System.Globalization;
using DP.Vision;
using DP.Vision.UI;

namespace DP.WorkFlow.Vision.UI;

/// <summary>ROI 列表中的一行。</summary>
/// <param name="Id">ROI 标识。</param>
/// <param name="Shape">形状名称。</param>
/// <param name="Purpose">包含或排除。</param>
/// <param name="Enabled">是否启用。</param>
/// <param name="Summary">原图坐标下的位置与尺寸摘要。</param>
public sealed record VisionRoiListItem(string Id, string Shape, string Purpose, bool Enabled, string Summary);

/// <summary>
/// 节点窗口左侧“ROI”列表页的共享模型。与右侧图像画布共用同一个 <see cref="RoiEditor"/>：
/// 在列表中选择、删除或修改用途会立即反映到画布，画布上的编辑也会刷新列表。
/// </summary>
public sealed class VisionRoiListModel
{
    private readonly VisionFrameEditorPageModel _frame;

    /// <summary>基于图像页模型创建 ROI 列表。</summary>
    public VisionRoiListModel(VisionFrameEditorPageModel frame) => _frame = frame ?? throw new ArgumentNullException(nameof(frame));

    /// <summary>节点是否有可编辑的测量范围；无范围能力的节点不显示 ROI 页。</summary>
    public bool IsAvailable => _frame.CanEdit && !_frame.IsTemplateEditor;

    /// <summary>是否支持包含/排除等完整面积 ROI。</summary>
    public bool SupportsRegions => _frame.SupportsRegions;

    /// <summary>当前选中的 ROI 标识。</summary>
    public string? SelectedId => _frame.Editor.SelectedId;

    /// <summary>最近一次编辑状态或错误。</summary>
    public string Status => _frame.Status;

    /// <summary>ROI 文档或选择变化时发生。</summary>
    public event EventHandler? Changed
    {
        add => _frame.Editor.Changed += value;
        remove => _frame.Editor.Changed -= value;
    }

    /// <summary>按文档顺序返回当前 ROI。</summary>
    public IReadOnlyList<VisionRoiListItem> Items => _frame.Editor.Document.Rois.Select(roi => new VisionRoiListItem(
        roi.Id, ShapeName(roi.Shape), roi.Purpose == ERoiPurpose.Exclude ? "排除" : "包含", roi.Enabled, Summary(roi.Shape))).ToArray();

    /// <summary>选中指定 ROI；null 表示取消选择，未知标识被忽略。</summary>
    public void Select(string? id)
    {
        if (id is not null && _frame.Editor.Document.Rois.All(roi => roi.Id != id)) return;
        if (_frame.Editor.SelectedId != id) _frame.Editor.Select(id);
    }

    /// <summary>删除选中的 ROI。</summary>
    public void DeleteSelected() => _frame.Editor.DeleteSelected();

    /// <summary>修改选中 ROI 的包含/排除用途，保留启用状态。</summary>
    public void SetSelectedPurpose(ERoiPurpose purpose)
    {
        if (Selected() is { } roi) _frame.Editor.SetSelectedMetadata(purpose, roi.Enabled);
    }

    /// <summary>启用或停用选中 ROI，保留用途。</summary>
    public void SetSelectedEnabled(bool enabled)
    {
        if (Selected() is { } roi) _frame.Editor.SetSelectedMetadata(roi.Purpose, enabled);
    }

    private RoiDefinition? Selected() => _frame.Editor.Document.Rois.FirstOrDefault(roi => roi.Id == _frame.Editor.SelectedId);

    private static string ShapeName(Geometry shape) => shape switch
    {
        RectangleGeometry { Angle: var angle } when Math.Abs(angle) > 1e-10 => "旋转矩形",
        RectangleGeometry => "矩形",
        EllipseGeometry => "椭圆",
        ContourGeometry { Closed: true } => "多边形",
        ContourGeometry => "轮廓",
        _ => shape.GetType().Name.Replace("Geometry", string.Empty, StringComparison.Ordinal)
    };

    private static string Summary(Geometry shape) => shape switch
    {
        RectangleGeometry r => $"中心({F(r.Center.X)}, {F(r.Center.Y)}) {F(r.Width)}×{F(r.Height)}{Angle(r.Angle)}",
        EllipseGeometry e => $"中心({F(e.Center.X)}, {F(e.Center.Y)}) 半径 {F(e.RadiusX)}×{F(e.RadiusY)}{Angle(e.Angle)}",
        ContourGeometry c => $"{c.Points.Count} 个顶点",
        _ => $"范围({F(shape.Bounds.X)}, {F(shape.Bounds.Y)}) {F(shape.Bounds.Width)}×{F(shape.Bounds.Height)}"
    };

    private static string Angle(double radians) =>
        Math.Abs(radians) > 1e-10 ? $" {F(radians * 180 / Math.PI)}°" : string.Empty;

    private static string F(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
