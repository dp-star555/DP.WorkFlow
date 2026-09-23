namespace DP.WorkFlow.UI;

/// <summary>命中的节点端口及其实际所在边。</summary>
public readonly record struct WorkflowPortHit(
    WorkflowCanvasNode Node,
    WorkflowPortDescriptor Port,
    WorkflowPoint Point,
    WorkflowPortSide Side);

/// <summary>拖放节点可插入的连接线段，以及新节点输入/输出应使用的边。</summary>
public readonly record struct WorkflowConnectionInsertion(
    WorkflowConnectionModel Connection,
    WorkflowPortSide InputSide,
    WorkflowPortSide OutputSide);

/// <summary>
/// WinForms 与 WPF 画布共用的命中测试、连接路径和标签定位逻辑。
/// 所有坐标均为屏幕坐标；平台控件只负责输入事件、绘制和文本测量。
/// </summary>
public static class WorkflowDesignerInteraction
{
    private const double PortHitRadius = 10;
    private const double WaypointHitHalfSize = 9;
    private const double ObstacleMargin = 14;
    private const double ObstacleSearchMargin = 160;

    /// <summary>返回指定屏幕坐标命中的最上层节点。</summary>
    public static WorkflowCanvasNode? HitNode(WorkflowDesignerSession session, double x, double y) =>
        session.Canvas.Nodes
            .Reverse()
            .FirstOrDefault(node => WorkflowDesignerGeometry.GetNodeScreenRect(session, node).Contains(x, y));

    /// <summary>返回指定屏幕坐标命中的端口及其实际边。</summary>
    /// <param name="session">设计器会话。</param>
    /// <param name="x">屏幕横坐标。</param>
    /// <param name="y">屏幕纵坐标。</param>
    /// <param name="direction">端口方向。</param>
    /// <param name="connectingFrom">
    /// 正在从输出端口拖出连接时的源节点；此时其他节点的输入端口在四条边上都可作为落点。
    /// </param>
    public static WorkflowPortHit? HitPort(
        WorkflowDesignerSession session,
        double x,
        double y,
        WorkflowPortDirection direction,
        WorkflowCanvasNode? connectingFrom = null)
    {
        foreach (var node in session.Canvas.Nodes.Reverse())
        {
            var ports = session.GetPorts(node.Node.Id, direction);
            foreach (var port in ports)
            {
                var point = WorkflowDesignerGeometry.GetPortScreenPoint(session, node, port, ports);
                if (Distance(point, x, y) <= PortHitRadius)
                    return new WorkflowPortHit(node, port, point, node.GetPortSide(port));
                if (direction != WorkflowPortDirection.Input || connectingFrom is null || connectingFrom == node)
                    continue;
                foreach (var side in Enum.GetValues<WorkflowPortSide>())
                {
                    var candidate = WorkflowDesignerGeometry.GetPortScreenPoint(session, node, port, ports, side);
                    if (Distance(candidate, x, y) <= PortHitRadius)
                        return new WorkflowPortHit(node, port, candidate, side);
                }
            }
        }
        return null;
    }

    /// <summary>命中唯一选中且只有一个输出端口的节点四边快捷连接目标。</summary>
    public static WorkflowPortHit? HitSingleOutputSideTarget(WorkflowDesignerSession session, double x, double y)
    {
        if (session.SelectedNodeIds.Count != 1) return null;
        var node = session.Canvas.Nodes.FirstOrDefault(item => session.SelectedNodeIds.Contains(item.Node.Id));
        if (node is null) return null;
        var outputs = session.GetPorts(node.Node.Id, WorkflowPortDirection.Output);
        if (outputs.Count != 1) return null;
        var bounds = WorkflowDesignerGeometry.GetNodeScreenRect(session, node);
        foreach (var side in Enum.GetValues<WorkflowPortSide>())
        {
            var point = GetSideCenter(bounds, side);
            if (Math.Pow(point.X - x, 2) + Math.Pow(point.Y - y, 2) <= 36)
                return new WorkflowPortHit(node, outputs[0], point, side);
        }
        return null;
    }

