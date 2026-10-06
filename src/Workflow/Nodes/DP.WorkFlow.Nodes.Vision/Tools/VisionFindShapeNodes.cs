using System.ComponentModel;
using DP.Vision;
using DP.Vision.Algorithms;

namespace DP.WorkFlow;

/// <summary>每把卡尺取哪一条边。</summary>
public enum EVisionEdgeChoice
{
    /// <summary>沿扫描方向第一条。</summary>
    [Description("首个")]
    First,
    /// <summary>梯度绝对值最大的一条。</summary>
    [Description("最强")]
    Strongest,
    /// <summary>沿扫描方向最后一条。</summary>
    [Description("末个")]
    Last
}

/// <summary>找圆时卡尺的径向扫描方向。</summary>
public enum EVisionFindCircleDirection
{
    /// <summary>从圆内向圆外。</summary>
    [Description("由内向外")]
    Outward,
    /// <summary>从圆外向圆内。</summary>
    [Description("由外向内")]
    Inward
}

/// <summary>
/// 找线、找圆的共同部分：在图像页画的搜索ROI上排布多把卡尺，每把取一个边缘点，再鲁棒拟合。
/// 绑定坐标系后搜索ROI随工件移动旋转，长度类参数为业务单位。
/// </summary>
public abstract class FindVisionShapeNodeModel : AnalyzeVisionFrameNodeModel, IWorkflowVisionAlgorithmNode
{
    /// <summary>卡尺实现选择。</summary>
    [Browsable(false)]
    public VisionAlgorithmSelection CaliperAlgorithm { get; set; } = new() { ImplementationId = "managed.caliper" };
    /// <summary>拟合实现选择。</summary>
    [Browsable(false)]
    public abstract VisionAlgorithmSelection FitterAlgorithm { get; set; }
    /// <summary>拟合能力契约。</summary>
    protected abstract Type FitterContract { get; }
    /// <inheritdoc/>
    public IReadOnlyList<WorkflowVisionAlgorithmSlot> GetAlgorithmSlots() => new[]
    {
        new WorkflowVisionAlgorithmSlot("caliper", typeof(ICaliperMeasurer), CaliperAlgorithm),
        new WorkflowVisionAlgorithmSlot("fitter", FitterContract, FitterAlgorithm)
    };
    /// <inheritdoc/>
    public override EWorkflowVisionRange RangeCapability => EWorkflowVisionRange.Region;
    /// <summary>搜索ROI只定义几何，不叠加区域掩膜。</summary>
    public override bool SupportsRegionMask => false;

    /// <summary>卡尺数量。</summary>
    [WorkflowProperty("卡尺数量", "沿搜索范围均匀排布的卡尺数，3..256。", Category = "卡尺")]
    public int CaliperCount { get; set; } = 10;
    /// <summary>每把卡尺的垂直采样半宽。</summary>
    [WorkflowProperty("采样半宽", "每把卡尺垂直于扫描方向的单侧采样步数0..63，实际半宽=半宽×间隔。", Category = "卡尺")]
    public int HalfWidth { get; set; } = 2;
    /// <summary>垂直采样间隔；绑定坐标系时为业务单位。</summary>
    [WorkflowProperty("垂直采样间隔", "原图有效间隔须在0.1..10像素；绑定坐标系时为业务单位。", Category = "卡尺")]
    public double BandSampleStep { get; set; } = 1;
    /// <summary>最小绝对灰度梯度。</summary>
    [WorkflowProperty("最小梯度", "绝对灰度变化/像素，低于此值不算边缘。", Category = "边缘")]
    public double MinimumGradient { get; set; } = 5;
    /// <summary>边缘极性。</summary>
    [WorkflowProperty("边缘极性", "沿扫描方向判断明暗变化。", Category = "边缘")]
    public ECaliperPolarity Polarity { get; set; }
    /// <summary>每把卡尺取哪条边。</summary>
    [WorkflowProperty("边缘选择", "一把卡尺找到多条边时取哪一条。", Category = "边缘")]
    public EVisionEdgeChoice EdgeChoice { get; set; }
    /// <summary>边缘最小间距；绑定坐标系时为业务单位。</summary>
    [WorkflowProperty("边缘最小间距", "同一把卡尺内梯度强者优先的非极大抑制距离；原图像素，绑定坐标系时为业务单位。", Category = "边缘")]
    public double MinimumSeparation { get; set; } = 2;
    /// <summary>内点距离阈值；绑定坐标系时为业务单位。</summary>
    [WorkflowProperty("内点距离阈值", "边缘点到拟合结果的距离不超过此值才算内点；原图像素，绑定坐标系时为业务单位。", Category = "拟合")]
    public double DistanceThreshold { get; set; } = 1;
    /// <summary>最少内点数。</summary>
    [WorkflowProperty("最少内点", "至少3，内点不足时执行失败。", Category = "拟合")]
    public int MinimumInliers { get; set; } = 3;

