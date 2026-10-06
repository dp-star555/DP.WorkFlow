using DP.Vision;
using DP.Vision.Algorithms;

namespace DP.WorkFlow.Vision.UI;

/// <summary>卡尺可拖动控制点。</summary>
public enum EVisionCaliperHandle
{
    /// <summary>扫描起点；圆弧卡尺为起始角。</summary>
    Start,
    /// <summary>扫描终点（箭头端）；圆弧卡尺为终止角。</summary>
    End,
    /// <summary>采样带宽度控制点（扫描路径两侧）。</summary>
    Width,
    /// <summary>采样带内部：整体平移。</summary>
    Body,
    /// <summary>圆弧圆心：整体平移。</summary>
    Center,
    /// <summary>圆弧半径控制点（扫描圆弧中点）。</summary>
    Radius
}

/// <summary>
/// 卡尺节点在图像上的可视化与拖动编辑，支持直线和圆弧两种形状：显示采样带外框、带箭头的扫描方向、
/// 与扫描方向垂直的投影线（圆弧为半径方向），以及各控制点。拖动直接写回节点参数；坐标均为原图像素。
/// 算法在扫描路径上每个位置沿投影线取 2×半宽+1 个点求平均得到灰度剖面，再沿箭头方向找边缘。
/// 绑定坐标系的节点参数是局部单位，只显示提示，不在原图上直接拖动。
/// </summary>
public sealed class VisionCaliperGizmo
{
    private const uint BandColor = 0xFF22D3EE;
    private const uint SampleColor = 0x7022D3EE;
    private const uint AxisColor = 0xFFFACC15;
    private const uint HandleColor = 0xFFF8FAFC;
    private const double HandleScreenRadius = 5;
    private const int MaximumHalfWidth = 63;
    private const double MinimumBandStep = .1;
    private const double MaximumBandStep = 10;
    private const double ProjectionScreenSpacing = 10;
    private readonly MeasureVisionCaliperNodeModel _node;
    private EVisionCaliperHandle? _drag;
    private PointD _dragAnchor;
    private string _dragOriginKey = string.Empty;
    private (double StartX, double StartY, double EndX, double EndY, double CenterX, double CenterY, double BandSampleStep) _dragOrigin;

    /// <summary>为卡尺节点创建可视化编辑器。</summary>
    /// <param name="node">节点窗口中的隔离编辑副本。</param>
    public VisionCaliperGizmo(MeasureVisionCaliperNodeModel node) => _node = node ?? throw new ArgumentNullException(nameof(node));

    /// <summary>参数为原图像素时可在图上拖动；绑定坐标系后参数为局部单位，不能直接拖动。</summary>
    public bool IsEditable => _node.Coordinates is null;

    /// <summary>当前是否正在拖动。</summary>
    public bool IsDragging => _drag.HasValue;

    /// <summary>当前形状。</summary>
    public EVisionCaliperShape Shape => _node.Shape;

    /// <summary>参数签名；任一几何参数变化时预览需要重绘。</summary>
    public string Key => FormattableString.Invariant(
        $"caliper:{_node.Shape}:{_node.StartX}:{_node.StartY}:{_node.EndX}:{_node.EndY}:{_node.CenterX}:{_node.CenterY}:{_node.Radius}:{_node.StartAngle}:{_node.SweepAngle}:{_node.HalfWidth}:{_node.BandSampleStep}:{_node.Polarity}:{_node.Coordinates is null}");

    /// <summary>扫描起点（原图像素）；圆弧卡尺为起始角处的弧上点。</summary>
    public PointD Start => IsArc ? ArcPoint(_node.Radius, 0) : new(_node.StartX, _node.StartY);

    /// <summary>扫描终点（原图像素）；圆弧卡尺为终止角处的弧上点。</summary>
    public PointD End => IsArc ? ArcPoint(_node.Radius, 1) : new(_node.EndX, _node.EndY);

    /// <summary>圆弧圆心（原图像素）。</summary>
    public PointD Center => new(_node.CenterX, _node.CenterY);

    /// <summary>采样带单侧宽度（原图像素）= 采样半宽 × 垂直采样间隔。</summary>
    public double HalfBand => _node.HalfWidth * _node.BandSampleStep;

