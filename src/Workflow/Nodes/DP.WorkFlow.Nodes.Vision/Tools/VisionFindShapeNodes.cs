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
    /// <summary>梯度绝对值最大的一条（边缘对模式为两边梯度绝对值之和最大的一对）。</summary>
    [Description("最强")]
    Strongest,
    /// <summary>沿扫描方向最后一条。</summary>
    [Description("末个")]
    Last
}

/// <summary>
/// 一把卡尺的原图位置：沿 <see cref="Start"/>→<see cref="End"/> 扫描，垂直方向单侧带宽 <see cref="HalfBand"/>。
/// 找圆的卡尺带 <see cref="ArcCenter"/>：采样带是以它为圆心的圆环扇形，<see cref="HalfBand"/> 为期望圆上的单侧弧长。
/// </summary>
/// <param name="Start">扫描起点。</param><param name="End">扫描终点。</param><param name="HalfBand">垂直方向单侧带宽，原图像素。</param>
/// <param name="ArcCenter">圆环扇形卡尺的圆心；直线卡尺为空。</param>
public sealed record VisionCaliperScan(PointD Start, PointD End, double HalfBand, PointD? ArcCenter = null)
{
    /// <summary>采样带外轮廓（原图像素，闭合多边形）；圆环扇形的内外弧按 <paramref name="arcSegments"/> 段折线近似。</summary>
    /// <param name="minimumHalfBand">显示用的最小单侧带宽（太窄时按屏幕最小宽度显示）。</param>
    /// <param name="arcSegments">每条弧的折线段数。</param>
    public IReadOnlyList<PointD> Outline(double minimumHalfBand = 0, int arcSegments = 12)
    {
        double half = Math.Max(HalfBand, minimumHalfBand);
        if (ArcCenter is not { } c)
        {
            double dx = End.X - Start.X, dy = End.Y - Start.Y, length = Math.Max(1e-12, Math.Sqrt(dx * dx + dy * dy));
            double nx = -dy / length * half, ny = dx / length * half;
            return new[] { new PointD(Start.X - nx, Start.Y - ny), new PointD(End.X - nx, End.Y - ny), new PointD(End.X + nx, End.Y + ny), new PointD(Start.X + nx, Start.Y + ny) };
        }
        var (angle, halfAngle, startRadius, endRadius) = Sector(half);
        var points = new List<PointD>(2 * arcSegments + 2);
        for (int i = 0; i <= arcSegments; i++) points.Add(Polar(c, startRadius, angle - halfAngle + 2 * halfAngle * i / arcSegments));
        for (int i = arcSegments; i >= 0; i--) points.Add(Polar(c, endRadius, angle - halfAngle + 2 * halfAngle * i / arcSegments));
        return points;
    }

    /// <summary>扇形参数：中心角、单侧张角（按期望圆半径上的弧长 <paramref name="half"/> 换算）、起止半径。</summary>
    internal (double Angle, double HalfAngle, double StartRadius, double EndRadius) Sector(double half)
    {
        var c = ArcCenter ?? throw new InvalidOperationException("不是圆环扇形卡尺。");
        double startRadius = Distance(c, Start), endRadius = Distance(c, End), radius = Math.Max(1e-9, (startRadius + endRadius) / 2);
        var mid = startRadius >= endRadius ? Start : End;
        return (Math.Atan2(mid.Y - c.Y, mid.X - c.X), Math.Min(Math.PI, half / radius), startRadius, endRadius);
    }