    /// <inheritdoc/>
    public override IReadOnlyList<string> ValidateConfiguration()
    {
        var errors = base.ValidateConfiguration().ToList();
        if (!FullImage) errors.Add("搜索范围只使用图像页绘制的ROI。");
        if (CaliperCount < 3 || CaliperCount > 256) errors.Add("卡尺数量必须为3..256。");
        if (HalfWidth < 0 || HalfWidth > 63) errors.Add("采样半宽必须为0..63。");
        if (!double.IsFinite(BandSampleStep) || BandSampleStep <= 0) errors.Add("垂直采样间隔必须为正数。");
        if (!double.IsFinite(MinimumGradient) || MinimumGradient <= 0) errors.Add("最小梯度必须为正数。");
        if (!double.IsFinite(MinimumSeparation) || MinimumSeparation <= 0) errors.Add("边缘最小间距必须为正数。");
        if (!double.IsFinite(DistanceThreshold) || DistanceThreshold <= 0) errors.Add("内点距离阈值必须为正数。");
        if (MinimumInliers < 3 || MinimumInliers > CaliperCount) errors.Add("最少内点必须为3到卡尺数量之间。");
        return errors;
    }

    /// <summary>唯一启用的包含ROI，且是指定形状；绑定坐标系时为业务坐标表达。</summary>
    internal T SearchRoi<T>(EWorkflowVisionRoiShape shape, string message) where T : Geometry
    {
        var enabled = Regions?.Where(r => r.Enabled).ToArray() ?? [];
        if (enabled.Length != 1 || enabled[0].Exclude || enabled[0].Shape != shape) throw new InvalidOperationException(message);
        return (T)enabled[0].ToGeometry();
    }

    /// <summary>本节点每把卡尺的原图扫描线；绑定坐标系时按本帧坐标系换算。</summary>
    internal abstract IReadOnlyList<(PointD Start, PointD End)> Scans(VisionCoordinateSystem? coordinates);

    /// <summary>业务坐标点换算到原图；未绑定坐标系时原样返回。</summary>
    protected static PointD ToImage(PointD point, VisionCoordinateSystem? coordinates)
    {
        if (coordinates is null) return point;
        var image = coordinates.LocalToImage.Map(new Coordinate2D(point.X, point.Y));
        return new PointD(image.X, image.Y);
    }
}

/// <summary>在矩形搜索框内找直线：卡尺沿框的宽度方向排布，沿高度方向扫描。</summary>
[WorkflowNode("Vision.FindLine", DisplayName = "找线", Category = WorkflowVisionCategories.Measurement,
    Description = "在矩形搜索框内排布多把卡尺找边缘点，鲁棒拟合直线。")]
public sealed class FindVisionLineNodeModel : FindVisionShapeNodeModel
{
    private const string BoxMessage = "请在图像页绘制一个矩形搜索框（只能有一个，且不能是排除ROI）；卡尺沿框的宽度方向排布、沿高度方向扫描。";
    /// <inheritdoc/>
    public override string NodeType => "Vision.FindLine";
    /// <inheritdoc/>
    [Browsable(false)]
    public override VisionAlgorithmSelection FitterAlgorithm { get; set; } = new() { ImplementationId = "managed.robust-line" };
    /// <inheritdoc/>
    protected override Type FitterContract => typeof(IRobustLineFitter);
    /// <summary>是否反向扫描。</summary>
    [WorkflowProperty("反向扫描", "默认沿搜索框高度方向从上到下扫描（框旋转时随之旋转）；开启后从下到上。极性按扫描方向判断。", Category = "卡尺")]
    public bool ReverseScan { get; set; }

    /// <inheritdoc/>
    public override IReadOnlyList<string> ValidateConfiguration()
    {
        var errors = base.ValidateConfiguration().ToList();
        try { _ = SearchBox(); } catch (InvalidOperationException error) { errors.Add(error.Message); }
        return errors;
    }