    /// <summary>返回矩形指定边的中点。</summary>
    public static WorkflowPoint GetSideCenter(WorkflowDesignerRect bounds, WorkflowPortSide side) => side switch
    {
        WorkflowPortSide.Left => new WorkflowPoint(bounds.X, bounds.Y + bounds.Height / 2),
        WorkflowPortSide.Top => new WorkflowPoint(bounds.X + bounds.Width / 2, bounds.Y),
        WorkflowPortSide.Right => new WorkflowPoint(bounds.X + bounds.Width, bounds.Y + bounds.Height / 2),
        WorkflowPortSide.Bottom => new WorkflowPoint(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height),
        _ => default
    };

    /// <summary>返回指定屏幕坐标附近的最上层连接。</summary>
    public static WorkflowConnectionModel? HitConnection(
        WorkflowDesignerSession session,
        double x,
        double y,
        double tolerance = 7) =>
        session.Canvas.Connections.Reverse().FirstOrDefault(connection =>
        {
            var points = GetConnectionPath(session, connection);
            return points.Zip(points.Skip(1), (first, second) => DistanceToSegment(x, y, first, second))
                .Any(distance => distance <= tolerance);
        });

    /// <summary>查找拖放位置附近可插入节点的连接线段。</summary>
    public static WorkflowConnectionInsertion? HitConnectionInsertion(
        WorkflowDesignerSession session,
        double x,
        double y,
        double tolerance)
    {
        foreach (var connection in session.Canvas.Connections.Reverse())
        {
            var points = GetConnectionPath(session, connection);
            for (var index = 0; index < points.Count - 1; index++)
            {
                var first = points[index];
                var second = points[index + 1];
                if (DistanceToSegment(x, y, first, second) > tolerance) continue;
                if (Math.Abs(second.X - first.X) >= Math.Abs(second.Y - first.Y))
                    return second.X >= first.X
                        ? new WorkflowConnectionInsertion(connection, WorkflowPortSide.Left, WorkflowPortSide.Right)
                        : new WorkflowConnectionInsertion(connection, WorkflowPortSide.Right, WorkflowPortSide.Left);
                return second.Y >= first.Y
                    ? new WorkflowConnectionInsertion(connection, WorkflowPortSide.Top, WorkflowPortSide.Bottom)
                    : new WorkflowConnectionInsertion(connection, WorkflowPortSide.Bottom, WorkflowPortSide.Top);
            }
        }
        return null;
    }

    /// <summary>返回选中连接上被指定屏幕坐标命中的手工拐点。</summary>
    public static (WorkflowConnectionModel Connection, int Index)? HitWaypoint(
        WorkflowDesignerSession session,
        double x,
        double y)
    {
        if (session.SelectedConnection is not { } connection)
            return null;
        for (var index = 0; index < connection.Waypoints.Count; index++)
        {
            var point = WorkflowDesignerGeometry.CanvasToScreen(
                session, connection.Waypoints[index].X, connection.Waypoints[index].Y);
            if (Math.Abs(point.X - x) <= WaypointHitHalfSize && Math.Abs(point.Y - y) <= WaypointHitHalfSize)
                return (connection, index);
        }
        return null;
    }

    /// <summary>返回与屏幕框选矩形相交的节点标识。</summary>
    public static string[] GetNodeIdsInScreenRect(WorkflowDesignerSession session, WorkflowDesignerRect marquee) =>
        session.Canvas.Nodes
            .Where(node =>
            {
                var rect = WorkflowDesignerGeometry.GetNodeScreenRect(session, node);
                return rect.X < marquee.X + marquee.Width && rect.X + rect.Width > marquee.X
                    && rect.Y < marquee.Y + marquee.Height && rect.Y + rect.Height > marquee.Y;
            })
            .Select(node => node.Node.Id)
            .ToArray();