    /// <summary>可拖出的最大单侧带宽（原图像素）= 63 × 10。</summary>
    public static double MaximumHalfBand => MaximumHalfWidth * MaximumBandStep;

    /// <summary>图上标注：形状尺寸、带宽、采样数与极性。</summary>
    public string Caption
    {
        get
        {
            var polarity = _node.Polarity switch
            {
                ECaliperPolarity.Rising => "暗→亮",
                ECaliperPolarity.Falling => "亮→暗",
                _ => "任意"
            };
            var band = FormattableString.Invariant($"带宽 ±{HalfBand:0.##}px（{_node.HalfWidth * 2 + 1} 点 × 间隔 {_node.BandSampleStep:0.##}px）· 极性 {polarity}");
            return IsArc
                ? FormattableString.Invariant($"圆弧卡尺 半径 {_node.Radius:0.#}px · 起始 {_node.StartAngle:0.#}° 扫描 {_node.SweepAngle:0.#}° · {band}")
                : FormattableString.Invariant($"卡尺 长度 {Length:0.#}px · {band}");
        }
    }

    /// <summary>状态栏操作提示。</summary>
    public string Hint => !IsEditable
        ? "卡尺已绑定坐标系，参数为局部单位，请在参数页编辑。"
        : IsArc
            ? "拖动起点/终点调整扫描角度，拖动弧中点菱形调整半径，拖动两侧方块调整带宽，拖动圆心或采样带内部整体平移。"
            : "拖动起点/终点调整扫描方向与长度，拖动两侧方块调整带宽，拖动采样带内部整体平移。";

    private bool IsArc => _node.Shape == EVisionCaliperShape.Arc;

    private double Length => IsArc ? Math.Abs(_node.SweepAngle) * Math.PI / 180 * Math.Max(0, _node.Radius) : Distance(Start, End);

    private bool IsDrawable => IsEditable && (IsArc
        ? _node.Radius > 0 && Math.Abs(_node.SweepAngle) > 1e-9 && double.IsFinite(_node.Radius) && double.IsFinite(_node.SweepAngle)
        : Length >= 1e-9);

    /// <summary>
    /// 切换卡尺形状并保持在图上的位置：直线转圆弧时得到经过原起点和终点的半圆，圆弧转直线时取圆弧两端为起终点。
    /// </summary>
    /// <param name="shape">目标形状。</param>
    /// <returns>形状发生变化时返回 <see langword="true"/>。</returns>
    public bool SetShape(EVisionCaliperShape shape)
    {
        if (_node.Shape == shape || !Enum.IsDefined(shape)) return false;
        if (shape == EVisionCaliperShape.Arc)
        {
            var (start, end) = (new PointD(_node.StartX, _node.StartY), new PointD(_node.EndX, _node.EndY));
            var length = Distance(start, end);
            if (length >= 2)
            {
                _node.CenterX = Round((start.X + end.X) / 2); _node.CenterY = Round((start.Y + end.Y) / 2);
                _node.Radius = Round(length / 2);
                _node.StartAngle = Round(Math.Atan2(start.Y - _node.CenterY, start.X - _node.CenterX) * 180 / Math.PI);
                _node.SweepAngle = 180;
            }
            if (_node.Radius < HalfBand) SetHalfBand(_node.Radius, _node.BandSampleStep);
        }
        else
        {
            var start = Start;
            var end = Math.Abs(Math.Abs(_node.SweepAngle) - 360) < 1e-6 ? ArcPoint(_node.Radius, .5) : End;
            _node.StartX = Round(start.X); _node.StartY = Round(start.Y); _node.EndX = Round(end.X); _node.EndY = Round(end.Y);
        }
        _node.Shape = shape;
        return true;
    }

    /// <summary>
    /// 生成卡尺叠加图形。控制点大小按屏幕像素固定，需要传入当前每屏幕像素对应的原图像素数。
    /// </summary>
    /// <param name="imagePixelsPerScreenPixel">当前缩放下 1 个屏幕像素对应的原图像素。</param>
    public IReadOnlyList<Visual> Visuals(double imagePixelsPerScreenPixel)
    {
        if (!IsDrawable) return Array.Empty<Visual>();
        var unit = Math.Max(1e-6, imagePixelsPerScreenPixel);
        return IsArc ? ArcVisuals(unit) : LineVisuals(unit);
    }

