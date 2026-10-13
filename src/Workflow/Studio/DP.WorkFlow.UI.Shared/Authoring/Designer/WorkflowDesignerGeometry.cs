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
/// 节点标题行的屏幕布局：状态点 · 图标 · 标题 · 执行序号徽标 · 耗时，同处一行、互不重叠且垂直居中。
/// 字号以屏幕像素表示（随缩放变化，不随系统 DPI 额外放大），使文字与卡片尺寸始终成比例。
/// 序号徽标和耗时宽度为 0 表示该项不显示。
/// </summary>
public readonly record struct WorkflowNodeHeaderLayout(
    WorkflowDesignerRect IndicatorBounds,
    WorkflowDesignerRect IconBounds,
    WorkflowDesignerRect TitleBounds,
    WorkflowDesignerRect OrderBounds,
    WorkflowDesignerRect DurationBounds,
    double TitleFontSize,
    double DetailFontSize);

/// <summary>
/// 节点正文的行结构（每行一格 <see cref="WorkflowDesignerGeometry.RowHeight"/>），自上而下：
/// 上边多端口名称行、左右侧端口行、外置数据成员行、下边多端口名称行。没有正文内容时整行数为 0，节点只有标题行。
/// </summary>
/// <param name="TopLabelRows">上边多端口名称行（0 或 1）。</param>
/// <param name="SideRows">左右两边端口行数（取两边端口数的较大者）。</param>
/// <param name="DataRows">外置数据成员行数。</param>
/// <param name="BottomLabelRows">下边多端口名称行（0 或 1）。</param>
public readonly record struct WorkflowNodeRows(int TopLabelRows, int SideRows, int DataRows, int BottomLabelRows)
{
    /// <summary>正文总行数。</summary>
    public int Count => TopLabelRows + SideRows + DataRows + BottomLabelRows;

    /// <summary>节点设计高度：标题行加正文各行。</summary>
    public double NodeHeight => WorkflowDesignerGeometry.HeaderHeight + Count * WorkflowDesignerGeometry.RowHeight;
}

/// <summary>提供画布与屏幕坐标换算。</summary>
public static class WorkflowDesignerGeometry
{
    /// <summary>
    /// 设计栅格边长（画布单位）。节点、端口、连线拐点与绘制的栅格线共用这一个值：
    /// 节点宽为 2 格整数倍、高为整格，吸附后四条边与居中端口都落在栅格线上；连线在画布坐标里按栅格路由，任何缩放下形状一致。
    /// </summary>
    public const double GridSize = 24;

    /// <summary>把画布坐标就近吸附到栅格线。</summary>
    /// <param name="value">画布坐标。</param>
    public static double SnapToGrid(double value) => Math.Round(value / GridSize) * GridSize;

    /// <summary>节点宽度向上取到 2 格的整数倍，使居中端口也落在栅格线上。</summary>
    /// <param name="width">内容所需宽度。</param>
    public static double QuantizeNodeWidth(double width) => Math.Max(2, Math.Ceiling(width / (2 * GridSize) - 1e-9)) * 2 * GridSize;

    /// <summary>节点高度向上取整格。</summary>
    /// <param name="height">内容所需高度。</param>
    public static double QuantizeNodeHeight(double height) => Math.Max(1, Math.Ceiling(height / GridSize - 1e-9)) * GridSize;

    /// <summary>节点圆角半径（屏幕像素），随缩放等比，缩小时不会变成胶囊形。</summary>
    /// <param name="zoom">当前缩放。</param>
    public static double NodeCornerRadius(double zoom) => Math.Max(1, 8 * zoom);

