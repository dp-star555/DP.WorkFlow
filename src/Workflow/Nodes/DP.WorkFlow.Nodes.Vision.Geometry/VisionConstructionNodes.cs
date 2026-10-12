using System.ComponentModel;
using DP.Vision;
using DP.Vision.Algorithms;

namespace DP.WorkFlow;

/// <summary>两线夹角的取值方式。</summary>
public enum EVisionLineAngleMode
{
    /// <summary>不考虑直线方向的锐角。</summary>
    [Description("锐角 0..90°")]
    Acute,
    /// <summary>按两条直线各自 A→B 方向的夹角。</summary>
    [Description("方向夹角 0..180°")]
    Direction,
    /// <summary>从直线 1 方向转到直线 2 方向的有向角，图像中顺时针为正。</summary>
    [Description("有向角 -180..180°（顺时针为正）")]
    Signed
}

/// <summary>线线交点：两条无限直线（延长线）的交点及锐角夹角。</summary>
[WorkflowNode("Vision.IntersectLines", DisplayName = "线线交点", Category = WorkflowVisionCategories.Geometry)]
public sealed class IntersectVisionLinesNodeModel : WorkflowVisionGeometryNodeModel
{
    /// <inheritdoc/>
    public override string NodeType => "Vision.IntersectLines";
    /// <summary>第一条直线。</summary>
    [WorkflowProperty("直线1", "找线、拟合直线或两点生成直线的输出；按无限直线求交，交点可在线段延长线上。", Category = "输入")]
    public WorkflowInput<VisionLine> LineA { get; set; } = WorkflowInput<VisionLine>.FromLiteral(null);
    /// <summary>第二条直线。</summary>
    [WorkflowProperty("直线2", "与直线1同帧、同坐标来源。", Category = "输入")]
    public WorkflowInput<VisionLine> LineB { get; set; } = WorkflowInput<VisionLine>.FromLiteral(null);
    /// <summary>判定为平行的最小夹角。</summary>
    [WorkflowProperty("最小夹角(°)", "两线夹角小于该值视为近乎平行，交点不稳定，节点故障（可走“失败”出口）。", Category = "判定")]
    public double MinimumAngleDegrees { get; set; } = .1;
    /// <inheritdoc/>
    public override IReadOnlyList<string> ValidateConfiguration()
    {
        var errors = base.ValidateConfiguration().ToList();
        if (!IsBound(LineA) || !IsBound(LineB)) errors.Add("线线交点必须绑定两条直线。");
        if (!double.IsFinite(MinimumAngleDegrees) || MinimumAngleDegrees < 0 || MinimumAngleDegrees >= 90) errors.Add("最小夹角须在 0..90° 之间。");
        return errors;
    }
}

/// <summary>两线夹角。</summary>
[WorkflowNode("Vision.MeasureLineAngle", DisplayName = "两线夹角", Category = WorkflowVisionCategories.Measurement)]
public sealed class MeasureVisionLineAngleNodeModel : WorkflowVisionGeometryNodeModel
{
    /// <inheritdoc/>
    public override string NodeType => "Vision.MeasureLineAngle";
    /// <summary>第一条直线。</summary>
    [WorkflowProperty("直线1", "找线、拟合直线或两点生成直线的输出。", Category = "输入")]
    public WorkflowInput<VisionLine> LineA { get; set; } = WorkflowInput<VisionLine>.FromLiteral(null);
    /// <summary>第二条直线。</summary>
    [WorkflowProperty("直线2", "与直线1同帧、同坐标来源。", Category = "输入")]
    public WorkflowInput<VisionLine> LineB { get; set; } = WorkflowInput<VisionLine>.FromLiteral(null);
    /// <summary>角度取值方式。</summary>
    [WorkflowProperty("角度方式", "锐角不看方向；方向夹角和有向角按各直线 A→B 方向（找线为扫描排布方向）。三种角度都会输出，此项决定“角度”的取值。", Category = "测量")]
    public EVisionLineAngleMode Mode { get; set; }
    /// <inheritdoc/>
    public override IReadOnlyList<string> ValidateConfiguration()
    {
        var errors = base.ValidateConfiguration().ToList();
        if (!IsBound(LineA) || !IsBound(LineB)) errors.Add("两线夹角必须绑定两条直线。");
        if (!Enum.IsDefined(Mode)) errors.Add("角度方式无效。");
        return errors;
    }
}