    private List<Visual> LineVisuals(double unit)
    {
        var (direction, normal) = Axes();
        var visuals = new List<Visual>();
        PointD At(PointD origin, double along, double across) =>
            new(origin.X + direction.X * along + normal.X * across, origin.Y + direction.Y * along + normal.Y * across);

        // 采样带外框。
        visuals.Add(new Visual("caliper-band", new ContourGeometry(new[]
        {
            At(Start, 0, -HalfBand), At(End, 0, -HalfBand), At(End, 0, HalfBand), At(Start, 0, HalfBand)
        }, closed: true), BandColor, Caption));
        // 投影线：垂直于扫描方向横跨采样带，表示每个扫描位置在哪条线上取点求平均；
        // 算法每 1px 一条，按屏幕间距抽稀显示，外框始终是真实带宽。
        if (HalfBand > 0)
            foreach (var (k, along) in ProjectionStations(unit))
                visuals.Add(new Visual($"caliper-sample{k}", new ContourGeometry(new[]
                {
                    At(Start, along, -HalfBand), At(Start, along, HalfBand)
                }), SampleColor));
        // 扫描方向：中心线 + 终点箭头。
        visuals.Add(new Visual("caliper-axis", new ContourGeometry(new[] { Start, End }), AxisColor));
        visuals.Add(Arrow(End, direction, normal, unit));
        // 控制点：起点（圆）、终点（实心圆）、两侧宽度把手（方块）。
        var radius = HandleScreenRadius * unit;
        AddEndHandles(visuals, radius);
        var middle = At(Start, Length / 2, 0);
        var angle = Math.Atan2(direction.Y, direction.X);
        foreach (var side in new[] { -1, 1 })
            visuals.Add(new Visual($"caliper-width{side}", new RectangleGeometry(At(middle, 0, side * WidthHandleOffset(unit)),
                radius * 1.6, radius * 1.6, angle), HandleColor));
        return visuals;
    }

    private List<Visual> ArcVisuals(double unit)
    {
        var visuals = new List<Visual>();
        double r = _node.Radius, inner = Math.Max(0, r - HalfBand), outer = r + HalfBand;
        var segments = Math.Clamp((int)Math.Ceiling(Math.Abs(_node.SweepAngle) / 3), 8, 120);
        IEnumerable<PointD> Arc(double radius) => Enumerable.Range(0, segments + 1).Select(i => ArcPoint(radius, (double)i / segments));

        // 采样带外框：外弧 + 内弧（反向）闭合成环形扇区。
        visuals.Add(new Visual("caliper-band", new ContourGeometry(Arc(outer).Concat(Arc(inner).Reverse()), closed: true), BandColor, Caption));
        // 投影线：沿半径方向横跨采样带。
        if (HalfBand > 0)
            foreach (var (k, along) in ProjectionStations(unit))
                visuals.Add(new Visual($"caliper-sample{k}", new ContourGeometry(new[]
                {
                    ArcPoint(inner, along / Length), ArcPoint(outer, along / Length)
                }), SampleColor));
        // 扫描方向：中心圆弧 + 终点切向箭头。
        visuals.Add(new Visual("caliper-axis", new ContourGeometry(Arc(r)), AxisColor));
        var (tangent, radial) = ArcAxes(1);
        visuals.Add(Arrow(End, tangent, radial, unit));
        // 控制点：圆心（十字 + 圆）、起点、终点、半径（菱形）、两侧宽度把手。
        var handle = HandleScreenRadius * unit;
        visuals.Add(new Visual("caliper-center-cross", new ContourGeometry(new[]
        {
            new PointD(Center.X - handle * 2, Center.Y), new PointD(Center.X + handle * 2, Center.Y)
        }), AxisColor));
        visuals.Add(new Visual("caliper-center-cross2", new ContourGeometry(new[]
        {
            new PointD(Center.X, Center.Y - handle * 2), new PointD(Center.X, Center.Y + handle * 2)
        }), AxisColor));
        visuals.Add(new Visual("caliper-center", new EllipseGeometry(Center, handle, handle), HandleColor));
        AddEndHandles(visuals, handle);
        var (_, middleRadial) = ArcAxes(.5);
        var angle = Math.Atan2(middleRadial.Y, middleRadial.X);
        visuals.Add(new Visual("caliper-radius", new RectangleGeometry(ArcPoint(r, .5), handle * 1.7, handle * 1.7, angle + Math.PI / 4), AxisColor));
        foreach (var side in new[] { -1, 1 })
            visuals.Add(new Visual($"caliper-width{side}", new RectangleGeometry(ArcPoint(r + side * WidthHandleOffset(unit), .5),
                handle * 1.6, handle * 1.6, angle), HandleColor));
        return visuals;
    }