    /// <summary>查找连接端口当前使用的节点边；节点或端口不存在时使用方向默认边。</summary>
    public static WorkflowPortSide FindPortSide(
        WorkflowDesignerSession session,
        string nodeId,
        string portKey,
        WorkflowPortDirection direction)
    {
        var node = session.Canvas.Nodes.FirstOrDefault(item => item.Node.Id == nodeId);
        var port = node is null
            ? null
            : session.GetPorts(nodeId, direction).FirstOrDefault(item => item.Key == portKey);
        return port is not null
            ? node!.GetPortSide(port)
            : direction == WorkflowPortDirection.Input ? WorkflowPortSide.Left : WorkflowPortSide.Right;
    }

    /// <summary>计算指定节点端口的屏幕坐标；节点或端口不存在时返回 null。</summary>
    public static WorkflowPoint? FindPortPoint(
        WorkflowDesignerSession session,
        string nodeId,
        string portKey,
        WorkflowPortDirection direction,
        WorkflowPortSide? sideOverride = null)
    {
        var node = session.Canvas.Nodes.FirstOrDefault(item => item.Node.Id == nodeId);
        if (node is null)
            return null;
        var ports = session.GetPorts(nodeId, direction);
        var port = ports.FirstOrDefault(item => item.Key == portKey);
        return port is null ? null : WorkflowDesignerGeometry.GetPortScreenPoint(session, node, port, ports, sideOverride);
    }

    /// <summary>计算连接经过视口转换和障碍物路由后的屏幕路径；端点缺失时返回空路径。</summary>
    public static IReadOnlyList<WorkflowPoint> GetConnectionPath(
        WorkflowDesignerSession session,
        WorkflowConnectionModel connection)
    {
        var start = FindPortPoint(session, connection.FromNodeId, connection.FromPort, WorkflowPortDirection.Output, connection.FromSide);
        var end = FindPortPoint(session, connection.ToNodeId, connection.ToPort, WorkflowPortDirection.Input, connection.ToSide);
        return start.HasValue && end.HasValue
            ? GetConnectionPath(session, connection, start.Value, end.Value)
            : Array.Empty<WorkflowPoint>();
    }

    /// <summary>以已知屏幕端点计算连接经过障碍物路由后的屏幕路径。</summary>
    public static IReadOnlyList<WorkflowPoint> GetConnectionPath(
        WorkflowDesignerSession session,
        WorkflowConnectionModel connection,
        WorkflowPoint start,
        WorkflowPoint end)
    {
        var waypoints = connection.Waypoints
            .Select(point => WorkflowDesignerGeometry.CanvasToScreen(session, point.X, point.Y))
            .ToArray();
        var search = new WorkflowDesignerRect(
            Math.Min(start.X, end.X) - ObstacleSearchMargin,
            Math.Min(start.Y, end.Y) - ObstacleSearchMargin,
            Math.Abs(end.X - start.X) + ObstacleSearchMargin * 2,
            Math.Abs(end.Y - start.Y) + ObstacleSearchMargin * 2);
        var obstacles = session.Canvas.Nodes
            .Where(node => node.Node.Id != connection.FromNodeId && node.Node.Id != connection.ToNodeId)
            .Select(node =>
            {
                var rect = WorkflowDesignerGeometry.GetNodeScreenRect(session, node);
                return new WorkflowDesignerRect(
                    rect.X - ObstacleMargin,
                    rect.Y - ObstacleMargin,
                    rect.Width + ObstacleMargin * 2,
                    rect.Height + ObstacleMargin * 2);
            })
            .Where(rect => rect.X <= search.X + search.Width && rect.X + rect.Width >= search.X
                && rect.Y <= search.Y + search.Height && rect.Y + rect.Height >= search.Y)
            .ToArray();
        return WorkflowOrthogonalRouter.Route(
            start,
            end,
            connection.FromSide ?? FindPortSide(session, connection.FromNodeId, connection.FromPort, WorkflowPortDirection.Output),
            connection.ToSide ?? FindPortSide(session, connection.ToNodeId, connection.ToPort, WorkflowPortDirection.Input),
            waypoints,
            obstacles);
    }