/// <summary>线与圆交点。</summary>
[WorkflowNode("Vision.IntersectLineCircle", DisplayName = "线圆交点", Category = WorkflowVisionCategories.Geometry)]
public sealed class IntersectVisionLineCircleNodeModel : WorkflowVisionGeometryNodeModel
{
    /// <inheritdoc/>
    public override string NodeType => "Vision.IntersectLineCircle";
    /// <summary>直线。</summary>
    [WorkflowProperty("直线", "按无限直线求交。", Category = "输入")]
    public WorkflowInput<VisionLine> Line { get; set; } = WorkflowInput<VisionLine>.FromLiteral(null);
    /// <summary>圆。</summary>
    [WorkflowProperty("圆", "绑定找圆输出的“拟合结果”（圆心与半径，原图像素）。", Category = "输入")]
    public WorkflowInput<RobustCircleResult> Circle { get; set; } = WorkflowInput<RobustCircleResult>.FromLiteral(null);
    /// <inheritdoc/>
    public override IReadOnlyList<string> ValidateConfiguration()
    {
        var errors = base.ValidateConfiguration().ToList();
        if (!IsBound(Line) || !IsBound(Circle)) errors.Add("线圆交点必须绑定直线和圆。");
        return errors;
    }
}

/// <summary>圆与圆交点。</summary>
[WorkflowNode("Vision.IntersectCircles", DisplayName = "圆圆交点", Category = WorkflowVisionCategories.Geometry)]
public sealed class IntersectVisionCirclesNodeModel : WorkflowVisionGeometryNodeModel
{
    /// <inheritdoc/>
    public override string NodeType => "Vision.IntersectCircles";
    /// <summary>第一个圆。</summary>
    [WorkflowProperty("圆1", "绑定找圆输出的“拟合结果”（圆心与半径，原图像素）。", Category = "输入")]
    public WorkflowInput<RobustCircleResult> CircleA { get; set; } = WorkflowInput<RobustCircleResult>.FromLiteral(null);
    /// <summary>第二个圆。</summary>
    [WorkflowProperty("圆2", "与圆1同帧、同坐标来源。", Category = "输入")]
    public WorkflowInput<RobustCircleResult> CircleB { get; set; } = WorkflowInput<RobustCircleResult>.FromLiteral(null);
    /// <inheritdoc/>
    public override IReadOnlyList<string> ValidateConfiguration()
    {
        var errors = base.ValidateConfiguration().ToList();
        if (!IsBound(CircleA) || !IsBound(CircleB)) errors.Add("圆圆交点必须绑定两个圆。");
        return errors;
    }
}

/// <summary>点到线的垂足（投影点）。</summary>
[WorkflowNode("Vision.ProjectPointToLine", DisplayName = "点到线垂足", Category = WorkflowVisionCategories.Geometry)]
public sealed class ProjectVisionPointToLineNodeModel : WorkflowVisionGeometryNodeModel
{
    /// <inheritdoc/>
    public override string NodeType => "Vision.ProjectPointToLine";
    /// <summary>视觉点。</summary>
    [WorkflowProperty("点", "带来源的视觉点。", Category = "输入")]
    public WorkflowInput<VisionPoint> Point { get; set; } = WorkflowInput<VisionPoint>.FromLiteral(null);
    /// <summary>直线。</summary>
    [WorkflowProperty("直线", "与点同帧、同坐标来源。", Category = "输入")]
    public WorkflowInput<VisionLine> Line { get; set; } = WorkflowInput<VisionLine>.FromLiteral(null);
    /// <summary>垂足是否限制在线段内。</summary>
    [WorkflowProperty("投影模式", "无限直线：垂足可落在线段延长线上；有限线段：超出端点时取最近端点。", Category = "测量")]
    public EVisionLineDistanceMode Mode { get; set; }
    /// <inheritdoc/>
    public override IReadOnlyList<string> ValidateConfiguration()
    {
        var errors = base.ValidateConfiguration().ToList();
        if (!IsBound(Point) || !IsBound(Line)) errors.Add("点到线垂足必须绑定点和直线。");
        if (!Enum.IsDefined(Mode)) errors.Add("投影模式无效。");
        return errors;
    }
}