    internal RectangleGeometry SearchBox() => SearchRoi<RectangleGeometry>(EWorkflowVisionRoiShape.Rectangle, BoxMessage);

    internal override IReadOnlyList<(PointD Start, PointD End)> Scans(VisionCoordinateSystem? coordinates)
    {
        var box = SearchBox();
        double scale = coordinates?.SimilarityScale ?? 1, angle = box.Angle + (coordinates?.RotationRadians ?? 0);
        var center = ToImage(box.Center, coordinates);
        double width = box.Width * scale, half = box.Height * scale / 2, sign = ReverseScan ? -1 : 1;
        // 宽度方向 u、高度方向 v，与矩形的顺时针旋转角一致（图像Y向下）。
        double ux = Math.Cos(angle), uy = Math.Sin(angle), vx = -uy * sign, vy = ux * sign;
        return Enumerable.Range(0, CaliperCount).Select(i =>
        {
            double t = -width / 2 + width * (i + .5) / CaliperCount;
            double px = center.X + t * ux, py = center.Y + t * uy;
            return (new PointD(px - half * vx, py - half * vy), new PointD(px + half * vx, py + half * vy));
        }).ToArray();
    }
}

/// <summary>沿期望圆找圆：卡尺沿圆周排布，沿半径方向扫描。</summary>
[WorkflowNode("Vision.FindCircle", DisplayName = "找圆", Category = WorkflowVisionCategories.Measurement,
    Description = "沿期望圆排布多把径向卡尺找边缘点，鲁棒拟合圆。")]
public sealed class FindVisionCircleNodeModel : FindVisionShapeNodeModel
{
    private const string CircleMessage = "请在图像页绘制一个圆形ROI作为期望圆（只能有一个，宽高相等，且不能是排除ROI）。";
    /// <inheritdoc/>
    public override string NodeType => "Vision.FindCircle";
    /// <inheritdoc/>
    [Browsable(false)]
    public override VisionAlgorithmSelection FitterAlgorithm { get; set; } = new() { ImplementationId = "managed.robust-circle" };
    /// <inheritdoc/>
    protected override Type FitterContract => typeof(IRobustCircleFitter);
    /// <summary>径向搜索长度；绑定坐标系时为业务单位。</summary>
    [WorkflowProperty("搜索长度", "每把卡尺沿半径方向的扫描长度，以期望圆为中心向内外各一半；原图像素，绑定坐标系时为业务单位。", Category = "卡尺")]
    public double SearchLength { get; set; } = 20;
    /// <summary>径向扫描方向。</summary>
    [WorkflowProperty("扫描方向", "极性按扫描方向判断：由内向外时暗圆亮背景的边为“由暗到亮”。", Category = "卡尺")]
    public EVisionFindCircleDirection Direction { get; set; }
    /// <summary>起始角，度。</summary>
    [WorkflowProperty("起始角", "从X轴正向起顺时针（图像Y向下）；绑定坐标系时相对坐标系X轴。", Category = "卡尺", Unit = "°")]
    public double StartAngle { get; set; }
    /// <summary>扫描角度范围，度。</summary>
    [WorkflowProperty("扫描角度", "卡尺覆盖的圆周角度，绝对值 (0, 360]；360为整圆，只找圆弧时缩小。", Category = "卡尺", Unit = "°")]
    public double SweepAngle { get; set; } = 360;

    /// <inheritdoc/>
    public override IReadOnlyList<string> ValidateConfiguration()
    {
        var errors = base.ValidateConfiguration().ToList();
        if (!double.IsFinite(SearchLength) || SearchLength <= 0) errors.Add("搜索长度必须为正数。");
        if (!double.IsFinite(StartAngle) || !double.IsFinite(SweepAngle) || Math.Abs(SweepAngle) < 1e-9 || Math.Abs(SweepAngle) > 360)
            errors.Add("扫描角度绝对值必须在 (0, 360] 内。");
        try
        {
            var circle = ExpectedCircle();
            if (circle.RadiusX - SearchLength / 2 < 0) errors.Add("搜索长度的一半不能超过期望圆半径。");
        }
        catch (InvalidOperationException error) { errors.Add(error.Message); }
        return errors;
    }

    internal EllipseGeometry ExpectedCircle()
    {
        var circle = SearchRoi<EllipseGeometry>(EWorkflowVisionRoiShape.Ellipse, CircleMessage);
        if (Math.Abs(circle.RadiusX - circle.RadiusY) > 1e-6 * Math.Max(circle.RadiusX, circle.RadiusY)) throw new InvalidOperationException(CircleMessage);
        return circle;
    }

