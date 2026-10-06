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

/// <summary>卡尺输出单个边缘还是边缘对。</summary>
public enum EVisionCaliperEdgeMode
{
    /// <summary>输出剖面上的各个边缘。</summary>
    [Description("单边缘")]
    Single,
    /// <summary>把相邻、极性相反的两个边缘配成一对，输出中点与宽度（如线宽、缝宽、引脚宽）。</summary>
    [Description("边缘对")]
    Pair
}

/// <summary>卡尺边缘：亚像素位置、沿扫描路径的距离（圆弧为弧长）和有符号梯度。</summary>
public sealed class VisionCaliperEdge
{
    internal VisionCaliperEdge(PointD position, double distance, double gradient, double? angleDegrees)
    { Position = position; Distance = distance; Gradient = gradient; AngleDegrees = angleDegrees; }
    /// <summary>原图像素边界坐标。</summary>
    public PointD Position { get; }
    /// <summary>沿扫描路径距离；圆弧卡尺为从起始角开始的弧长，原图像素。</summary>
    public double Distance { get; }
    /// <summary>有符号梯度，灰度/像素，沿扫描方向。</summary>
    public double Gradient { get; }
    /// <summary>圆弧卡尺边缘所在的原图角度（度，X轴起顺时针）；直线卡尺为空。</summary>
    public double? AngleDegrees { get; }
}

/// <summary>边缘对：沿扫描方向先后两个极性相反的边缘，宽度为两者沿扫描路径的距离差。</summary>
public sealed class VisionCaliperEdgePair
{
    internal VisionCaliperEdgePair(VisionCaliperEdge first, VisionCaliperEdge second, PointD midpoint)
    { First = first; Second = second; Midpoint = midpoint; }
    /// <summary>第一个边缘（极性符合“边缘极性”设置）。</summary>
    public VisionCaliperEdge First { get; }
    /// <summary>第二个边缘（极性与第一个相反）。</summary>
    public VisionCaliperEdge Second { get; }
    /// <summary>两边缘中点（沿扫描路径），原图像素。</summary>
    public PointD Midpoint { get; }
    /// <summary>宽度：沿扫描路径的距离差，原图像素。</summary>
    public double Width => Second.Distance - First.Distance;
}

/// <summary>
/// 边缘配对（与 HALCON measure_pairs 的思路一致）：边缘按扫描距离排序，从近到远找第一个极性符合要求的边缘，
/// 再向后找第一个极性相反、宽度落在 [最小宽度, 最大宽度] 内的边缘配成一对；配对后从第二个边缘之后继续，不重叠。
/// </summary>
public static class VisionEdgePairing
{
    /// <summary>返回配对的下标 (第一个, 第二个)。</summary>
    /// <param name="distances">按升序排列的边缘距离。</param><param name="gradients">同序有符号梯度。</param>
    /// <param name="firstPolarity">第一个边缘的极性；任意时第一个边缘可为任一极性，第二个与之相反。</param>
    /// <param name="minimumWidth">最小宽度（含），像素。</param><param name="maximumWidth">最大宽度（含），像素。</param>
    public static IReadOnlyList<(int First, int Second)> Pair(IReadOnlyList<double> distances, IReadOnlyList<double> gradients,
        ECaliperPolarity firstPolarity, double minimumWidth, double maximumWidth)
    {
        ArgumentNullException.ThrowIfNull(distances); ArgumentNullException.ThrowIfNull(gradients);
        if (distances.Count != gradients.Count) throw new ArgumentException("边缘距离与梯度数量不一致。");
        var pairs = new List<(int, int)>();
        for (int i = 0; i < distances.Count; i++)
        {
            double g = gradients[i];
            if (firstPolarity == ECaliperPolarity.Rising && g <= 0 || firstPolarity == ECaliperPolarity.Falling && g >= 0 || g == 0) continue;
            for (int j = i + 1; j < distances.Count; j++)
            {
                if (Math.Sign(gradients[j]) != -Math.Sign(g)) continue;
                double width = distances[j] - distances[i];
                if (width > maximumWidth) break;
                if (width < minimumWidth) continue;
                pairs.Add((i, j)); i = j; break;
            }
        }
        return pairs;
    }
}