    /// <summary>
    /// 栅格线的屏幕间距：细线每格一条，主线每 <see cref="MajorGridCells"/> 格一条。细线间距小于 8 像素时不再绘制（返回 0），
    /// 主线也小于 8 像素时按 2 的幂合并，线条始终是设计栅格的整数倍，不会因缩小而糊成一片或错位。
    /// </summary>
    /// <param name="zoom">当前缩放。</param>
    public static (double Minor, double Major) GridLineSpacing(double zoom)
    {
        var minor = GridSize * Math.Max(0.01, zoom);
        var major = minor * MajorGridCells;
        while (major < 8) major *= 2;
        return (minor >= 8 ? minor : 0, major);
    }

    /// <summary>主栅格线间隔的格数。</summary>
    public const int MajorGridCells = 5;

    /// <summary>节点边框宽度（屏幕像素）：设计宽度随缩放等比，最细 1 像素。</summary>
    /// <param name="zoom">当前缩放。</param>
    /// <param name="designWidth">缩放为 1 时的边框宽度，见 <see cref="WorkflowNodeVisualStyle.BorderWidth"/>。</param>
    public static double NodeBorderWidth(double zoom, double designWidth) => Math.Max(1, Math.Min(designWidth, designWidth * zoom));

    /// <summary>端口圆点半径（屏幕像素）：平时为直径 6 的小圆点，悬停节点或拖线时放大便于拾取。</summary>
    /// <param name="zoom">当前缩放。</param>
    /// <param name="emphasized">是否放大显示。</param>
    public static double PortScreenRadius(double zoom, bool emphasized = false) => Math.Max(1.5, (emphasized ? 4.5 : 3) * zoom);

    /// <summary>连线终点箭头：长度、半宽以及箭尖距端口中心的内缩（屏幕像素），随缩放等比并限制在可辨认范围内。</summary>
    /// <param name="zoom">当前缩放。</param>
    public static (double Length, double HalfWidth, double Inset) ConnectionArrow(double zoom)
    {
        var length = Math.Max(3, Math.Min(12, 8 * zoom));
        return (length, length / 2, PortScreenRadius(zoom) + Math.Max(1, 2 * zoom));
    }

    /// <summary>连接标签在缩放小于此值时不绘制（文字已不可读，只会遮挡连线）。</summary>
    public const double ConnectionLabelMinimumZoom = 0.5;

    /// <summary>节点标题行的设计高度（2 格）。</summary>
    public const double HeaderHeight = 2 * GridSize;
    /// <summary>节点正文每行的设计高度（1 格）：侧边端口、外置数据成员、多端口名称各占一行。</summary>
    public const double RowHeight = GridSize;
    /// <summary>节点允许的最小设计高度：只有标题行。</summary>
    public const double MinimumNodeHeight = HeaderHeight;
    /// <summary>端口命中使用的基础半径。</summary>
    public const double PortRadius = 5;

    /// <summary>标题文字的设计像素字号。</summary>
    public const double TitleFontPixels = 13;
    /// <summary>执行序号、耗时、端口与连接标签的设计像素字号。</summary>
    public const double DetailFontPixels = 11;
    /// <summary>标题行图标的设计边长。</summary>
    public const double HeaderIconSize = 16;

