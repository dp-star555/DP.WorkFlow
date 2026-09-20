namespace DP.WorkFlow.UI;

/// <summary>
/// 使用稀疏可见性网格和带转角惩罚的 A* 算法生成正交连线路径。
/// </summary>
public static class WorkflowOrthogonalRouter
{
    private const double Epsilon = 0.01;

    /// <summary>计算避开节点障碍物的正交路径。</summary>
    /// <param name="start">路径起点。</param>
    /// <param name="end">路径终点。</param>
    /// <param name="startSide">起点端口所在边。</param>
    /// <param name="endSide">终点端口所在边。</param>
    /// <param name="waypoints">新的连接拐点集合。</param>
    /// <param name="obstacles">需要避开的矩形障碍物。</param>
    /// <param name="leadDistance">连接端点离开节点边界的引线长度。</param>
    /// <returns>返回操作结果；具体含义参见方法说明。</returns>
    public static IReadOnlyList<WorkflowPoint> Route(
        WorkflowPoint start,
        WorkflowPoint end,
        WorkflowPortSide startSide,
        WorkflowPortSide endSide,
        IReadOnlyList<WorkflowPoint>? waypoints,
        IReadOnlyList<WorkflowDesignerRect>? obstacles,
        double leadDistance = 28)
    {
        var startLead = Offset(start, startSide, leadDistance);
        var endLead = Offset(end, endSide, leadDistance);
        var anchors = new List<WorkflowPoint> { startLead };
        if (waypoints is not null)
            anchors.AddRange(waypoints);
        anchors.Add(endLead);
        var result = new List<WorkflowPoint> { start };
        for (var index = 0; index < anchors.Count - 1; index++)
        {
            var leg = RouteLeg(
                anchors[index],
                anchors[index + 1],
                obstacles ?? Array.Empty<WorkflowDesignerRect>(),
                index == 0 ? DirectionOf(startSide) : null,
                index == anchors.Count - 2 ? DirectionOf(endSide) : null);
            foreach (var point in leg)
                AddDistinct(result, point);
        }
        AddDistinct(result, end);
        return Compact(result);
    }