    internal static PointD Polar(PointD center, double radius, double angle) => new(center.X + radius * Math.Cos(angle), center.Y + radius * Math.Sin(angle));
    private static double Distance(PointD a, PointD b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
}

/// <summary>边缘对模式下一把卡尺选中的边缘对。</summary>
/// <param name="First">第一个边缘。</param><param name="Second">第二个边缘。</param><param name="Width">宽度，原图像素。</param>
public sealed record VisionFoundEdgePair(PointD First, PointD Second, double Width);

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
    [WorkflowProperty("边缘选择", "一把卡尺找到多条边（边缘对模式为多对）时取哪一条（对）。", Category = "边缘")]
    public EVisionEdgeChoice EdgeChoice { get; set; }
    /// <summary>边缘模式。</summary>
    [WorkflowProperty("边缘模式", "单边缘：每把卡尺取一个边缘点拟合；边缘对：每把卡尺取一对相邻、极性相反的边缘，用中点拟合中心线/中心圆并输出各卡尺宽度。边缘对模式下“边缘极性”指第一个边缘。", Category = "边缘")]
    public EVisionCaliperEdgeMode EdgeMode { get; set; }
    /// <summary>边缘对最小宽度；绑定坐标系时为业务单位。</summary>
    [WorkflowPropertyVisibleWhen(nameof(EdgeMode), nameof(EVisionCaliperEdgeMode.Pair))]
    [WorkflowProperty("最小宽度", "边缘对宽度下限（含）；原图像素，绑定坐标系时为业务单位。", Category = "边缘对")]
    public double MinimumPairWidth { get; set; }
    /// <summary>边缘对最大宽度；绑定坐标系时为业务单位。</summary>
    [WorkflowPropertyVisibleWhen(nameof(EdgeMode), nameof(EVisionCaliperEdgeMode.Pair))]
    [WorkflowProperty("最大宽度", "边缘对宽度上限（含）；原图像素，绑定坐标系时为业务单位。", Category = "边缘对")]
    public double MaximumPairWidth { get; set; } = 1000;
    /// <summary>边缘最小间距；绑定坐标系时为业务单位。</summary>
    [WorkflowProperty("边缘最小间距", "同一把卡尺内梯度强者优先的非极大抑制距离；原图像素，绑定坐标系时为业务单位。", Category = "边缘")]
    public double MinimumSeparation { get; set; } = 2;
    /// <summary>内点距离阈值；绑定坐标系时为业务单位。</summary>
    [WorkflowProperty("内点距离阈值", "边缘点到拟合结果的距离不超过此值才算内点；原图像素，绑定坐标系时为业务单位。", Category = "拟合")]
    public double DistanceThreshold { get; set; } = 1;
    /// <summary>拟合时剔除的点数。</summary>
    [WorkflowProperty("忽略点数", "拟合时按残差从大到小剔除的点数（逐个剔除并重新拟合），用于排除毛刺、缺口等干扰；被剔除的点显示为忽略点。0..卡尺数量−最少内点。", Category = "拟合")]
    public int IgnoreCount { get; set; }
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
        if (IgnoreCount < 0 || IgnoreCount > CaliperCount - MinimumInliers) errors.Add("忽略点数必须为0到（卡尺数量−最少内点）之间。");
        if (!Enum.IsDefined(EdgeMode) || !double.IsFinite(MinimumPairWidth) || !double.IsFinite(MaximumPairWidth) || MinimumPairWidth < 0 || MaximumPairWidth <= MinimumPairWidth)
            errors.Add("边缘对宽度范围无效：最小宽度 ≥ 0 且小于最大宽度。");
        return errors;
    }

    /// <summary>新建节点的默认搜索ROI标识；拿到第一张图像前用占位尺寸，便于在没有图像时也能通过校验。</summary>
    internal const string DefaultRoiId = "search";

    /// <summary>新建节点的占位搜索ROI（还没有图像时使用）。</summary>
    protected abstract WorkflowVisionRoi PlaceholderRoi();

    /// <summary>按图像尺寸给出合适的默认搜索ROI（居中）。</summary>
    protected abstract WorkflowVisionRoi DefaultRoi(int imageWidth, int imageHeight);

    /// <summary>
    /// 搜索ROI为空或仍是新建时的占位值时，按首张图像尺寸改为居中的默认搜索ROI；用户改过或绑定坐标系时不动。
    /// </summary>
    /// <param name="imageWidth">图像宽度。</param><param name="imageHeight">图像高度。</param>
    /// <returns>修改了搜索ROI时返回 <see langword="true"/>。</returns>
    public bool FitPlaceholderSearchRoi(int imageWidth, int imageHeight)
    {
        if (Coordinates is not null || imageWidth < 8 || imageHeight < 8) return false;
        // 旧配方没有搜索范围时也补一个居中的默认范围，便于直接在图上拖动调整。
        if (Regions is null || Regions.Count == 0) { Regions = new() { DefaultRoi(imageWidth, imageHeight) }; return true; }
        if (Regions.Count != 1 || !SameRoi(Regions[0], PlaceholderRoi())) return false;
        Regions = new() { DefaultRoi(imageWidth, imageHeight) };
        return true;
    }

    private static bool SameRoi(WorkflowVisionRoi a, WorkflowVisionRoi b) => a.Id == b.Id && a.Shape == b.Shape && a.Enabled == b.Enabled
        && a.Exclude == b.Exclude && a.CenterX == b.CenterX && a.CenterY == b.CenterY && a.Width == b.Width && a.Height == b.Height && a.Angle == b.Angle;

    /// <summary>唯一启用的包含ROI，且是指定形状；绑定坐标系时为业务坐标表达。</summary>
    internal T SearchRoi<T>(EWorkflowVisionRoiShape shape, string message) where T : Geometry
    {
        var enabled = Regions?.Where(r => r.Enabled).ToArray() ?? [];
        if (enabled.Length != 1 || enabled[0].Exclude || enabled[0].Shape != shape) throw new InvalidOperationException(message);
        return (T)enabled[0].ToGeometry();
    }

    /// <summary>本节点每把卡尺的原图扫描线；绑定坐标系时按本帧坐标系换算。</summary>
    internal abstract IReadOnlyList<(PointD Start, PointD End)> Scans(VisionCoordinateSystem? coordinates);

    /// <summary>圆环扇形卡尺的原图圆心；直线排布的卡尺为空。</summary>
    internal virtual PointD? ArcCenter(VisionCoordinateSystem? coordinates) => null;

    /// <summary>按当前搜索ROI排布的各把卡尺（原图像素），供图像页预览；搜索ROI无效时抛出 <see cref="InvalidOperationException"/>。</summary>
    /// <param name="coordinates">本帧坐标系；未绑定时为空。</param>
    public IReadOnlyList<VisionCaliperScan> CaliperScans(VisionCoordinateSystem? coordinates = null)
    {
        double halfBand = HalfWidth * BandSampleStep * (coordinates?.SimilarityScale ?? 1);
        var center = ArcCenter(coordinates);
        return Scans(coordinates).Select(s => new VisionCaliperScan(s.Start, s.End, halfBand, center)).ToArray();
    }

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
    /// <summary>新建时带一个占位搜索框，首次拿到图像时自动居中。</summary>
    public FindVisionLineNodeModel() => Regions = new() { PlaceholderRoi() };
    /// <inheritdoc/>
    public override string NodeType => "Vision.FindLine";
    /// <inheritdoc/>
    protected override WorkflowVisionRoi PlaceholderRoi() => new()
    { Id = DefaultRoiId, Shape = EWorkflowVisionRoiShape.Rectangle, CenterX = 320, CenterY = 240, Width = 240, Height = 80 };
    /// <inheritdoc/>
    protected override WorkflowVisionRoi DefaultRoi(int imageWidth, int imageHeight) => new()
    {
        Id = DefaultRoiId, Shape = EWorkflowVisionRoiShape.Rectangle, CenterX = imageWidth / 2d, CenterY = imageHeight / 2d,
        Width = Math.Round(imageWidth * .5), Height = Math.Round(Math.Max(8, imageHeight * .2))
    };
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
    /// <summary>新建时带一个占位期望圆，首次拿到图像时自动居中。</summary>
    public FindVisionCircleNodeModel() => Regions = new() { PlaceholderRoi() };
    /// <inheritdoc/>
    public override string NodeType => "Vision.FindCircle";
    /// <inheritdoc/>
    protected override WorkflowVisionRoi PlaceholderRoi() => new()
    { Id = DefaultRoiId, Shape = EWorkflowVisionRoiShape.Ellipse, CenterX = 320, CenterY = 240, Width = 160, Height = 160 };
    /// <inheritdoc/>
    protected override WorkflowVisionRoi DefaultRoi(int imageWidth, int imageHeight)
    {
        var diameter = Math.Round(Math.Max(SearchLength + 4, Math.Min(imageWidth, imageHeight) * .4));
        return new() { Id = DefaultRoiId, Shape = EWorkflowVisionRoiShape.Ellipse, CenterX = imageWidth / 2d, CenterY = imageHeight / 2d, Width = diameter, Height = diameter };
    }
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

    internal override PointD? ArcCenter(VisionCoordinateSystem? coordinates) => ToImage(ExpectedCircle().Center, coordinates);

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
    internal VisionFindLineResult(RobustLineResult fit, VisionShapeProbe probe, IReadOnlyList<int> inliers) { Fit = fit; Probe = probe; InlierIndices = inliers; }
    /// <summary>参与拟合的计算点在 <see cref="EdgePoints"/> 中的序号（已排除“忽略点数”剔除的点）。</summary>
    public IReadOnlyList<int> InlierIndices { get; }
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
    /// <summary>各卡尺找到的边缘点（边缘对模式为中点；未找到的卡尺不在列表中），带来源。</summary>
    public IReadOnlyList<VisionPoint> EdgePoints => Probe.Points(Fit.CoordinateSystem);
    /// <summary>与 <see cref="EdgePoints"/> 同序：真为参与拟合的计算点，假为被忽略的点。</summary>
    public IReadOnlyList<bool> Inliers => Probe.InlierFlags(InlierIndices);
    /// <summary>参与拟合的计算点。</summary>
    public IReadOnlyList<VisionPoint> InlierPoints => Probe.Select(Fit.CoordinateSystem, InlierIndices, inliers: true);
    /// <summary>被忽略的点。</summary>
    public IReadOnlyList<VisionPoint> OutlierPoints => Probe.Select(Fit.CoordinateSystem, InlierIndices, inliers: false);
    /// <summary>各把卡尺的原图位置。</summary>
    public IReadOnlyList<VisionCaliperScan> CaliperScans => Probe.CaliperScans;
    /// <summary>边缘对模式下与 <see cref="EdgePoints"/> 同序的边缘对；单边缘模式为空。</summary>
    public IReadOnlyList<VisionFoundEdgePair> EdgePairs => Probe.Pairs;
    /// <summary>边缘对模式下计算点的平均宽度，原图像素；单边缘模式为空。</summary>
    public double? MeanWidth => Probe.InlierWidths(InlierIndices) is { Count: > 0 } w ? w.Average() : null;
    /// <summary>边缘对模式下计算点的最小宽度。</summary>
    public double? MinimumWidth => Probe.InlierWidths(InlierIndices) is { Count: > 0 } w ? w.Min() : null;
    /// <summary>边缘对模式下计算点的最大宽度。</summary>
    public double? MaximumWidth => Probe.InlierWidths(InlierIndices) is { Count: > 0 } w ? w.Max() : null;
    /// <summary>卡尺数量。</summary>
    public int CaliperCount => Probe.Scans.Count;
    /// <summary>找到边缘的卡尺数。</summary>
    public int FoundCount => Probe.Edges.Count;
    /// <summary>内点数。</summary>
    public int InlierCount => InlierIndices.Count;
    /// <summary>被忽略的点数。</summary>
    public int OutlierCount => FoundCount - InlierCount;
    /// <summary>内点距离RMS，原图像素。</summary>
    public double RmsError => Fit.RmsError;
    /// <inheritdoc/>
    public IReadOnlyList<Geometry> DisplayGeometry => Probe.Display(new ContourGeometry(new[] { Fit.A, Fit.B }));
    /// <inheritdoc/>
    public string Summary => FormattableString.Invariant(
        $"找线：{FoundCount}/{CaliperCount} 把卡尺找到边缘，计算点 {InlierCount}，忽略点 {OutlierCount}，RMS {RmsError:F3}px，角度 {AngleDegrees:F2}°{WidthNote}{Probe.OutsideNote}");
    private string WidthNote => MeanWidth is { } mean ? FormattableString.Invariant($"，平均宽度 {mean:F3}px（{MinimumWidth:F3}..{MaximumWidth:F3}）") : "";
}

