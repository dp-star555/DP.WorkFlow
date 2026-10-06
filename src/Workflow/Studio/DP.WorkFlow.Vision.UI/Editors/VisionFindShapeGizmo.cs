using DP.Vision;
using DP.Vision.Algorithms;

namespace DP.WorkFlow.Vision.UI;

/// <summary>
/// 找线/找圆在图像上的可视化与拖动编辑，风格与卡尺一致：
/// 找线画黄色期望直线（拖两端改位置与方向）、青色搜索框与每把卡尺、黄色箭头表示各卡尺的搜索方向；
/// 找圆画黄色期望圆/圆弧、内外搜索边界与径向卡尺。白色方块调整搜索长度与卡尺宽度，拖动内部整体平移。
/// 卡尺位置直接取节点的 <see cref="FindVisionShapeNodeModel.CaliperScans"/>，与运行时完全一致。
/// 绑定坐标系时按本帧坐标系换算到原图显示，拖动结果换算回局部单位写回。
/// </summary>
public sealed class VisionFindShapeGizmo : IVisionCanvasGizmo
{
    private const uint BandColor = 0xFF22D3EE;
    private const uint CaliperColor = 0xD022D3EE;
    private const uint BoxColor = 0x90CBD5E1;
    private const uint AxisColor = 0xFFFACC15;
    private const uint HandleColor = 0xFFF8FAFC;
    private const double HandleScreenRadius = 5;
    private const int MaximumHalfWidth = 63;
    private readonly FindVisionShapeNodeModel _node;
    private VisionCoordinateSystem? _coordinates;
    private EVisionCaliperHandle? _drag;
    private PointD _dragAnchor, _dragCenter, _dragCaliperCenter;
    private double _dragStep;
    private string _dragOriginKey = string.Empty;

    /// <summary>为找线/找圆节点创建图上编辑器。</summary>
    /// <param name="node">节点窗口中的隔离编辑副本。</param>
    public VisionFindShapeGizmo(FindVisionShapeNodeModel node) => _node = node ?? throw new ArgumentNullException(nameof(node));

    /// <inheritdoc/>
    public VisionCoordinateSystem? Coordinates { get => _coordinates; set => _coordinates = value; }

    /// <inheritdoc/>
    public bool IsEditable => Roi is not null && (!Bound || Similarity is not null);

    /// <inheritdoc/>
    public bool IsDragging => _drag.HasValue;

    /// <inheritdoc/>
    public string Key
    {
        get
        {
            var roi = Roi;
            var circle = _node as FindVisionCircleNodeModel;
            return FormattableString.Invariant(
                $"find:{roi?.CenterX}:{roi?.CenterY}:{roi?.Width}:{roi?.Height}:{roi?.Angle}:{_node.CaliperCount}:{_node.HalfWidth}:{_node.BandSampleStep}:{(_node as FindVisionLineNodeModel)?.ReverseScan}:{circle?.SearchLength}:{circle?.Direction}:{circle?.StartAngle}:{circle?.SweepAngle}:{_node.Polarity}:{_node.EdgeMode}:{(Bound ? _coordinates?.FrameId : null)}");
        }
    }

    /// <inheritdoc/>
    public string Caption
    {
        get
        {
            if (!IsEditable) return string.Empty;
            var polarity = _node.Polarity switch { ECaliperPolarity.Rising => "暗→亮", ECaliperPolarity.Falling => "亮→暗", _ => "任意" };
            var pair = _node.EdgeMode == EVisionCaliperEdgeMode.Pair ? " · 边缘对" : "";
            return IsCircle
                ? FormattableString.Invariant($"找圆 · {_node.CaliperCount} 把卡尺 · 半径 {Radius:0.#}px · 搜索长度 {SearchLength:0.#}px · 卡尺宽度 ±{HalfBand:0.##}px · 极性 {polarity}{pair}")
                : FormattableString.Invariant($"找线 · {_node.CaliperCount} 把卡尺 · 长度 {LineLength:0.#}px · 搜索长度 {SearchLength:0.#}px · 卡尺宽度 ±{HalfBand:0.##}px · 极性 {polarity}{pair}");
        }
    }

