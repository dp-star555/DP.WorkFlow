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
    /// <summary>直线卡尺的算法原始结果；圆弧卡尺为空。</summary>
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
    public string Summary => Shape == EVisionCaliperShape.Arc
        ? ArcDirection is EVisionArcScanDirection.InnerToOuter or EVisionArcScanDirection.OuterToInner
            ? FormattableString.Invariant($"圆弧卡尺（{(ArcDirection == EVisionArcScanDirection.InnerToOuter ? "由内到外" : "由外到内")}）{Profiles.Count} 个径向卡尺，边缘 {Count}；半径 {Radius:0.##}px。")
            : FormattableString.Invariant($"圆弧卡尺边缘 {Count}；半径 {Radius:0.##}px；剖面采样 {Profile.Count}。")
        : $"卡尺边缘 {Count}；剖面采样 {Profile.Count}；梯度峰抛物线插值。";

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
    public VisionArcCaliperOptions(PointD center, double radius, double startAngleDegrees, double sweepDegrees, int halfWidth = 2,
        double minimumGradient = 5, ECaliperPolarity polarity = ECaliperPolarity.Any, double minimumSeparation = 2, double bandSampleStep = 1,
        EVisionArcScanDirection direction = EVisionArcScanDirection.AlongArc, int caliperCount = 1)
    {
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
        int count = (int)Math.Ceiling(length) + 1;
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
        var edges = FindEdges(profile, step, options, token).Select(e =>
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
        int count = (int)Math.Ceiling(length) + 1;
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
            foreach (var e in FindEdges(profile, step, options, token))
            {
                if (edges.Count >= 4096) throw new InvalidOperationException("Caliper evidence budget exceeded.");
                var radius = outward ? inner + e.Distance : outer - e.Distance;
                edges.Add(new VisionCaliperEdge(options.PointAt(radius, middle), e.Distance, e.Gradient, angle, c));
            }
            profiles.Add(Array.AsReadOnly(profile));
        }
        return VisionCaliperMeasurement.FromArc(frameId, options, step, mean, Array.AsReadOnly(edges.ToArray()), profiles.AsReadOnly());
    }

    // 中心差分梯度 → 极大值抛物线插值 → 按梯度强度的间距抑制；返回按距离排序的 (距离, 梯度)。
    private static IEnumerable<(double Distance, double Gradient)> FindEdges(double[] profile, double step, VisionArcCaliperOptions options, CancellationToken token)
    {
        int count = profile.Length;
        var gradient = new double[count];
        for (int i = 1; i < count - 1; i++) gradient[i] = (profile[i + 1] - profile[i - 1]) / (2 * step);
        var candidates = new List<(double Distance, double Gradient)>();
        for (int i = 2; i < count - 2; i++)
        {
            token.ThrowIfCancellationRequested();
            double g = gradient[i], strength = Math.Abs(g), left = Math.Abs(gradient[i - 1]), right = Math.Abs(gradient[i + 1]);
            if (strength < options.MinimumGradient || strength < left || strength <= right
                || options.Polarity == ECaliperPolarity.Rising && g <= 0 || options.Polarity == ECaliperPolarity.Falling && g >= 0) continue;
            double denominator = left - 2 * strength + right;
            double delta = Math.Abs(denominator) < 1e-12 ? 0 : Math.Max(-.5, Math.Min(.5, .5 * (left - right) / denominator));
            candidates.Add(((i + delta) * step, g));
        }
        var selected = new SortedSet<double>(); var edges = new List<(double Distance, double Gradient)>();
        foreach (var edge in candidates.OrderByDescending(e => Math.Abs(e.Gradient)).ThenBy(e => e.Distance))
        {
            token.ThrowIfCancellationRequested();
            if (selected.GetViewBetween(edge.Distance - options.MinimumSeparation, edge.Distance + options.MinimumSeparation)
                .Any(distance => Math.Abs(distance - edge.Distance) < options.MinimumSeparation)) continue;
            if (edges.Count >= 4096) throw new InvalidOperationException("Caliper evidence budget exceeded.");
            selected.Add(edge.Distance); edges.Add(edge);
        }
        return edges.OrderBy(e => e.Distance);
    }

    private static double Sample(byte[] pixels, int width, int height, double x, double y)
    {
        x = Math.Max(0, Math.Min(width - 1, x - .5)); y = Math.Max(0, Math.Min(height - 1, y - .5));
        int ix = (int)Math.Floor(x), iy = (int)Math.Floor(y), nx = Math.Min(width - 1, ix + 1), ny = Math.Min(height - 1, iy + 1);
        double fx = x - ix, fy = y - iy;
        return (pixels[iy * width + ix] * (1 - fx) + pixels[iy * width + nx] * fx) * (1 - fy)
            + (pixels[ny * width + ix] * (1 - fx) + pixels[ny * width + nx] * fx) * fy;
    }
}