    private void AddEndHandles(List<Visual> visuals, double radius)
    {
        visuals.Add(new Visual("caliper-start", new EllipseGeometry(Start, radius, radius), HandleColor));
        visuals.Add(new Visual("caliper-end", new EllipseGeometry(End, radius * 1.2, radius * 1.2), AxisColor));
    }

    private Visual Arrow(PointD tip, (double X, double Y) direction, (double X, double Y) normal, double unit)
    {
        var head = Math.Min(Length / 3, 14 * unit);
        PointD At(double along, double across) =>
            new(tip.X + direction.X * along + normal.X * across, tip.Y + direction.Y * along + normal.Y * across);
        return new Visual("caliper-arrow", new ContourGeometry(new[] { At(-head, -head * .55), tip, At(-head, head * .55) }), AxisColor);
    }

    // 投影线位置：沿扫描路径按屏幕间距抽稀，不画两端（与外框重合）。
    private IEnumerable<(int Index, double Along)> ProjectionStations(double unit)
    {
        var spacing = Math.Max(1, ProjectionScreenSpacing * unit);
        var count = (int)Math.Floor(Length / spacing);
        for (var k = 1; k < count; k++) yield return (k, k * spacing);
    }

    // 宽度把手离扫描路径至少 1.5 个把手半径（圆弧另有中点半径把手，至少 2.5 个），带宽很窄时也能抓住。
    private double WidthHandleOffset(double unit) => Math.Max(HalfBand, HandleScreenRadius * (IsArc ? 2.5 : 1.5) * unit);

    /// <summary>命中测试：返回指针下的控制点；终点优先于起点，控制点优先于采样带内部。</summary>
    /// <param name="point">原图坐标。</param>
    /// <param name="imagePixelsPerScreenPixel">当前每屏幕像素对应的原图像素。</param>
    public EVisionCaliperHandle? Hit(PointD point, double imagePixelsPerScreenPixel)
    {
        if (!IsDrawable) return null;
        var unit = Math.Max(1e-6, imagePixelsPerScreenPixel);
        var tolerance = (HandleScreenRadius + 4) * unit;
        if (Distance(point, End) <= tolerance) return EVisionCaliperHandle.End;
        if (Distance(point, Start) <= tolerance) return EVisionCaliperHandle.Start;
        if (IsArc)
        {
            if (Distance(point, Center) <= tolerance) return EVisionCaliperHandle.Center;
            if (Distance(point, ArcPoint(_node.Radius, .5)) <= tolerance) return EVisionCaliperHandle.Radius;
            var offset = WidthHandleOffset(unit);
            if (Distance(point, ArcPoint(_node.Radius + offset, .5)) <= tolerance || Distance(point, ArcPoint(_node.Radius - offset, .5)) <= tolerance)
                return EVisionCaliperHandle.Width;
            var (rho, t) = Polar(point);
            return t is >= 0 and <= 1 && Math.Abs(rho - _node.Radius) <= Math.Max(HalfBand, tolerance) ? EVisionCaliperHandle.Body : null;
        }
        var (along, across) = Project(point);
        if (Math.Abs(along - Length / 2) <= tolerance && Math.Abs(Math.Abs(across) - WidthHandleOffset(unit)) <= tolerance)
            return EVisionCaliperHandle.Width;
        if (along >= 0 && along <= Length && Math.Abs(across) <= Math.Max(HalfBand, tolerance))
            return EVisionCaliperHandle.Body;
        return null;
    }