    /// <summary>判断端口在指定边是否存在连接。</summary>
    public static bool IsPortConnectedAtSide(
        WorkflowDesignerSession session,
        WorkflowCanvasNode node,
        WorkflowPortDescriptor port,
        WorkflowPortSide side) =>
        session.Canvas.Connections.Any(connection => port.Direction == WorkflowPortDirection.Output
            ? connection.FromNodeId == node.Node.Id && connection.FromPort == port.Key
              && (connection.FromSide ?? node.GetPortSide(port)) == side
            : connection.ToNodeId == node.Node.Id && connection.ToPort == port.Key
              && (connection.ToSide ?? node.GetPortSide(port)) == side);

    /// <summary>判断节点指定边上是否已有任一方向的连接端点。</summary>
    public static bool HasConnectedEndpointAtSide(
        WorkflowDesignerSession session,
        WorkflowCanvasNode node,
        WorkflowPortSide side) =>
        session.GetPorts(node.Node.Id, WorkflowPortDirection.Input)
            .Concat(session.GetPorts(node.Node.Id, WorkflowPortDirection.Output))
            .Any(port => IsPortConnectedAtSide(session, node, port, side));

    /// <summary>返回连接单独覆盖到非默认边、需要额外绘制的端点。</summary>
    public static IEnumerable<(WorkflowPortDescriptor Port, WorkflowPortSide Side)> GetSideOverrideEndpoints(
        WorkflowDesignerSession session,
        WorkflowCanvasNode node,
        WorkflowPortDirection direction)
    {
        var ports = session.GetPorts(node.Node.Id, direction);
        var endpoints = session.Canvas.Connections
            .Select(connection => direction == WorkflowPortDirection.Input
                ? (NodeId: connection.ToNodeId, PortKey: connection.ToPort, Side: connection.ToSide)
                : (NodeId: connection.FromNodeId, PortKey: connection.FromPort, Side: connection.FromSide))
            .Where(item => item.NodeId == node.Node.Id && item.Side.HasValue)
            .Distinct()
            .ToArray();
        foreach (var endpoint in endpoints)
        {
            var port = ports.FirstOrDefault(item => item.Key == endpoint.PortKey);
            if (port is not null && node.GetPortSide(port) != endpoint.Side!.Value)
                yield return (port, endpoint.Side.Value);
        }
    }

    /// <summary>节点指定方向存在多个端口时才显示端口标签。</summary>
    public static bool ShouldDrawPortLabel(
        WorkflowDesignerSession session,
        WorkflowCanvasNode node,
        WorkflowPortDirection direction) =>
        session.GetPorts(node.Node.Id, direction).Count > 1;

    /// <summary>多输出或非 Success 输出的连接需要显示输出语义标签。</summary>
    public static bool ShouldDrawConnectionLabel(WorkflowDesignerSession session, WorkflowConnectionModel connection) =>
        session.GetPorts(connection.FromNodeId, WorkflowPortDirection.Output).Count > 1
        || connection.FromPort != WorkflowPorts.Success;

    /// <summary>生成节点执行序号和耗时显示文本；节点尚未执行时返回 null。</summary>
    public static string? GetRuntimeDisplayText(WorkflowDesignerSession session, WorkflowCanvasNode node)
    {
        var info = session.GetNodeRuntimeInfo(node.Node.Id);
        if (info is not { ExecutionCount: > 0 }) return null;
        var elapsed = info.State == E_NodeState.Running && info.StartedAt.HasValue
            ? DateTimeOffset.UtcNow - info.StartedAt.Value
            : info.Elapsed;
        return $"#{info.ExecutionSequence}  {FormatElapsed(elapsed)}";
    }