/// <summary>找圆结果：拟合圆、各卡尺边缘点及显示图形。</summary>
public sealed class VisionFindCircleResult : IVisionGeometryFact
{
    internal VisionFindCircleResult(RobustCircleResult fit, VisionShapeProbe probe, IReadOnlyList<int> inliers) { Fit = fit; Probe = probe; InlierIndices = inliers; }
    /// <summary>参与拟合的计算点在 <see cref="EdgePoints"/> 中的序号（已排除“忽略点数”剔除的点）。</summary>
    public IReadOnlyList<int> InlierIndices { get; }
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
    /// <summary>各卡尺找到的边缘点（边缘对模式为中点；未找到的卡尺不在列表中），带来源。</summary>
    public IReadOnlyList<VisionPoint> EdgePoints => Probe.Points(Fit.CoordinateSystem);
    /// <summary>与 <see cref="EdgePoints"/> 同序：真为参与拟合的计算点，假为被忽略的点。</summary>
    public IReadOnlyList<bool> Inliers => Probe.InlierFlags(InlierIndices);
    /// <summary>参与拟合的计算点。</summary>
    public IReadOnlyList<VisionPoint> InlierPoints => Probe.Select(Fit.CoordinateSystem, InlierIndices, inliers: true);
    /// <summary>被忽略的点。</summary>
    public IReadOnlyList<VisionPoint> OutlierPoints => Probe.Select(Fit.CoordinateSystem, InlierIndices, inliers: false);
    /// <summary>各把卡尺的原图位置。</summary>
    public IReadOnlyList<VisionCaliperScan> CaliperScans => Probe.CaliperScans;
    /// <summary>边缘对模式下与 <see cref="EdgePoints"/> 同序的边缘对；单边缘模式为空。</summary>
    public IReadOnlyList<VisionFoundEdgePair> EdgePairs => Probe.Pairs;
    /// <summary>边缘对模式下计算点的平均宽度（环宽），原图像素；单边缘模式为空。</summary>
    public double? MeanWidth => Probe.InlierWidths(InlierIndices) is { Count: > 0 } w ? w.Average() : null;
    /// <summary>边缘对模式下计算点的最小宽度。</summary>
    public double? MinimumWidth => Probe.InlierWidths(InlierIndices) is { Count: > 0 } w ? w.Min() : null;
    /// <summary>边缘对模式下计算点的最大宽度。</summary>
    public double? MaximumWidth => Probe.InlierWidths(InlierIndices) is { Count: > 0 } w ? w.Max() : null;
    /// <summary>卡尺数量。</summary>
    public int CaliperCount => Probe.Scans.Count;
    /// <summary>找到边缘的卡尺数。</summary>
    public int FoundCount => Probe.Edges.Count;
    /// <summary>内点数。</summary>
    public int InlierCount => InlierIndices.Count;
    /// <summary>被忽略的点数。</summary>
    public int OutlierCount => FoundCount - InlierCount;
    /// <summary>内点径向距离RMS，原图像素。</summary>
    public double RmsError => Fit.RmsError;
    /// <inheritdoc/>
    public IReadOnlyList<Geometry> DisplayGeometry => Probe.Display(new EllipseGeometry(Fit.Center, Fit.Radius, Fit.Radius));
    /// <inheritdoc/>
    public string Summary => FormattableString.Invariant(
        $"找圆：{FoundCount}/{CaliperCount} 把卡尺找到边缘，计算点 {InlierCount}，忽略点 {OutlierCount}，半径 {Radius:F3}px，RMS {RmsError:F3}px{WidthNote}{Probe.OutsideNote}");
    private string WidthNote => MeanWidth is { } mean ? FormattableString.Invariant($"，平均宽度 {mean:F3}px（{MinimumWidth:F3}..{MaximumWidth:F3}）") : "";
}

