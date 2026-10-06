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
        ? FormattableString.Invariant($"圆弧卡尺边缘 {Count}；半径 {Radius:0.##}px；剖面采样 {Profile.Count}。")
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
    public VisionArcCaliperOptions(PointD center, double radius, double startAngleDegrees, double sweepDegrees, int halfWidth = 2,
        double minimumGradient = 5, ECaliperPolarity polarity = ECaliperPolarity.Any, double minimumSeparation = 2, double bandSampleStep = 1)
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
/// 圆弧卡尺：沿扫描圆弧按 1px 弧长均匀取点，每点沿半径方向取 2×半宽+1 个双线性采样求平均得到剖面，
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
        if (info.Layout != EPixelLayout.Gray8) throw new NotSupportedException("圆弧卡尺需要显式预处理为 Gray8。");
        if ((long)info.Width * info.Height > 16777216) throw new ArgumentException("Caliper image budget exceeded.");
        var length = options.Length;
        int count = (int)Math.Ceiling(length) + 1;
        double step = length / (count - 1);
        double inner = options.Radius - options.HalfWidth * options.BandSampleStep, outer = options.Radius + options.HalfWidth * options.BandSampleStep;
        var pixels = new byte[info.ByteLength]; frame.Image.CopyTo(0, pixels, 0, pixels.Length);
        var profile = new double[count]; var gradient = new double[count];
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
                profile[i] += Sample(pixels, info.Width, info.Height, p.X, p.Y);
            }
            profile[i] /= options.HalfWidth * 2 + 1;
        }
        for (int i = 1; i < count - 1; i++) gradient[i] = (profile[i + 1] - profile[i - 1]) / (2 * step);
        var candidates = new List<VisionCaliperEdge>();
        for (int i = 2; i < count - 2; i++)
        {
            token.ThrowIfCancellationRequested();
            double g = gradient[i], strength = Math.Abs(g), left = Math.Abs(gradient[i - 1]), right = Math.Abs(gradient[i + 1]);
            if (strength < options.MinimumGradient || strength < left || strength <= right
                || options.Polarity == ECaliperPolarity.Rising && g <= 0 || options.Polarity == ECaliperPolarity.Falling && g >= 0) continue;
            double denominator = left - 2 * strength + right;
            double delta = Math.Abs(denominator) < 1e-12 ? 0 : Math.Max(-.5, Math.Min(.5, .5 * (left - right) / denominator));
            double distance = (i + delta) * step, t = distance / length;
            candidates.Add(new VisionCaliperEdge(options.PointAt(options.Radius, t), distance, g, options.StartAngleDegrees + options.SweepDegrees * t));
        }
        var selected = new SortedSet<double>(); var edges = new List<VisionCaliperEdge>();
        foreach (var edge in candidates.OrderByDescending(e => Math.Abs(e.Gradient)).ThenBy(e => e.Distance))
        {
            token.ThrowIfCancellationRequested();
            if (selected.GetViewBetween(edge.Distance - options.MinimumSeparation, edge.Distance + options.MinimumSeparation)
                .Any(distance => Math.Abs(distance - edge.Distance) < options.MinimumSeparation)) continue;
            if (edges.Count >= 4096) throw new InvalidOperationException("Caliper evidence budget exceeded.");
            selected.Add(edge.Distance); edges.Add(edge);
        }
        return VisionCaliperMeasurement.FromArc(frame.FrameId, options, step, profile, Array.AsReadOnly(edges.OrderBy(e => e.Distance).ToArray()));
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