    /// <summary>将运行耗时格式化为合适精度的文本。</summary>
    public static string FormatElapsed(TimeSpan elapsed) => elapsed.TotalSeconds >= 1
        ? $"{elapsed.TotalSeconds:F2}s"
        : elapsed.TotalMilliseconds >= 10
            ? $"{elapsed.TotalMilliseconds:F0}ms"
            : elapsed.TotalMilliseconds >= 1
                ? $"{elapsed.TotalMilliseconds:F1}ms"
                : $"{elapsed.TotalMilliseconds:F3}ms";

    /// <summary>构建包含可选手工拐点的基础正交折线路径，用于连接拖动预览。</summary>
    public static IReadOnlyList<WorkflowPoint> BuildOrthogonalPath(
        WorkflowPoint start,
        WorkflowPoint end,
        IReadOnlyList<WorkflowPoint> waypoints,
        WorkflowPortSide startSide = WorkflowPortSide.Right,
        WorkflowPortSide endSide = WorkflowPortSide.Left)
    {
        if (waypoints.Count > 0)
        {
            var anchors = new[] { start }.Concat(waypoints).Append(end).ToArray();
            var manual = new List<WorkflowPoint> { start };
            foreach (var pair in anchors.Zip(anchors.Skip(1)))
            {
                AddDistinct(manual, new WorkflowPoint(pair.Second.X, pair.First.Y));
                AddDistinct(manual, pair.Second);
            }
            return manual;
        }

        const double clearance = 28;
        var startLead = Offset(start, startSide, clearance);
        var endLead = Offset(end, endSide, clearance);
        var result = new List<WorkflowPoint> { start, startLead };
        var startHorizontal = startSide is WorkflowPortSide.Left or WorkflowPortSide.Right;
        var endHorizontal = endSide is WorkflowPortSide.Left or WorkflowPortSide.Right;
        if (startHorizontal && endHorizontal)
        {
            var middleX = (startLead.X + endLead.X) / 2;
            AddDistinct(result, new WorkflowPoint(middleX, startLead.Y));
            AddDistinct(result, new WorkflowPoint(middleX, endLead.Y));
        }
        else if (!startHorizontal && !endHorizontal)
        {
            var middleY = (startLead.Y + endLead.Y) / 2;
            AddDistinct(result, new WorkflowPoint(startLead.X, middleY));
            AddDistinct(result, new WorkflowPoint(endLead.X, middleY));
        }
        else
        {
            AddDistinct(result, new WorkflowPoint(endLead.X, startLead.Y));
        }
        AddDistinct(result, endLead);
        AddDistinct(result, end);
        return result;
    }

    /// <summary>计算连接路径指定相对位置（0～1）处的坐标。</summary>
    public static WorkflowPoint PointAlongPath(IReadOnlyList<WorkflowPoint> points, double position)
    {
        if (points.Count == 0) return default;
        if (points.Count == 1) return points[0];
        var lengths = SegmentLengths(points);
        var remaining = lengths.Sum() * Math.Clamp(position, 0, 1);
        for (var index = 0; index < lengths.Length; index++)
        {
            if (remaining > lengths[index]) { remaining -= lengths[index]; continue; }
            var ratio = lengths[index] <= 0 ? 0 : remaining / lengths[index];
            return new WorkflowPoint(
                points[index].X + (points[index + 1].X - points[index].X) * ratio,
                points[index].Y + (points[index + 1].Y - points[index].Y) * ratio);
        }
        return points[^1];
    }

