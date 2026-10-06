using DP.Vision;
using DP.Vision.Algorithms;

namespace DP.WorkFlow.Vision.UI;

/// <summary>卡尺可拖动控制点。</summary>
public enum EVisionCaliperHandle
{
    /// <summary>扫描起点。</summary>
    Start,
    /// <summary>扫描终点（箭头端）。</summary>
    End,
    /// <summary>采样带宽度控制点（扫描线两侧）。</summary>
    Width,
    /// <summary>采样带内部：整体平移。</summary>
    Body
}

/// <summary>
/// 卡尺节点在图像上的可视化与拖动编辑：显示采样带外框、带箭头的扫描方向、垂直于扫描方向的投影线，
/// 以及起点/终点/宽度三类控制点。拖动直接写回节点的起终点、采样半宽与垂直采样间隔；坐标均为原图像素。
/// 算法在扫描线上每个位置沿投影线取 2×半宽+1 个点求平均得到灰度剖面，再沿箭头方向找边缘。
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
    private (double StartX, double StartY, double EndX, double EndY, int HalfWidth, double BandSampleStep) _dragOrigin;

    /// <summary>为卡尺节点创建可视化编辑器。</summary>
    /// <param name="node">节点窗口中的隔离编辑副本。</param>
    public VisionCaliperGizmo(MeasureVisionCaliperNodeModel node) => _node = node ?? throw new ArgumentNullException(nameof(node));

    /// <summary>参数为原图像素时可在图上拖动；绑定坐标系后参数为局部单位，不能直接拖动。</summary>
    public bool IsEditable => _node.Coordinates is null;

    /// <summary>当前是否正在拖动。</summary>
    public bool IsDragging => _drag.HasValue;

    /// <summary>参数签名；任一几何参数变化时预览需要重绘。</summary>
    public string Key => FormattableString.Invariant(
        $"caliper:{_node.StartX}:{_node.StartY}:{_node.EndX}:{_node.EndY}:{_node.HalfWidth}:{_node.BandSampleStep}:{_node.Polarity}:{_node.Coordinates is null}");

    /// <summary>扫描起点（原图像素）。</summary>
    public PointD Start => new(_node.StartX, _node.StartY);

    /// <summary>扫描终点（原图像素）。</summary>
    public PointD End => new(_node.EndX, _node.EndY);

    /// <summary>采样带单侧宽度（原图像素）= 采样半宽 × 垂直采样间隔。</summary>
    public double HalfBand => _node.HalfWidth * _node.BandSampleStep;

    /// <summary>可拖出的最大单侧带宽（原图像素）= 63 × 10。</summary>
    public static double MaximumHalfBand => MaximumHalfWidth * MaximumBandStep;

    /// <summary>图上标注：长度、带宽、采样数与极性。</summary>
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
            return FormattableString.Invariant(
                $"卡尺 长度 {Length:0.#}px · 带宽 ±{HalfBand:0.##}px（{_node.HalfWidth * 2 + 1} 点 × 间隔 {_node.BandSampleStep:0.##}px）· 极性 {polarity}");
        }
    }

    private double Length => Distance(Start, End);

    /// <summary>
    /// 生成卡尺叠加图形。控制点大小按屏幕像素固定，需要传入当前每屏幕像素对应的原图像素数。
    /// </summary>
    /// <param name="imagePixelsPerScreenPixel">当前缩放下 1 个屏幕像素对应的原图像素。</param>
    public IReadOnlyList<Visual> Visuals(double imagePixelsPerScreenPixel)
    {
        if (!IsEditable || Length < 1e-9) return Array.Empty<Visual>();
        var unit = Math.Max(1e-6, imagePixelsPerScreenPixel);
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
        {
            var spacing = Math.Max(1, ProjectionScreenSpacing * unit);
            var count = (int)Math.Floor(Length / spacing);
            for (var k = 1; k < count; k++)
                visuals.Add(new Visual($"caliper-sample{k}", new ContourGeometry(new[]
                {
                    At(Start, k * spacing, -HalfBand), At(Start, k * spacing, HalfBand)
                }), SampleColor));
        }
        // 扫描方向：中心线 + 终点箭头。
        visuals.Add(new Visual("caliper-axis", new ContourGeometry(new[] { Start, End }), AxisColor));
        var head = Math.Min(Length / 3, 14 * unit);
        visuals.Add(new Visual("caliper-arrow", new ContourGeometry(new[]
        {
            At(End, -head, -head * 0.55), End, At(End, -head, head * 0.55)
        }), AxisColor));
        // 控制点：起点（圆）、终点（实心圆）、两侧宽度把手（方块）。
        var radius = HandleScreenRadius * unit;
        visuals.Add(new Visual("caliper-start", new EllipseGeometry(Start, radius, radius), HandleColor));
        visuals.Add(new Visual("caliper-end", new EllipseGeometry(End, radius * 1.2, radius * 1.2), AxisColor));
        var middle = At(Start, Length / 2, 0);
        var angle = Math.Atan2(direction.Y, direction.X);
        foreach (var side in new[] { -1, 1 })
            visuals.Add(new Visual($"caliper-width{side}", new RectangleGeometry(At(middle, 0, side * Math.Max(HalfBand, radius * 1.5)),
                radius * 1.6, radius * 1.6, angle), HandleColor));
        return visuals;
    }

    /// <summary>命中测试：返回指针下的控制点；终点优先于起点，控制点优先于采样带内部。</summary>
    /// <param name="point">原图坐标。</param>
    /// <param name="imagePixelsPerScreenPixel">当前每屏幕像素对应的原图像素。</param>
    public EVisionCaliperHandle? Hit(PointD point, double imagePixelsPerScreenPixel)
    {
        if (!IsEditable || Length < 1e-9) return null;
        var tolerance = (HandleScreenRadius + 4) * Math.Max(1e-6, imagePixelsPerScreenPixel);
        if (Distance(point, End) <= tolerance) return EVisionCaliperHandle.End;
        if (Distance(point, Start) <= tolerance) return EVisionCaliperHandle.Start;
        var (along, across) = Project(point);
        var widthOffset = Math.Max(HalfBand, HandleScreenRadius * 1.5 * imagePixelsPerScreenPixel);
        if (Math.Abs(along - Length / 2) <= tolerance && Math.Abs(Math.Abs(across) - widthOffset) <= tolerance)
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
        _dragOrigin = (_node.StartX, _node.StartY, _node.EndX, _node.EndY, _node.HalfWidth, _node.BandSampleStep);
    }

    /// <summary>按指针位置更新正在拖动的控制点并写回节点参数。</summary>
    /// <param name="point">当前指针位置（原图坐标）。</param>
    /// <returns>参数发生变化时返回 <see langword="true"/>。</returns>
    public bool Drag(PointD point)
    {
        if (_drag is not { } handle) return false;
        var before = Key;
        switch (handle)
        {
            case EVisionCaliperHandle.Start:
                if (Distance(point, End) >= 1) { _node.StartX = Round(point.X); _node.StartY = Round(point.Y); }
                break;
            case EVisionCaliperHandle.End:
                if (Distance(point, Start) >= 1) { _node.EndX = Round(point.X); _node.EndY = Round(point.Y); }
                break;
            case EVisionCaliperHandle.Width:
                SetHalfBand(Math.Abs(Project(point).Across));
                break;
            case EVisionCaliperHandle.Body:
                var dx = point.X - _dragAnchor.X;
                var dy = point.Y - _dragAnchor.Y;
                _node.StartX = Round(_dragOrigin.StartX + dx); _node.StartY = Round(_dragOrigin.StartY + dy);
                _node.EndX = Round(_dragOrigin.EndX + dx); _node.EndY = Round(_dragOrigin.EndY + dy);
                break;
        }
        return Key != before;
    }

    /// <summary>结束拖动。</summary>
    /// <returns>本次拖动是否修改了参数。</returns>
    public bool EndDrag()
    {
        if (_drag is null) return false;
        _drag = null;
        return (_node.StartX, _node.StartY, _node.EndX, _node.EndY, _node.HalfWidth, _node.BandSampleStep) != _dragOrigin;
    }

    /// <summary>
    /// 按目标单侧带宽设置采样半宽；半宽最多 63 步，超出时放大垂直采样间隔（最大 10px），
    /// 所以带宽可拖到 630px。缩回时恢复拖动开始时的间隔，不会自动改得比用户设定更细。
    /// </summary>
    private void SetHalfBand(double halfBand)
    {
        var baseStep = Math.Clamp(_dragOrigin.BandSampleStep, MinimumBandStep, MaximumBandStep);
        var target = Math.Min(halfBand, MaximumHalfBand);
        var step = target <= MaximumHalfWidth * baseStep
            ? baseStep
            : Math.Min(MaximumBandStep, Math.Ceiling(target / MaximumHalfWidth * 100) / 100);
        _node.BandSampleStep = step;
        _node.HalfWidth = Math.Clamp((int)Math.Round(target / step), 0, MaximumHalfWidth);
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

    private static double Distance(PointD a, PointD b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    private static double Round(double value) => Math.Round(value, 2);
}