    internal override IReadOnlyList<(PointD Start, PointD End)> Scans(VisionCoordinateSystem? coordinates)
    {
        var circle = ExpectedCircle();
        double scale = coordinates?.SimilarityScale ?? 1, rotation = coordinates?.RotationRadians ?? 0;
        var center = ToImage(circle.Center, coordinates);
        double radius = circle.RadiusX * scale, half = SearchLength * scale / 2;
        double inner = Direction == EVisionFindCircleDirection.Outward ? radius - half : radius + half;
        double outer = Direction == EVisionFindCircleDirection.Outward ? radius + half : radius - half;
        bool full = Math.Abs(Math.Abs(SweepAngle) - 360) < 1e-9;
        return Enumerable.Range(0, CaliperCount).Select(i =>
        {
            // 整圆均匀分布不重复端点；圆弧时卡尺落在各等分段中点。
            double fraction = full ? (double)i / CaliperCount : (i + .5) / CaliperCount;
            double angle = (StartAngle + SweepAngle * fraction) * Math.PI / 180 + rotation, dx = Math.Cos(angle), dy = Math.Sin(angle);
            return (new PointD(center.X + inner * dx, center.Y + inner * dy), new PointD(center.X + outer * dx, center.Y + outer * dy));
        }).ToArray();
    }
}

/// <summary>找线结果：拟合直线、各卡尺边缘点及显示图形。</summary>
public sealed class VisionFindLineResult : IVisionGeometryFact
{
    internal VisionFindLineResult(RobustLineResult fit, VisionShapeProbe probe) { Fit = fit; Probe = probe; }
    /// <summary>鲁棒拟合原始结果。</summary>
    public RobustLineResult Fit { get; }
    internal VisionShapeProbe Probe { get; }
    /// <inheritdoc/>
    public string FrameId => Fit.FrameId;
    /// <summary>带来源的拟合直线，可直接连接距离节点。</summary>
    public VisionLine MeasuredLine => Fit.MeasuredLine;
    /// <summary>直线方向角（度，从原图X轴起顺时针，(-90, 90]）。</summary>
    public double AngleDegrees
    {
        get
        {
            double angle = Math.Atan2(Fit.B.Y - Fit.A.Y, Fit.B.X - Fit.A.X) * 180 / Math.PI;
            return angle > 90 ? angle - 180 : angle <= -90 ? angle + 180 : angle;
        }
    }
    /// <summary>各卡尺找到的边缘点（未找到的卡尺不在列表中），带来源。</summary>
    public IReadOnlyList<VisionPoint> EdgePoints => Probe.Points(Fit.CoordinateSystem);
    /// <summary>卡尺数量。</summary>
    public int CaliperCount => Probe.Scans.Count;
    /// <summary>找到边缘的卡尺数。</summary>
    public int FoundCount => Probe.Edges.Count;
    /// <summary>内点数。</summary>
    public int InlierCount => Fit.InlierCount;
    /// <summary>内点距离RMS，原图像素。</summary>
    public double RmsError => Fit.RmsError;
    /// <inheritdoc/>
    public IReadOnlyList<Geometry> DisplayGeometry => Probe.Display(new ContourGeometry(new[] { Fit.A, Fit.B }));
    /// <inheritdoc/>
    public string Summary => FormattableString.Invariant(
        $"找线：{FoundCount}/{CaliperCount} 把卡尺找到边缘，内点 {InlierCount}，RMS {RmsError:F3}px，角度 {AngleDegrees:F2}°{Probe.OutsideNote}");
}

