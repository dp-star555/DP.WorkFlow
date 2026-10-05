namespace DP.WorkFlow.UI;

/// <summary>UI 框架无关的矩形。</summary>
/// <param name="X">矩形左上角的横坐标。</param>
/// <param name="Y">矩形左上角的纵坐标。</param>
/// <param name="Width">矩形宽度。</param>
/// <param name="Height">矩形高度。</param>
public readonly record struct WorkflowDesignerRect(double X, double Y, double Width, double Height)
{
    /// <summary>判断指定坐标是否位于矩形边界内（包含边界）。</summary>
    /// <param name="x">横坐标。</param>
    /// <param name="y">纵坐标。</param>
    public bool Contains(double x, double y) =>
        x >= X && x <= X + Width && y >= Y && y <= Y + Height;
}

/// <summary>
/// 节点标题栏的屏幕布局：状态指示灯、标题与运行摘要同处一行且互不重叠，三者都在标题栏内垂直居中。
/// 字号以屏幕像素表示（随缩放变化，不随系统 DPI 额外放大），使文字与卡片尺寸始终成比例。
/// </summary>
public readonly record struct WorkflowNodeHeaderLayout(
    WorkflowDesignerRect IndicatorBounds,
    WorkflowDesignerRect TitleBounds,
    WorkflowDesignerRect RuntimeBounds,
    double TitleFontSize,
    double RuntimeFontSize);

/// <summary>提供画布与屏幕坐标换算。</summary>
public static class WorkflowDesignerGeometry
{
    /// <summary>节点标题区域的设计高度。</summary>
    public const double HeaderHeight = 32;
    /// <summary>节点允许的最小设计高度。</summary>
    public const double MinimumNodeHeight = 64;
    /// <summary>同一垂直边上相邻端口的设计间距。</summary>
    public const double VerticalPortSpacing = 22;
    /// <summary>节点正文区域的垂直内边距。</summary>
    public const double NodeBodyVerticalPadding = 8;
    /// <summary>端口命中与绘制使用的基础半径。</summary>
    public const double PortRadius = 5;

    /// <summary>标题文字的设计像素字号。</summary>
    public const double TitleFontPixels = 12;
    /// <summary>运行摘要、端口与连接标签的设计像素字号。</summary>
    public const double DetailFontPixels = 10.5;

    /// <summary>
    /// 在标题栏一行内依次放置状态指示灯、标题和右对齐的执行序号/耗时。运行摘要最多占可用宽度的一半，
    /// 长标题只在自己的区域内省略，不能覆盖右侧运行信息。
    /// </summary>
    /// <param name="nodeBounds">节点屏幕矩形。</param>
    /// <param name="zoom">当前缩放。</param>
    /// <param name="measuredRuntimeWidth">运行摘要测量宽度；为 0 表示没有运行摘要。</param>
    public static WorkflowNodeHeaderLayout CalculateNodeHeaderLayout(
        WorkflowDesignerRect nodeBounds,
        double zoom,
        double measuredRuntimeWidth)
    {
        zoom = Math.Max(0.05, zoom);
        var headerHeight = Math.Min(nodeBounds.Height, HeaderHeight * zoom);
        var indicator = 8 * zoom;
        var titleLeft = nodeBounds.X + 24 * zoom;
        var contentRight = nodeBounds.X + nodeBounds.Width - 10 * zoom;
        var availableWidth = Math.Max(0, contentRight - titleLeft);
        var gap = measuredRuntimeWidth > 0 ? 8 * zoom : 0;
        var runtimeWidth = measuredRuntimeWidth <= 0 ? 0 : Math.Min(measuredRuntimeWidth, availableWidth * 0.5);
        var titleWidth = Math.Max(0, availableWidth - runtimeWidth - gap);
        return new WorkflowNodeHeaderLayout(
            new WorkflowDesignerRect(nodeBounds.X + 10 * zoom, nodeBounds.Y + (headerHeight - indicator) / 2, indicator, indicator),
            new WorkflowDesignerRect(titleLeft, nodeBounds.Y, titleWidth, headerHeight),
            new WorkflowDesignerRect(contentRight - runtimeWidth, nodeBounds.Y, runtimeWidth, headerHeight),
            Math.Max(3, TitleFontPixels * zoom),
            Math.Max(3, DetailFontPixels * zoom));
    }

    /// <summary>将画布坐标按照当前缩放和平移参数转换为屏幕坐标。</summary>
    /// <param name="session">设计器会话。</param>
    /// <param name="x">横坐标。</param>
    /// <param name="y">纵坐标。</param>
    public static WorkflowPoint CanvasToScreen(WorkflowDesignerSession session, double x, double y) =>
        new(x * session.Zoom + session.PanX, y * session.Zoom + session.PanY);