/// <summary>线线交点结果。</summary>
public sealed class VisionLineIntersectionResult : IVisionGeometryFact
{
    internal VisionLineIntersectionResult(VisionPoint point, VisionLine a, VisionLine b, double angle)
    { Point = point; LineA = a; LineB = b; AngleDegrees = angle; }
    /// <inheritdoc/>
    [DisplayName("图像标识")] public string FrameId => Point.FrameId;
    /// <summary>交点，带来源，可直接连接其它几何节点。</summary>
    [DisplayName("交点")] public VisionPoint Point { get; }
    /// <summary>交点原图X。</summary>
    [DisplayName("交点X")] public double X => Point.ImagePosition.X;
    /// <summary>交点原图Y。</summary>
    [DisplayName("交点Y")] public double Y => Point.ImagePosition.Y;
    /// <summary>两线锐角夹角（度）。</summary>
    [DisplayName("夹角(°)")] public double AngleDegrees { get; }
    /// <summary>第一条直线。</summary>
    [DisplayName("直线1")] public VisionLine LineA { get; }
    /// <summary>第二条直线。</summary>
    [DisplayName("直线2")] public VisionLine LineB { get; }
    /// <summary>共同坐标来源。</summary>
    [DisplayName("坐标系")] public VisionCoordinateSystem? CoordinateSystem => Point.CoordinateSystem;
    /// <inheritdoc/>
    [DisplayName("显示图形")]
    public IReadOnlyList<Geometry> DisplayGeometry => VisionConstruction.LinesTo(Point.ImagePosition, LineA, LineB)
        .Append(VisionConstruction.Marker(Point.ImagePosition)).ToArray();
    /// <inheritdoc/>
    [DisplayName("摘要")]
    public string Summary => FormattableString.Invariant($"线线交点 原图({X:F3}, {Y:F3})；夹角 {AngleDegrees:F3}°");
}

/// <summary>两线夹角结果。</summary>
public sealed class VisionLineAngleResult : IVisionGeometryFact
{
    internal VisionLineAngleResult(VisionLine a, VisionLine b, EVisionLineAngleMode mode, double signed, VisionPoint? intersection)
    {
        LineA = a; LineB = b; Mode = mode; SignedDegrees = signed; Intersection = intersection;
        DirectionDegrees = Math.Abs(signed);
        AcuteDegrees = DirectionDegrees > 90 ? 180 - DirectionDegrees : DirectionDegrees;
    }
    /// <inheritdoc/>
    [DisplayName("图像标识")] public string FrameId => LineA.FrameId;
    /// <summary>按“角度方式”取值的角度（度）。</summary>
    [DisplayName("角度(°)")]
    public double AngleDegrees => Mode switch
    {
        EVisionLineAngleMode.Direction => DirectionDegrees,
        EVisionLineAngleMode.Signed => SignedDegrees,
        _ => AcuteDegrees
    };
    /// <summary>角度方式。</summary>
    [DisplayName("角度方式")] public EVisionLineAngleMode Mode { get; }
    /// <summary>锐角 0..90°。</summary>
    [DisplayName("锐角(°)")] public double AcuteDegrees { get; }
    /// <summary>方向夹角 0..180°。</summary>
    [DisplayName("方向夹角(°)")] public double DirectionDegrees { get; }
    /// <summary>有向角 (-180, 180]，从直线1到直线2顺时针为正。</summary>
    [DisplayName("有向角(°)")] public double SignedDegrees { get; }
    /// <summary>两线交点；平行时为空。</summary>
    [DisplayName("交点")] public VisionPoint? Intersection { get; }
    /// <summary>第一条直线。</summary>
    [DisplayName("直线1")] public VisionLine LineA { get; }
    /// <summary>第二条直线。</summary>
    [DisplayName("直线2")] public VisionLine LineB { get; }
    /// <inheritdoc/>
    [DisplayName("显示图形")]
    public IReadOnlyList<Geometry> DisplayGeometry => (Intersection is { } point
            ? VisionConstruction.LinesTo(point.ImagePosition, LineA, LineB).Append(VisionConstruction.Marker(point.ImagePosition))
            : new Geometry[] { VisionConstruction.Segment(LineA), VisionConstruction.Segment(LineB) })
        .ToArray();
    /// <inheritdoc/>
    [DisplayName("摘要")]
    public string Summary => FormattableString.Invariant(
        $"两线夹角 {AngleDegrees:F3}°（锐角 {AcuteDegrees:F3}°，方向夹角 {DirectionDegrees:F3}°，有向角 {SignedDegrees:F3}°）{(Intersection is null ? "；两线平行" : "")}");
}