    /// <summary>在两个锚点之间搜索转弯数优先、距离次优的正交路径。</summary>
    /// <param name="start">路径起点。</param>
    /// <param name="end">路径终点。</param>
    /// <param name="obstacles">需要避开的矩形障碍物。</param>
    /// <param name="initialDirection">离开起点时要求的行进方向。</param>
    /// <param name="terminalDirection">到达终点时要求的行进方向。</param>
    /// <returns>返回操作结果；具体含义参见方法说明。</returns>
    private static IReadOnlyList<WorkflowPoint> RouteLeg(
        WorkflowPoint start,
        WorkflowPoint end,
        IReadOnlyList<WorkflowDesignerRect> obstacles,
        TravelDirection? initialDirection,
        TravelDirection? terminalDirection)
    {
        var directDirection = NearlyEqual(start.X, end.X)
            ? TravelDirection.Vertical
            : NearlyEqual(start.Y, end.Y) ? TravelDirection.Horizontal : TravelDirection.None;
        if (directDirection is not TravelDirection.None
            && (!initialDirection.HasValue || initialDirection.Value == directDirection)
            && (!terminalDirection.HasValue || terminalDirection.Value == directDirection)
            && SegmentClear(start, end, obstacles))
        {
            return new[] { start, end };
        }

        var xs = new[] { start.X, end.X }
            .Concat(obstacles.SelectMany(rect => new[] { rect.X, rect.X + rect.Width }))
            .DistinctBy(RoundCoordinate)
            .OrderBy(value => value)
            .ToArray();
        var ys = new[] { start.Y, end.Y }
            .Concat(obstacles.SelectMany(rect => new[] { rect.Y, rect.Y + rect.Height }))
            .DistinctBy(RoundCoordinate)
            .OrderBy(value => value)
            .ToArray();
        var points = (from x in xs from y in ys select new WorkflowPoint(x, y))
            .Where(point => !obstacles.Any(rect => IsStrictlyInside(point, rect)))
            .ToList();
        AddUnique(points, start);
        AddUnique(points, end);
        var startIndex = FindPoint(points, start);
        var endIndex = FindPoint(points, end);
        var adjacency = BuildAdjacency(points, obstacles);
        var queue = new PriorityQueue<RouteState, (int Bends, double EstimatedDistance)>();
        var startState = new RouteState(startIndex, initialDirection ?? TravelDirection.None);
        var costs = new Dictionary<RouteState, RouteCost> { [startState] = new RouteCost(0, 0) };
        var previous = new Dictionary<RouteState, RouteState>();
        queue.Enqueue(startState, (0, Manhattan(start, end)));
        RouteState? completed = null;
        RouteCost? completedCost = null;
        while (queue.TryDequeue(out var state, out var priority))
        {
            if (completedCost.HasValue
                && (priority.Bends > completedCost.Value.Bends
                    || (priority.Bends == completedCost.Value.Bends
                        && priority.EstimatedDistance >= completedCost.Value.Distance)))
            {
                break;
            }
            if (state.PointIndex == endIndex)
            {
                var cost = costs[state];
                var terminalTurn = terminalDirection.HasValue
                    && state.Direction is not TravelDirection.None
                    && state.Direction != terminalDirection.Value ? 1 : 0;
                var finalCost = new RouteCost(cost.Bends + terminalTurn, cost.Distance);
                if (!completedCost.HasValue || Compare(finalCost, completedCost.Value) < 0)
                {
                    completed = state;
                    completedCost = finalCost;
                }
                continue;
            }
            var current = points[state.PointIndex];
            foreach (var neighborIndex in adjacency[state.PointIndex])
            {
                var neighbor = points[neighborIndex];
                var direction = NearlyEqual(current.X, neighbor.X) ? TravelDirection.Vertical : TravelDirection.Horizontal;
                var turn = state.Direction is TravelDirection.None || state.Direction == direction ? 0 : 1;
                var next = new RouteState(neighborIndex, direction);
                var currentCost = costs[state];
                var nextCost = new RouteCost(
                    currentCost.Bends + turn,
                    currentCost.Distance + Manhattan(current, neighbor));
                if (costs.TryGetValue(next, out var known) && Compare(known, nextCost) <= 0)
                    continue;
                costs[next] = nextCost;
                previous[next] = state;
                queue.Enqueue(next, (nextCost.Bends, nextCost.Distance + Manhattan(neighbor, end)));
            }
        }
        if (!completed.HasValue)
            return SimpleFallback(start, end);
        var path = new List<WorkflowPoint>();
        var cursor = completed.Value;
        path.Add(points[cursor.PointIndex]);
        while (previous.TryGetValue(cursor, out var parent))
        {
            cursor = parent;
            path.Add(points[cursor.PointIndex]);
        }
        path.Reverse();
        return Compact(path);
    }

    /// <summary>为候选路由点构建水平和垂直邻接表。</summary>
    /// <param name="points">路径点或候选点集合。</param>
    /// <param name="obstacles">需要避开的矩形障碍物。</param>
    /// <returns>返回操作结果；具体含义参见方法说明。</returns>
    private static List<int>[] BuildAdjacency(
        IReadOnlyList<WorkflowPoint> points,
        IReadOnlyList<WorkflowDesignerRect> obstacles)
    {
        var result = Enumerable.Range(0, points.Count).Select(_ => new List<int>()).ToArray();
        ConnectGroups(points, result, obstacles, points.Select((point, index) => (point, index)).GroupBy(item => RoundCoordinate(item.point.Y)), horizontal: true);
        ConnectGroups(points, result, obstacles, points.Select((point, index) => (point, index)).GroupBy(item => RoundCoordinate(item.point.X)), horizontal: false);
        return result;
    }

    /// <summary>连接同一水平线或垂直线上的相邻候选点。</summary>
    /// <param name="points">路径点或候选点集合。</param>
    /// <param name="adjacency">待构建的邻接表。</param>
    /// <param name="obstacles">需要避开的矩形障碍物。</param>
    /// <param name="groups">按横坐标或纵坐标分组的候选点。</param>
    /// <param name="horizontal">是否按水平方向分组。</param>
    private static void ConnectGroups(
        IReadOnlyList<WorkflowPoint> points,
        IReadOnlyList<List<int>> adjacency,
        IReadOnlyList<WorkflowDesignerRect> obstacles,
        IEnumerable<IGrouping<long, (WorkflowPoint point, int index)>> groups,
        bool horizontal)
    {
        foreach (var group in groups)
        {
            var ordered = horizontal
                ? group.OrderBy(item => item.point.X).ToArray()
                : group.OrderBy(item => item.point.Y).ToArray();
            for (var index = 0; index < ordered.Length - 1; index++)
            {
                var first = ordered[index];
                var second = ordered[index + 1];
                if (!SegmentClear(points[first.index], points[second.index], obstacles))
                    continue;
                adjacency[first.index].Add(second.index);
                adjacency[second.index].Add(first.index);
            }
        }
    }