/// <summary>找圆结果：拟合圆、各卡尺边缘点及显示图形。</summary>
public sealed class VisionFindCircleResult : IVisionGeometryFact
{
    internal VisionFindCircleResult(RobustCircleResult fit, VisionShapeProbe probe) { Fit = fit; Probe = probe; }
    /// <summary>鲁棒拟合原始结果。</summary>
    public RobustCircleResult Fit { get; }
    internal VisionShapeProbe Probe { get; }
    /// <inheritdoc/>
    public string FrameId => Fit.FrameId;
    /// <summary>带来源的圆心，可直接连接几何节点。</summary>
    public VisionPoint MeasuredCenter => Fit.MeasuredCenter;
    /// <summary>半径，原图像素。</summary>
    public double Radius => Fit.Radius;
    /// <summary>直径，原图像素。</summary>
    public double Diameter => Fit.Diameter;
    /// <summary>业务单位半径；未绑定坐标系时为空。</summary>
    public double? LocalRadius => Fit.LocalRadius;
    /// <summary>各卡尺找到的边缘点（未找到的卡尺不在列表中），带来源。</summary>
    public IReadOnlyList<VisionPoint> EdgePoints => Probe.Points(Fit.CoordinateSystem);
    /// <summary>卡尺数量。</summary>
    public int CaliperCount => Probe.Scans.Count;
    /// <summary>找到边缘的卡尺数。</summary>
    public int FoundCount => Probe.Edges.Count;
    /// <summary>内点数。</summary>
    public int InlierCount => Fit.InlierCount;
    /// <summary>内点径向距离RMS，原图像素。</summary>
    public double RmsError => Fit.RmsError;
    /// <inheritdoc/>
    public IReadOnlyList<Geometry> DisplayGeometry => Probe.Display(new EllipseGeometry(Fit.Center, Fit.Radius, Fit.Radius));
    /// <inheritdoc/>
    public string Summary => FormattableString.Invariant(
        $"找圆：{FoundCount}/{CaliperCount} 把卡尺找到边缘，内点 {InlierCount}，半径 {Radius:F3}px，RMS {RmsError:F3}px{Probe.OutsideNote}");
}

/// <summary>一次卡尺排布的扫描线和各卡尺选中的边缘点。</summary>
internal sealed class VisionShapeProbe(string frameId, IReadOnlyList<(PointD Start, PointD End)> scans, IReadOnlyList<PointD> edges, int outside)
{
    public IReadOnlyList<(PointD Start, PointD End)> Scans { get; } = scans;
    public IReadOnlyList<PointD> Edges { get; } = edges;
    public string OutsideNote => outside == 0 ? "" : $"；{outside} 把卡尺超出图像未测量";
    public IReadOnlyList<VisionPoint> Points(VisionCoordinateSystem? coordinates) => Edges.Select(p => new VisionPoint(frameId, p, coordinates)).ToArray();
    // 第一项为拟合图形（承载摘要），随后是各卡尺扫描线和边缘点。
    public IReadOnlyList<Geometry> Display(Geometry fitted) => new[] { fitted }
        .Concat(Scans.Select(s => (Geometry)new ContourGeometry(new[] { s.Start, s.End })))
        .Concat(Edges.Select(p => (Geometry)new EllipseGeometry(p, 1.5, 1.5))).ToArray();
}

/// <summary>按节点排布卡尺、逐把取边缘点；超出图像的卡尺跳过并计数。</summary>
internal static class VisionShapeFinding
{
    internal static VisionShapeProbe Probe(FindVisionShapeNodeModel node, IWorkflowNodeExecutionContext context, ImageFrame frame,
        VisionCoordinateSystem? coordinates, CancellationToken token)
    {
        var errors = node.ValidateConfiguration();
        if (errors.Count > 0) throw new InvalidOperationException(string.Join("；", errors));
        double scale = coordinates?.SimilarityScale ?? 1, step = node.BandSampleStep * scale;
        if (step < .1 || step > 10) throw new InvalidOperationException("垂直采样间隔换算到原图须在0.1..10像素。");
        var scans = node.Scans(coordinates);
        var edges = new List<PointD>(); int outside = 0;
        foreach (var (start, end) in scans)
        {
            token.ThrowIfCancellationRequested();
            double length = Math.Sqrt(Math.Pow(end.X - start.X, 2) + Math.Pow(end.Y - start.Y, 2));
            if (length < 4) throw new InvalidOperationException("卡尺扫描长度换算到原图不足4像素，请加大搜索范围。");
            if (!Inside(frame.Image.Info, start, end, node.HalfWidth * step)) { outside++; continue; }
            var options = new CaliperOptions(start, end, node.HalfWidth, node.MinimumGradient, node.Polarity, node.MinimumSeparation * scale, step);
            var result = WorkflowVisionAlgorithmInvocation.Invoke(context, "caliper", node.CaliperAlgorithm, "managed.caliper",
                (ICaliperMeasurer measurer) => measurer.Measure(frame, options, token), token);
            if (Pick(result.Edges, node.EdgeChoice) is { } edge) edges.Add(edge.Position);
        }
        return new VisionShapeProbe(frame.FrameId, scans, edges, outside);
    }