/// <summary>线圆或圆圆交点结果：0（不相交时节点故障）、1（相切）或 2 个交点。</summary>
public sealed class VisionCurveIntersectionResult : IVisionGeometryFact
{
    internal VisionCurveIntersectionResult(string kind, IReadOnlyList<VisionPoint> points, IReadOnlyList<Geometry> inputs)
    { _kind = kind; Points = points; _inputs = inputs; }
    private readonly string _kind;
    private readonly IReadOnlyList<Geometry> _inputs;
    /// <inheritdoc/>
    [DisplayName("图像标识")] public string FrameId => Points[0].FrameId;
    /// <summary>全部交点（1 或 2 个），顺序见 <see cref="First"/>。</summary>
    [DisplayName("交点列表")] public IReadOnlyList<VisionPoint> Points { get; }
    /// <summary>交点数：1 为相切，2 为相交。</summary>
    [DisplayName("交点数")] public int Count => Points.Count;
    /// <summary>
    /// 第一个交点：线圆交点沿直线 A→B 方向在前的一个；圆圆交点为从圆1圆心看向圆2圆心时右侧（图像中顺时针侧）的一个。
    /// </summary>
    [DisplayName("交点1")] public VisionPoint First => Points[0];
    /// <summary>第二个交点；相切时与第一个相同。</summary>
    [DisplayName("交点2")] public VisionPoint Second => Points[^1];
    /// <summary>是否相切（只有一个交点）。</summary>
    [DisplayName("是否相切")] public bool Tangent => Points.Count == 1;
    /// <summary>共同坐标来源。</summary>
    [DisplayName("坐标系")] public VisionCoordinateSystem? CoordinateSystem => Points[0].CoordinateSystem;
    /// <inheritdoc/>
    [DisplayName("显示图形")]
    public IReadOnlyList<Geometry> DisplayGeometry => _inputs.Concat(Points.Select(p => VisionConstruction.Marker(p.ImagePosition))).ToArray();
    /// <inheritdoc/>
    [DisplayName("摘要")]
    public string Summary => _kind + (Tangent ? "：相切，1 个交点 " : "：2 个交点 ")
        + string.Join("、", Points.Select(p => FormattableString.Invariant($"({p.ImagePosition.X:F3}, {p.ImagePosition.Y:F3})")));
}

/// <summary>点到线垂足结果。</summary>
public sealed class VisionPerpendicularFootResult : IVisionGeometryFact
{
    internal VisionPerpendicularFootResult(VisionPoint point, VisionPoint foot, VisionLine line, EVisionLineDistanceMode mode)
    {
        Point = point; Foot = foot; Line = line; Mode = mode;
        Distance = Math.Sqrt(Math.Pow(point.ImagePosition.X - foot.ImagePosition.X, 2) + Math.Pow(point.ImagePosition.Y - foot.ImagePosition.Y, 2));
    }
    /// <inheritdoc/>
    [DisplayName("图像标识")] public string FrameId => Point.FrameId;
    /// <summary>垂足，带来源，可直接连接其它几何节点。</summary>
    [DisplayName("垂足")] public VisionPoint Foot { get; }
    /// <summary>垂足原图X。</summary>
    [DisplayName("垂足X")] public double X => Foot.ImagePosition.X;
    /// <summary>垂足原图Y。</summary>
    [DisplayName("垂足Y")] public double Y => Foot.ImagePosition.Y;
    /// <summary>输入点到垂足的距离，原图像素。</summary>
    [DisplayName("距离")] public double Distance { get; }
    /// <summary>输入点。</summary>
    [DisplayName("输入点")] public VisionPoint Point { get; }
    /// <summary>直线。</summary>
    [DisplayName("直线")] public VisionLine Line { get; }
    /// <summary>投影模式。</summary>
    [DisplayName("投影模式")] public EVisionLineDistanceMode Mode { get; }
    /// <summary>共同坐标来源。</summary>
    [DisplayName("坐标系")] public VisionCoordinateSystem? CoordinateSystem => Foot.CoordinateSystem;
    /// <inheritdoc/>
    [DisplayName("显示图形")]
    public IReadOnlyList<Geometry> DisplayGeometry => VisionConstruction.LinesTo(Foot.ImagePosition, Line)
        .Append(new ContourGeometry(new[] { Point.ImagePosition, Foot.ImagePosition }))
        .Append(VisionConstruction.Marker(Point.ImagePosition)).Append(VisionConstruction.Marker(Foot.ImagePosition)).ToArray();
    /// <inheritdoc/>
    [DisplayName("摘要")]
    public string Summary => FormattableString.Invariant(
        $"垂足 原图({X:F3}, {Y:F3})；点到{(Mode == EVisionLineDistanceMode.Segments ? "线段" : "直线")}距离 {Distance:F3} image-px");
}

