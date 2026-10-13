using System.Globalization;

namespace DP.WorkFlow.UI;

/// <summary>矢量路径中的一段：直线或三次贝塞尔曲线，坐标为绝对坐标。</summary>
/// <param name="IsCubic">是否为三次贝塞尔曲线；否则为直线（只使用 <paramref name="End"/>）。</param>
/// <param name="Control1">第一控制点。</param>
/// <param name="Control2">第二控制点。</param>
/// <param name="End">终点。</param>
public readonly record struct WorkflowVectorSegment(bool IsCubic, WorkflowPoint Control1, WorkflowPoint Control2, WorkflowPoint End);

/// <summary>矢量路径中的一个子路径：起点、依次相连的线段，以及是否闭合。</summary>
/// <param name="Start">子路径起点。</param>
/// <param name="Segments">依次相连的线段。</param>
/// <param name="Closed">是否闭合回起点。</param>
public sealed record WorkflowVectorFigure(WorkflowPoint Start, IReadOnlyList<WorkflowVectorSegment> Segments, bool Closed);

/// <summary>
/// SVG 路径数据（d 属性）的精简解析器：支持 M/L/H/V/C/S/Q/A/Z 及其相对形式，圆弧转换为三次贝塞尔曲线。
/// 结果与界面框架无关，WinForms 与 WPF 各自把子路径转换为本地图形，因此两边绘制的图标完全一致。
/// </summary>
public static class WorkflowVectorPath
{
    /// <summary>解析路径数据。</summary>
    /// <param name="data">SVG 路径数据。</param>
    /// <returns>子路径集合。</returns>
    /// <exception cref="FormatException">路径数据包含不支持的命令或数字格式错误。</exception>
    public static IReadOnlyList<WorkflowVectorFigure> Parse(string data)
    {
        Guard.NotNull(data);
        var figures = new List<WorkflowVectorFigure>();
        var reader = new Reader(data);
        var segments = new List<WorkflowVectorSegment>();
        WorkflowPoint start = default, current = default, lastControl = default;
        char previous = ' ';
        bool open = false;
        void Flush(bool closed)
        {
            if (open && (segments.Count > 0 || closed)) figures.Add(new WorkflowVectorFigure(start, segments.ToArray(), closed));
            segments.Clear();
            open = false;
        }
        char command = ' ';
        while (reader.SkipSeparators())
        {
            if (reader.PeekCommand(out var next)) { command = next; reader.Advance(); }
            else if (command == ' ') throw new FormatException("路径数据在命令之外出现了坐标。");
            bool relative = char.IsLower(command);
            var origin = relative ? current : default;
            var upper = char.ToUpperInvariant(command);
            // 闭合后不经 M 直接绘制：以当前点为新子路径起点（SVG 规则）。
            if (!open && upper is not ('M' or 'Z')) { start = current; open = true; }
            switch (upper)
            {
                case 'M':
                    Flush(false);
                    current = Add(origin, reader.Point());
                    start = current; open = true;
                    // M 之后的隐式坐标对按 L 处理。
                    command = relative ? 'l' : 'L';
                    break;
                case 'L':
                    current = Add(origin, reader.Point());
                    segments.Add(Line(current));
                    break;
                case 'H':
                    current = new WorkflowPoint((relative ? current.X : 0) + reader.Number(), current.Y);
                    segments.Add(Line(current));
                    break;
                case 'V':
                    current = new WorkflowPoint(current.X, (relative ? current.Y : 0) + reader.Number());
                    segments.Add(Line(current));
                    break;
                case 'C':
                {
                    var c1 = Add(origin, reader.Point());
                    var c2 = Add(origin, reader.Point());
                    var end = Add(origin, reader.Point());
                    segments.Add(new WorkflowVectorSegment(true, c1, c2, end));
                    lastControl = c2; current = end;
                    break;
                }
                case 'S':
                {
                    var c1 = previous is 'C' or 'S' ? Reflect(lastControl, current) : current;
                    var c2 = Add(origin, reader.Point());
                    var end = Add(origin, reader.Point());
                    segments.Add(new WorkflowVectorSegment(true, c1, c2, end));
                    lastControl = c2; current = end;
                    break;
                }
                case 'Q':
                {
                    var q = Add(origin, reader.Point());
                    var end = Add(origin, reader.Point());
                    segments.Add(new WorkflowVectorSegment(true,
                        new WorkflowPoint(current.X + 2d / 3 * (q.X - current.X), current.Y + 2d / 3 * (q.Y - current.Y)),
                        new WorkflowPoint(end.X + 2d / 3 * (q.X - end.X), end.Y + 2d / 3 * (q.Y - end.Y)), end));
                    current = end;
                    break;
                }
                case 'A':
                {
                    double rx = reader.Number(), ry = reader.Number(), rotation = reader.Number();
                    bool large = reader.Flag(), sweep = reader.Flag();
                    var end = Add(origin, reader.Point());
                    segments.AddRange(Arc(current, end, rx, ry, rotation, large, sweep));
                    current = end;
                    break;
                }
                case 'Z':
                    Flush(true);
                    current = start;
                    break;
                default:
                    throw new FormatException($"不支持的路径命令：{command}");
            }
            previous = upper;
            if (upper == 'Z') command = ' ';
        }
        Flush(false);
        return figures;
    }

