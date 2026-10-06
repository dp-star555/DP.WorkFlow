using System.ComponentModel;
using DP.Vision;
using DP.Vision.Algorithms;

namespace DP.WorkFlow;

/// <summary>卡尺扫描路径形状。</summary>
public enum EVisionCaliperShape
{
    /// <summary>沿起点到终点的直线扫描，采样带垂直于直线。</summary>
    [Description("直线卡尺")]
    Line,
    /// <summary>沿圆弧扫描，采样带沿半径方向。</summary>
    [Description("圆弧卡尺")]
    Arc
}

/// <summary>圆弧卡尺的搜索方向。</summary>
public enum EVisionArcScanDirection
{
    /// <summary>沿圆弧从起始角扫到终止角，采样带沿半径方向求平均。</summary>
    [Description("沿圆弧")]
    AlongArc,
    /// <summary>沿半径从内圈搜到外圈；圆弧上均匀分布多个径向卡尺，每个沿圆弧方向求平均。</summary>
    [Description("由内到外")]
    InnerToOuter,
    /// <summary>沿半径从外圈搜到内圈。</summary>
    [Description("由外到内")]
    OuterToInner
}

/// <summary>多个卡尺拟合时，每个卡尺取哪个边缘作为拟合点。</summary>
public enum EVisionCaliperFitPoint
{
    /// <summary>梯度绝对值最大的边缘。</summary>
    [Description("最强边缘")]
    Strongest,
    /// <summary>沿搜索方向的第一个边缘。</summary>
    [Description("第一个边缘")]
    First,
    /// <summary>沿搜索方向的最后一个边缘。</summary>
    [Description("最后一个边缘")]
    Last
}

/// <summary>卡尺内拟合的几何类型。</summary>
public enum EVisionCaliperFitKind
{
    /// <summary>直线（直线卡尺的多个子卡尺）。</summary>
    Line,
    /// <summary>圆（圆弧卡尺径向搜索的多个卡尺）。</summary>
    Circle
}

/// <summary>
/// 卡尺内拟合结果：每个卡尺取一个拟合点（<see cref="Points"/>，与 <see cref="CaliperIndices"/> 同序），
/// 鲁棒拟合后标出参与拟合的计算点（<see cref="Inliers"/> 为真）与被忽略的点。坐标为原图像素。
/// </summary>
public sealed class VisionCaliperFit
{
    internal VisionCaliperFit(EVisionCaliperFitKind kind, IReadOnlyList<PointD> points, IReadOnlyList<int> calipers, IReadOnlyList<bool> inliers,
        double rmsError, PointD? a = null, PointD? b = null, PointD? center = null, double? radius = null)
    {
        Kind = kind; Points = points; CaliperIndices = calipers; Inliers = inliers; RmsError = rmsError; A = a; B = b; Center = center; Radius = radius;
    }
    /// <summary>拟合类型。</summary>
    public EVisionCaliperFitKind Kind { get; }
    /// <summary>每个找到边缘的卡尺取出的拟合点。</summary>
    public IReadOnlyList<PointD> Points { get; }
    /// <summary>拟合点所属卡尺序号。</summary>
    public IReadOnlyList<int> CaliperIndices { get; }
    /// <summary>与拟合点同序：真为参与拟合的计算点，假为被忽略的点。</summary>
    public IReadOnlyList<bool> Inliers { get; }
    /// <summary>参与拟合的点数。</summary>
    public int InlierCount => Inliers.Count(i => i);
    /// <summary>被忽略的点数。</summary>
    public int OutlierCount => Inliers.Count - InlierCount;
    /// <summary>参与拟合点到拟合几何的均方根距离，原图像素。</summary>
    public double RmsError { get; }
    /// <summary>拟合线段端点 A（直线）。</summary>
    public PointD? A { get; }
    /// <summary>拟合线段端点 B（直线）。</summary>
    public PointD? B { get; }
    /// <summary>拟合圆心（圆）。</summary>
    public PointD? Center { get; }
    /// <summary>拟合半径（圆），原图像素。</summary>
    public double? Radius { get; }
}

/// <summary>卡尺边缘：亚像素位置、沿扫描路径的距离（圆弧为弧长）和有符号梯度。</summary>
public sealed class VisionCaliperEdge
{
    internal VisionCaliperEdge(PointD position, double distance, double gradient, double? angleDegrees, int caliperIndex = 0)
    { Position = position; Distance = distance; Gradient = gradient; AngleDegrees = angleDegrees; CaliperIndex = caliperIndex; }
    /// <summary>原图像素边界坐标。</summary>
    public PointD Position { get; }
    /// <summary>沿扫描路径距离；沿圆弧扫描时为从起始角开始的弧长，径向搜索时为从搜索起点（内圈或外圈）开始的径向距离，原图像素。</summary>
    public double Distance { get; }
    /// <summary>有符号梯度，灰度/像素，沿扫描方向。</summary>
    public double Gradient { get; }
    /// <summary>圆弧卡尺边缘所在的原图角度（度，X轴起顺时针）；直线卡尺为空。</summary>
    public double? AngleDegrees { get; }
    /// <summary>径向搜索时边缘所属卡尺的序号（从起始角起 0 开始）；其它情况为 0。</summary>
    public int CaliperIndex { get; }
}

/// <summary>
/// 直线或圆弧卡尺的采样证据，成员名与直线卡尺原结果一致（起终点、剖面、边缘、边缘点），下游绑定不需要改路径。
/// 圆弧卡尺另外给出圆心、半径与角度范围，均为原图像素/度。
/// </summary>
public sealed class VisionCaliperMeasurement : IWorkflowVisionFrameFact
{
    private VisionCaliperMeasurement(string frameId, EVisionCaliperShape shape, PointD start, PointD end, double sampleStep,
        IReadOnlyList<double> profile, IReadOnlyList<VisionCaliperEdge> edges, IReadOnlyList<PointD> path, CaliperResult? line)
    {
        FrameId = frameId; Shape = shape; Start = start; End = end; SampleStep = sampleStep;
        Profile = profile; Edges = edges; Path = path; Line = line;
    }