    /// <summary>开始拖动控制点。</summary>
    /// <param name="handle">控制点。</param>
    /// <param name="point">按下位置（原图坐标）。</param>
    public void BeginDrag(EVisionCaliperHandle handle, PointD point)
    {
        _drag = handle;
        _dragAnchor = point;
        _dragOriginKey = Key;
        _dragOrigin = (_node.StartX, _node.StartY, _node.EndX, _node.EndY, _node.CenterX, _node.CenterY, _node.BandSampleStep);
    }

    /// <summary>按指针位置更新正在拖动的控制点并写回节点参数。</summary>
    /// <param name="point">当前指针位置（原图坐标）。</param>
    /// <returns>参数发生变化时返回 <see langword="true"/>。</returns>
    public bool Drag(PointD point)
    {
        if (_drag is not { } handle) return false;
        var before = Key;
        if (IsArc) DragArc(handle, point);
        else DragLine(handle, point);
        return Key != before;
    }

    private void DragLine(EVisionCaliperHandle handle, PointD point)
    {
        switch (handle)
        {
            case EVisionCaliperHandle.Start:
                if (Distance(point, End) >= 1) { _node.StartX = Round(point.X); _node.StartY = Round(point.Y); }
                break;
            case EVisionCaliperHandle.End:
                if (Distance(point, Start) >= 1) { _node.EndX = Round(point.X); _node.EndY = Round(point.Y); }
                break;
            case EVisionCaliperHandle.Width:
                SetHalfBand(Math.Abs(Project(point).Across), _dragOrigin.BandSampleStep);
                break;
            case EVisionCaliperHandle.Body:
                var dx = point.X - _dragAnchor.X;
                var dy = point.Y - _dragAnchor.Y;
                _node.StartX = Round(_dragOrigin.StartX + dx); _node.StartY = Round(_dragOrigin.StartY + dy);
                _node.EndX = Round(_dragOrigin.EndX + dx); _node.EndY = Round(_dragOrigin.EndY + dy);
                break;
        }
    }

    private void DragArc(EVisionCaliperHandle handle, PointD point)
    {
        var sign = Math.Sign(_node.SweepAngle);
        var angle = Math.Atan2(point.Y - _node.CenterY, point.X - _node.CenterX) * 180 / Math.PI;
        switch (handle)
        {
            case EVisionCaliperHandle.Start:
            {
                // 终止角保持不动，起始角跟随指针，扫描方向不变。
                var endAngle = _node.StartAngle + _node.SweepAngle;
                var sweep = Positive(sign * (endAngle - angle));
                if (Distance(point, Center) < 1 || sweep < 1) break;
                _node.StartAngle = Round(Normalize(angle)); _node.SweepAngle = Round(sign * sweep);
                break;
            }
            case EVisionCaliperHandle.End:
            {
                var sweep = Positive(sign * (angle - _node.StartAngle));
                if (Distance(point, Center) < 1 || sweep < 1) break;
                _node.SweepAngle = Round(sign * sweep);
                break;
            }
            case EVisionCaliperHandle.Radius:
                _node.Radius = Round(Math.Max(Math.Max(1, HalfBand), Distance(point, Center)));
                break;
            case EVisionCaliperHandle.Width:
                SetHalfBand(Math.Min(_node.Radius, Math.Abs(Distance(point, Center) - _node.Radius)), _dragOrigin.BandSampleStep);
                break;
            case EVisionCaliperHandle.Center or EVisionCaliperHandle.Body:
                _node.CenterX = Round(_dragOrigin.CenterX + point.X - _dragAnchor.X);
                _node.CenterY = Round(_dragOrigin.CenterY + point.Y - _dragAnchor.Y);
                break;
        }
    }

    /// <summary>结束拖动。</summary>
    /// <returns>本次拖动是否修改了参数。</returns>
    public bool EndDrag()
    {
        if (_drag is null) return false;
        _drag = null;
        return Key != _dragOriginKey;
    }

