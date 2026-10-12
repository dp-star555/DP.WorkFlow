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

/// <summary>沿期望矩形四边找矩形：每条边排布“卡尺数量”把卡尺、垂直于边扫描，每边鲁棒拟合直线，相邻两边求交得到角点。</summary>
[WorkflowNode("Vision.FindRectangle", DisplayName = "找矩形", Category = WorkflowVisionCategories.Measurement,
    Description = "沿期望矩形四边各排布多把卡尺找边缘点，每边鲁棒拟合直线，相邻边求交得到四个角点、中心、宽高和角度。")]
public sealed class FindVisionRectangleNodeModel : FindVisionShapeNodeModel
{
    private const string BoxMessage = "请在图像页绘制一个矩形ROI作为期望矩形（可旋转；只能有一个，且不能是排除ROI）。";
    /// <summary>四条边的名称，与卡尺、结果中各边的顺序一致（顺时针，图像Y向下）。</summary>
    public static IReadOnlyList<string> SideNames { get; } = Array.AsReadOnly(new[] { "上边", "右边", "下边", "左边" });
    /// <summary>新建时带一个占位期望矩形，首次拿到图像时自动居中。</summary>
    public FindVisionRectangleNodeModel() => Regions = new() { PlaceholderRoi() };
    /// <inheritdoc/>
    public override string NodeType => "Vision.FindRectangle";
    /// <inheritdoc/>
    protected override WorkflowVisionRoi PlaceholderRoi() => new()
    { Id = DefaultRoiId, Shape = EWorkflowVisionRoiShape.Rectangle, CenterX = 320, CenterY = 240, Width = 200, Height = 140 };
    /// <inheritdoc/>
    protected override WorkflowVisionRoi DefaultRoi(int imageWidth, int imageHeight) => new()
    {
        Id = DefaultRoiId, Shape = EWorkflowVisionRoiShape.Rectangle, CenterX = imageWidth / 2d, CenterY = imageHeight / 2d,
        Width = Math.Round(Math.Max(SearchLength + 8, imageWidth * .4)), Height = Math.Round(Math.Max(SearchLength + 8, imageHeight * .4))
    };
    /// <inheritdoc/>
    [Browsable(false)]
    public override VisionAlgorithmSelection FitterAlgorithm { get; set; } = new() { ImplementationId = "managed.robust-line" };
    /// <inheritdoc/>
    protected override Type FitterContract => typeof(IRobustLineFitter);
    /// <summary>垂直于边的搜索长度；绑定坐标系时为业务单位。</summary>
    [WorkflowProperty("搜索长度", "每把卡尺垂直于边的扫描长度，以期望边为中心内外各一半；原图像素，绑定坐标系时为业务单位。", Category = "卡尺")]
    public double SearchLength { get; set; } = 20;
    /// <summary>扫描方向。</summary>
    [WorkflowProperty("扫描方向", "由内向外：从矩形内部扫向外部；极性按扫描方向判断，暗矩形亮背景的边为“由暗到亮”。", Category = "卡尺")]
    public EVisionFindCircleDirection Direction { get; set; }

    /// <inheritdoc/>
    public override IReadOnlyList<string> ValidateConfiguration()
    {
        var errors = base.ValidateConfiguration().ToList();
        if (!double.IsFinite(SearchLength) || SearchLength <= 0) errors.Add("搜索长度必须为正数。");
        else
        {
            try
            {
                var box = ExpectedRectangle();
                if (SearchLength >= Math.Min(box.Width, box.Height)) errors.Add("搜索长度必须小于期望矩形的短边，否则对边的卡尺会互相越过。");
            }
            catch (InvalidOperationException error) { errors.Add(error.Message); }
        }
        return errors;
    }

    internal RectangleGeometry ExpectedRectangle() => SearchRoi<RectangleGeometry>(EWorkflowVisionRoiShape.Rectangle, BoxMessage);

    internal override IReadOnlyList<(PointD Start, PointD End)> Scans(VisionCoordinateSystem? coordinates) =>
        SideScans(coordinates).SelectMany(side => side).ToArray();