/// <summary>交点/夹角/垂足的纯托管计算；全部在原图像素中进行，结果点保留输入的共同坐标来源。</summary>
public static class VisionConstruction
{
    /// <summary>两条无限直线的交点及锐角夹角。</summary>
    /// <param name="a">直线1。</param><param name="b">直线2。</param><param name="minimumAngleDegrees">小于该夹角视为平行。</param>
    /// <returns>交点结果。</returns>
    public static VisionLineIntersectionResult IntersectLines(VisionLine a, VisionLine b, double minimumAngleDegrees = .1)
    {
        var coordinates = Common(a.CoordinateSystem, b.CoordinateSystem, a.FrameId, b.FrameId);
        var signed = SignedAngle(a, b);
        double acute = Math.Abs(signed) > 90 ? 180 - Math.Abs(signed) : Math.Abs(signed);
        if (acute < Math.Max(minimumAngleDegrees, 1e-9))
            throw new InvalidOperationException(FormattableString.Invariant($"两条直线近乎平行（夹角 {acute:F4}°，小于 {minimumAngleDegrees:G}°），交点不稳定。"));
        var point = LineIntersection(a, b) ?? throw new InvalidOperationException(
            FormattableString.Invariant($"两条直线近乎平行（夹角 {acute:F4}°），交点超出 ±{CoordinateLimit:G} 像素的坐标范围。"));
        return new VisionLineIntersectionResult(new VisionPoint(a.FrameId, point, coordinates), a, b, acute);
    }

    /// <summary>两线夹角；平行时交点为空。</summary>
    /// <param name="a">直线1。</param><param name="b">直线2。</param><param name="mode">“角度”取值方式。</param>
    /// <returns>夹角结果。</returns>
    public static VisionLineAngleResult MeasureAngle(VisionLine a, VisionLine b, EVisionLineAngleMode mode)
    {
        var coordinates = Common(a.CoordinateSystem, b.CoordinateSystem, a.FrameId, b.FrameId);
        var signed = SignedAngle(a, b);
        // 平行或近乎平行时交点在坐标范围外，夹角仍然有效，交点为空。
        var point = LineIntersection(a, b);
        return new VisionLineAngleResult(a, b, mode, signed, point is { } p ? new VisionPoint(a.FrameId, p, coordinates) : null);
    }