    private static CaliperEdge? Pick(IReadOnlyList<CaliperEdge> edges, EVisionEdgeChoice choice) => edges.Count == 0 ? null : choice switch
    {
        EVisionEdgeChoice.First => edges.MinBy(e => e.Distance),
        EVisionEdgeChoice.Last => edges.MaxBy(e => e.Distance),
        _ => edges.MaxBy(e => Math.Abs(e.Gradient))
    };

    // 与卡尺的采样规则一致：采样带四角都须落在像素中心范围 [0.5, 尺寸-0.5] 内。
    private static bool Inside(ImageInfo info, PointD start, PointD end, double halfBand)
    {
        double dx = end.X - start.X, dy = end.Y - start.Y, length = Math.Sqrt(dx * dx + dy * dy);
        double nx = -dy / length * halfBand, ny = dx / length * halfBand;
        return new[] { (start.X + nx, start.Y + ny), (start.X - nx, start.Y - ny), (end.X + nx, end.Y + ny), (end.X - nx, end.Y - ny) }
            .All(p => p.Item1 >= .5 && p.Item2 >= .5 && p.Item1 <= info.Width - .5 && p.Item2 <= info.Height - .5);
    }
}

/// <summary>找线：卡尺取点后鲁棒拟合直线。</summary>
public sealed class FindVisionLineNodeHandler : WorkflowNodeHandler<FindVisionLineNodeModel>
{
    /// <inheritdoc/>
    protected override ValueTask<NodeExecutionResult> ExecuteAsync(FindVisionLineNodeModel node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
    {
        var frame = context.ResolveInput(node.Frame) ?? throw new InvalidOperationException("输入帧为空。");
        var coordinates = node.ResolveCoordinates(frame, context);
        var probe = VisionShapeFinding.Probe(node, context, frame, coordinates, cancellationToken);
        if (probe.Edges.Count < node.MinimumInliers)
            throw new InvalidOperationException($"找线失败：只有 {probe.Edges.Count} 把卡尺找到边缘，至少需要 {node.MinimumInliers} 个点。");
        var fit = WorkflowVisionAlgorithmInvocation.Invoke(context, "fitter", node.FitterAlgorithm, "managed.robust-line",
            (IRobustLineFitter fitter) => fitter.Fit(frame.FrameId, probe.Edges, node.DistanceThreshold * (coordinates?.SimilarityScale ?? 1), 256, node.MinimumInliers, cancellationToken),
            cancellationToken);
        if (coordinates is not null) fit = fit.InCoordinates(coordinates);
        var result = new VisionFindLineResult(fit, probe);
        return ValueTask.FromResult(NodeExecutionResult.Continue(output: result, projection: WorkflowVisionFrameScope.Stage(context, frame, result)));
    }
}

/// <summary>找圆：径向卡尺取点后鲁棒拟合圆。</summary>
public sealed class FindVisionCircleNodeHandler : WorkflowNodeHandler<FindVisionCircleNodeModel>
{
    /// <inheritdoc/>
    protected override ValueTask<NodeExecutionResult> ExecuteAsync(FindVisionCircleNodeModel node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
    {
        var frame = context.ResolveInput(node.Frame) ?? throw new InvalidOperationException("输入帧为空。");
        var coordinates = node.ResolveCoordinates(frame, context);
        var probe = VisionShapeFinding.Probe(node, context, frame, coordinates, cancellationToken);
        if (probe.Edges.Count < node.MinimumInliers)
            throw new InvalidOperationException($"找圆失败：只有 {probe.Edges.Count} 把卡尺找到边缘，至少需要 {node.MinimumInliers} 个点。");
        var fit = WorkflowVisionAlgorithmInvocation.Invoke(context, "fitter", node.FitterAlgorithm, "managed.robust-circle",
            (IRobustCircleFitter fitter) => fitter.Fit(frame.FrameId, probe.Edges, node.DistanceThreshold * (coordinates?.SimilarityScale ?? 1), 256, node.MinimumInliers, cancellationToken),
            cancellationToken);
        if (coordinates is not null) fit = fit.InCoordinates(coordinates);
        var result = new VisionFindCircleResult(fit, probe);
        return ValueTask.FromResult(NodeExecutionResult.Continue(output: result, projection: WorkflowVisionFrameScope.Stage(context, frame, result)));
    }
}