    /// <summary>
    /// 计算标题行布局：左起状态点、图标、标题，右起耗时、执行序号徽标。序号和耗时合计最多占可用宽度的一半，
    /// 长标题只在自己的区域内省略，不会覆盖序号（回环时靠序号找最后执行的节点）。
    /// </summary>
    /// <param name="nodeBounds">节点屏幕矩形。</param>
    /// <param name="zoom">当前缩放。</param>
    /// <param name="measuredOrderWidth">序号文字测量宽度；0 表示不显示序号。</param>
    /// <param name="measuredDurationWidth">耗时文字测量宽度；0 表示不显示耗时。</param>
    public static WorkflowNodeHeaderLayout CalculateNodeHeaderLayout(
        WorkflowDesignerRect nodeBounds,
        double zoom,
        double measuredOrderWidth,
        double measuredDurationWidth = 0)
    {
        zoom = Math.Max(0.05, zoom);
        var headerHeight = Math.Min(nodeBounds.Height, HeaderHeight * zoom);
        double Center(double size) => nodeBounds.Y + (headerHeight - size) / 2;
        var indicator = 7 * zoom;
        var icon = HeaderIconSize * zoom;
        var left = nodeBounds.X + 10 * zoom;
        var indicatorBounds = new WorkflowDesignerRect(left, Center(indicator), indicator, indicator);
        var iconBounds = new WorkflowDesignerRect(left + indicator + 8 * zoom, Center(icon), icon, icon);
        var titleLeft = iconBounds.X + icon + 8 * zoom;
        var right = nodeBounds.X + nodeBounds.Width - 10 * zoom;
        var available = Math.Max(0, right - titleLeft);
        var gap = 6 * zoom;
        var badgeHeight = Math.Min(headerHeight, 18 * zoom);
        var orderWidth = measuredOrderWidth > 0 ? measuredOrderWidth + 10 * zoom : 0;
        var durationWidth = measuredDurationWidth > 0 ? measuredDurationWidth : 0;
        var runtimeWidth = orderWidth + durationWidth + (orderWidth > 0 && durationWidth > 0 ? gap : 0);
        if (runtimeWidth > available * 0.5 && runtimeWidth > 0)
        {
            // 空间不足时先压缩耗时，序号保持完整。
            durationWidth = Math.Max(0, durationWidth - (runtimeWidth - available * 0.5));
            runtimeWidth = orderWidth + durationWidth + (orderWidth > 0 && durationWidth > 0 ? gap : 0);
        }
        var durationBounds = new WorkflowDesignerRect(right - durationWidth, nodeBounds.Y, durationWidth, headerHeight);
        var orderRight = durationWidth > 0 ? durationBounds.X - gap : right;
        var orderBounds = new WorkflowDesignerRect(orderRight - orderWidth, Center(badgeHeight), orderWidth, orderWidth > 0 ? badgeHeight : 0);
        var titleWidth = Math.Max(0, available - runtimeWidth - (runtimeWidth > 0 ? 8 * zoom : 0));
        return new WorkflowNodeHeaderLayout(
            indicatorBounds,
            iconBounds,
            new WorkflowDesignerRect(titleLeft, nodeBounds.Y, titleWidth, headerHeight),
            orderBounds,
            durationBounds,
            Math.Max(3, TitleFontPixels * zoom),
            Math.Max(3, DetailFontPixels * zoom));
    }

    /// <summary>
    /// 由端口所在边和外置数据成员数确定正文行结构：左右边各端口一行；某方向端口多于一个时，上/下边要显示端口名称，各占一行；
    /// 每个外置数据成员一行。设计器尺寸计算与绘制共用这一规则。
    /// </summary>
    /// <param name="ports">节点显示的端口及其所在边。</param>
    /// <param name="dataMembers">外置数据成员数。</param>
    public static WorkflowNodeRows GetNodeRows(IEnumerable<(WorkflowPortDescriptor Port, WorkflowPortSide Side)> ports, int dataMembers)
    {
        var list = ports.ToArray();
        bool Labeled(WorkflowPortDirection direction) => list.Count(item => item.Port.Direction == direction) > 1;
        int left = list.Count(item => item.Side == WorkflowPortSide.Left), right = list.Count(item => item.Side == WorkflowPortSide.Right);
        return new WorkflowNodeRows(
            list.Any(item => item.Side == WorkflowPortSide.Top && Labeled(item.Port.Direction)) ? 1 : 0,
            Math.Max(left, right),
            Math.Max(0, dataMembers),
            list.Any(item => item.Side == WorkflowPortSide.Bottom && Labeled(item.Port.Direction)) ? 1 : 0);
    }