/// <summary>
/// 单个卡尺（直线或圆弧）的采样证据：剖面、边缘，以及边缘对模式下的边缘对与宽度。
/// 成员名与直线卡尺原结果一致（起终点、剖面、边缘、边缘点），下游绑定不需要改路径。
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

    internal static VisionCaliperMeasurement FromLineProfile(string frameId, PointD start, PointD end, double step, double[] profile,
        IReadOnlyList<VisionCaliperEdge> edges) =>
        new(frameId, EVisionCaliperShape.Line, start, end, step, Array.AsReadOnly(profile), edges, new[] { start, end }, null);

    internal static VisionCaliperMeasurement FromArc(string frameId, VisionArcCaliperOptions options, double step, double[] profile,
        IReadOnlyList<VisionCaliperEdge> edges)
    {
        var path = new PointD[Math.Clamp((int)Math.Ceiling(Math.Abs(options.SweepDegrees) / 3), 8, 120) + 1];
        for (var i = 0; i < path.Length; i++) path[i] = options.PointAt(options.Radius, (double)i / (path.Length - 1));
        return new VisionCaliperMeasurement(frameId, EVisionCaliperShape.Arc, options.PointAt(options.Radius, 0), options.PointAt(options.Radius, 1),
            step, Array.AsReadOnly((double[])profile.Clone()), edges, path, null)
        {
            Center = options.Center, Radius = options.Radius, StartAngleDegrees = options.StartAngleDegrees, SweepDegrees = options.SweepDegrees
        };
    }

    /// <summary>按边缘对模式配对边缘；单边缘模式原样返回。</summary>
    /// <param name="mode">边缘模式。</param><param name="firstPolarity">第一个边缘的极性。</param>
    /// <param name="minimumWidth">最小宽度，原图像素。</param><param name="maximumWidth">最大宽度，原图像素。</param>
    public VisionCaliperMeasurement WithEdgeMode(EVisionCaliperEdgeMode mode, ECaliperPolarity firstPolarity, double minimumWidth, double maximumWidth)
    {
        var copy = (VisionCaliperMeasurement)MemberwiseClone();
        copy.EdgeMode = mode;
        if (mode != EVisionCaliperEdgeMode.Pair) { copy.Pairs = Array.Empty<VisionCaliperEdgePair>(); return copy; }
        var indices = VisionEdgePairing.Pair(Edges.Select(e => e.Distance).ToArray(), Edges.Select(e => e.Gradient).ToArray(),
            firstPolarity, minimumWidth, maximumWidth);
        copy.Pairs = Array.AsReadOnly(indices.Select(p => new VisionCaliperEdgePair(Edges[p.First], Edges[p.Second],
            PointAtDistance((Edges[p.First].Distance + Edges[p.Second].Distance) / 2))).ToArray());
        return copy;
    }

    // 沿扫描路径距离对应的原图点：直线按起终点插值，圆弧按弧长换算角度。
    private PointD PointAtDistance(double distance)
    {
        if (Shape == EVisionCaliperShape.Arc && Center is { } center && Radius is { } radius && StartAngleDegrees is { } start && SweepDegrees is { } sweep)
        {
            var angle = (start + Math.Sign(sweep) * distance / radius * 180 / Math.PI) * Math.PI / 180;
            return new PointD(center.X + radius * Math.Cos(angle), center.Y + radius * Math.Sin(angle));
        }
        double dx = End.X - Start.X, dy = End.Y - Start.Y, length = Math.Sqrt(dx * dx + dy * dy);
        return length < 1e-12 ? Start : new PointD(Start.X + dx / length * distance, Start.Y + dy / length * distance);
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
    /// <summary>沿扫描路径的均匀采样步长，原图像素。</summary>
    public double SampleStep { get; }
    /// <summary>一维灰度均值剖面。</summary>
    public IReadOnlyList<double> Profile { get; }
    /// <summary>按扫描距离排序的边缘。</summary>
    public IReadOnlyList<VisionCaliperEdge> Edges { get; }
    /// <summary>边缘数。</summary>
    public int Count => Edges.Count;
    /// <summary>边缘模式。</summary>
    public EVisionCaliperEdgeMode EdgeMode { get; private set; }
    /// <summary>边缘对（边缘对模式），按扫描距离排序；单边缘模式为空。</summary>
    public IReadOnlyList<VisionCaliperEdgePair> Pairs { get; private set; } = Array.Empty<VisionCaliperEdgePair>();
    /// <summary>边缘对数量。</summary>
    public int PairCount => Pairs.Count;
    /// <summary>各边缘对宽度，原图像素。</summary>
    public IReadOnlyList<double> Widths => Array.AsReadOnly(Pairs.Select(p => p.Width).ToArray());
    /// <summary>第一个边缘对的宽度；没有边缘对时为空。</summary>
    public double? Width => Pairs.Count == 0 ? null : Pairs[0].Width;
    /// <summary>平均宽度；没有边缘对时为空。</summary>
    public double? MeanWidth => Pairs.Count == 0 ? null : Pairs.Average(p => p.Width);
    /// <summary>最小宽度；没有边缘对时为空。</summary>
    public double? MinimumWidth => Pairs.Count == 0 ? null : Pairs.Min(p => p.Width);
    /// <summary>最大宽度；没有边缘对时为空。</summary>
    public double? MaximumWidth => Pairs.Count == 0 ? null : Pairs.Max(p => p.Width);
    /// <summary>直线卡尺、1px 采样间隔时的算法原始结果；其它情况为空。</summary>
    [Browsable(false)]
    public CaliperResult? Line { get; }
    /// <summary>可选本帧定位来源；位置、距离均保持原图像素。</summary>
    public VisionCoordinateSystem? CoordinateSystem { get; private set; }
    /// <summary>同序边缘点，带来源帧与坐标系。</summary>
    public IReadOnlyList<VisionPoint> MeasuredEdges => Array.AsReadOnly(Edges.Select(e => new VisionPoint(FrameId, e.Position, CoordinateSystem)).ToArray());
    /// <summary>与边缘对同序的中点，带来源帧与坐标系。</summary>
    public IReadOnlyList<VisionPoint> MeasuredPairCenters => Array.AsReadOnly(Pairs.Select(p => new VisionPoint(FrameId, p.Midpoint, CoordinateSystem)).ToArray());
    /// <summary>与边缘同序的双坐标边缘；未绑定坐标系时为空。</summary>
    public IReadOnlyList<LocatedPoint>? LocatedEdges { get; private set; }
    /// <summary>扫描起点的双坐标。</summary>
    public LocatedPoint? LocatedStart => CoordinateSystem?.Locate(Start);
    /// <summary>扫描终点的双坐标。</summary>
    public LocatedPoint? LocatedEnd => CoordinateSystem?.Locate(End);
    /// <summary>完成状态，空边缘不是执行错误。</summary>
    public EAlgorithmStatus Status => EAlgorithmStatus.Completed;

    /// <inheritdoc/>
    public string Summary
    {
        get
        {
            var head = Shape == EVisionCaliperShape.Arc
                ? FormattableString.Invariant($"圆弧卡尺边缘 {Count}；半径 {Radius:0.##}px；剖面采样 {Profile.Count}。")
                : $"卡尺边缘 {Count}；剖面采样 {Profile.Count}；梯度峰抛物线插值。";
            if (EdgeMode != EVisionCaliperEdgeMode.Pair) return head;
            return Pairs.Count == 0
                ? head + " 边缘对 0。"
                : head + FormattableString.Invariant($" 边缘对 {Pairs.Count}，宽度 {Pairs[0].Width:0.###}px（平均 {MeanWidth:0.###}，最小 {MinimumWidth:0.###}，最大 {MaximumWidth:0.###}）。");
        }
    }

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
    /// <param name="scanStep">沿圆弧的剖面采样间隔 0.1..10 像素。</param>
    public VisionArcCaliperOptions(PointD center, double radius, double startAngleDegrees, double sweepDegrees, int halfWidth = 2,
        double minimumGradient = 5, ECaliperPolarity polarity = ECaliperPolarity.Any, double minimumSeparation = 2, double bandSampleStep = 1,
        double scanStep = 1)
    {
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
        if (!double.IsFinite(scanStep) || scanStep < .1 || scanStep > 10) throw new ArgumentException("采样间隔须为 0.1..10 像素。");
        Center = center; Radius = radius; StartAngleDegrees = startAngleDegrees; SweepDegrees = sweepDegrees; HalfWidth = halfWidth;
        MinimumGradient = minimumGradient; Polarity = polarity; MinimumSeparation = minimumSeparation; BandSampleStep = bandSampleStep; ScanStep = scanStep;
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
    /// <summary>沿圆弧的剖面采样间隔。</summary>
    public double ScanStep { get; }
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
/// 圆弧卡尺（与 HALCON gen_measure_arc 的思路一致）：沿扫描圆弧按采样间隔均匀取点，每点沿半径方向取 2×半宽+1 个双线性采样求平均得到剖面，
/// 中心差分求梯度，梯度极大值抛物线插值得到亚像素边缘，按梯度强度做弧长间距抑制。与直线卡尺同一套剖面/峰值规则。
/// </summary>
public static class VisionArcCaliper
{
    /// <summary>测量圆弧采样带。</summary>
    /// <param name="frame">Gray8 输入帧。</param><param name="options">采样配置。</param><param name="token">取消。</param>
    public static VisionCaliperMeasurement Measure(ImageFrame frame, VisionArcCaliperOptions options, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(frame); ArgumentNullException.ThrowIfNull(options);
        var info = frame.Image.Info;
        var pixels = VisionCaliperProfile.Pixels(frame);
        double inner = options.Radius - options.HalfWidth * options.BandSampleStep, outer = options.Radius + options.HalfWidth * options.BandSampleStep;
        var length = options.Length;
        int count = (int)Math.Ceiling(length / options.ScanStep) + 1;
        double step = length / (count - 1);
        var profile = new double[count];
        for (int i = 0; i < count; i++)
        {
            token.ThrowIfCancellationRequested();
            double t = (double)i / (count - 1);
            foreach (var p in new[] { options.PointAt(inner, t), options.PointAt(outer, t) })
                if (p.X < .5 || p.Y < .5 || p.X > info.Width - .5 || p.Y > info.Height - .5)
                    throw new ArgumentException("Caliper band extends outside sampleable pixel centers.");
            for (int b = -options.HalfWidth; b <= options.HalfWidth; b++)
            {
                var p = options.PointAt(options.Radius + b * options.BandSampleStep, t);
                profile[i] += VisionCaliperProfile.Sample(pixels, info.Width, info.Height, p.X, p.Y);
            }
            profile[i] /= options.HalfWidth * 2 + 1;
        }
        var edges = VisionCaliperProfile.FindEdges(profile, step, options.MinimumGradient, options.Polarity, options.MinimumSeparation, token).Select(e =>
        {
            var t = e.Distance / length;
            return new VisionCaliperEdge(options.PointAt(options.Radius, t), e.Distance, e.Gradient, options.StartAngleDegrees + options.SweepDegrees * t);
        }).ToArray();
        return VisionCaliperMeasurement.FromArc(frame.FrameId, options, step, profile, Array.AsReadOnly(edges));
    }
}

/// <summary>
/// 直线卡尺：采样间隔为 1px 时调用可替换的直线卡尺算法实现；其它间隔使用同一规则的托管采样
/// （双线性、垂直方向求平均、中心差分、抛物线插值、间距抑制）。
/// </summary>
public static class VisionLineCaliper
{
    /// <summary>测量直线卡尺。</summary>
    /// <param name="frame">Gray8 输入帧。</param><param name="options">采样带。</param><param name="scanStep">沿起点→终点的剖面采样间隔 0.1..10。</param>
    /// <param name="measurer">1px 采样间隔时使用的直线卡尺算法；为空时始终用托管采样。</param><param name="token">取消。</param>
    public static VisionCaliperMeasurement Measure(ImageFrame frame, CaliperOptions options, double scanStep, Func<CaliperOptions, CaliperResult>? measurer,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(frame); ArgumentNullException.ThrowIfNull(options);
        if (!double.IsFinite(scanStep) || scanStep < .1 || scanStep > 10) throw new ArgumentException("采样间隔须为 0.1..10 像素。");
        if (measurer is not null && Math.Abs(scanStep - 1) < 1e-12) return VisionCaliperMeasurement.FromLine(measurer(options));
        var info = frame.Image.Info;
        var pixels = VisionCaliperProfile.Pixels(frame);
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
                profile[i] += VisionCaliperProfile.Sample(pixels, info.Width, info.Height,
                    options.Start.X + dx * i * step - dy * b * options.BandSampleStep, options.Start.Y + dy * i * step + dx * b * options.BandSampleStep);
            profile[i] /= options.HalfWidth * 2 + 1;
        }
        var edges = VisionCaliperProfile.FindEdges(profile, step, options.MinimumGradient, options.Polarity, options.MinimumSeparation, token)
            .Select(e => new VisionCaliperEdge(new PointD(options.Start.X + dx * e.Distance, options.Start.Y + dy * e.Distance), e.Distance, e.Gradient, null))
            .ToArray();
        return VisionCaliperMeasurement.FromLineProfile(frame.FrameId, options.Start, options.End, step, profile, Array.AsReadOnly(edges));
    }
}

/// <summary>卡尺剖面找边：中心差分梯度 → 梯度极大值抛物线插值 → 按梯度强度的间距抑制，与 DP.Vision 直线卡尺规则一致。</summary>
internal static class VisionCaliperProfile
{
    internal static byte[] Pixels(ImageFrame frame)
    {
        var info = frame.Image.Info;
        if (info.Layout != EPixelLayout.Gray8) throw new NotSupportedException("卡尺需要显式预处理为 Gray8。");
        if ((long)info.Width * info.Height > 16777216) throw new ArgumentException("Caliper image budget exceeded.");
        var pixels = new byte[info.ByteLength]; frame.Image.CopyTo(0, pixels, 0, pixels.Length);
        return pixels;
    }

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

    internal static double Sample(byte[] pixels, int width, int height, double x, double y)
    {
        x = Math.Max(0, Math.Min(width - 1, x - .5)); y = Math.Max(0, Math.Min(height - 1, y - .5));
        int ix = (int)Math.Floor(x), iy = (int)Math.Floor(y), nx = Math.Min(width - 1, ix + 1), ny = Math.Min(height - 1, iy + 1);
        double fx = x - ix, fy = y - iy;
        return (pixels[iy * width + ix] * (1 - fx) + pixels[iy * width + nx] * fx) * (1 - fy)
            + (pixels[ny * width + ix] * (1 - fx) + pixels[ny * width + nx] * fx) * fy;
    }
}