    /// <summary>
    /// 按目标单侧带宽设置采样半宽；半宽最多 63 步，超出时放大垂直采样间隔（最大 10px），
    /// 所以带宽可拖到 630px。缩回时恢复拖动开始时的间隔，不会自动改得比用户设定更细。
    /// </summary>
    private void SetHalfBand(double halfBand, double originStep)
    {
        var baseStep = Math.Clamp(originStep, MinimumBandStep, MaximumBandStep);
        var target = Math.Min(halfBand, MaximumHalfBand);
        var step = target <= MaximumHalfWidth * baseStep
            ? baseStep
            : Math.Min(MaximumBandStep, Math.Ceiling(target / MaximumHalfWidth * 100) / 100);
        _node.BandSampleStep = step;
        _node.HalfWidth = Math.Clamp((int)Math.Floor(target / step + .5), 0, MaximumHalfWidth);
        // 圆弧内侧不能越过圆心。
        if (IsArc) while (_node.HalfWidth > 0 && _node.HalfWidth * step > _node.Radius) _node.HalfWidth--;
    }

    private PointD ArcPoint(double radius, double t)
    {
        var angle = (_node.StartAngle + _node.SweepAngle * t) * Math.PI / 180;
        return new PointD(_node.CenterX + radius * Math.Cos(angle), _node.CenterY + radius * Math.Sin(angle));
    }

    // 扫描进度 t 处的切向（沿扫描方向）与径向（向外）单位向量。
    private ((double X, double Y) Tangent, (double X, double Y) Radial) ArcAxes(double t)
    {
        var angle = (_node.StartAngle + _node.SweepAngle * t) * Math.PI / 180;
        var sign = Math.Sign(_node.SweepAngle);
        return ((-Math.Sin(angle) * sign, Math.Cos(angle) * sign), (Math.Cos(angle), Math.Sin(angle)));
    }

    // 点相对圆心的距离和扫描进度（0..1 在扫描范围内）。
    private (double Rho, double T) Polar(PointD point)
    {
        var angle = Math.Atan2(point.Y - _node.CenterY, point.X - _node.CenterX) * 180 / Math.PI;
        var sweep = Math.Abs(_node.SweepAngle);
        var delta = Modulo(Math.Sign(_node.SweepAngle) * (angle - _node.StartAngle), 360);
        return (Distance(point, Center), sweep >= 360 ? delta / 360 : delta / sweep);
    }

    private ((double X, double Y) Direction, (double X, double Y) Normal) Axes()
    {
        var length = Math.Max(1e-9, Length);
        var direction = ((End.X - Start.X) / length, (End.Y - Start.Y) / length);
        return (direction, (-direction.Item2, direction.Item1));
    }

    private (double Along, double Across) Project(PointD point)
    {
        var (direction, normal) = Axes();
        var dx = point.X - Start.X;
        var dy = point.Y - Start.Y;
        return (dx * direction.X + dy * direction.Y, dx * normal.X + dy * normal.Y);
    }

    private static double Modulo(double value, double divisor) => ((value % divisor) + divisor) % divisor;

    // 角度差折算到 (0, 360]：拖到与另一端重合视为整圆。
    private static double Positive(double degrees) { var value = Modulo(degrees, 360); return value < 1e-9 ? 360 : value; }

    private static double Normalize(double degrees) { var value = Modulo(degrees + 180, 360) - 180; return value; }

    private static double Distance(PointD a, PointD b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    private static double Round(double value) => Math.Round(value, 2);
}

/// <summary>卡尺节点“区域类型”下拉框的一项：直线卡尺或圆弧卡尺。</summary>
/// <param name="Shape">卡尺形状。</param>
/// <param name="Text">显示文字。</param>
public sealed record VisionCaliperShapeChoice(EVisionCaliperShape Shape, string Text)
{
    /// <summary>全部卡尺形状。</summary>
    public static IReadOnlyList<VisionCaliperShapeChoice> All { get; } = Array.AsReadOnly(new[]
    {
        new VisionCaliperShapeChoice(EVisionCaliperShape.Line, "直线卡尺"),
        new VisionCaliperShapeChoice(EVisionCaliperShape.Arc, "圆弧卡尺")
    });

    /// <summary>下拉框图标键。</summary>
    public string IconKey => "caliper-" + Shape;

    /// <inheritdoc/>
    public override string ToString() => Text;
}