    /// <summary>无限直线与圆的交点；不相交时抛出。</summary>
    /// <param name="line">直线。</param><param name="circle">圆（原图像素）。</param>
    /// <returns>1 个（相切）或 2 个交点，沿直线 A→B 方向排序。</returns>
    public static VisionCurveIntersectionResult IntersectLineCircle(VisionLine line, RobustCircleResult circle)
    {
        var coordinates = Common(line.CoordinateSystem, circle.CoordinateSystem, line.FrameId, circle.FrameId);
        PointD p = line.A.ImagePosition, q = line.B.ImagePosition, c = circle.Center;
        double dx = q.X - p.X, dy = q.Y - p.Y, length = Math.Sqrt(dx * dx + dy * dy), ux = dx / length, uy = dy / length;
        // 圆心在直线上的投影参数 t0 及圆心到直线的距离 d。
        double t0 = (c.X - p.X) * ux + (c.Y - p.Y) * uy;
        double fx = p.X + ux * t0, fy = p.Y + uy * t0, d = Math.Sqrt((c.X - fx) * (c.X - fx) + (c.Y - fy) * (c.Y - fy)), r = circle.Radius;
        double tolerance = 1e-9 * Math.Max(1, r);
        if (d > r + tolerance)
            throw new InvalidOperationException(FormattableString.Invariant($"直线与圆不相交：圆心到直线 {d:F3}px，大于半径 {r:F3}px。"));
        double half = d >= r - tolerance ? 0 : Math.Sqrt(r * r - d * d);
        var points = (half == 0 ? new[] { t0 } : new[] { t0 - half, t0 + half })
            .Select(t => new VisionPoint(line.FrameId, new PointD(p.X + ux * t, p.Y + uy * t), coordinates)).ToArray();
        return new VisionCurveIntersectionResult("线圆交点", points,
            new Geometry[] { Segment(line), new EllipseGeometry(c, r, r) }.Concat(LinesTo(points[0].ImagePosition, line)).ToArray());
    }

    /// <summary>两圆交点；分离、内含或同心时抛出。</summary>
    /// <param name="a">圆1。</param><param name="b">圆2。</param>
    /// <returns>1 个（相切）或 2 个交点，第一个在从圆1圆心看向圆2圆心的右侧（图像中顺时针侧）。</returns>
    public static VisionCurveIntersectionResult IntersectCircles(RobustCircleResult a, RobustCircleResult b)
    {
        var coordinates = Common(a.CoordinateSystem, b.CoordinateSystem, a.FrameId, b.FrameId);
        PointD c1 = a.Center, c2 = b.Center;
        double r1 = a.Radius, r2 = b.Radius, dx = c2.X - c1.X, dy = c2.Y - c1.Y, d = Math.Sqrt(dx * dx + dy * dy);
        double tolerance = 1e-9 * Math.Max(1, Math.Max(r1, r2));
        if (d <= tolerance) throw new InvalidOperationException("两圆同心，没有确定的交点。");
        if (d > r1 + r2 + tolerance)
            throw new InvalidOperationException(FormattableString.Invariant($"两圆分离不相交：圆心距 {d:F3}px，大于半径和 {r1 + r2:F3}px。"));
        if (d < Math.Abs(r1 - r2) - tolerance)
            throw new InvalidOperationException(FormattableString.Invariant($"一个圆在另一个圆内，不相交：圆心距 {d:F3}px，小于半径差 {Math.Abs(r1 - r2):F3}px。"));
        // 交点弦中点到圆心1的距离 along，弦半长 half；在图像坐标（Y向下）中 (-uy, ux) 指向从圆1看向圆2的右侧。
        double along = (d * d + r1 * r1 - r2 * r2) / (2 * d), ux = dx / d, uy = dy / d;
        double h2 = r1 * r1 - along * along, half = h2 <= tolerance * Math.Max(1, r1) ? 0 : Math.Sqrt(h2);
        double mx = c1.X + ux * along, my = c1.Y + uy * along;
        var positions = half == 0
            ? new[] { new PointD(mx, my) }
            : new[] { new PointD(mx - uy * half, my + ux * half), new PointD(mx + uy * half, my - ux * half) };
        var points = positions.Select(p => new VisionPoint(a.FrameId, p, coordinates)).ToArray();
        return new VisionCurveIntersectionResult("圆圆交点", points, new Geometry[] { new EllipseGeometry(c1, r1, r1), new EllipseGeometry(c2, r2, r2) });
    }

    /// <summary>点到无限直线或有限线段的垂足。</summary>
    /// <param name="point">点。</param><param name="line">直线。</param><param name="mode">无限直线或有限线段。</param>
    /// <returns>垂足结果。</returns>
    public static VisionPerpendicularFootResult Project(VisionPoint point, VisionLine line, EVisionLineDistanceMode mode)
    {
        var coordinates = Common(point.CoordinateSystem, line.CoordinateSystem, point.FrameId, line.FrameId);
        PointD p = point.ImagePosition, a = line.A.ImagePosition, b = line.B.ImagePosition;
        double dx = b.X - a.X, dy = b.Y - a.Y, t = ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / (dx * dx + dy * dy);
        if (mode == EVisionLineDistanceMode.Segments) t = Math.Max(0, Math.Min(1, t));
        var foot = new VisionPoint(point.FrameId, new PointD(a.X + t * dx, a.Y + t * dy), coordinates);
        return new VisionPerpendicularFootResult(point, foot, line, mode);
    }