/// <summary>一次卡尺排布的扫描线和各卡尺选中的边缘点。</summary>
internal sealed class VisionShapeProbe(string frameId, IReadOnlyList<(PointD Start, PointD End)> scans, IReadOnlyList<PointD> edges, int outside,
    double halfBand = 0, IReadOnlyList<VisionFoundEdgePair>? pairs = null, PointD? arcCenter = null)
{
    public IReadOnlyList<(PointD Start, PointD End)> Scans { get; } = scans;
    public IReadOnlyList<PointD> Edges { get; } = edges;
    public IReadOnlyList<VisionFoundEdgePair> Pairs { get; } = pairs ?? Array.Empty<VisionFoundEdgePair>();
    public IReadOnlyList<VisionCaliperScan> CaliperScans => Scans.Select(s => new VisionCaliperScan(s.Start, s.End, halfBand, arcCenter)).ToArray();
    public IReadOnlyList<bool> InlierFlags(IReadOnlyList<int> inliers)
    {
        var flags = new bool[Edges.Count];
        foreach (var index in inliers) if (index >= 0 && index < flags.Length) flags[index] = true;
        return flags;
    }
    public IReadOnlyList<VisionPoint> Select(VisionCoordinateSystem? coordinates, IReadOnlyList<int> inlierIndices, bool inliers)
    {
        var flags = InlierFlags(inlierIndices);
        return Edges.Where((_, i) => flags[i] == inliers).Select(p => new VisionPoint(frameId, p, coordinates)).ToArray();
    }
    public IReadOnlyList<double> InlierWidths(IReadOnlyList<int> inliers)
    {
        if (Pairs.Count != Edges.Count) return Array.Empty<double>();
        var flags = InlierFlags(inliers);
        return Pairs.Where((_, i) => flags[i]).Select(p => p.Width).ToArray();
    }
    public string OutsideNote => outside == 0 ? "" : $"；{outside} 把卡尺超出图像未测量";
    public IReadOnlyList<VisionPoint> Points(VisionCoordinateSystem? coordinates) => Edges.Select(p => new VisionPoint(frameId, p, coordinates)).ToArray();
    // 第一项为拟合图形（承载摘要），随后是各卡尺扫描线和边缘点。
    public IReadOnlyList<Geometry> Display(Geometry fitted) => new[] { fitted }
        .Concat(Scans.Select(s => (Geometry)new ContourGeometry(new[] { s.Start, s.End })))
        .Concat(Edges.Select(p => (Geometry)new EllipseGeometry(p, 1.5, 1.5))).ToArray();
}