    /// <summary>包装直线卡尺算法结果。</summary>
    /// <param name="result">直线卡尺结果。</param>
    public static VisionCaliperMeasurement FromLine(CaliperResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var measurement = new VisionCaliperMeasurement(result.FrameId, EVisionCaliperShape.Line, result.Start, result.End, result.SampleStep, result.Profile,
            Array.AsReadOnly(result.Edges.Select(e => new VisionCaliperEdge(e.Position, e.Distance, e.Gradient, null)).ToArray()),
            new[] { result.Start, result.End }, result);
        return result.CoordinateSystem is { } system ? measurement.InCoordinates(system) : measurement;
    }

    internal static VisionCaliperMeasurement FromLineCalipers(string frameId, PointD start, PointD end, double step,
        IReadOnlyList<IReadOnlyList<double>> profiles, IReadOnlyList<VisionCaliperEdge> edges, CaliperResult? line)
    {
        var mean = new double[profiles.Count == 0 ? 0 : profiles.Min(p => p.Count)];
        foreach (var profile in profiles)
            for (var i = 0; i < mean.Length; i++) mean[i] += profile[i] / profiles.Count;
        return new VisionCaliperMeasurement(frameId, EVisionCaliperShape.Line, start, end, step, Array.AsReadOnly(mean), edges, new[] { start, end }, line)
        {
            Profiles = profiles
        };
    }

    internal VisionCaliperMeasurement WithFit(VisionCaliperFit? fit, string? message)
    {
        var copy = (VisionCaliperMeasurement)MemberwiseClone();
        copy.Fit = fit; copy.FitMessage = message;
        return copy;
    }

    internal static VisionCaliperMeasurement FromArc(string frameId, VisionArcCaliperOptions options, double step, double[] profile,
        IReadOnlyList<VisionCaliperEdge> edges, IReadOnlyList<IReadOnlyList<double>>? profiles = null)
    {
        var path = new PointD[Math.Clamp((int)Math.Ceiling(Math.Abs(options.SweepDegrees) / 3), 8, 120) + 1];
        for (var i = 0; i < path.Length; i++) path[i] = options.PointAt(options.Radius, (double)i / (path.Length - 1));
        return new VisionCaliperMeasurement(frameId, EVisionCaliperShape.Arc, options.PointAt(options.Radius, 0), options.PointAt(options.Radius, 1),
            step, Array.AsReadOnly((double[])profile.Clone()), edges, path, null)
        {
            Center = options.Center, Radius = options.Radius, StartAngleDegrees = options.StartAngleDegrees, SweepDegrees = options.SweepDegrees,
            ArcDirection = options.Direction, Profiles = profiles ?? Array.AsReadOnly(new IReadOnlyList<double>[] { Array.AsReadOnly((double[])profile.Clone()) })
        };
    }

    /// <summary>输入帧。</summary>
    public string FrameId { get; }
    /// <summary>卡尺形状。</summary>
    public EVisionCaliperShape Shape { get; }
    /// <summary>扫描起点；圆弧卡尺为起始角处的弧上点。</summary>
    public PointD Start { get; }
    /// <summary>扫描终点；圆弧卡尺为终止角处的弧上点。</summary>
    public PointD End { get; }
    /// <summary>圆弧圆心（原图像素）；直线卡尺为空。</summary>
    public PointD? Center { get; private init; }
    /// <summary>扫描圆弧半径（原图像素）；直线卡尺为空。</summary>
    public double? Radius { get; private init; }
    /// <summary>起始角（度，X轴起顺时针）；直线卡尺为空。</summary>
    public double? StartAngleDegrees { get; private init; }
    /// <summary>扫描角度范围（度），正为顺时针；直线卡尺为空。</summary>
    public double? SweepDegrees { get; private init; }
    /// <summary>圆弧卡尺的搜索方向；直线卡尺为空。</summary>
    public EVisionArcScanDirection? ArcDirection { get; private init; }
    /// <summary>每个卡尺各自的灰度剖面；径向搜索时每个径向卡尺一条，其它情况只有一条（同 <see cref="Profile"/>）。</summary>
    public IReadOnlyList<IReadOnlyList<double>> Profiles { get; private init; } = Array.Empty<IReadOnlyList<double>>();
    /// <summary>沿扫描路径的均匀采样步长，原图像素。</summary>
    public double SampleStep { get; }
    /// <summary>一维灰度均值剖面。</summary>
    public IReadOnlyList<double> Profile { get; }
    /// <summary>按扫描距离排序的边缘。</summary>
    public IReadOnlyList<VisionCaliperEdge> Edges { get; }
    /// <summary>边缘数。</summary>
    public int Count => Edges.Count;
    /// <summary>卡尺内拟合结果（直线卡尺多个子卡尺拟合直线、圆弧径向搜索拟合圆）；卡尺数量少于 3 或拟合失败时为空。</summary>
    public VisionCaliperFit? Fit { get; private set; }
    /// <summary>未拟合或拟合失败的原因；成功或不需要拟合时为空。</summary>
    public string? FitMessage { get; private set; }
    /// <summary>拟合直线（带来源帧与坐标系）；没有直线拟合时为空。</summary>
    public VisionLine? MeasuredFitLine => Fit is { Kind: EVisionCaliperFitKind.Line, A: { } a, B: { } b }
        ? new VisionLine(new VisionPoint(FrameId, a, CoordinateSystem), new VisionPoint(FrameId, b, CoordinateSystem)) : null;
    /// <summary>直线卡尺单个卡尺、1px 搜索间隔时的算法原始结果；其它情况为空。</summary>
    [Browsable(false)]
    public CaliperResult? Line { get; }
    /// <summary>可选本帧定位来源；位置、距离均保持原图像素。</summary>
    public VisionCoordinateSystem? CoordinateSystem { get; private set; }
    /// <summary>同序边缘点，带来源帧与坐标系。</summary>
    public IReadOnlyList<VisionPoint> MeasuredEdges => Array.AsReadOnly(Edges.Select(e => new VisionPoint(FrameId, e.Position, CoordinateSystem)).ToArray());
    /// <summary>与边缘同序的双坐标边缘；未绑定坐标系时为空。</summary>
    public IReadOnlyList<LocatedPoint>? LocatedEdges { get; private set; }
    /// <summary>扫描起点的双坐标。</summary>
    public LocatedPoint? LocatedStart => CoordinateSystem?.Locate(Start);
    /// <summary>扫描终点的双坐标。</summary>
    public LocatedPoint? LocatedEnd => CoordinateSystem?.Locate(End);
    /// <summary>完成状态，空边缘不是执行错误。</summary>
    public EAlgorithmStatus Status => EAlgorithmStatus.Completed;