    /// <summary>将屏幕坐标投影为连接路径上的相对位置，结果限制在 0.05～0.95。</summary>
    public static double ProjectPathPosition(IReadOnlyList<WorkflowPoint> points, double x, double y)
    {
        if (points.Count < 2) return 0.5;
        var lengths = SegmentLengths(points);
        var total = lengths.Sum();
        var traversed = 0d;
        var bestDistance = double.MaxValue;
        var bestPosition = 0.5;
        for (var index = 0; index < lengths.Length; index++)
        {
            var first = points[index];
            var second = points[index + 1];
            var dx = second.X - first.X;
            var dy = second.Y - first.Y;
            var ratio = lengths[index] <= 0 ? 0 : Math.Clamp(((x - first.X) * dx + (y - first.Y) * dy) / (lengths[index] * lengths[index]), 0, 1);
            var nearestX = first.X + dx * ratio;
            var nearestY = first.Y + dy * ratio;
            var distance = Math.Pow(nearestX - x, 2) + Math.Pow(nearestY - y, 2);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestPosition = total <= 0 ? 0.5 : (traversed + lengths[index] * ratio) / total;
            }
            traversed += lengths[index];
        }
        return Math.Clamp(bestPosition, 0.05, 0.95);
    }

    /// <summary>计算点到有限线段的最短距离。</summary>
    public static double DistanceToSegment(double x, double y, WorkflowPoint start, WorkflowPoint end)
    {
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        if (dx == 0 && dy == 0)
            return Distance(start, x, y);
        var t = Math.Clamp(((x - start.X) * dx + (y - start.Y) * dy) / (dx * dx + dy * dy), 0, 1);
        return Distance(new WorkflowPoint(start.X + t * dx, start.Y + t * dy), x, y);
    }

    /// <summary>判断坐标是否位于矩形向外扩展 <paramref name="margin"/> 后的区域内。</summary>
    public static bool IsNearRect(WorkflowDesignerRect rect, double x, double y, double margin) =>
        x >= rect.X - margin && x <= rect.X + rect.Width + margin
        && y >= rect.Y - margin && y <= rect.Y + rect.Height + margin;

    /// <summary>计算指定坐标距离矩形最近的边。</summary>
    public static WorkflowPortSide NearestSide(WorkflowDesignerRect rect, double x, double y)
    {
        var distances = new[]
        {
            (Side: WorkflowPortSide.Left, Distance: Math.Abs(x - rect.X)),
            (Side: WorkflowPortSide.Top, Distance: Math.Abs(y - rect.Y)),
            (Side: WorkflowPortSide.Right, Distance: Math.Abs(x - (rect.X + rect.Width))),
            (Side: WorkflowPortSide.Bottom, Distance: Math.Abs(y - (rect.Y + rect.Height)))
        };
        return distances.MinBy(item => item.Distance).Side;
    }

    /// <summary>直接替换连接拐点，用于拖动期间的实时预览（不进入撤销栈）。</summary>
    public static void SetLiveWaypoints(WorkflowConnectionModel connection, IEnumerable<WorkflowPoint> points)
    {
        connection.Waypoints.Clear();
        foreach (var point in points)
            connection.Waypoints.Add(point);
    }

    private static double[] SegmentLengths(IReadOnlyList<WorkflowPoint> points) =>
        points.Zip(points.Skip(1), (first, second) => Distance(first, second.X, second.Y)).ToArray();

    private static double Distance(WorkflowPoint point, double x, double y) =>
        Math.Sqrt(Math.Pow(point.X - x, 2) + Math.Pow(point.Y - y, 2));

    private static WorkflowPoint Offset(WorkflowPoint point, WorkflowPortSide side, double distance) => side switch
    {
        WorkflowPortSide.Left => new WorkflowPoint(point.X - distance, point.Y),
        WorkflowPortSide.Top => new WorkflowPoint(point.X, point.Y - distance),
        WorkflowPortSide.Right => new WorkflowPoint(point.X + distance, point.Y),
        WorkflowPortSide.Bottom => new WorkflowPoint(point.X, point.Y + distance),
        _ => point
    };

    private static void AddDistinct(ICollection<WorkflowPoint> points, WorkflowPoint point)
    {
        if (points.LastOrDefault() != point)
            points.Add(point);
    }
}