/// <summary>拟合时按残差剔除指定数量的点（对应 VisionPro 找线/找圆的“忽略点数”）。</summary>
public static class VisionFitIgnoring
{
    /// <summary>
    /// 带“忽略点数”的拟合：先用全部点拟合，剔除到拟合结果残差最大的一个点后重新拟合，重复 <paramref name="ignore"/> 次；
    /// 最后一次拟合的内点（换算回原始序号）为计算点，其余（被剔除的与拟合判为外点的）为忽略点。
    /// </summary>
    /// <param name="points">全部拟合点。</param><param name="ignore">剔除点数。</param><param name="minimumInliers">剔除后至少保留的点数。</param>
    /// <param name="fit">拟合函数。</param><param name="inliers">取拟合结果的内点序号（相对传入点）。</param><param name="residual">点到拟合结果的残差。</param>
    public static (TFit Fit, IReadOnlyList<int> Inliers) FitIgnoring<TFit>(IReadOnlyList<PointD> points, int ignore, int minimumInliers,
        Func<IReadOnlyList<PointD>, TFit> fit, Func<TFit, IReadOnlyList<int>> inliers, Func<TFit, PointD, double> residual)
    {
        var kept = Enumerable.Range(0, points.Count).ToList();
        for (int k = 0; k < ignore && kept.Count - 1 >= minimumInliers; k++)
        {
            var current = fit(kept.Select(i => points[i]).ToArray());
            var worst = kept.MaxBy(i => residual(current, points[i]));
            kept.Remove(worst);
        }
        var final = fit(kept.Select(i => points[i]).ToArray());
        return (final, inliers(final).Select(i => kept[i]).OrderBy(i => i).ToArray());
    }