    /// <summary>四条边（上、右、下、左）各自的卡尺扫描线；卡尺落在每条边各等分段中点，不压角。</summary>
    internal IReadOnlyList<IReadOnlyList<(PointD Start, PointD End)>> SideScans(VisionCoordinateSystem? coordinates)
    {
        var box = ExpectedRectangle();
        double scale = coordinates?.SimilarityScale ?? 1, angle = box.Angle + (coordinates?.RotationRadians ?? 0);
        var center = ToImage(box.Center, coordinates);
        double halfWidth = box.Width * scale / 2, halfHeight = box.Height * scale / 2, half = SearchLength * scale / 2;
        double sign = Direction == EVisionFindCircleDirection.Outward ? 1 : -1;
        // 宽度方向 u、高度方向 v（图像Y向下）；每条边：边中点、沿边方向、向外法向、边长。
        var u = (X: Math.Cos(angle), Y: Math.Sin(angle)); var v = (X: -u.Y, Y: u.X);
        var sides = new[]
        {
            (Middle: Offset(center, v, -halfHeight), Along: u, Out: (X: -v.X, Y: -v.Y), Length: 2 * halfWidth),
            (Middle: Offset(center, u, halfWidth), Along: v, Out: u, Length: 2 * halfHeight),
            (Middle: Offset(center, v, halfHeight), Along: (X: -u.X, Y: -u.Y), Out: v, Length: 2 * halfWidth),
            (Middle: Offset(center, u, -halfWidth), Along: (X: -v.X, Y: -v.Y), Out: (X: -u.X, Y: -u.Y), Length: 2 * halfHeight)
        };
        return sides.Select(side => (IReadOnlyList<(PointD, PointD)>)Enumerable.Range(0, CaliperCount).Select(i =>
        {
            var at = Offset(side.Middle, side.Along, -side.Length / 2 + side.Length * (i + .5) / CaliperCount);
            return (Offset(at, side.Out, -sign * half), Offset(at, side.Out, sign * half));
        }).ToArray()).ToArray();

        static PointD Offset(PointD point, (double X, double Y) direction, double distance) => new(point.X + direction.X * distance, point.Y + direction.Y * distance);
    }
}

/// <summary>找线结果：拟合直线、各卡尺边缘点及显示图形。</summary>
public sealed class VisionFindLineResult : IVisionGeometryFact, IVisionDisplayFact
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
    public IReadOnlyList<Geometry> DisplayGeometry => VisionDisplay.Geometries(DisplayItems);
    /// <inheritdoc/>
    [Browsable(false)]
    public IReadOnlyList<VisionDisplayItem> DisplayItems => Probe.DisplayItems(
        VisionDisplayItem.Segment(Fit.A, Fit.B, EVisionDisplayRole.Result, FormattableString.Invariant($"{AngleDegrees:F2}°")), InlierIndices);
    /// <inheritdoc/>
    public string Summary => FormattableString.Invariant(
        $"找线：{FoundCount}/{CaliperCount} 把卡尺找到边缘，计算点 {InlierCount}，忽略点 {OutlierCount}，RMS {RmsError:F3}px，角度 {AngleDegrees:F2}°{WidthNote}{Probe.OutsideNote}");
    private string WidthNote => MeanWidth is { } mean ? FormattableString.Invariant($"，平均宽度 {mean:F3}px（{MinimumWidth:F3}..{MaximumWidth:F3}）") : "";
}

/// <summary>找圆结果：拟合圆、各卡尺边缘点及显示图形。</summary>
public sealed class VisionFindCircleResult : IVisionGeometryFact, IVisionDisplayFact
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
    public IReadOnlyList<Geometry> DisplayGeometry => VisionDisplay.Geometries(DisplayItems);
    /// <inheritdoc/>
    [Browsable(false)]
    public IReadOnlyList<VisionDisplayItem> DisplayItems => Probe.DisplayItems(
        new VisionDisplayItem(new EllipseGeometry(Fit.Center, Fit.Radius, Fit.Radius), EVisionDisplayRole.Result, FormattableString.Invariant($"R {Radius:F3}")), InlierIndices);
    /// <inheritdoc/>
    public string Summary => FormattableString.Invariant(
        $"找圆：{FoundCount}/{CaliperCount} 把卡尺找到边缘，计算点 {InlierCount}，忽略点 {OutlierCount}，半径 {Radius:F3}px，RMS {RmsError:F3}px{WidthNote}{Probe.OutsideNote}");
    private string WidthNote => MeanWidth is { } mean ? FormattableString.Invariant($"，平均宽度 {mean:F3}px（{MinimumWidth:F3}..{MaximumWidth:F3}）") : "";
}