    /// <summary>从直线1方向转到直线2方向的有向角（度，(-180, 180]，图像中顺时针为正）。</summary>
    private static double SignedAngle(VisionLine a, VisionLine b)
    {
        double ax = a.B.ImagePosition.X - a.A.ImagePosition.X, ay = a.B.ImagePosition.Y - a.A.ImagePosition.Y;
        double bx = b.B.ImagePosition.X - b.A.ImagePosition.X, by = b.B.ImagePosition.Y - b.A.ImagePosition.Y;
        var angle = Math.Atan2(ax * by - ay * bx, ax * bx + ay * by) * 180 / Math.PI;
        return angle <= -180 ? angle + 360 : angle;
    }

    /// <summary>视觉点坐标的上限（原图像素）；与 <see cref="PointD"/> 的取值范围一致。</summary>
    private const double CoordinateLimit = 1e7;

    // 平行，或交点超出视觉点坐标范围（近乎平行时交点在极远处）时返回 null；先判断再构造 PointD，避免其参数越界异常。
    private static PointD? LineIntersection(VisionLine a, VisionLine b)
    {
        PointD p = a.A.ImagePosition, q = b.A.ImagePosition;
        double rx = a.B.ImagePosition.X - p.X, ry = a.B.ImagePosition.Y - p.Y, sx = b.B.ImagePosition.X - q.X, sy = b.B.ImagePosition.Y - q.Y;
        double cross = rx * sy - ry * sx;
        if (Math.Abs(cross) <= 1e-12 * Math.Sqrt((rx * rx + ry * ry) * (sx * sx + sy * sy))) return null;
        double t = ((q.X - p.X) * sy - (q.Y - p.Y) * sx) / cross, x = p.X + t * rx, y = p.Y + t * ry;
        return double.IsFinite(x) && double.IsFinite(y) && Math.Abs(x) <= CoordinateLimit && Math.Abs(y) <= CoordinateLimit ? new PointD(x, y) : null;
    }

    private static VisionCoordinateSystem? Common(VisionCoordinateSystem? a, VisionCoordinateSystem? b, string frameA, string frameB)
    {
        if (frameA != frameB) throw new InvalidOperationException("禁止混合不同帧的几何事实。");
        if (!Same(a, b)) throw new InvalidOperationException("两个输入的坐标来源不同，请绑定共同的“坐标系”或先显式转换。");
        return a;
    }

    private static bool Same(VisionCoordinateSystem? a, VisionCoordinateSystem? b) =>
        ReferenceEquals(a, b) || a != null && b != null && a.FrameId == b.FrameId
        && a.CoordinateSystemId == b.CoordinateSystemId && a.Definition.Signature == b.Definition.Signature
        && a.ImageWidth == b.ImageWidth && a.ImageHeight == b.ImageHeight
        && a.LocalToImage.M11 == b.LocalToImage.M11 && a.LocalToImage.M12 == b.LocalToImage.M12
        && a.LocalToImage.M21 == b.LocalToImage.M21 && a.LocalToImage.M22 == b.LocalToImage.M22
        && a.LocalToImage.Tx == b.LocalToImage.Tx && a.LocalToImage.Ty == b.LocalToImage.Ty;

    internal static Geometry Segment(VisionLine line) => new ContourGeometry(new[] { line.A.ImagePosition, line.B.ImagePosition });

    /// <summary>画出直线本身，并在点落在线段外时补一段延长线到该点。</summary>
    internal static IEnumerable<Geometry> LinesTo(PointD point, params VisionLine[] lines)
    {
        foreach (var line in lines)
        {
            yield return Segment(line);
            PointD a = line.A.ImagePosition, b = line.B.ImagePosition;
            double dx = b.X - a.X, dy = b.Y - a.Y, t = ((point.X - a.X) * dx + (point.Y - a.Y) * dy) / (dx * dx + dy * dy);
            if (t < 0) yield return new ContourGeometry(new[] { a, point });
            else if (t > 1) yield return new ContourGeometry(new[] { b, point });
        }
    }