    /// <inheritdoc/>
    public string Summary => FitSummary.Length == 0 ? ShapeSummary : ShapeSummary + " " + FitSummary + "。";

    private string ShapeSummary => Shape == EVisionCaliperShape.Arc
        ? ArcDirection is EVisionArcScanDirection.InnerToOuter or EVisionArcScanDirection.OuterToInner
            ? FormattableString.Invariant($"圆弧卡尺（{(ArcDirection == EVisionArcScanDirection.InnerToOuter ? "由内到外" : "由外到内")}）{Profiles.Count} 个径向卡尺，边缘 {Count}；半径 {Radius:0.##}px。")
            : FormattableString.Invariant($"圆弧卡尺边缘 {Count}；半径 {Radius:0.##}px；剖面采样 {Profile.Count}。")
        : Profiles.Count > 1
            ? $"直线卡尺 {Profiles.Count} 个子卡尺，边缘 {Count}；剖面采样 {Profile.Count}。"
            : $"卡尺边缘 {Count}；剖面采样 {Profile.Count}；梯度峰抛物线插值。";

    /// <summary>拟合说明：计算点/忽略点与误差，或未拟合原因。</summary>
    public string FitSummary => Fit is { } fit
        ? FormattableString.Invariant($"{(fit.Kind == EVisionCaliperFitKind.Line ? "拟合直线" : $"拟合圆 半径 {fit.Radius:0.###}px")}：计算点 {fit.InlierCount}，忽略点 {fit.OutlierCount}，RMS {fit.RmsError:0.####}px")
        : FitMessage ?? string.Empty;

    /// <summary>扫描路径折线（原图像素），供图上显示。</summary>
    [Browsable(false)]
    public IReadOnlyList<PointD> Path { get; }

    /// <summary>附加同帧定位坐标，不改变采样证据。</summary>
    /// <param name="system">同帧坐标系。</param>
    public VisionCaliperMeasurement InCoordinates(VisionCoordinateSystem system)
    {
        ArgumentNullException.ThrowIfNull(system);
        if (system.FrameId != FrameId) throw new InvalidOperationException("Result coordinate frame mismatch.");
        var copy = (VisionCaliperMeasurement)MemberwiseClone();
        copy.CoordinateSystem = system;
        copy.LocatedEdges = Array.AsReadOnly(Edges.Select(e => system.Locate(e.Position)).ToArray());
        return copy;
    }
}

/// <summary>圆弧卡尺采样配置，原图像素与度；角度从 X 轴起顺时针（图像 Y 向下）。</summary>
public sealed class VisionArcCaliperOptions
{
    /// <summary>构造圆弧采样带。</summary>
    /// <param name="center">圆心。</param><param name="radius">扫描圆弧半径。</param>
    /// <param name="startAngleDegrees">起始角。</param><param name="sweepDegrees">扫描角度范围，正为顺时针，绝对值 (0, 360]。</param>
    /// <param name="halfWidth">半径方向单侧采样步数 0..63。</param><param name="minimumGradient">最小绝对梯度。</param>
    /// <param name="polarity">沿扫描方向的极性。</param><param name="minimumSeparation">边缘最小弧长间距。</param>
    /// <param name="bandSampleStep">半径方向采样间隔 0.1..10。</param>
    /// <param name="direction">搜索方向：沿圆弧，或沿半径由内到外/由外到内。</param>
    /// <param name="caliperCount">径向搜索时沿圆弧均匀分布的卡尺数 1..128；沿圆弧扫描时忽略。</param>
    /// <param name="scanStep">沿搜索方向的采样间隔 0.1..10 像素。</param>
    /// <param name="fitPoint">径向搜索拟合圆时每个卡尺取哪个边缘。</param>
    /// <param name="fitDistanceThreshold">拟合内点距离阈值，像素。</param>
    public VisionArcCaliperOptions(PointD center, double radius, double startAngleDegrees, double sweepDegrees, int halfWidth = 2,
        double minimumGradient = 5, ECaliperPolarity polarity = ECaliperPolarity.Any, double minimumSeparation = 2, double bandSampleStep = 1,
        EVisionArcScanDirection direction = EVisionArcScanDirection.AlongArc, int caliperCount = 1, double scanStep = 1,
        EVisionCaliperFitPoint fitPoint = EVisionCaliperFitPoint.Strongest, double fitDistanceThreshold = 1)
    {
        if (!double.IsFinite(scanStep) || scanStep < .1 || scanStep > 10) throw new ArgumentException("搜索间隔须为 0.1..10 像素。");
        if (!Enum.IsDefined(fitPoint) || !double.IsFinite(fitDistanceThreshold) || fitDistanceThreshold <= 0 || fitDistanceThreshold > 1000)
            throw new ArgumentException("拟合参数无效。");
        ScanStep = scanStep; FitPoint = fitPoint; FitDistanceThreshold = fitDistanceThreshold;
        if (!Enum.IsDefined(direction)) throw new ArgumentException("未知圆弧卡尺搜索方向。");
        if (!double.IsFinite(center.X) || !double.IsFinite(center.Y) || !double.IsFinite(radius) || radius <= 0 || radius > 65535)
            throw new ArgumentException("圆弧卡尺圆心或半径无效。");
        if (!double.IsFinite(startAngleDegrees) || !double.IsFinite(sweepDegrees) || Math.Abs(sweepDegrees) < 1e-9 || Math.Abs(sweepDegrees) > 360)
            throw new ArgumentException("圆弧卡尺扫描角度范围必须在 (0, 360] 度。");
        if (halfWidth < 0 || halfWidth > 63 || !double.IsFinite(bandSampleStep) || bandSampleStep < .1 || bandSampleStep > 10)
            throw new ArgumentException("圆弧卡尺采样半宽须为 0..63，半径方向采样间隔须为 0.1..10。");
        if (radius - halfWidth * bandSampleStep < 0) throw new ArgumentException("圆弧卡尺采样带内侧越过圆心，请减小带宽或增大半径。");
        var length = Math.Abs(sweepDegrees) * Math.PI / 180 * radius;
        if (length < 4 || length > 65535) throw new ArgumentException("圆弧卡尺弧长须为 4..65535 像素。");
        if (!double.IsFinite(minimumGradient) || minimumGradient <= 0 || !double.IsFinite(minimumSeparation) || minimumSeparation <= 0
            || !Enum.IsDefined(polarity)) throw new ArgumentException("圆弧卡尺边缘参数无效。");
        if (direction != EVisionArcScanDirection.AlongArc)
        {
            if (caliperCount < 1 || caliperCount > 128) throw new ArgumentException("径向卡尺数量须为 1..128。");
            if (halfWidth * bandSampleStep < 2) throw new ArgumentException("径向搜索范围（带宽）至少 ±2px。");
        }
        Direction = direction; CaliperCount = direction == EVisionArcScanDirection.AlongArc ? 1 : caliperCount;
        Center = center; Radius = radius; StartAngleDegrees = startAngleDegrees; SweepDegrees = sweepDegrees; HalfWidth = halfWidth;
        MinimumGradient = minimumGradient; Polarity = polarity; MinimumSeparation = minimumSeparation; BandSampleStep = bandSampleStep;
    }

    /// <summary>圆心。</summary>
    public PointD Center { get; }
    /// <summary>扫描圆弧半径。</summary>
    public double Radius { get; }
    /// <summary>起始角（度）。</summary>
    public double StartAngleDegrees { get; }
    /// <summary>扫描角度范围（度），正为顺时针。</summary>
    public double SweepDegrees { get; }
    /// <summary>半径方向单侧采样步数。</summary>
    public int HalfWidth { get; }
    /// <summary>最小梯度。</summary>
    public double MinimumGradient { get; }
    /// <summary>边缘极性，沿扫描方向。</summary>
    public ECaliperPolarity Polarity { get; }
    /// <summary>边缘最小弧长间距。</summary>
    public double MinimumSeparation { get; }
    /// <summary>半径方向采样间隔。</summary>
    public double BandSampleStep { get; }
    /// <summary>搜索方向。</summary>
    public EVisionArcScanDirection Direction { get; }
    /// <summary>沿搜索方向的采样间隔，像素。</summary>
    public double ScanStep { get; }
    /// <summary>拟合时每个卡尺取的边缘。</summary>
    public EVisionCaliperFitPoint FitPoint { get; }
    /// <summary>拟合内点距离阈值，像素。</summary>
    public double FitDistanceThreshold { get; }
    /// <summary>径向卡尺数；沿圆弧扫描时为 1。</summary>
    public int CaliperCount { get; }
    /// <summary>单侧带宽（径向搜索时为搜索范围半长），原图像素。</summary>
    public double HalfBand => HalfWidth * BandSampleStep;
    /// <summary>扫描弧长。</summary>
    public double Length => Math.Abs(SweepDegrees) * Math.PI / 180 * Radius;

    /// <summary>返回半径 <paramref name="radius"/> 上、扫描进度 <paramref name="t"/>（0..1）处的点。</summary>
    /// <param name="radius">采样半径。</param><param name="t">扫描进度。</param>
    public PointD PointAt(double radius, double t)
    {
        var angle = (StartAngleDegrees + SweepDegrees * t) * Math.PI / 180;
        return new PointD(Center.X + radius * Math.Cos(angle), Center.Y + radius * Math.Sin(angle));
    }
}

/// <summary>
/// 圆弧卡尺。沿圆弧扫描：沿扫描圆弧按 1px 弧长均匀取点，每点沿半径方向取 2×半宽+1 个双线性采样求平均得到剖面。
/// 径向搜索：圆弧均分为 N 段，每段一个径向卡尺，沿半径在 [半径−带宽, 半径+带宽] 内按 1px 取点（由内到外或由外到内），
/// 每点沿该段圆弧按约 1px 弧长求平均。两种方式都用中心差分求梯度、梯度极大值抛物线插值得到亚像素边缘、按梯度强度做间距抑制，
/// 与直线卡尺同一套剖面/峰值规则。
/// </summary>
public static class VisionArcCaliper
{
    /// <summary>测量圆弧采样带。</summary>
    /// <param name="frame">Gray8 输入帧。</param><param name="options">采样配置。</param><param name="token">取消。</param>
    public static VisionCaliperMeasurement Measure(ImageFrame frame, VisionArcCaliperOptions options, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(frame); ArgumentNullException.ThrowIfNull(options);
        var info = frame.Image.Info;
        if (info.Layout != EPixelLayout.Gray8) throw new NotSupportedException("圆弧卡尺需要显式预处理为 Gray8。");
        if ((long)info.Width * info.Height > 16777216) throw new ArgumentException("Caliper image budget exceeded.");
        var pixels = new byte[info.ByteLength]; frame.Image.CopyTo(0, pixels, 0, pixels.Length);
        double inner = options.Radius - options.HalfBand, outer = options.Radius + options.HalfBand;
        void Check(PointD p)
        {
            if (p.X < .5 || p.Y < .5 || p.X > info.Width - .5 || p.Y > info.Height - .5)
                throw new ArgumentException("Caliper band extends outside sampleable pixel centers.");
        }
        double Pixel(PointD p) => Sample(pixels, info.Width, info.Height, p.X, p.Y);
        return options.Direction == EVisionArcScanDirection.AlongArc
            ? MeasureAlongArc(frame.FrameId, options, inner, outer, Check, Pixel, token)
            : MeasureRadial(frame.FrameId, options, inner, outer, Check, Pixel, token);
    }

    private static VisionCaliperMeasurement MeasureAlongArc(string frameId, VisionArcCaliperOptions options, double inner, double outer,
        Action<PointD> check, Func<PointD, double> pixel, CancellationToken token)
    {
        var length = options.Length;
        int count = (int)Math.Ceiling(length / options.ScanStep) + 1;
        double step = length / (count - 1);
        var profile = new double[count];
        for (int i = 0; i < count; i++)
        {
            token.ThrowIfCancellationRequested();
            double t = (double)i / (count - 1);
            check(options.PointAt(inner, t)); check(options.PointAt(outer, t));
            for (int b = -options.HalfWidth; b <= options.HalfWidth; b++)
                profile[i] += pixel(options.PointAt(options.Radius + b * options.BandSampleStep, t));
            profile[i] /= options.HalfWidth * 2 + 1;
        }
        var edges = VisionCaliperProfile.FindEdges(profile, step, options.MinimumGradient, options.Polarity, options.MinimumSeparation, token).Select(e =>
        {
            var t = e.Distance / length;
            return new VisionCaliperEdge(options.PointAt(options.Radius, t), e.Distance, e.Gradient, options.StartAngleDegrees + options.SweepDegrees * t);
        }).ToArray();
        return VisionCaliperMeasurement.FromArc(frameId, options, step, profile, Array.AsReadOnly(edges));
    }

    private static VisionCaliperMeasurement MeasureRadial(string frameId, VisionArcCaliperOptions options, double inner, double outer,
        Action<PointD> check, Func<PointD, double> pixel, CancellationToken token)
    {
        bool outward = options.Direction == EVisionArcScanDirection.InnerToOuter;
        double length = outer - inner;
        int count = (int)Math.Ceiling(length / options.ScanStep) + 1;
        double step = length / (count - 1);
        // 每段圆弧在扫描半径处约 1px 取一个投影点，最多 127 个。
        var segmentLength = options.Length / options.CaliperCount;
        int projection = Math.Clamp((int)Math.Ceiling(segmentLength), 1, 127);
        var edges = new List<VisionCaliperEdge>(); var profiles = new List<IReadOnlyList<double>>();
        var mean = new double[count];
        for (int c = 0; c < options.CaliperCount; c++)
        {
            token.ThrowIfCancellationRequested();
            var ts = Enumerable.Range(0, projection).Select(j => (c + (j + .5) / projection) / options.CaliperCount).ToArray();
            foreach (var t in ts) { check(options.PointAt(inner, t)); check(options.PointAt(outer, t)); }
            var profile = new double[count];
            for (int i = 0; i < count; i++)
            {
                var radius = outward ? inner + i * step : outer - i * step;
                foreach (var t in ts) profile[i] += pixel(options.PointAt(radius, t));
                profile[i] /= projection;
                mean[i] += profile[i] / options.CaliperCount;
            }
            var middle = (c + .5) / options.CaliperCount;
            var angle = options.StartAngleDegrees + options.SweepDegrees * middle;
            foreach (var e in VisionCaliperProfile.FindEdges(profile, step, options.MinimumGradient, options.Polarity, options.MinimumSeparation, token))
            {
                if (edges.Count >= 4096) throw new InvalidOperationException("Caliper evidence budget exceeded.");
                var radius = outward ? inner + e.Distance : outer - e.Distance;
                edges.Add(new VisionCaliperEdge(options.PointAt(radius, middle), e.Distance, e.Gradient, angle, c));
            }
            profiles.Add(Array.AsReadOnly(profile));
        }
        var measurement = VisionCaliperMeasurement.FromArc(frameId, options, step, mean, Array.AsReadOnly(edges.ToArray()), profiles.AsReadOnly());
        return VisionCaliperFitting.Apply(measurement, EVisionCaliperFitKind.Circle, options.CaliperCount, options.FitPoint, options.FitDistanceThreshold, token);
    }

    internal static double Sample(byte[] pixels, int width, int height, double x, double y)
    {
        x = Math.Max(0, Math.Min(width - 1, x - .5)); y = Math.Max(0, Math.Min(height - 1, y - .5));
        int ix = (int)Math.Floor(x), iy = (int)Math.Floor(y), nx = Math.Min(width - 1, ix + 1), ny = Math.Min(height - 1, iy + 1);
        double fx = x - ix, fy = y - iy;
        return (pixels[iy * width + ix] * (1 - fx) + pixels[iy * width + nx] * fx) * (1 - fy)
            + (pixels[ny * width + ix] * (1 - fx) + pixels[ny * width + nx] * fx) * fy;
    }
}

/// <summary>卡尺剖面找边：中心差分梯度 → 梯度极大值抛物线插值 → 按梯度强度的间距抑制，与 DP.Vision 直线卡尺规则一致。</summary>
internal static class VisionCaliperProfile
{
    /// <summary>返回按距离排序的 (距离, 梯度)。</summary>
    internal static IReadOnlyList<(double Distance, double Gradient)> FindEdges(double[] profile, double step, double minimumGradient,
        ECaliperPolarity polarity, double minimumSeparation, CancellationToken token)
    {
        int count = profile.Length;
        var gradient = new double[count];
        for (int i = 1; i < count - 1; i++) gradient[i] = (profile[i + 1] - profile[i - 1]) / (2 * step);
        var candidates = new List<(double Distance, double Gradient)>();
        for (int i = 2; i < count - 2; i++)
        {
            token.ThrowIfCancellationRequested();
            double g = gradient[i], strength = Math.Abs(g), left = Math.Abs(gradient[i - 1]), right = Math.Abs(gradient[i + 1]);
            if (strength < minimumGradient || strength < left || strength <= right
                || polarity == ECaliperPolarity.Rising && g <= 0 || polarity == ECaliperPolarity.Falling && g >= 0) continue;
            double denominator = left - 2 * strength + right;
            double delta = Math.Abs(denominator) < 1e-12 ? 0 : Math.Max(-.5, Math.Min(.5, .5 * (left - right) / denominator));
            candidates.Add(((i + delta) * step, g));
        }
        var selected = new SortedSet<double>(); var edges = new List<(double Distance, double Gradient)>();
        foreach (var edge in candidates.OrderByDescending(e => Math.Abs(e.Gradient)).ThenBy(e => e.Distance))
        {
            token.ThrowIfCancellationRequested();
            if (selected.GetViewBetween(edge.Distance - minimumSeparation, edge.Distance + minimumSeparation)
                .Any(distance => Math.Abs(distance - edge.Distance) < minimumSeparation)) continue;
            if (edges.Count >= 4096) throw new InvalidOperationException("Caliper evidence budget exceeded.");
            selected.Add(edge.Distance); edges.Add(edge);
        }
        return edges.OrderBy(e => e.Distance).ToArray();
    }
}

/// <summary>
/// 直线卡尺配置：采样带沿宽度方向均分为 N 个并排的子卡尺，每个都沿起点→终点搜索；N ≥ 3 时用每个子卡尺取出的边缘点拟合直线。
/// </summary>
public sealed class VisionLineCaliperOptions
{
    /// <summary>构造直线卡尺。</summary>
    /// <param name="start">起点，原图像素。</param><param name="end">终点。</param>
    /// <param name="halfWidth">垂直方向单侧采样步数 0..63。</param><param name="minimumGradient">最小梯度。</param>
    /// <param name="polarity">极性，沿起点→终点。</param><param name="minimumSeparation">边缘最小间距。</param>
    /// <param name="bandSampleStep">垂直采样间隔 0.1..10。</param><param name="scanStep">沿起点→终点的搜索间隔 0.1..10。</param>
    /// <param name="caliperCount">并排子卡尺数 1..64，不超过垂直采样点数。</param>
    /// <param name="fitPoint">拟合时每个子卡尺取的边缘。</param><param name="fitDistanceThreshold">拟合内点距离阈值。</param>
    public VisionLineCaliperOptions(PointD start, PointD end, int halfWidth = 2, double minimumGradient = 5, ECaliperPolarity polarity = ECaliperPolarity.Any,
        double minimumSeparation = 2, double bandSampleStep = 1, double scanStep = 1, int caliperCount = 1,
        EVisionCaliperFitPoint fitPoint = EVisionCaliperFitPoint.Strongest, double fitDistanceThreshold = 1)
    {
        Whole = new CaliperOptions(start, end, halfWidth, minimumGradient, polarity, minimumSeparation, bandSampleStep);
        if (!double.IsFinite(scanStep) || scanStep < .1 || scanStep > 10) throw new ArgumentException("搜索间隔须为 0.1..10 像素。");
        if (caliperCount < 1 || caliperCount > 64 || caliperCount > halfWidth * 2 + 1)
            throw new ArgumentException("卡尺数量须为 1..64，且不超过垂直采样点数（2×采样半宽+1）。");
        if (!Enum.IsDefined(fitPoint) || !double.IsFinite(fitDistanceThreshold) || fitDistanceThreshold <= 0 || fitDistanceThreshold > 1000)
            throw new ArgumentException("拟合参数无效。");
        ScanStep = scanStep; CaliperCount = caliperCount; FitPoint = fitPoint; FitDistanceThreshold = fitDistanceThreshold;
    }

    /// <summary>整条采样带。</summary>
    public CaliperOptions Whole { get; }
    /// <summary>搜索间隔。</summary>
    public double ScanStep { get; }
    /// <summary>并排子卡尺数。</summary>
    public int CaliperCount { get; }
    /// <summary>拟合时每个子卡尺取的边缘。</summary>
    public EVisionCaliperFitPoint FitPoint { get; }
    /// <summary>拟合内点距离阈值。</summary>
    public double FitDistanceThreshold { get; }

    /// <summary>第 <paramref name="index"/> 个子卡尺：沿宽度方向的一段，半宽为其中能对称容纳的采样步数。</summary>
    /// <param name="index">子卡尺序号，0 在法向负侧。</param>
    public CaliperOptions SubCaliper(int index)
    {
        if (CaliperCount == 1) return Whole;
        var (normalX, normalY) = Normal;
        int samples = Whole.HalfWidth * 2 + 1;
        int sub = (samples / CaliperCount - 1) / 2;
        double offset = (-Whole.HalfWidth + (index + .5) * samples / CaliperCount - .5) * Whole.BandSampleStep;
        return new CaliperOptions(new PointD(Whole.Start.X + normalX * offset, Whole.Start.Y + normalY * offset),
            new PointD(Whole.End.X + normalX * offset, Whole.End.Y + normalY * offset), sub, Whole.MinimumGradient, Whole.Polarity,
            Whole.MinimumSeparation, Whole.BandSampleStep);
    }

    internal (double X, double Y) Normal
    {
        get
        {
            double dx = Whole.End.X - Whole.Start.X, dy = Whole.End.Y - Whole.Start.Y, length = Math.Sqrt(dx * dx + dy * dy);
            return (-dy / length, dx / length);
        }
    }
}

/// <summary>
/// 直线卡尺：每个子卡尺沿起点→终点取剖面找边。搜索间隔为 1px 时调用可替换的直线卡尺算法实现，
/// 否则使用同一规则的托管采样（双线性、垂直方向求平均、中心差分、抛物线插值、间距抑制）。
/// </summary>
public static class VisionLineCaliper
{
    /// <summary>测量直线卡尺并在子卡尺数 ≥ 3 时拟合直线。</summary>
    /// <param name="frame">Gray8 输入帧。</param><param name="options">配置。</param>
    /// <param name="measurer">1px 搜索间隔时使用的直线卡尺算法；为空时始终用托管采样。</param><param name="token">取消。</param>
    public static VisionCaliperMeasurement Measure(ImageFrame frame, VisionLineCaliperOptions options, Func<CaliperOptions, CaliperResult>? measurer,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(frame); ArgumentNullException.ThrowIfNull(options);
        var edges = new List<VisionCaliperEdge>(); var profiles = new List<IReadOnlyList<double>>();
        CaliperResult? single = null; double step = 1;
        byte[]? pixels = null;
        for (int c = 0; c < options.CaliperCount; c++)
        {
            token.ThrowIfCancellationRequested();
            var sub = options.SubCaliper(c);
            if (measurer is not null && Math.Abs(options.ScanStep - 1) < 1e-12)
            {
                var result = measurer(sub);
                if (options.CaliperCount == 1) single = result;
                step = result.SampleStep;
                profiles.Add(result.Profile);
                edges.AddRange(result.Edges.Select(e => new VisionCaliperEdge(e.Position, e.Distance, e.Gradient, null, c)));
                continue;
            }
            pixels ??= Pixels(frame);
            var (profile, subStep) = ManagedProfile(frame, pixels, sub, options.ScanStep, token);
            step = subStep;
            profiles.Add(Array.AsReadOnly(profile));
            double dx = (sub.End.X - sub.Start.X), dy = (sub.End.Y - sub.Start.Y), length = Math.Sqrt(dx * dx + dy * dy);
            foreach (var e in VisionCaliperProfile.FindEdges(profile, subStep, sub.MinimumGradient, sub.Polarity, sub.MinimumSeparation, token))
                edges.Add(new VisionCaliperEdge(new PointD(sub.Start.X + dx / length * e.Distance, sub.Start.Y + dy / length * e.Distance),
                    e.Distance, e.Gradient, null, c));
        }
        var measurement = VisionCaliperMeasurement.FromLineCalipers(frame.FrameId, options.Whole.Start, options.Whole.End, step,
            profiles.AsReadOnly(), Array.AsReadOnly(edges.OrderBy(e => e.CaliperIndex).ThenBy(e => e.Distance).ToArray()), single);
        return VisionCaliperFitting.Apply(measurement, EVisionCaliperFitKind.Line, options.CaliperCount, options.FitPoint, options.FitDistanceThreshold, token);
    }

    private static byte[] Pixels(ImageFrame frame)
    {
        var info = frame.Image.Info;
        if (info.Layout != EPixelLayout.Gray8) throw new NotSupportedException("Caliper requires explicit Gray8 preprocessing.");
        if ((long)info.Width * info.Height > 16777216) throw new ArgumentException("Caliper image budget exceeded.");
        var pixels = new byte[info.ByteLength]; frame.Image.CopyTo(0, pixels, 0, pixels.Length);
        return pixels;
    }

    private static (double[] Profile, double Step) ManagedProfile(ImageFrame frame, byte[] pixels, CaliperOptions options, double scanStep, CancellationToken token)
    {
        var info = frame.Image.Info;
        double dx = options.End.X - options.Start.X, dy = options.End.Y - options.Start.Y, length = Math.Sqrt(dx * dx + dy * dy);
        dx /= length; dy /= length;
        double half = options.HalfWidth * options.BandSampleStep;
        foreach (var p in new[] { options.Start, options.End })
            foreach (int sign in new[] { -1, 1 })
            {
                double x = p.X - dy * half * sign, y = p.Y + dx * half * sign;
                if (x < .5 || y < .5 || x > info.Width - .5 || y > info.Height - .5) throw new ArgumentException("Caliper band extends outside sampleable pixel centers.");
            }
        int count = (int)Math.Ceiling(length / scanStep) + 1; double step = length / (count - 1);
        var profile = new double[count];
        for (int i = 0; i < count; i++)
        {
            token.ThrowIfCancellationRequested();
            for (int b = -options.HalfWidth; b <= options.HalfWidth; b++)
                profile[i] += VisionArcCaliper.Sample(pixels, info.Width, info.Height,
                    options.Start.X + dx * i * step - dy * b * options.BandSampleStep, options.Start.Y + dy * i * step + dx * b * options.BandSampleStep);
            profile[i] /= options.HalfWidth * 2 + 1;
        }
        return (profile, step);
    }
}

/// <summary>卡尺内拟合：每个卡尺按规则取一个边缘点，直线用 RANSAC＋正交 TLS，圆用确定性 RANSAC＋代数最小二乘精修。</summary>
public static class VisionCaliperFitting
{
    internal static VisionCaliperMeasurement Apply(VisionCaliperMeasurement measurement, EVisionCaliperFitKind kind, int caliperCount,
        EVisionCaliperFitPoint rule, double threshold, CancellationToken token)
    {
        if (caliperCount < 3) return caliperCount == 1 ? measurement : measurement.WithFit(null, "卡尺数量少于 3，未拟合。");
        var picks = measurement.Edges.GroupBy(e => e.CaliperIndex).OrderBy(g => g.Key).Select(g => rule switch
        {
            EVisionCaliperFitPoint.First => g.OrderBy(e => e.Distance).First(),
            EVisionCaliperFitPoint.Last => g.OrderByDescending(e => e.Distance).First(),
            _ => g.OrderByDescending(e => Math.Abs(e.Gradient)).ThenBy(e => e.Distance).First()
        }).ToArray();
        if (picks.Length < 3) return measurement.WithFit(null, $"只有 {picks.Length} 个卡尺找到边缘，至少需要 3 个才能拟合。");
        var points = picks.Select(e => e.Position).ToArray();
        var calipers = Array.AsReadOnly(picks.Select(e => e.CaliperIndex).ToArray());
        try
        {
            if (kind == EVisionCaliperFitKind.Line)
            {
                var line = new RobustLineFitter().Fit(measurement.FrameId, points, threshold, 256, 3, token);
                var inliers = new bool[points.Length];
                foreach (var index in line.InlierIndices) inliers[index] = true;
                return measurement.WithFit(new VisionCaliperFit(kind, Array.AsReadOnly(points), calipers, Array.AsReadOnly(inliers), line.RmsError, line.A, line.B), null);
            }
            var circle = FitCircle(points, threshold, token);
            return measurement.WithFit(new VisionCaliperFit(kind, Array.AsReadOnly(points), calipers, Array.AsReadOnly(circle.Inliers), circle.Rms,
                center: circle.Center, radius: circle.Radius), null);
        }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException)
        {
            return measurement.WithFit(null, "拟合失败：" + error.Message);
        }
    }

    /// <summary>
    /// 鲁棒圆拟合：点数 ≤ 20 时枚举全部三点组合，否则用固定种子取 256 组；按内点数（|到圆心距离−半径| ≤ 阈值）最多、
    /// 其次误差最小选最佳圆，再用内点做代数最小二乘（Kåsa）精修并重新判定内点。
    /// </summary>
    /// <param name="points">原图点，3..8192 个。</param><param name="threshold">内点距离阈值，像素。</param><param name="token">取消。</param>
    public static (PointD Center, double Radius, bool[] Inliers, double Rms) FitCircle(IReadOnlyList<PointD> points, double threshold, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(points);
        if (points.Count < 3 || points.Count > 8192) throw new ArgumentException("圆拟合需要 3..8192 个点。");
        if (!double.IsFinite(threshold) || threshold <= 0) throw new ArgumentException("圆拟合距离阈值无效。");
        IEnumerable<(int, int, int)> Triples()
        {
            int n = points.Count;
            if (n <= 20)
            {
                for (int i = 0; i < n; i++) for (int j = i + 1; j < n; j++) for (int k = j + 1; k < n; k++) yield return (i, j, k);
                yield break;
            }
            var random = new Random(0x5EED);
            for (int t = 0; t < 256; t++)
            {
                int i = random.Next(n), j = random.Next(n - 1), k = random.Next(n - 2);
                if (j >= i) j++;
                var (a, b) = (Math.Min(i, j), Math.Max(i, j));
                if (k >= a) k++; if (k >= b) k++;
                yield return (i, j, k);
            }
        }
        (PointD Center, double Radius)? best = null; int bestCount = 0; double bestError = double.MaxValue;
        foreach (var (i, j, k) in Triples())
        {
            token.ThrowIfCancellationRequested();
            if (Circumcircle(points[i], points[j], points[k]) is not { } candidate) continue;
            var (count, error) = Score(points, candidate.Center, candidate.Radius, threshold);
            if (count > bestCount || count == bestCount && error < bestError) { best = candidate; bestCount = count; bestError = error; }
        }
        if (best is not { } chosen || bestCount < 3) throw new InvalidOperationException("没有足够的点落在同一个圆上。");
        var inliers = points.Select(p => Math.Abs(Distance(p, chosen.Center) - chosen.Radius) <= threshold).ToArray();
        var refined = LeastSquares(points.Where((_, index) => inliers[index]).ToArray()) ?? chosen;
        inliers = points.Select(p => Math.Abs(Distance(p, refined.Center) - refined.Radius) <= threshold).ToArray();
        if (inliers.Count(i => i) < 3) throw new InvalidOperationException("精修后内点不足 3 个。");
        var residuals = points.Where((_, index) => inliers[index]).Select(p => Distance(p, refined.Center) - refined.Radius).ToArray();
        return (refined.Center, refined.Radius, inliers, Math.Sqrt(residuals.Average(r => r * r)));
    }

    private static (int Count, double Error) Score(IReadOnlyList<PointD> points, PointD center, double radius, double threshold)
    {
        int count = 0; double error = 0;
        foreach (var p in points)
        {
            var d = Math.Abs(Distance(p, center) - radius);
            if (d <= threshold) { count++; error += d * d; }
        }
        return (count, error);
    }

    private static (PointD Center, double Radius)? Circumcircle(PointD a, PointD b, PointD c)
    {
        double d = 2 * (a.X * (b.Y - c.Y) + b.X * (c.Y - a.Y) + c.X * (a.Y - b.Y));
        if (Math.Abs(d) < 1e-9) return null;
        double a2 = a.X * a.X + a.Y * a.Y, b2 = b.X * b.X + b.Y * b.Y, c2 = c.X * c.X + c.Y * c.Y;
        var center = new PointD((a2 * (b.Y - c.Y) + b2 * (c.Y - a.Y) + c2 * (a.Y - b.Y)) / d, (a2 * (c.X - b.X) + b2 * (a.X - c.X) + c2 * (b.X - a.X)) / d);
        var radius = Distance(a, center);
        return double.IsFinite(radius) && radius > 0 ? (center, radius) : null;
    }

    // Kåsa 代数拟合：最小化 Σ(x²+y²+Dx+Ey+F)²，相对质心计算以保持数值稳定。
    private static (PointD Center, double Radius)? LeastSquares(IReadOnlyList<PointD> points)
    {
        if (points.Count < 3) return null;
        double mx = points.Average(p => p.X), my = points.Average(p => p.Y);
        double suu = 0, suv = 0, svv = 0, suuu = 0, svvv = 0, suvv = 0, svuu = 0;
        foreach (var p in points)
        {
            double u = p.X - mx, v = p.Y - my;
            suu += u * u; suv += u * v; svv += v * v; suuu += u * u * u; svvv += v * v * v; suvv += u * v * v; svuu += v * u * u;
        }
        double det = suu * svv - suv * suv;
        if (Math.Abs(det) < 1e-12) return null;
        double r1 = .5 * (suuu + suvv), r2 = .5 * (svvv + svuu);
        double uc = (r1 * svv - r2 * suv) / det, vc = (r2 * suu - r1 * suv) / det;
        double radius = Math.Sqrt(uc * uc + vc * vc + (suu + svv) / points.Count);
        return double.IsFinite(radius) && radius > 0 ? (new PointD(uc + mx, vc + my), radius) : null;
    }

    private static double Distance(PointD a, PointD b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
}