    private static WorkflowVectorSegment Line(WorkflowPoint end) => new(false, default, default, end);
    private static WorkflowPoint Add(WorkflowPoint origin, WorkflowPoint point) => new(origin.X + point.X, origin.Y + point.Y);
    private static WorkflowPoint Reflect(WorkflowPoint control, WorkflowPoint about) => new(2 * about.X - control.X, 2 * about.Y - control.Y);

    /// <summary>SVG 端点参数化圆弧 → 中心参数化 → 每段不超过 90° 的三次贝塞尔近似。</summary>
    private static IEnumerable<WorkflowVectorSegment> Arc(WorkflowPoint from, WorkflowPoint to, double rx, double ry, double rotationDegrees, bool large, bool sweep)
    {
        if (Math.Abs(from.X - to.X) < 1e-12 && Math.Abs(from.Y - to.Y) < 1e-12) yield break;
        rx = Math.Abs(rx); ry = Math.Abs(ry);
        if (rx < 1e-12 || ry < 1e-12) { yield return Line(to); yield break; }
        double phi = rotationDegrees * Math.PI / 180, cos = Math.Cos(phi), sin = Math.Sin(phi);
        double dx = (from.X - to.X) / 2, dy = (from.Y - to.Y) / 2;
        double x1 = cos * dx + sin * dy, y1 = -sin * dx + cos * dy;
        double lambda = x1 * x1 / (rx * rx) + y1 * y1 / (ry * ry);
        if (lambda > 1) { var scale = Math.Sqrt(lambda); rx *= scale; ry *= scale; }
        double numerator = rx * rx * ry * ry - rx * rx * y1 * y1 - ry * ry * x1 * x1;
        double denominator = rx * rx * y1 * y1 + ry * ry * x1 * x1;
        double factor = Math.Sqrt(Math.Max(0, numerator / denominator)) * (large == sweep ? -1 : 1);
        double cx1 = factor * rx * y1 / ry, cy1 = -factor * ry * x1 / rx;
        double cx = cos * cx1 - sin * cy1 + (from.X + to.X) / 2, cy = sin * cx1 + cos * cy1 + (from.Y + to.Y) / 2;
        double Angle(double ux, double uy, double vx, double vy)
        {
            var a = Math.Atan2(ux * vy - uy * vx, ux * vx + uy * vy);
            return a;
        }
        double theta = Angle(1, 0, (x1 - cx1) / rx, (y1 - cy1) / ry);
        double delta = Angle((x1 - cx1) / rx, (y1 - cy1) / ry, (-x1 - cx1) / rx, (-y1 - cy1) / ry);
        if (!sweep && delta > 0) delta -= 2 * Math.PI;
        else if (sweep && delta < 0) delta += 2 * Math.PI;
        int count = (int)Math.Ceiling(Math.Abs(delta) / (Math.PI / 2) - 1e-9);
        double step = delta / count, k = 4d / 3 * Math.Tan(step / 4);
        WorkflowPoint Map(double ex, double ey) => new(cx + cos * rx * ex - sin * ry * ey, cy + sin * rx * ex + cos * ry * ey);
        for (int i = 0; i < count; i++)
        {
            double a1 = theta + i * step, a2 = a1 + step;
            double c1 = Math.Cos(a1), s1 = Math.Sin(a1), c2 = Math.Cos(a2), s2 = Math.Sin(a2);
            var end = i == count - 1 ? to : Map(c2, s2);
            yield return new WorkflowVectorSegment(true, Map(c1 - k * s1, s1 + k * c1), Map(c2 + k * s2, s2 - k * c2), end);
        }
    }

    private sealed class Reader(string text)
    {
        private int _index;

        public bool SkipSeparators()
        {
            while (_index < text.Length && (char.IsWhiteSpace(text[_index]) || text[_index] == ',')) _index++;
            return _index < text.Length;
        }

        public bool PeekCommand(out char command)
        {
            command = text[_index];
            return char.IsLetter(command) && command is not ('e' or 'E');
        }

        public void Advance() => _index++;

        public double Number()
        {
            SkipSeparators();
            int start = _index;
            if (_index < text.Length && (text[_index] == '-' || text[_index] == '+')) _index++;
            bool dot = false, digits = false;
            while (_index < text.Length)
            {
                char c = text[_index];
                if (char.IsDigit(c)) { digits = true; _index++; }
                else if (c == '.' && !dot) { dot = true; _index++; }
                else break;
            }
            if (_index < text.Length && (text[_index] == 'e' || text[_index] == 'E'))
            {
                _index++;
                if (_index < text.Length && (text[_index] == '-' || text[_index] == '+')) _index++;
                while (_index < text.Length && char.IsDigit(text[_index])) _index++;
            }
            if (!digits) throw new FormatException($"路径数据在位置 {start} 处缺少数字。");
            return double.Parse(text.Substring(start, _index - start), NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        /// <summary>圆弧标志位可以紧贴书写（如 "a8 8 0 0110 0"），按单个字符读取。</summary>
        public bool Flag()
        {
            SkipSeparators();
            if (_index >= text.Length || (text[_index] != '0' && text[_index] != '1')) throw new FormatException("圆弧标志位必须为 0 或 1。");
            return text[_index++] == '1';
        }

        public WorkflowPoint Point() => new(Number(), Number());
    }
}