    /// <inheritdoc/>
    public string Hint => Roi is null
        ? "还没有搜索范围：先运行一次流程取图，打开后自动生成。"
        : Bound && Similarity is null
            ? _coordinates is null ? "已绑定坐标系，本帧没有坐标系结果，无法显示；请先运行流程。" : "绑定的坐标系不是旋转+等比缩放，请在参数页编辑。"
            : (Bound ? "按本帧坐标系显示，拖动结果换算为局部单位写回。" : "") + (IsCircle
                ? "黄色圆为期望圆，青色为各把卡尺，黄色箭头为搜索方向。拖动圆心平移，拖动黄色菱形调整半径，拖动白色方块调整搜索长度，拖动卡尺旁的小方块调整卡尺宽度，拖动起点/终点调整角度范围。"
                : "黄线为期望直线，青色为各把卡尺，黄色箭头为搜索方向。拖动两端调整位置与方向，拖动白色方块调整搜索长度，拖动卡尺旁的小方块调整卡尺宽度，拖动框内整体平移。");

    private bool IsCircle => _node is FindVisionCircleNodeModel;

    // 唯一启用的包含ROI且形状正确时才可编辑（与节点校验一致）。
    private WorkflowVisionRoi? Roi => _node.Regions is { Count: 1 } regions && regions[0] is { Enabled: true, Exclude: false } roi
        && roi.Shape == (IsCircle ? EWorkflowVisionRoiShape.Ellipse : EWorkflowVisionRoiShape.Rectangle) ? roi : null;

    private bool Bound => _node.Coordinates is not null;
    private VisionCoordinateSystem? Similarity => Bound && _coordinates is { IsSimilarity: true } system ? system : null;
    private double Scale => Similarity?.SimilarityScale ?? 1;
    private double Rotation => Similarity?.RotationRadians ?? 0;
    private VisionCoordinateSystem? ScanCoordinates => Bound ? Similarity : null;

    private PointD Center => Roi is { } roi ? ToImage(roi.CenterX, roi.CenterY) : default;
    private double Angle => (Roi?.Angle ?? 0) + Rotation;
    private double LineLength => (Roi?.Width ?? 0) * Scale;
    private double Radius => (Roi?.Width ?? 0) / 2 * Scale;
    private double SearchLength => _node is FindVisionCircleNodeModel circle ? circle.SearchLength * Scale : (Roi?.Height ?? 0) * Scale;
    private double HalfBand => _node.HalfWidth * _node.BandSampleStep * Scale;
    private double CircleStartDegrees => ((FindVisionCircleNodeModel)_node).StartAngle + Rotation * 180 / Math.PI;
    private double CircleSweep => ((FindVisionCircleNodeModel)_node).SweepAngle;
    private bool FullCircle => IsCircle && Math.Abs(Math.Abs(CircleSweep) - 360) < 1e-9;

    private (double X, double Y) U => (Math.Cos(Angle), Math.Sin(Angle));
    private PointD LineStart => Offset(Center, U, -LineLength / 2);
    private PointD LineEnd => Offset(Center, U, LineLength / 2);