    /// <summary>点到过 A、B 的直线的距离。</summary>
    /// <param name="a">直线上一点。</param><param name="b">直线上另一点。</param><param name="p">待测点。</param>
    public static double LineDistance(PointD a, PointD b, PointD p)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y, length = Math.Sqrt(dx * dx + dy * dy);
        return length < 1e-12 ? Math.Sqrt((p.X - a.X) * (p.X - a.X) + (p.Y - a.Y) * (p.Y - a.Y)) : Math.Abs((p.X - a.X) * dy - (p.Y - a.Y) * dx) / length;
    }

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
        var scans = node.CaliperScans(coordinates);
        var edges = new List<PointD>(); var pairs = new List<VisionFoundEdgePair>(); int outside = 0;
        bool pairMode = node.EdgeMode == EVisionCaliperEdgeMode.Pair;
        foreach (var scan in scans)
        {
            token.ThrowIfCancellationRequested();
            var (start, end) = (scan.Start, scan.End);
            double length = Math.Sqrt(Math.Pow(end.X - start.X, 2) + Math.Pow(end.Y - start.Y, 2));
            if (length < 4) throw new InvalidOperationException("卡尺扫描长度换算到原图不足4像素，请加大搜索范围。");
            if (!Inside(frame.Image.Info, scan)) { outside++; continue; }
            // 边缘对模式需要两种极性的边缘，配对时再按“边缘极性”判断第一个边缘。
            var polarity = pairMode ? ECaliperPolarity.Any : node.Polarity;
            IReadOnlyList<VisionCaliperEdge> found;
            if (scan.ArcCenter is not null)
                // 找圆：圆环扇形采样带，沿半径扫描、沿圆弧方向求平均（扇形外侧更宽，与圆周方向一致）。
                found = VisionSectorCaliper.Measure(frame, scan, node.HalfWidth, step, node.MinimumGradient, polarity, node.MinimumSeparation * scale, token);
            else
            {
                var options = new CaliperOptions(start, end, node.HalfWidth, node.MinimumGradient, polarity, node.MinimumSeparation * scale, step);
                found = VisionCaliperMeasurement.FromLine(WorkflowVisionAlgorithmInvocation.Invoke(context, "caliper", node.CaliperAlgorithm, "managed.caliper",
                    (ICaliperMeasurer measurer) => measurer.Measure(frame, options, token), token)).Edges;
            }
            if (!pairMode)
            {
                if (Pick(found, node.EdgeChoice) is { } edge) edges.Add(edge.Position);
                continue;
            }
            if (PickPair(found, node.Polarity, node.MinimumPairWidth * scale, node.MaximumPairWidth * scale, node.EdgeChoice) is { } pair)
            {
                edges.Add(new PointD((pair.First.Position.X + pair.Second.Position.X) / 2, (pair.First.Position.Y + pair.Second.Position.Y) / 2));
                pairs.Add(new VisionFoundEdgePair(pair.First.Position, pair.Second.Position, pair.Second.Distance - pair.First.Distance));
            }
        }
        return new VisionShapeProbe(frame.FrameId, scans.Select(s => (s.Start, s.End)).ToArray(), edges, outside, node.HalfWidth * step,
            pairMode ? pairs : null, node.ArcCenter(coordinates));
    }

    private static VisionCaliperEdge? Pick(IReadOnlyList<VisionCaliperEdge> edges, EVisionEdgeChoice choice) => edges.Count == 0 ? null : choice switch
    {
        EVisionEdgeChoice.First => edges.MinBy(e => e.Distance),
        EVisionEdgeChoice.Last => edges.MaxBy(e => e.Distance),
        _ => edges.MaxBy(e => Math.Abs(e.Gradient))
    };

    // 边缘对：同一套配对规则，再按边缘选择取首个/末个/两边梯度之和最强的一对。
    private static (VisionCaliperEdge First, VisionCaliperEdge Second)? PickPair(IReadOnlyList<VisionCaliperEdge> edges, ECaliperPolarity polarity, double minimum, double maximum,
        EVisionEdgeChoice choice)
    {
        var sorted = edges.OrderBy(e => e.Distance).ToArray();
        var pairs = VisionEdgePairing.Pair(sorted.Select(e => e.Distance).ToArray(), sorted.Select(e => e.Gradient).ToArray(), polarity, minimum, maximum)
            .Select(p => (First: sorted[p.First], Second: sorted[p.Second])).ToArray();
        if (pairs.Length == 0) return null;
        return choice switch
        {
            EVisionEdgeChoice.First => pairs[0],
            EVisionEdgeChoice.Last => pairs[^1],
            _ => pairs.MaxBy(p => Math.Abs(p.First.Gradient) + Math.Abs(p.Second.Gradient))
        };
    }

    // 与卡尺的采样规则一致：采样带轮廓都须落在像素中心范围 [0.5, 尺寸-0.5] 内。
    private static bool Inside(ImageInfo info, VisionCaliperScan scan) =>
        scan.Outline().All(p => p.X >= .5 && p.Y >= .5 && p.X <= info.Width - .5 && p.Y <= info.Height - .5);
}