    /// <summary>按会话中节点当前显示的端口和外置成员计算正文行结构。</summary>
    /// <param name="session">设计器会话。</param>
    /// <param name="node">画布节点。</param>
    public static WorkflowNodeRows GetNodeRows(WorkflowDesignerSession session, WorkflowCanvasNode node)
    {
        var ports = session.GetPorts(node.Node.Id, WorkflowPortDirection.Input)
            .Concat(session.GetPorts(node.Node.Id, WorkflowPortDirection.Output))
            .Select(port => (port, node.GetPortSide(port)));
        return GetNodeRows(ports, session.GetExposedOutputMembers(node).Count);
    }

    /// <summary>第 <paramref name="index"/> 个正文行（从 0 起，不含标题行）中心相对节点顶边的屏幕偏移。</summary>
    /// <param name="index">正文行序号。</param>
    /// <param name="zoom">当前缩放。</param>
    public static double RowCenterOffset(double index, double zoom) => (HeaderHeight + (index + 0.5) * RowHeight) * Math.Max(0.05, zoom);

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
        Guard.NotNull(port);
        Guard.NotNull(ports);
        var rect = GetNodeScreenRect(session, node);
        var actualSide = sideOverride ?? node.GetPortSide(port);
        var sameSide = ports.Where(item => ReferenceEquals(item, port) || node.GetPortSide(item) == actualSide).ToArray();
        var index = Array.IndexOf(sameSide, port);
        if (index < 0)
            index = sameSide.ToList().FindIndex(item => item.Key == port.Key && item.Direction == port.Direction);
        var ratio = (Math.Max(0, index) + 1d) / (Math.Max(1, sameSide.Length) + 1d);
        // 左右边端口逐行排列，与该行的端口名称对齐；行结构见 GetNodeRows。
        // 连接端点临时换到没有端口行的边时（端口边覆盖），按该边的可用区间均分，单个端点位于竖直中点。
        double SideY()
        {
            var rows = GetNodeRows(session, node);
            if (index >= 0 && index < rows.SideRows) return rect.Y + RowCenterOffset(rows.TopLabelRows + index, session.Zoom);
            var (top, height) = GetSideTrack(rect, session.Zoom, rows);
            return top + height * ratio;
        }
        var raw = actualSide switch
        {
            WorkflowPortSide.Left => new WorkflowPoint(rect.X, SideY()),
            WorkflowPortSide.Top => new WorkflowPoint(rect.X + rect.Width * ratio, rect.Y),
            WorkflowPortSide.Right => new WorkflowPoint(rect.X + rect.Width, SideY()),
            WorkflowPortSide.Bottom => new WorkflowPoint(rect.X + rect.Width * ratio, rect.Y + rect.Height),
            _ => throw new ArgumentOutOfRangeException(nameof(port), actualSide, "未知端口边。")
        };
        // 上下边单端口严格位于边的中点；多端口沿边分布并吸附栅格。
        if (!session.SnapToGrid || sameSide.Length == 1 || actualSide is WorkflowPortSide.Left or WorkflowPortSide.Right) return raw;
        var canvasPoint = ScreenToCanvas(session, raw.X, raw.Y);
        var snapped = session.SnapPoint(canvasPoint);
        var screenSnapped = CanvasToScreen(session, snapped.X, snapped.Y);
        return new WorkflowPoint(screenSnapped.X, raw.Y);
    }

    /// <summary>
    /// 端口名称的文字区域：左右边端口的名称写在端口所在行内（左边左对齐、右边右对齐，各占半宽）；
    /// 上/下边多端口的名称写在紧贴该边的名称行内，以端口为中心、宽度为相邻端口间距。
    /// </summary>
    /// <param name="nodeRect">节点屏幕矩形。</param>
    /// <param name="zoom">当前缩放。</param>
    /// <param name="side">端口所在边。</param>
    /// <param name="port">端口屏幕坐标。</param>
    /// <param name="sameSideCount">同一边的端口数。</param>
    /// <returns>文字区域与水平对齐方式（-1 左、0 居中、1 右）。</returns>
    public static (WorkflowDesignerRect Bounds, int Alignment) GetPortLabelBounds(
        WorkflowDesignerRect nodeRect, double zoom, WorkflowPortSide side, WorkflowPoint port, int sameSideCount)
    {
        zoom = Math.Max(0.05, zoom);
        var row = RowHeight * zoom;
        var inset = 10 * zoom;
        var half = nodeRect.Width / 2;
        var slot = nodeRect.Width / (Math.Max(1, sameSideCount) + 1);
        return side switch
        {
            WorkflowPortSide.Left => (new WorkflowDesignerRect(nodeRect.X + inset, port.Y - row / 2, Math.Max(0, half - inset - 2 * zoom), row), -1),
            WorkflowPortSide.Right => (new WorkflowDesignerRect(nodeRect.X + half + 2 * zoom, port.Y - row / 2, Math.Max(0, half - inset - 2 * zoom), row), 1),
            WorkflowPortSide.Top => (new WorkflowDesignerRect(port.X - slot / 2, nodeRect.Y + HeaderHeight * zoom, slot, row), 0),
            _ => (new WorkflowDesignerRect(port.X - slot / 2, nodeRect.Y + nodeRect.Height - row, slot, row), 0)
        };
    }

    /// <summary>
    /// 左右两边端口行所在区间（屏幕坐标）；没有侧边端口行时为整个节点高度，侧边锚点取节点竖直中点。
    /// </summary>
    /// <param name="rect">节点屏幕矩形。</param>
    /// <param name="zoom">当前缩放。</param>
    /// <param name="rows">节点正文行结构。</param>
    public static (double Top, double Height) GetSideTrack(WorkflowDesignerRect rect, double zoom, WorkflowNodeRows rows = default)
    {
        if (rows.SideRows == 0) return (rect.Y, Math.Max(1, rect.Height));
        zoom = Math.Max(0.05, zoom);
        return (rect.Y + (HeaderHeight + rows.TopLabelRows * RowHeight) * zoom, rows.SideRows * RowHeight * zoom);
    }

    /// <summary>外置数据成员行所在区间（屏幕坐标）；没有数据成员时高度为 0。</summary>
    /// <param name="rect">节点屏幕矩形。</param>
    /// <param name="zoom">当前缩放。</param>
    /// <param name="rows">节点正文行结构。</param>
    public static (double Top, double Height) GetDataBand(WorkflowDesignerRect rect, double zoom, WorkflowNodeRows rows)
    {
        zoom = Math.Max(0.05, zoom);
        return (rect.Y + (HeaderHeight + (rows.TopLabelRows + rows.SideRows) * RowHeight) * zoom, rows.DataRows * RowHeight * zoom);
    }

    /// <summary>
    /// 计算节点右边缘各数据端口的屏幕坐标：每个外置成员一行，位于侧边端口行之后、下边名称行之前。
    /// </summary>
    /// <param name="session">设计器会话。</param>
    /// <param name="node">目标画布节点。</param>
    public static IReadOnlyList<(WorkflowOutputMember Member, WorkflowPoint Point)> GetDataPortPoints(
        WorkflowDesignerSession session,
        WorkflowCanvasNode node)
    {
        var members = session.GetExposedOutputMembers(node);
        if (members.Count == 0) return Array.Empty<(WorkflowOutputMember, WorkflowPoint)>();
        var rect = GetNodeScreenRect(session, node);
        var rows = GetNodeRows(session, node);
        return members
            .Select((member, index) => (member, new WorkflowPoint(rect.X + rect.Width,
                rect.Y + RowCenterOffset(rows.TopLabelRows + rows.SideRows + index, session.Zoom))))
            .ToArray();
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
        return GetPortScreenPoint(session, node, ports[Polyfill.Clamp(index, 0, ports.Length - 1)], ports);
    }
}