    private IReadOnlyList<VisionCaliperScan> Scans()
    {
        try { return _node.CaliperScans(ScanCoordinates); }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException or NotSupportedException) { return Array.Empty<VisionCaliperScan>(); }
    }

    /// <inheritdoc/>
    public IReadOnlyList<Visual> Visuals(double imagePixelsPerScreenPixel)
    {
        if (!IsEditable) return Array.Empty<Visual>();
        var unit = Math.Max(1e-6, imagePixelsPerScreenPixel);
        var visuals = new List<Visual>();
        var handle = HandleScreenRadius * unit;
        if (IsCircle)
        {
            var segments = Math.Clamp((int)Math.Ceiling(Math.Abs(CircleSweep) / 3), 8, 120);
            IEnumerable<PointD> Arc(double radius) => Enumerable.Range(0, segments + 1).Select(i => CirclePoint(radius, (double)i / segments));
            visuals.Add(new Visual("find-outer", new ContourGeometry(Arc(Radius + SearchLength / 2)), BoxColor));
            visuals.Add(new Visual("find-inner", new ContourGeometry(Arc(Math.Max(0, Radius - SearchLength / 2))), BoxColor));
            AddCalipers(visuals, unit);
            visuals.Add(new Visual("find-expected", new ContourGeometry(Arc(Radius)), AxisColor));
            visuals.Add(new Visual("find-center-x", new ContourGeometry(new[] { Offset(Center, (1, 0), -2 * handle), Offset(Center, (1, 0), 2 * handle) }), AxisColor));
            visuals.Add(new Visual("find-center-y", new ContourGeometry(new[] { Offset(Center, (0, 1), -2 * handle), Offset(Center, (0, 1), 2 * handle) }), AxisColor));
            visuals.Add(new Visual("find-center", new EllipseGeometry(Center, handle, handle), HandleColor));
            visuals.Add(new Visual("find-start", new EllipseGeometry(CirclePoint(Radius, 0), handle, handle), HandleColor));
            if (!FullCircle) visuals.Add(new Visual("find-end", new EllipseGeometry(CirclePoint(Radius, 1), handle * 1.2, handle * 1.2), AxisColor));
            var middle = MiddleAngle;
            visuals.Add(new Visual("find-radius", new RectangleGeometry(CirclePointAt(Radius, middle), handle * 1.7, handle * 1.7, middle + Math.PI / 4), AxisColor));
            visuals.Add(new Visual("find-length", new RectangleGeometry(CirclePointAt(Radius + SearchLength / 2, middle), handle * 1.6, handle * 1.6, middle), HandleColor));
        }
        else
        {
            var (u, v) = (U, ScanDirection());
            PointD Corner(double a, double b) => Offset(Offset(Center, u, a * LineLength / 2), v, b * SearchLength / 2);
            visuals.Add(new Visual("find-box", new ContourGeometry(new[] { Corner(-1, -1), Corner(1, -1), Corner(1, 1), Corner(-1, 1) }, closed: true), BoxColor));
            AddCalipers(visuals, unit);
            visuals.Add(new Visual("find-expected", new ContourGeometry(new[] { LineStart, LineEnd }), AxisColor));
            visuals.Add(new Visual("find-start", new EllipseGeometry(LineStart, handle, handle), HandleColor));
            visuals.Add(new Visual("find-end", new EllipseGeometry(LineEnd, handle * 1.2, handle * 1.2), AxisColor));
            foreach (var side in new[] { -1, 1 })
                visuals.Add(new Visual($"find-length{side}", new RectangleGeometry(Offset(Center, v, side * SearchLength / 2), handle * 1.6, handle * 1.6, Angle), HandleColor));
        }
        if (WidthHandlePoint(unit) is { } width)
            visuals.Add(new Visual("find-width", new RectangleGeometry(width, handle * 1.3, handle * 1.3, Angle), HandleColor));
        return visuals;
    }

    // 每把卡尺：半透明填充的青色采样带（太窄时按屏幕最小宽度显示），与淡灰色搜索边界区分；扫描方向上画黄色箭头。
    private void AddCalipers(List<Visual> visuals, double unit)
    {
        var scans = Scans();
        for (int i = 0; i < scans.Count; i++)
        {
            var (start, end) = (scans[i].Start, scans[i].End);
            double length = Distance(start, end);
            if (length < 1e-9) continue;
            var direction = ((end.X - start.X) / length, (end.Y - start.Y) / length);
            var normal = (-direction.Item2, direction.Item1);
            var half = Math.Max(scans[i].HalfBand, 2.5 * unit);
            visuals.Add(new Visual($"find-caliper{i}", new ContourGeometry(new[]
            {
                Offset(start, normal, -half), Offset(end, normal, -half), Offset(end, normal, half), Offset(start, normal, half)
            }, closed: true, filled: true), CaliperColor));
            var head = Math.Min(length / 3, 9 * unit);
            visuals.Add(new Visual($"find-arrow{i}", new ContourGeometry(new[]
            {
                Offset(Offset(end, direction, -head), normal, -head * .6), end, Offset(Offset(end, direction, -head), normal, head * .6)
            }), AxisColor));
        }
    }

    // 卡尺宽度把手：中间那把卡尺中点的一侧，离中心线至少 2.5 个把手半径。
    private PointD? WidthHandlePoint(double unit)
    {
        var scans = Scans();
        if (scans.Count == 0) return null;
        var middle = scans[scans.Count / 2];
        double length = Distance(middle.Start, middle.End);
        if (length < 1e-9) return null;
        var normal = (-(middle.End.Y - middle.Start.Y) / length, (middle.End.X - middle.Start.X) / length);
        var center = new PointD((middle.Start.X + middle.End.X) / 2, (middle.Start.Y + middle.End.Y) / 2);
        return Offset(center, normal, Math.Max(middle.HalfBand, HandleScreenRadius * 2.5 * unit));
    }

    /// <inheritdoc/>
    public EVisionCaliperHandle? Hit(PointD point, double imagePixelsPerScreenPixel)
    {
        if (!IsEditable) return null;
        var unit = Math.Max(1e-6, imagePixelsPerScreenPixel);
        var tolerance = (HandleScreenRadius + 4) * unit;
        if (IsCircle)
        {
            if (Distance(point, Center) <= tolerance) return EVisionCaliperHandle.Center;
            if (!FullCircle && Distance(point, CirclePoint(Radius, 1)) <= tolerance) return EVisionCaliperHandle.End;
            if (Distance(point, CirclePoint(Radius, 0)) <= tolerance) return EVisionCaliperHandle.Start;
            if (Distance(point, CirclePointAt(Radius, MiddleAngle)) <= tolerance) return EVisionCaliperHandle.Radius;
            if (Distance(point, CirclePointAt(Radius + SearchLength / 2, MiddleAngle)) <= tolerance) return EVisionCaliperHandle.Length;
            if (WidthHandlePoint(unit) is { } circleWidth && Distance(point, circleWidth) <= tolerance) return EVisionCaliperHandle.Width;
            return Math.Abs(Distance(point, Center) - Radius) <= Math.Max(SearchLength / 2, tolerance) ? EVisionCaliperHandle.Body : null;
        }
        if (Distance(point, LineEnd) <= tolerance) return EVisionCaliperHandle.End;
        if (Distance(point, LineStart) <= tolerance) return EVisionCaliperHandle.Start;
        var v = ScanDirection();
        foreach (var side in new[] { -1, 1 })
            if (Distance(point, Offset(Center, v, side * SearchLength / 2)) <= tolerance) return EVisionCaliperHandle.Length;
        if (WidthHandlePoint(unit) is { } width && Distance(point, width) <= tolerance) return EVisionCaliperHandle.Width;
        var (along, across) = (Dot(point, Center, U), Dot(point, Center, v));
        return Math.Abs(along) <= LineLength / 2 && Math.Abs(across) <= Math.Max(SearchLength / 2, tolerance) ? EVisionCaliperHandle.Body : null;
    }

    /// <inheritdoc/>
    public void BeginDrag(EVisionCaliperHandle handle, PointD point, double imagePixelsPerScreenPixel = 1)
    {
        _drag = handle; _dragAnchor = point; _dragCenter = Center; _dragOriginKey = Key;
        _dragStep = _node.BandSampleStep * Scale;
        var scans = Scans();
        _dragCaliperCenter = scans.Count == 0 ? Center
            : new PointD((scans[scans.Count / 2].Start.X + scans[scans.Count / 2].End.X) / 2, (scans[scans.Count / 2].Start.Y + scans[scans.Count / 2].End.Y) / 2);
    }

    /// <inheritdoc/>
    public bool Drag(PointD point)
    {
        if (_drag is not { } handle || Roi is not { } roi) return false;
        var before = Key;
        if (IsCircle) DragCircle(handle, point, roi, (FindVisionCircleNodeModel)_node);
        else DragLine(handle, point, roi);
        return Key != before;
    }

    private void DragLine(EVisionCaliperHandle handle, PointD point, WorkflowVisionRoi roi)
    {
        switch (handle)
        {
            case EVisionCaliperHandle.Start or EVisionCaliperHandle.End:
            {
                var fixedEnd = handle == EVisionCaliperHandle.Start ? LineEnd : LineStart;
                var (start, end) = handle == EVisionCaliperHandle.Start ? (point, fixedEnd) : (fixedEnd, point);
                var length = Distance(start, end);
                if (length < 4) break;
                WriteCenter(roi, new PointD((start.X + end.X) / 2, (start.Y + end.Y) / 2));
                roi.Width = Round(length / Scale);
                roi.Angle = Math.Round(Math.Atan2(end.Y - start.Y, end.X - start.X) - Rotation, 6);
                break;
            }
            case EVisionCaliperHandle.Length:
                roi.Height = Round(Math.Max(4, 2 * Math.Abs(Dot(point, Center, ScanDirection()))) / Scale);
                break;
            case EVisionCaliperHandle.Width:
                SetHalfBand(Math.Abs(Dot(point, _dragCaliperCenter, U)));
                break;
            case EVisionCaliperHandle.Body:
                WriteCenter(roi, new PointD(_dragCenter.X + point.X - _dragAnchor.X, _dragCenter.Y + point.Y - _dragAnchor.Y));
                break;
        }
    }

    private void DragCircle(EVisionCaliperHandle handle, PointD point, WorkflowVisionRoi roi, FindVisionCircleNodeModel circle)
    {
        var center = Center;
        var angle = Math.Atan2(point.Y - center.Y, point.X - center.X) * 180 / Math.PI;
        var sign = Math.Sign(CircleSweep) == 0 ? 1 : Math.Sign(CircleSweep);
        var rotation = Rotation * 180 / Math.PI;
        switch (handle)
        {
            case EVisionCaliperHandle.Center or EVisionCaliperHandle.Body:
                WriteCenter(roi, new PointD(_dragCenter.X + point.X - _dragAnchor.X, _dragCenter.Y + point.Y - _dragAnchor.Y));
                break;
            case EVisionCaliperHandle.Radius:
            {
                var radius = Math.Max(SearchLength / 2 + 1, Distance(point, center));
                roi.Width = roi.Height = Round(2 * radius / Scale);
                break;
            }
            case EVisionCaliperHandle.Length:
                circle.SearchLength = Round(Math.Clamp(2 * Math.Abs(Distance(point, center) - Radius), 4, 2 * Radius) / Scale);
                break;
            case EVisionCaliperHandle.Width:
            {
                // 卡尺宽度沿圆周（切向）：取到中间卡尺中心的切向距离。
                var mid = Math.Atan2(_dragCaliperCenter.Y - center.Y, _dragCaliperCenter.X - center.X);
                SetHalfBand(Math.Abs(Dot(point, _dragCaliperCenter, (-Math.Sin(mid), Math.Cos(mid)))));
                break;
            }
            case EVisionCaliperHandle.Start:
            {
                if (Distance(point, center) < 1) break;
                if (FullCircle) { circle.StartAngle = Math.Round(Normalize(angle - rotation), 2); break; }
                var endAngle = CircleStartDegrees + CircleSweep;
                var sweep = Positive(sign * (endAngle - angle));
                if (sweep < 1) break;
                circle.StartAngle = Math.Round(Normalize(angle - rotation), 2); circle.SweepAngle = Math.Round(sign * sweep, 2);
                break;
            }
            case EVisionCaliperHandle.End:
            {
                var sweep = Positive(sign * (angle - CircleStartDegrees));
                if (Distance(point, center) < 1 || sweep < 1) break;
                circle.SweepAngle = Math.Round(sign * sweep, 2);
                break;
            }
        }
    }

    /// <inheritdoc/>
    public bool EndDrag()
    {
        if (_drag is null) return false;
        _drag = null;
        return Key != _dragOriginKey;
    }

    // 卡尺宽度 = 采样半宽 × 垂直采样间隔；半宽最多 63 步，超出时放大间隔（原图最大 10px）。
    private void SetHalfBand(double halfBand)
    {
        var baseStep = Math.Clamp(_dragStep, .1, 10);
        var step = halfBand <= MaximumHalfWidth * baseStep ? baseStep : Math.Min(10, Math.Ceiling(halfBand / MaximumHalfWidth * 100) / 100);
        _node.BandSampleStep = Bound ? Round(step / Scale) : step;
        _node.HalfWidth = Math.Clamp((int)Math.Floor(Math.Min(halfBand, MaximumHalfWidth * 10) / step + .5), 0, MaximumHalfWidth);
    }

    // 找线的扫描方向（与节点一致：沿框高度方向，反向扫描时相反）。
    private (double X, double Y) ScanDirection()
    {
        var sign = _node is FindVisionLineNodeModel { ReverseScan: true } ? -1 : 1;
        var (ux, uy) = U;
        return (-uy * sign, ux * sign);
    }

    private double MiddleAngle => (CircleStartDegrees + CircleSweep * .5) * Math.PI / 180;

    private PointD CirclePoint(double radius, double t) => CirclePointAt(radius, (CircleStartDegrees + CircleSweep * t) * Math.PI / 180);

    private PointD CirclePointAt(double radius, double angle) => new(Center.X + radius * Math.Cos(angle), Center.Y + radius * Math.Sin(angle));

    private void WriteCenter(WorkflowVisionRoi roi, PointD image)
    {
        if (Similarity is { } system)
        {
            var local = system.ImageToLocal.Map(new Coordinate2D(image.X, image.Y));
            roi.CenterX = Round(local.X); roi.CenterY = Round(local.Y);
            return;
        }
        roi.CenterX = Round(image.X); roi.CenterY = Round(image.Y);
    }

    private PointD ToImage(double x, double y)
    {
        if (Similarity is not { } system) return new PointD(x, y);
        var point = system.LocalToImage.Map(new Coordinate2D(x, y));
        return new PointD(point.X, point.Y);
    }

    private static PointD Offset(PointD point, (double X, double Y) direction, double distance) =>
        new(point.X + direction.X * distance, point.Y + direction.Y * distance);

    private static double Dot(PointD point, PointD origin, (double X, double Y) direction) =>
        (point.X - origin.X) * direction.X + (point.Y - origin.Y) * direction.Y;

    private static double Distance(PointD a, PointD b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    private static double Modulo(double value, double divisor) => ((value % divisor) + divisor) % divisor;

    private static double Positive(double degrees) { var value = Modulo(degrees, 360); return value < 1e-9 ? 360 : value; }

    private static double Normalize(double degrees) => Modulo(degrees + 180, 360) - 180;

    private double Round(double value) => Math.Round(value, Bound ? 4 : 2);
}