/// <summary>找矩形结果：四条边的找线结果、四个角点、中心、宽高与角度。</summary>
public sealed class VisionFindRectangleResult : IVisionGeometryFact, IVisionDisplayFact
{
    internal VisionFindRectangleResult(IReadOnlyList<VisionFindLineResult> sides, IReadOnlyList<PointD> corners, VisionCoordinateSystem? coordinates)
    {
        Sides = sides; _corners = corners; _coordinates = coordinates;
    }
    private readonly IReadOnlyList<PointD> _corners;
    private readonly VisionCoordinateSystem? _coordinates;
    /// <summary>上、右、下、左四条边的找线结果（各自的拟合直线、边缘点、计算点/忽略点）。</summary>
    public IReadOnlyList<VisionFindLineResult> Sides { get; }
    /// <inheritdoc/>
    public string FrameId => Sides[0].FrameId;
    /// <summary>四条边的拟合直线（上、右、下、左），可直接连接距离、交点等几何节点。</summary>
    public IReadOnlyList<VisionLine> SideLines => Sides.Select(s => s.MeasuredLine).ToArray();
    /// <summary>四个角点：左上、右上、右下、左下（按期望矩形的方向），带来源。</summary>
    public IReadOnlyList<VisionPoint> Corners => _corners.Select(p => new VisionPoint(FrameId, p, _coordinates)).ToArray();
    /// <summary>中心（四个角点的平均），带来源。</summary>
    public VisionPoint RectangleCenter => new(FrameId, Center, _coordinates);
    /// <summary>中心X，原图像素。</summary>
    public double CenterX => Center.X;
    /// <summary>中心Y，原图像素。</summary>
    public double CenterY => Center.Y;
    /// <summary>宽度（上下两边长度的平均），原图像素。</summary>
    public double Width => (Distance(_corners[0], _corners[1]) + Distance(_corners[3], _corners[2])) / 2;
    /// <summary>高度（左右两边长度的平均），原图像素。</summary>
    public double Height => (Distance(_corners[0], _corners[3]) + Distance(_corners[1], _corners[2])) / 2;
    /// <summary>业务单位宽度；未绑定坐标系时为空。</summary>
    public double? LocalWidth => _coordinates is { } c ? Width / c.SimilarityScale : null;
    /// <summary>业务单位高度；未绑定坐标系时为空。</summary>
    public double? LocalHeight => _coordinates is { } c ? Height / c.SimilarityScale : null;
    /// <summary>宽度方向角（度，从原图X轴起顺时针，(-180, 180]），取上下两边方向的平均。</summary>
    public double AngleDegrees
    {
        get
        {
            double x = _corners[1].X - _corners[0].X + _corners[2].X - _corners[3].X, y = _corners[1].Y - _corners[0].Y + _corners[2].Y - _corners[3].Y;
            return Math.Atan2(y, x) * 180 / Math.PI;
        }
    }
    /// <summary>四个内角与 90° 的最大偏差（度），反映矩形的垂直度。</summary>
    public double MaximumCornerError => Enumerable.Range(0, 4).Max(i =>
    {
        var (prev, at, next) = (_corners[(i + 3) % 4], _corners[i], _corners[(i + 1) % 4]);
        double ax = prev.X - at.X, ay = prev.Y - at.Y, bx = next.X - at.X, by = next.Y - at.Y;
        double cos = (ax * bx + ay * by) / Math.Max(1e-12, Math.Sqrt(ax * ax + ay * ay) * Math.Sqrt(bx * bx + by * by));
        return Math.Abs(Math.Acos(Math.Clamp(cos, -1, 1)) * 180 / Math.PI - 90);
    });
    /// <summary>卡尺总数（四边合计）。</summary>
    public int CaliperCount => Sides.Sum(s => s.CaliperCount);
    /// <summary>找到边缘的卡尺数。</summary>
    public int FoundCount => Sides.Sum(s => s.FoundCount);
    /// <summary>内点数。</summary>
    public int InlierCount => Sides.Sum(s => s.InlierCount);
    /// <summary>被忽略的点数。</summary>
    public int OutlierCount => Sides.Sum(s => s.OutlierCount);
    /// <summary>四边内点距离RMS的最大值，原图像素。</summary>
    public double RmsError => Sides.Max(s => s.RmsError);
    /// <inheritdoc/>
    public IReadOnlyList<Geometry> DisplayGeometry => VisionDisplay.Geometries(DisplayItems);
    /// <inheritdoc/>
    [Browsable(false)]
    public IReadOnlyList<VisionDisplayItem> DisplayItems
    {
        get
        {
            var items = new List<VisionDisplayItem>
            {
                new(new ContourGeometry(_corners, closed: true), EVisionDisplayRole.Result, FormattableString.Invariant($"{Width:F2}×{Height:F2}")),
                VisionDisplayItem.Point(Center, detail: FormattableString.Invariant($"中心 ({CenterX:F4},{CenterY:F4})；角度 {AngleDegrees:F3}°"))
            };
            // 各边的扫描线、计算点/忽略点与边缘对宽度线；各边的拟合直线已由矩形轮廓表示，不重复画。
            foreach (var side in Sides) items.AddRange(side.DisplayItems.Skip(1));
            for (int i = 0; i < 4; i++)
                items.Add(VisionDisplayItem.Point(_corners[i], detail: FormattableString.Invariant($"{CornerNames[i]}角点 ({_corners[i].X:F4},{_corners[i].Y:F4})")));
            return items;
        }
    }
    /// <inheritdoc/>
    public string Summary => FormattableString.Invariant(
        $"找矩形：{FoundCount}/{CaliperCount} 把卡尺找到边缘，计算点 {InlierCount}，忽略点 {OutlierCount}，宽 {Width:F3}px，高 {Height:F3}px，角度 {AngleDegrees:F3}°，中心 ({CenterX:F3},{CenterY:F3})，最大直角偏差 {MaximumCornerError:F3}°，最大RMS {RmsError:F3}px")
        + string.Concat(Sides.Select(s => s.Probe.OutsideNote).Distinct());
    private static readonly string[] CornerNames = { "左上", "右上", "右下", "左下" };
    private PointD Center => new(_corners.Average(p => p.X), _corners.Average(p => p.Y));
    private static double Distance(PointD a, PointD b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
}

/// <summary>一次卡尺排布的扫描线和各卡尺选中的边缘点。</summary>
internal sealed class VisionShapeProbe(string frameId, IReadOnlyList<(PointD Start, PointD End)> scans, IReadOnlyList<PointD> edges, int outside,
    double halfBand = 0, IReadOnlyList<VisionFoundEdgePair>? pairs = null, PointD? arcCenter = null, int clipped = 0)
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
    public string OutsideNote => (outside == 0 ? "" : $"；{outside} 把卡尺超出图像未测量")
        + (clipped == 0 ? "" : $"；{clipped} 把卡尺部分超出图像，只在图内部分取点");
    public IReadOnlyList<VisionPoint> Points(VisionCoordinateSystem? coordinates) => Edges.Select(p => new VisionPoint(frameId, p, coordinates)).ToArray();
    // 拟合线/圆为结果，各卡尺扫描线为输入，计算点/忽略点为标记，边缘对模式另画宽度测量线；点击点和宽度线给出坐标与宽度。
    public IReadOnlyList<VisionDisplayItem> DisplayItems(VisionDisplayItem fitted, IReadOnlyList<int> inliers)
    {
        var flags = InlierFlags(inliers);
        var items = new List<VisionDisplayItem> { fitted };
        items.AddRange(Scans.Select(s => VisionDisplayItem.Segment(s.Start, s.End, EVisionDisplayRole.Input)));
        for (int i = 0; i < Pairs.Count; i++)
            items.Add(VisionDisplayItem.Segment(Pairs[i].First, Pairs[i].Second, EVisionDisplayRole.Measurement,
                detail: FormattableString.Invariant($"边缘对宽度 {Pairs[i].Width:F4}px")));
        for (int i = 0; i < Edges.Count; i++)
            items.Add(VisionDisplayItem.Point(Edges[i], flags[i] ? EVisionDisplayRole.Accepted : EVisionDisplayRole.Rejected,
                detail: FormattableString.Invariant($"{(flags[i] ? "计算点" : "忽略点")} ({Edges[i].X:F4},{Edges[i].Y:F4})")
                    + (i < Pairs.Count ? FormattableString.Invariant($"；宽度 {Pairs[i].Width:F4}px") : "")));
        return items;
    }
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

/// <summary>按节点排布卡尺、逐把取边缘点；部分超出图像的卡尺只测图内部分，图内部分不足时跳过并计数。</summary>
internal static class VisionShapeFinding
{
    /// <param name="node">节点。</param><param name="context">执行上下文。</param><param name="frame">输入帧。</param>
    /// <param name="coordinates">本帧坐标系。</param><param name="token">取消。</param>
    /// <param name="scans">只测这些卡尺（如找矩形的一条边）；为空时测节点全部卡尺。</param>
    internal static VisionShapeProbe Probe(FindVisionShapeNodeModel node, IWorkflowNodeExecutionContext context, ImageFrame frame,
        VisionCoordinateSystem? coordinates, CancellationToken token, IReadOnlyList<VisionCaliperScan>? scans = null)
    {
        var errors = node.ValidateConfiguration();
        if (errors.Count > 0) throw new InvalidOperationException(string.Join("；", errors));
        double scale = coordinates?.SimilarityScale ?? 1, step = node.BandSampleStep * scale;
        if (step < .1 || step > 10) throw new InvalidOperationException("垂直采样间隔换算到原图须在0.1..10像素。");
        scans ??= node.CaliperScans(coordinates);
        var edges = new List<PointD>(); var pairs = new List<VisionFoundEdgePair>(); int outside = 0, clipped = 0;
        bool pairMode = node.EdgeMode == EVisionCaliperEdgeMode.Pair;
        foreach (var scan in scans)
        {
            token.ThrowIfCancellationRequested();
            var (start, end) = (scan.Start, scan.End);
            double length = Math.Sqrt(Math.Pow(end.X - start.X, 2) + Math.Pow(end.Y - start.Y, 2));
            if (length < 4) throw new InvalidOperationException("卡尺扫描长度换算到原图不足4像素，请加大搜索范围。");
            bool inside = Inside(frame.Image.Info, scan);
            // 边缘对模式需要两种极性的边缘，配对时再按“边缘极性”判断第一个边缘。
            var polarity = pairMode ? ECaliperPolarity.Any : node.Polarity;
            IReadOnlyList<VisionCaliperEdge> found;
            if (scan.ArcCenter is not null)
            {
                // 找圆：圆环扇形采样带，沿半径扫描、沿圆弧方向求平均（扇形外侧更宽，与圆周方向一致）。
                // 部分超出图像时只在整段弧都在图内的半径范围内取边缘，扇形张角不变。
                found = VisionSectorCaliper.Measure(frame, scan, node.HalfWidth, step, node.MinimumGradient, polarity, node.MinimumSeparation * scale,
                    out bool measured, token);
                if (!measured) { outside++; continue; }
            }
            else
            {
                // 部分超出图像时截取采样带完全在图内的一段（带宽不变），距离仍从原起点计。
                (double From, double To)? span = inside ? (0d, length) : VisionLineScanClip.Inside(frame.Image.Info, start, end, scan.HalfBand);
                if (span is not { } clip || clip.To - clip.From < 4) { outside++; continue; }
                // 端点向内收一点，避免浮点舍入让采样带角点恰好落在可采样范围之外。
                double from = inside ? 0 : clip.From + 1e-6, to = inside ? length : clip.To - 1e-6;
                double ux = (end.X - start.X) / length, uy = (end.Y - start.Y) / length;
                var options = new CaliperOptions(new PointD(start.X + ux * from, start.Y + uy * from), new PointD(start.X + ux * to, start.Y + uy * to),
                    node.HalfWidth, node.MinimumGradient, polarity, node.MinimumSeparation * scale, step);
                found = VisionCaliperMeasurement.FromLine(WorkflowVisionAlgorithmInvocation.Invoke(context, "caliper", node.CaliperAlgorithm, "managed.caliper",
                    (ICaliperMeasurer measurer) => measurer.Measure(frame, options, token), token)).Edges;
                if (from > 0) found = found.Select(e => new VisionCaliperEdge(e.Position, e.Distance + from, e.Gradient, e.AngleDegrees)).ToArray();
            }
            if (!inside) clipped++;
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
            pairMode ? pairs : null, node.ArcCenter(coordinates), clipped);
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

/// <summary>直线卡尺采样带与图像可采样区域（像素中心范围）的交集。</summary>
public static class VisionLineScanClip
{
    /// <summary>
    /// 沿起点→终点的距离区间 [from, to]：区间内每个位置的整条垂直采样带（±<paramref name="halfBand"/>）都落在
    /// [0.5, 宽-0.5]×[0.5, 高-0.5] 内；没有这样的位置时返回 null。
    /// </summary>
    /// <param name="info">图像尺寸。</param><param name="start">扫描起点。</param><param name="end">扫描终点。</param>
    /// <param name="halfBand">垂直单侧带宽，原图像素。</param><returns>图内距离区间；完全在图外时为 null。</returns>
    public static (double From, double To)? Inside(ImageInfo info, PointD start, PointD end, double halfBand)
    {
        double dx = end.X - start.X, dy = end.Y - start.Y, length = Math.Sqrt(dx * dx + dy * dy);
        if (length < 1e-12) return null;
        double ux = dx / length, uy = dy / length, nx = -uy * halfBand, ny = ux * halfBand;
        double from = 0, to = length;
        // 采样带两条边线 q(t) = q0 + u·t，每个坐标约束给出一个 t 区间，取交集。
        foreach (var (ox, oy) in new[] { (nx, ny), (-nx, -ny) })
        {
            Limit(start.X + ox, ux, .5, info.Width - .5);
            Limit(start.Y + oy, uy, .5, info.Height - .5);
        }
        return to >= from ? (from, to) : null;

        void Limit(double origin, double direction, double minimum, double maximum)
        {
            if (Math.Abs(direction) < 1e-12)
            {
                if (origin < minimum || origin > maximum) to = -1;
                return;
            }
            double a = (minimum - origin) / direction, b = (maximum - origin) / direction;
            from = Math.Max(from, Math.Min(a, b));
            to = Math.Min(to, Math.Max(a, b));
        }
    }
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
        double minimumGradient, ECaliperPolarity polarity, double minimumSeparation, CancellationToken token = default) =>
        Measure(frame, scan, halfWidth, bandSampleStep, minimumGradient, polarity, minimumSeparation, out _, token);

    /// <summary>
    /// 测量一把圆环扇形卡尺；扇形部分超出图像时，只保留其梯度计算所用剖面点的整段弧都在图内的边缘。
    /// </summary>
    /// <param name="frame">Gray8 输入帧。</param><param name="scan">带圆心的卡尺位置。</param>
    /// <param name="halfWidth">圆弧方向单侧采样点数。</param><param name="bandSampleStep">期望圆上的圆弧方向采样间隔，原图像素。</param>
    /// <param name="minimumGradient">最小绝对梯度。</param><param name="polarity">沿扫描方向的极性。</param>
    /// <param name="minimumSeparation">边缘最小间距。</param>
    /// <param name="measured">图内可用的剖面长度足以找边缘（至少 5 个连续剖面点）时为 true。</param><param name="token">取消。</param>
    /// <returns>按距扫描起点距离排序的边缘。</returns>
    public static IReadOnlyList<VisionCaliperEdge> Measure(ImageFrame frame, VisionCaliperScan scan, int halfWidth, double bandSampleStep,
        double minimumGradient, ECaliperPolarity polarity, double minimumSeparation, out bool measured, CancellationToken token = default)
    {
        var center = scan.ArcCenter ?? throw new ArgumentException("不是圆环扇形卡尺。", nameof(scan));
        var info = frame.Image.Info;
        var pixels = VisionCaliperProfile.Pixels(frame);
        var (angle, halfAngle, startRadius, endRadius) = scan.Sector(halfWidth * bandSampleStep);
        double length = Math.Abs(endRadius - startRadius), sign = Math.Sign(endRadius - startRadius);
        double angleStep = halfWidth == 0 ? 0 : halfAngle / halfWidth;
        int count = (int)Math.Ceiling(length) + 1; double step = length / (count - 1);
        var profile = new double[count];
        var valid = new bool[count];
        for (int i = 0; i < count; i++)
        {
            token.ThrowIfCancellationRequested();
            double radius = startRadius + sign * i * step;
            valid[i] = true;
            for (int b = -halfWidth; b <= halfWidth; b++)
            {
                var p = VisionCaliperScan.Polar(center, radius, angle + b * angleStep);
                valid[i] &= p.X >= .5 && p.Y >= .5 && p.X <= info.Width - .5 && p.Y <= info.Height - .5;
                profile[i] += VisionCaliperProfile.Sample(pixels, info.Width, info.Height, p.X, p.Y);
            }
            profile[i] /= halfWidth * 2 + 1;
        }
        // 边缘在剖面点 i 处由 i-2..i+2 的剖面值求得；这几个点的整段弧都在图内时才可信（图外采样被钳到边缘像素）。
        int run = 0, longest = 0;
        foreach (bool inside in valid) longest = Math.Max(longest, run = inside ? run + 1 : 0);
        measured = longest >= 5;
        if (!measured) return Array.Empty<VisionCaliperEdge>();
        bool Usable(double distance)
        {
            int i = (int)Math.Round(distance / step);
            for (int k = Math.Max(0, i - 2); k <= Math.Min(count - 1, i + 2); k++) if (!valid[k]) return false;
            return true;
        }
        return VisionCaliperProfile.FindEdges(profile, step, minimumGradient, polarity, minimumSeparation, token)
            .Where(e => Usable(e.Distance))
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

/// <summary>找矩形：四条边各自卡尺取点、鲁棒拟合直线，相邻边求交得到角点。</summary>
public sealed class FindVisionRectangleNodeHandler : WorkflowNodeHandler<FindVisionRectangleNodeModel>
{
    /// <inheritdoc/>
    protected override ValueTask<NodeExecutionResult> ExecuteAsync(FindVisionRectangleNodeModel node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
    {
        var frame = context.ResolveInput(node.Frame) ?? throw new InvalidOperationException("输入帧为空。");
        var coordinates = node.ResolveCoordinates(frame, context);
        var all = node.CaliperScans(coordinates);
        var sides = new VisionFindLineResult[4];
        for (int side = 0; side < 4; side++)
        {
            var name = FindVisionRectangleNodeModel.SideNames[side];
            var probe = VisionShapeFinding.Probe(node, context, frame, coordinates, cancellationToken, all.Skip(side * node.CaliperCount).Take(node.CaliperCount).ToArray());
            if (probe.Edges.Count < node.MinimumInliers)
                throw new InvalidOperationException($"找矩形失败：{name}只有 {probe.Edges.Count} 把卡尺找到边缘，至少需要 {node.MinimumInliers} 个点。");
            var (fit, inliers) = VisionFitIgnoring.FitIgnoring(probe.Edges, node.IgnoreCount, node.MinimumInliers,
                points => WorkflowVisionAlgorithmInvocation.Invoke(context, "fitter", node.FitterAlgorithm, "managed.robust-line",
                    (IRobustLineFitter fitter) => fitter.Fit(frame.FrameId, points, node.DistanceThreshold * (coordinates?.SimilarityScale ?? 1), 256, node.MinimumInliers, cancellationToken),
                    cancellationToken),
                fit => fit.InlierIndices, (fit, p) => VisionFitIgnoring.LineDistance(fit.A, fit.B, p));
            if (coordinates is not null) fit = fit.InCoordinates(coordinates);
            sides[side] = new VisionFindLineResult(fit, probe, inliers);
        }
        // 角点：左上 = 左∩上，右上 = 上∩右，右下 = 右∩下，左下 = 下∩左。
        var corners = new[] { Corner(sides[3], sides[0], "左上"), Corner(sides[0], sides[1], "右上"), Corner(sides[1], sides[2], "右下"), Corner(sides[2], sides[3], "左下") };
        var result = new VisionFindRectangleResult(sides, corners, coordinates);
        return ValueTask.FromResult(NodeExecutionResult.Continue(output: result, projection: WorkflowVisionFrameScope.Stage(context, frame, result)));
    }

    private static PointD Corner(VisionFindLineResult a, VisionFindLineResult b, string name)
    {
        var (p, q, r, t) = (a.Fit.A, a.Fit.B, b.Fit.A, b.Fit.B);
        double ux = q.X - p.X, uy = q.Y - p.Y, vx = t.X - r.X, vy = t.Y - r.Y;
        double cross = ux * vy - uy * vx, norms = Math.Sqrt(ux * ux + uy * uy) * Math.Sqrt(vx * vx + vy * vy);
        // 相邻两边夹角小于约 6° 时交点极不稳定，按失败处理而不输出远处的角点。
        if (norms < 1e-12 || Math.Abs(cross) / norms < .1)
            throw new InvalidOperationException($"找矩形失败：{name}角处的两条边接近平行，无法求角点；请检查搜索范围是否压在同一条边上。");
        double s = ((r.X - p.X) * vy - (r.Y - p.Y) * vx) / cross;
        return new PointD(p.X + s * ux, p.Y + s * uy);
    }
}