/// <summary>
/// 一次多选节点拖动：拖动期间实时改写节点坐标与关联拐点，提交时先还原再以一个可撤销操作写入会话。
/// </summary>
public sealed class WorkflowNodeDragOperation
{
    private readonly WorkflowDesignerSession _session;
    private readonly WorkflowPoint _startCanvas;
    private readonly Dictionary<string, WorkflowPoint> _nodeOrigins = new(StringComparer.Ordinal);
    private readonly Dictionary<WorkflowConnectionModel, WorkflowPoint[]> _waypointOrigins = new();

    /// <summary>以当前选中节点和指针画布坐标开始拖动。</summary>
    public WorkflowNodeDragOperation(WorkflowDesignerSession session, WorkflowPoint startCanvas)
    {
        _session = session;
        _startCanvas = startCanvas;
        foreach (var selected in session.Canvas.Nodes.Where(item => session.SelectedNodeIds.Contains(item.Node.Id)))
            _nodeOrigins[selected.Node.Id] = new WorkflowPoint(selected.X, selected.Y);
        foreach (var connection in session.Canvas.Connections.Where(connection =>
                     (session.SelectedNodeIds.Contains(connection.FromNodeId)
                      || session.SelectedNodeIds.Contains(connection.ToNodeId))
                     && connection.Waypoints.Count > 0))
        {
            _waypointOrigins[connection] = connection.Waypoints.ToArray();
        }
    }

    /// <summary>按指针画布坐标实时移动节点；两端都被拖动的连接平移拐点，否则清空拐点重新路由。</summary>
    public void Update(WorkflowPoint currentCanvas)
    {
        var deltaX = currentCanvas.X - _startCanvas.X;
        var deltaY = currentCanvas.Y - _startCanvas.Y;
        foreach (var selected in _session.Canvas.Nodes.Where(item => _nodeOrigins.ContainsKey(item.Node.Id)))
        {
            var origin = _nodeOrigins[selected.Node.Id];
            var snapped = _session.SnapNodePosition(selected, new WorkflowPoint(origin.X + deltaX, origin.Y + deltaY));
            selected.X = snapped.X;
            selected.Y = snapped.Y;
        }
        foreach (var pair in _waypointOrigins)
        {
            var movesBothEnds = _nodeOrigins.ContainsKey(pair.Key.FromNodeId)
                && _nodeOrigins.ContainsKey(pair.Key.ToNodeId);
            WorkflowDesignerInteraction.SetLiveWaypoints(pair.Key, movesBothEnds
                ? pair.Value.Select(point => new WorkflowPoint(point.X + deltaX, point.Y + deltaY))
                : Array.Empty<WorkflowPoint>());
        }
    }

    /// <summary>还原实时修改后以一次 MoveNodes 提交最终位置。</summary>
    public void Commit()
    {
        var positions = _nodeOrigins.Keys.ToDictionary(
            id => id,
            id =>
            {
                var selected = _session.Canvas.Nodes.First(item => item.Node.Id == id);
                return new WorkflowPoint(selected.X, selected.Y);
            },
            StringComparer.Ordinal);
        foreach (var pair in _nodeOrigins)
        {
            var selected = _session.Canvas.Nodes.First(item => item.Node.Id == pair.Key);
            selected.X = pair.Value.X;
            selected.Y = pair.Value.Y;
        }
        RestoreWaypoints();
        _session.MoveNodes(positions);
    }

    /// <summary>放弃拖动并还原节点坐标与拐点。</summary>
    public void Cancel()
    {
        foreach (var pair in _nodeOrigins)
        {
            var node = _session.Canvas.Nodes.FirstOrDefault(item => item.Node.Id == pair.Key);
            if (node is not null) { node.X = pair.Value.X; node.Y = pair.Value.Y; }
        }
        RestoreWaypoints();
    }

    private void RestoreWaypoints()
    {
        foreach (var pair in _waypointOrigins)
            WorkflowDesignerInteraction.SetLiveWaypoints(pair.Key, pair.Value);
    }
}