    /// <summary>将屏幕坐标反向转换为画布坐标。</summary>
    /// <param name="session">设计器会话。</param>
    /// <param name="x">横坐标。</param>
    /// <param name="y">纵坐标。</param>
    public static WorkflowPoint ScreenToCanvas(WorkflowDesignerSession session, double x, double y) =>
        new((x - session.PanX) / session.Zoom, (y - session.PanY) / session.Zoom);

    /// <summary>计算节点经过当前视口变换后的屏幕矩形。</summary>
    /// <param name="session">设计器会话。</param>
    /// <param name="node">目标画布节点或节点模型。</param>
    public static WorkflowDesignerRect GetNodeScreenRect(
        WorkflowDesignerSession session,
        WorkflowCanvasNode node) =>
        new(
            node.X * session.Zoom + session.PanX,
            node.Y * session.Zoom + session.PanY,
            node.Width * session.Zoom,
            node.Height * session.Zoom);

    /// <summary>计算端口在节点指定边上的屏幕坐标。</summary>
    /// <param name="session">设计器会话。</param>
    /// <param name="node">目标画布节点或节点模型。</param>
    /// <param name="port">目标端口描述。</param>
    /// <param name="ports">参与同边排列的端口集合。</param>
    /// <param name="sideOverride">可选的连接端点所在边覆盖。</param>
    public static WorkflowPoint GetPortScreenPoint(
        WorkflowDesignerSession session,
        WorkflowCanvasNode node,
        WorkflowPortDescriptor port,
        IReadOnlyList<WorkflowPortDescriptor> ports,
        WorkflowPortSide? sideOverride = null)
    {
        ArgumentNullException.ThrowIfNull(port);
        ArgumentNullException.ThrowIfNull(ports);
        var rect = GetNodeScreenRect(session, node);
        var actualSide = sideOverride ?? node.GetPortSide(port);
        var sameSide = ports.Where(item => ReferenceEquals(item, port) || node.GetPortSide(item) == actualSide).ToArray();
        var index = Array.IndexOf(sameSide, port);
        if (index < 0)
            index = sameSide.ToList().FindIndex(item => item.Key == port.Key && item.Direction == port.Direction);
        var ratio = (Math.Max(0, index) + 1d) / (Math.Max(1, sameSide.Length) + 1d);
        var verticalTop = sameSide.Length > 1
            ? rect.Y + Math.Min(rect.Height * 0.45, HeaderHeight * session.Zoom)
            : rect.Y;
        var verticalHeight = sameSide.Length > 1
            ? Math.Max(1, rect.Y + rect.Height - verticalTop)
            : rect.Height;
        var raw = actualSide switch
        {
            WorkflowPortSide.Left => new WorkflowPoint(rect.X, verticalTop + verticalHeight * ratio),
            WorkflowPortSide.Top => new WorkflowPoint(rect.X + rect.Width * ratio, rect.Y),
            WorkflowPortSide.Right => new WorkflowPoint(rect.X + rect.Width, verticalTop + verticalHeight * ratio),
            WorkflowPortSide.Bottom => new WorkflowPoint(rect.X + rect.Width * ratio, rect.Y + rect.Height),
            _ => throw new ArgumentOutOfRangeException(nameof(port), actualSide, "未知端口边。")
        };
        // 单端口必须严格位于边中点；多端口才沿边分布并吸附栅格。
        if (!session.SnapToGrid || sameSide.Length == 1) return raw;
        var canvasPoint = ScreenToCanvas(session, raw.X, raw.Y);
        var snapped = session.SnapPoint(canvasPoint);
        var screenSnapped = CanvasToScreen(session, snapped.X, snapped.Y);
        return actualSide is WorkflowPortSide.Left or WorkflowPortSide.Right
            ? new WorkflowPoint(raw.X, screenSnapped.Y)
            : new WorkflowPoint(screenSnapped.X, raw.Y);
    }

    /// <summary>使用默认上输入、下输出的简化几何调用。</summary>
    /// <param name="session">设计器会话。</param>
    /// <param name="node">目标画布节点或节点模型。</param>
    /// <param name="direction">端口方向或路径方向。</param>
    /// <param name="index">目标元素索引。</param>
    /// <param name="count">元素总数。</param>
    public static WorkflowPoint GetPortScreenPoint(
        WorkflowDesignerSession session,
        WorkflowCanvasNode node,
        WorkflowPortDirection direction,
        int index,
        int count)
    {
        var side = direction == WorkflowPortDirection.Input ? WorkflowPortSide.Top : WorkflowPortSide.Bottom;
        var ports = Enumerable.Range(0, Math.Max(1, count))
            .Select(item => new WorkflowPortDescriptor(item.ToString(), direction, side: side))
            .ToArray();
        return GetPortScreenPoint(session, node, ports[Math.Clamp(index, 0, ports.Length - 1)], ports);
    }
}