    /// <summary>判断候选线段是否避开所有障碍物内部。</summary>
    /// <param name="first">第一个值或坐标。</param>
    /// <param name="second">第二个值或坐标。</param>
    /// <param name="obstacles">需要避开的矩形障碍物。</param>
    /// <returns>返回操作结果；具体含义参见方法说明。</returns>
    private static bool SegmentClear(
        WorkflowPoint first,
        WorkflowPoint second,
        IReadOnlyList<WorkflowDesignerRect> obstacles) =>
        obstacles.All(rect => !CrossesInterior(first, second, rect));

    /// <summary>判断水平或垂直线段是否穿过矩形内部。</summary>
    /// <param name="first">第一个值或坐标。</param>
    /// <param name="second">第二个值或坐标。</param>
    /// <param name="rect">目标矩形。</param>
    /// <returns>返回操作结果；具体含义参见方法说明。</returns>
    private static bool CrossesInterior(WorkflowPoint first, WorkflowPoint second, WorkflowDesignerRect rect)
    {
        if (NearlyEqual(first.Y, second.Y))
            return first.Y > rect.Y + Epsilon && first.Y < rect.Y + rect.Height - Epsilon
                && Math.Max(first.X, second.X) > rect.X + Epsilon
                && Math.Min(first.X, second.X) < rect.X + rect.Width - Epsilon;
        if (NearlyEqual(first.X, second.X))
            return first.X > rect.X + Epsilon && first.X < rect.X + rect.Width - Epsilon
                && Math.Max(first.Y, second.Y) > rect.Y + Epsilon
                && Math.Min(first.Y, second.Y) < rect.Y + rect.Height - Epsilon;
        return true;
    }

    /// <summary>在路由搜索失败时生成简单的直角折线路径。</summary>
    /// <param name="start">路径起点。</param>
    /// <param name="end">路径终点。</param>
    /// <returns>返回操作结果；具体含义参见方法说明。</returns>
    private static IReadOnlyList<WorkflowPoint> SimpleFallback(WorkflowPoint start, WorkflowPoint end)
    {
        var middleX = (start.X + end.X) / 2;
        return Compact(new[] { start, new WorkflowPoint(middleX, start.Y), new WorkflowPoint(middleX, end.Y), end });
    }

    /// <summary>将端口所在边转换为路径初始行进方向。</summary>
    /// <param name="side">端口所在边。</param>
    /// <returns>返回操作结果；具体含义参见方法说明。</returns>
    private static TravelDirection DirectionOf(WorkflowPortSide side) => side is WorkflowPortSide.Left or WorkflowPortSide.Right
        ? TravelDirection.Horizontal
        : TravelDirection.Vertical;

    /// <summary>沿指定端口边的外法线偏移坐标。</summary>
    /// <param name="point">目标坐标。</param>
    /// <param name="side">端口所在边。</param>
    /// <param name="distance">沿端口边外法线偏移的距离。</param>
    /// <returns>返回操作结果；具体含义参见方法说明。</returns>
    private static WorkflowPoint Offset(WorkflowPoint point, WorkflowPortSide side, double distance) => side switch
    {
        WorkflowPortSide.Left => new WorkflowPoint(point.X - distance, point.Y),
        WorkflowPortSide.Top => new WorkflowPoint(point.X, point.Y - distance),
        WorkflowPortSide.Right => new WorkflowPoint(point.X + distance, point.Y),
        WorkflowPortSide.Bottom => new WorkflowPoint(point.X, point.Y + distance),
        _ => point
    };

    /// <summary>删除路径中的重复点及不必要的共线点。</summary>
    /// <param name="source">源数据或路径点集合。</param>
    /// <returns>返回操作结果；具体含义参见方法说明。</returns>
    private static IReadOnlyList<WorkflowPoint> Compact(IEnumerable<WorkflowPoint> source)
    {
        var points = new List<WorkflowPoint>();
        foreach (var point in source)
        {
            AddDistinct(points, point);
            while (points.Count >= 3)
            {
                var first = points[^3];
                var middle = points[^2];
                var last = points[^1];
                if ((!NearlyEqual(first.X, middle.X) || !NearlyEqual(middle.X, last.X))
                    && (!NearlyEqual(first.Y, middle.Y) || !NearlyEqual(middle.Y, last.Y)))
                    break;
                points.RemoveAt(points.Count - 2);
            }
        }
        return points;
    }