/// <summary>
/// 找圆用的圆环扇形卡尺：沿半径从起点到终点按 1px 取剖面，每点沿圆弧方向取 2×半宽+1 个双线性采样求平均
/// （采样间隔按期望圆上的弧长换算成固定张角，扇形外侧采样更疏），再按与直线卡尺相同的梯度峰值规则找边缘。
/// </summary>
public static class VisionSectorCaliper
{
    /// <summary>测量一把圆环扇形卡尺。</summary>
    /// <param name="frame">Gray8 输入帧。</param><param name="scan">带圆心的卡尺位置。</param>
    /// <param name="halfWidth">圆弧方向单侧采样点数。</param><param name="bandSampleStep">期望圆上的圆弧方向采样间隔，原图像素。</param>
    /// <param name="minimumGradient">最小绝对梯度。</param><param name="polarity">沿扫描方向的极性。</param>
    /// <param name="minimumSeparation">边缘最小间距。</param><param name="token">取消。</param>
    /// <returns>按距扫描起点距离排序的边缘。</returns>
    public static IReadOnlyList<VisionCaliperEdge> Measure(ImageFrame frame, VisionCaliperScan scan, int halfWidth, double bandSampleStep,
        double minimumGradient, ECaliperPolarity polarity, double minimumSeparation, CancellationToken token = default)
    {
        var center = scan.ArcCenter ?? throw new ArgumentException("不是圆环扇形卡尺。", nameof(scan));
        var info = frame.Image.Info;
        var pixels = VisionCaliperProfile.Pixels(frame);
        var (angle, halfAngle, startRadius, endRadius) = scan.Sector(halfWidth * bandSampleStep);
        double length = Math.Abs(endRadius - startRadius), sign = Math.Sign(endRadius - startRadius);
        double angleStep = halfWidth == 0 ? 0 : halfAngle / halfWidth;
        int count = (int)Math.Ceiling(length) + 1; double step = length / (count - 1);
        var profile = new double[count];
        for (int i = 0; i < count; i++)
        {
            token.ThrowIfCancellationRequested();
            double radius = startRadius + sign * i * step;
            for (int b = -halfWidth; b <= halfWidth; b++)
            {
                var p = VisionCaliperScan.Polar(center, radius, angle + b * angleStep);
                profile[i] += VisionCaliperProfile.Sample(pixels, info.Width, info.Height, p.X, p.Y);
            }
            profile[i] /= halfWidth * 2 + 1;
        }
        return VisionCaliperProfile.FindEdges(profile, step, minimumGradient, polarity, minimumSeparation, token)
            .Select(e => new VisionCaliperEdge(VisionCaliperScan.Polar(center, startRadius + sign * e.Distance, angle), e.Distance, e.Gradient, null))
            .ToArray();
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
        var (fit, inliers) = VisionFitIgnoring.FitIgnoring(probe.Edges, node.IgnoreCount, node.MinimumInliers,
            points => WorkflowVisionAlgorithmInvocation.Invoke(context, "fitter", node.FitterAlgorithm, "managed.robust-line",
                (IRobustLineFitter fitter) => fitter.Fit(frame.FrameId, points, node.DistanceThreshold * (coordinates?.SimilarityScale ?? 1), 256, node.MinimumInliers, cancellationToken),
                cancellationToken),
            fit => fit.InlierIndices, (fit, p) => VisionFitIgnoring.LineDistance(fit.A, fit.B, p));
        if (coordinates is not null) fit = fit.InCoordinates(coordinates);
        var result = new VisionFindLineResult(fit, probe, inliers);
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
        var (fit, inliers) = VisionFitIgnoring.FitIgnoring(probe.Edges, node.IgnoreCount, node.MinimumInliers,
            points => WorkflowVisionAlgorithmInvocation.Invoke(context, "fitter", node.FitterAlgorithm, "managed.robust-circle",
                (IRobustCircleFitter fitter) => fitter.Fit(frame.FrameId, points, node.DistanceThreshold * (coordinates?.SimilarityScale ?? 1), 256, node.MinimumInliers, cancellationToken),
                cancellationToken),
            fit => fit.InlierIndices,
            (fit, p) => Math.Abs(Math.Sqrt((p.X - fit.Center.X) * (p.X - fit.Center.X) + (p.Y - fit.Center.Y) * (p.Y - fit.Center.Y)) - fit.Radius));
        if (coordinates is not null) fit = fit.InCoordinates(coordinates);
        var result = new VisionFindCircleResult(fit, probe, inliers);
        return ValueTask.FromResult(NodeExecutionResult.Continue(output: result, projection: WorkflowVisionFrameScope.Stage(context, frame, result)));
    }
}