    internal static Geometry Marker(PointD point) => new EllipseGeometry(point, 3, 3);
}

/// <summary>线线交点。</summary>
public sealed class IntersectVisionLinesNodeHandler : WorkflowNodeHandler<IntersectVisionLinesNodeModel>
{
    /// <inheritdoc/>
    protected override ValueTask<NodeExecutionResult> ExecuteAsync(IntersectVisionLinesNodeModel node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (frame, coordinates) = WorkflowVisionGeometryExecution.Resolve(node, context);
        var a = WorkflowVisionGeometryExecution.Align(context.ResolveInput(node.LineA), frame, coordinates);
        var b = WorkflowVisionGeometryExecution.Align(context.ResolveInput(node.LineB), frame, coordinates);
        return WorkflowVisionGeometryExecution.Output(context, frame, VisionConstruction.IntersectLines(a, b, node.MinimumAngleDegrees));
    }
}

/// <summary>两线夹角。</summary>
public sealed class MeasureVisionLineAngleNodeHandler : WorkflowNodeHandler<MeasureVisionLineAngleNodeModel>
{
    /// <inheritdoc/>
    protected override ValueTask<NodeExecutionResult> ExecuteAsync(MeasureVisionLineAngleNodeModel node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (frame, coordinates) = WorkflowVisionGeometryExecution.Resolve(node, context);
        var a = WorkflowVisionGeometryExecution.Align(context.ResolveInput(node.LineA), frame, coordinates);
        var b = WorkflowVisionGeometryExecution.Align(context.ResolveInput(node.LineB), frame, coordinates);
        return WorkflowVisionGeometryExecution.Output(context, frame, VisionConstruction.MeasureAngle(a, b, node.Mode));
    }
}

/// <summary>线圆交点。</summary>
public sealed class IntersectVisionLineCircleNodeHandler : WorkflowNodeHandler<IntersectVisionLineCircleNodeModel>
{
    /// <inheritdoc/>
    protected override ValueTask<NodeExecutionResult> ExecuteAsync(IntersectVisionLineCircleNodeModel node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (frame, coordinates) = WorkflowVisionGeometryExecution.Resolve(node, context);
        var line = WorkflowVisionGeometryExecution.Align(context.ResolveInput(node.Line), frame, coordinates);
        var circle = WorkflowVisionGeometryExecution.Align(context.ResolveInput(node.Circle), frame, coordinates);
        return WorkflowVisionGeometryExecution.Output(context, frame, VisionConstruction.IntersectLineCircle(line, circle));
    }
}

/// <summary>圆圆交点。</summary>
public sealed class IntersectVisionCirclesNodeHandler : WorkflowNodeHandler<IntersectVisionCirclesNodeModel>
{
    /// <inheritdoc/>
    protected override ValueTask<NodeExecutionResult> ExecuteAsync(IntersectVisionCirclesNodeModel node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (frame, coordinates) = WorkflowVisionGeometryExecution.Resolve(node, context);
        var a = WorkflowVisionGeometryExecution.Align(context.ResolveInput(node.CircleA), frame, coordinates);
        var b = WorkflowVisionGeometryExecution.Align(context.ResolveInput(node.CircleB), frame, coordinates);
        return WorkflowVisionGeometryExecution.Output(context, frame, VisionConstruction.IntersectCircles(a, b));
    }
}

/// <summary>点到线垂足。</summary>
public sealed class ProjectVisionPointToLineNodeHandler : WorkflowNodeHandler<ProjectVisionPointToLineNodeModel>
{
    /// <inheritdoc/>
    protected override ValueTask<NodeExecutionResult> ExecuteAsync(ProjectVisionPointToLineNodeModel node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (frame, coordinates) = WorkflowVisionGeometryExecution.Resolve(node, context);
        var point = WorkflowVisionGeometryExecution.Align(context.ResolveInput(node.Point), frame, coordinates);
        var line = WorkflowVisionGeometryExecution.Align(context.ResolveInput(node.Line), frame, coordinates);
        return WorkflowVisionGeometryExecution.Output(context, frame, VisionConstruction.Project(point, line, node.Mode));
    }
}