    /// <summary>仅在坐标不同于最后一个点时追加路径点。</summary>
    /// <param name="points">路径点或候选点集合。</param>
    /// <param name="point">目标坐标。</param>
    private static void AddDistinct(ICollection<WorkflowPoint> points, WorkflowPoint point)
    {
        if (points.Count == 0 || points.Last() != point)
            points.Add(point);
    }

    /// <summary>仅在集合中不存在相同坐标时添加候选点。</summary>
    /// <param name="points">路径点或候选点集合。</param>
    /// <param name="point">目标坐标。</param>
    private static void AddUnique(ICollection<WorkflowPoint> points, WorkflowPoint point)
    {
        if (!points.Any(item => NearlyEqual(item.X, point.X) && NearlyEqual(item.Y, point.Y)))
            points.Add(point);
    }

    /// <summary>查找与给定坐标近似相等的候选点索引。</summary>
    /// <param name="points">路径点或候选点集合。</param>
    /// <param name="point">目标坐标。</param>
    /// <returns>返回操作结果；具体含义参见方法说明。</returns>
    private static int FindPoint(IReadOnlyList<WorkflowPoint> points, WorkflowPoint point)
    {
        for (var index = 0; index < points.Count; index++)
            if (NearlyEqual(points[index].X, point.X) && NearlyEqual(points[index].Y, point.Y))
                return index;
        throw new InvalidOperationException("路由端点不在可见性网格中。");
    }

    /// <summary>判断点是否严格位于矩形内部，不包含边界。</summary>
    /// <param name="point">目标坐标。</param>
    /// <param name="rect">目标矩形。</param>
    /// <returns>返回操作结果；具体含义参见方法说明。</returns>
    private static bool IsStrictlyInside(WorkflowPoint point, WorkflowDesignerRect rect) =>
        point.X > rect.X + Epsilon && point.X < rect.X + rect.Width - Epsilon
        && point.Y > rect.Y + Epsilon && point.Y < rect.Y + rect.Height - Epsilon;

    /// <summary>计算两点之间的曼哈顿距离。</summary>
    /// <param name="first">第一个值或坐标。</param>
    /// <param name="second">第二个值或坐标。</param>
    /// <returns>返回操作结果；具体含义参见方法说明。</returns>
    private static double Manhattan(WorkflowPoint first, WorkflowPoint second) =>
        Math.Abs(first.X - second.X) + Math.Abs(first.Y - second.Y);

    /// <summary>判断两个值或坐标是否在允许误差内相等。</summary>
    /// <param name="first">第一个值或坐标。</param>
    /// <param name="second">第二个值或坐标。</param>
    /// <returns>返回操作结果；具体含义参见方法说明。</returns>
    private static bool NearlyEqual(double first, double second) => Math.Abs(first - second) <= Epsilon;

    /// <summary>将浮点坐标量化为用于去重的整数。</summary>
    /// <param name="value">要校验、转换或写入的值。</param>
    /// <returns>返回操作结果；具体含义参见方法说明。</returns>
    private static long RoundCoordinate(double value) => (long)Math.Round(value * 100);

    /// <summary>按照转弯数优先、路径长度次优的规则比较路由代价。</summary>
    /// <param name="first">第一个值或坐标。</param>
    /// <param name="second">第二个值或坐标。</param>
    /// <returns>返回操作结果；具体含义参见方法说明。</returns>
    private static int Compare(RouteCost first, RouteCost second)
    {
        var bendComparison = first.Bends.CompareTo(second.Bends);
        return bendComparison != 0 ? bendComparison : first.Distance.CompareTo(second.Distance);
    }

    /// <summary>表示路由搜索中的候选点索引和到达方向。</summary>
    /// <param name="PointIndex">候选路由点索引。</param>
    /// <param name="Direction">到达候选点时的行进方向。</param>
    private readonly record struct RouteState(int PointIndex, TravelDirection Direction);

    /// <summary>表示路由搜索累计的转弯次数和距离。</summary>
    /// <param name="Bends">累计转弯次数。</param>
    /// <param name="Distance">累计路径长度。</param>
    private readonly record struct RouteCost(int Bends, double Distance);

    /// <summary>表示正交路径当前沿水平或垂直方向行进。</summary>
    private enum TravelDirection
    {
        /// <summary>尚未形成可判断的行进方向。</summary>
        None,
        /// <summary>沿水平方向行进。</summary>
        Horizontal,
        /// <summary>沿垂直方向行进。</summary>
        Vertical
    }
}
