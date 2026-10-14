using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using DP.WorkFlow.UI;

namespace DP.WorkFlow.UI.Wpf;

/// <summary>基于 WPF DrawingContext 的工作流画布，使用共享设计会话。</summary>
public sealed class WorkflowDesignerControl : FrameworkElement
{
    private static Brush CanvasBrush => Brush(WorkflowDesignerPalette.Canvas);
    /// <summary>标识 Session 依赖属性。</summary>
    public static readonly DependencyProperty SessionProperty = DependencyProperty.Register(
        nameof(Session),
        typeof(WorkflowDesignerSession),
        typeof(WorkflowDesignerControl),
        new FrameworkPropertyMetadata(null, OnSessionPropertyChanged));

    private WorkflowDesignerSession? _session;
    private WorkflowNodeDragOperation? _nodeDrag;
    private WorkflowConnectionModel? _dragConnection;
    private WorkflowConnectionModel? _dragLabelConnection;
    private double _dragLabelOrigin;
    private int _dragWaypointIndex = -1;
    private WorkflowPoint _dragWaypointOrigin;
    private Point _marqueeStart;
    private Point _marqueeCurrent;
    private bool _marqueeAdditive;
    private Point _panStart;
    private WorkflowPoint _panOrigin;
    private WorkflowPortHit? _connectionStart;
    private WorkflowPortSide? _portDropSide;
    private Point _pointer;
    private string? _hoverNodeId;
    private (WorkflowCanvasNode Node, WorkflowOutputMember Member, WorkflowPoint Point)? _dataLinkStart;
    private InteractionMode _mode;
    private readonly System.Windows.Threading.DispatcherTimer _runtimeTimer;

    /// <summary>初始化工作流设计画布并配置绘制、拖放和运行状态刷新。</summary>
    public WorkflowDesignerControl()
    {
        Focusable = true;
        ClipToBounds = true;
        _runtimeTimer = new System.Windows.Threading.DispatcherTimer(
            TimeSpan.FromMilliseconds(100),
            System.Windows.Threading.DispatcherPriority.Background,
            (_, _) =>
            {
                if (_session?.RuntimeSnapshot?.ExecutionState is E_WorkflowExecutionState.Running)
                    InvalidateVisual();
            },
            Dispatcher);
        _runtimeTimer.Start();
        AllowDrop = true;
        DragEnter += OnToolboxDrag;
        DragOver += OnToolboxDrag;
        Drop += OnToolboxDrop;
        Unloaded += (_, _) =>
        {
            _runtimeTimer.Stop();
            if (_session is not null)
                _session.Changed -= OnDesignerSessionChanged;
        };
        Loaded += (_, _) =>
        {
            _runtimeTimer.Start();
            if (_session is not null)
            {
                _session.Changed -= OnDesignerSessionChanged;
                _session.Changed += OnDesignerSessionChanged;
            }
        };
    }

    /// <summary>获取或设置共享设计会话。</summary>
    public WorkflowDesignerSession? Session
    {
        get => (WorkflowDesignerSession?)GetValue(SessionProperty);
        set => SetValue(SessionProperty, value);
    }

    /// <summary>双击节点时发生。</summary>
    public event EventHandler<IWorkflowNodeModel>? NodeEditRequested;

    /// <summary>交互连接失败时发生。</summary>
    public event EventHandler<string>? InteractionError;

    /// <summary>绘制画布背景、网格、连接、节点和当前交互提示。</summary>
    /// <param name="drawingContext">WPF 绘图上下文。</param>
    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        drawingContext.DrawRectangle(CanvasBrush, null, new Rect(RenderSize));
        DrawGrid(drawingContext);
        if (_session is null)
            return;
        DrawConnections(drawingContext);
        DrawDataLinks(drawingContext);
        foreach (var node in _session.Canvas.Nodes)
            DrawNode(drawingContext, node);
        if (_mode == InteractionMode.DataLink && _dataLinkStart is { } dataLink)
            DrawDataCurve(drawingContext, new Pen(DataPortBrush, 2) { DashStyle = DashStyles.Dash }, dataLink.Point, new WorkflowPoint(_pointer.X, _pointer.Y));
        if (_connectionStart.HasValue)
        {
            DrawOrthogonalPath(
                drawingContext,
                new Pen(Freeze(new SolidColorBrush(Color.FromRgb(56, 189, 248))), 2),
                WorkflowDesignerInteraction.BuildOrthogonalPath(
                    _connectionStart.Value.Point,
                    new WorkflowPoint(_pointer.X, _pointer.Y),
                    Array.Empty<WorkflowPoint>()));
        }
        DrawMarquee(drawingContext);
    }

    /// <summary>验证拖入数据是否为工作流工具箱项目。</summary>
    private static void OnToolboxDrag(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(typeof(WorkflowToolboxItem))
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    /// <summary>处理工具箱项目放置，在空白处或连接线上创建节点。</summary>
    private void OnToolboxDrop(object sender, DragEventArgs e)
    {
        if (_session is null || e.Data.GetData(typeof(WorkflowToolboxItem)) is not WorkflowToolboxItem item)
            return;
        var point = e.GetPosition(this);
        var canvas = WorkflowDesignerGeometry.ScreenToCanvas(_session, point.X, point.Y);
        var insertion = WorkflowDesignerInteraction.HitConnectionInsertion(_session, point.X, point.Y, 14);
        if (insertion is null
            || _session.AddNodeOnConnection(
                item.NodeType,
                canvas.X - 90,
                canvas.Y - 30,
                insertion.Value.Connection,
                insertion.Value.InputSide,
                insertion.Value.OutputSide) is null)
        {
            _session.AddNode(item.NodeType, canvas.X - 90, canvas.Y - 30);
        }
        e.Handled = true;
    }

    /// <summary>处理鼠标按下并启动选择、拖动、平移、连接或框选交互。</summary>
    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        if (_session is null)
            return;
        _pointer = e.GetPosition(this);
        if (e.ChangedButton == MouseButton.Right)
        {
            var contextConnection = HitConnectionLabel(_pointer.X, _pointer.Y) ?? WorkflowDesignerInteraction.HitConnection(_session, _pointer.X, _pointer.Y);
            if (contextConnection is not null)
            {
                _session.SelectConnection(contextConnection);
                ShowConnectionMenu(contextConnection);
                e.Handled = true;
                return;
            }
        }
        if (e.ChangedButton is MouseButton.Middle or MouseButton.Right)
        {
            _mode = InteractionMode.Pan;
            _panStart = _pointer;
            _panOrigin = new WorkflowPoint(_session.PanX, _session.PanY);
            CaptureMouse();
            Cursor = Cursors.Hand;
            return;
        }
        if (e.ChangedButton != MouseButton.Left)
            return;
        var waypoint = WorkflowDesignerInteraction.HitWaypoint(_session, _pointer.X, _pointer.Y);
        if (waypoint.HasValue)
        {
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            {
                _session.RemoveConnectionWaypoint(waypoint.Value.Connection, waypoint.Value.Index);
                return;
            }
            _dragConnection = waypoint.Value.Connection;
            _dragWaypointIndex = waypoint.Value.Index;
            _dragWaypointOrigin = waypoint.Value.Connection.Waypoints[waypoint.Value.Index];
            _mode = InteractionMode.MoveWaypoint;
            CaptureMouse();
            return;
        }
        var labelConnection = HitConnectionLabel(_pointer.X, _pointer.Y);
        if (labelConnection is not null)
        {
            _session.SelectConnection(labelConnection);
            _dragLabelConnection = labelConnection;
            _dragLabelOrigin = labelConnection.LabelPosition;
            _mode = InteractionMode.MoveConnectionLabel;
            CaptureMouse();
            return;
        }
        if (WorkflowDesignerInteraction.HitDataPort(_session, _pointer.X, _pointer.Y) is { } dataPort)
        {
            _session.SelectNode(dataPort.Node.Node.Id);
            _dataLinkStart = dataPort;
            _mode = InteractionMode.DataLink;
            Cursor = Cursors.Cross;
            CaptureMouse();
            InvalidateVisual();
            return;
        }
        var portHit = WorkflowDesignerInteraction.HitSingleOutputSideTarget(_session, _pointer.X, _pointer.Y)
            ?? HitPort(_pointer.X, _pointer.Y, WorkflowPortDirection.Output)
            ?? HitPort(_pointer.X, _pointer.Y, WorkflowPortDirection.Input);
        if (portHit.HasValue)
        {
            _session.SelectNode(portHit.Value.Node.Node.Id);
            _connectionStart = portHit;
            _portDropSide = portHit.Value.Side;
            _mode = InteractionMode.MovePort;
            CaptureMouse();
            InvalidateVisual();
            return;
        }
        var node = WorkflowDesignerInteraction.HitNode(_session, _pointer.X, _pointer.Y);
        if (node is null)
        {
            var connection = WorkflowDesignerInteraction.HitConnection(_session, _pointer.X, _pointer.Y);
            if (connection is not null)
                _session.SelectConnection(connection);
            else
            {
                _marqueeStart = _pointer;
                _marqueeCurrent = _pointer;
                _marqueeAdditive = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
                if (!_marqueeAdditive)
                    _session.SelectNodes(Array.Empty<string>());
                _mode = InteractionMode.Marquee;
                CaptureMouse();
            }
        }
        else
        {
            var additive = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
            if (additive || !_session.SelectedNodeIds.Contains(node.Node.Id))
                _session.SelectNode(node.Node.Id, additive);
            if (!_session.SelectedNodeIds.Contains(node.Node.Id))
                return;
            _nodeDrag = new WorkflowNodeDragOperation(
                _session, WorkflowDesignerGeometry.ScreenToCanvas(_session, _pointer.X, _pointer.Y));
            _mode = InteractionMode.MoveNode;
            CaptureMouse();
        }
    }

    /// <summary>处理指针移动并实时更新当前交互状态。</summary>
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        _pointer = e.GetPosition(this);
        if (_session is null)
            return;
        if (_mode == InteractionMode.Pan)
        {
            _session.SetViewport(
                _session.Zoom,
                _panOrigin.X + _pointer.X - _panStart.X,
                _panOrigin.Y + _pointer.Y - _panStart.Y);
        }
        else if (_mode == InteractionMode.MoveNode && _nodeDrag is not null)
        {
            _nodeDrag.Update(WorkflowDesignerGeometry.ScreenToCanvas(_session, _pointer.X, _pointer.Y));
            InvalidateVisual();
        }
        else if (_mode == InteractionMode.MoveWaypoint && _dragConnection is not null && _dragWaypointIndex >= 0)
        {
            _dragConnection.Waypoints[_dragWaypointIndex] = _session.SnapPoint(
                WorkflowDesignerGeometry.ScreenToCanvas(_session, _pointer.X, _pointer.Y));
            InvalidateVisual();
        }
        else if (_mode == InteractionMode.MoveConnectionLabel && _dragLabelConnection is not null)
        {
            _dragLabelConnection.LabelPosition = WorkflowDesignerInteraction.ProjectPathPosition(
                WorkflowDesignerInteraction.GetConnectionPath(_session, _dragLabelConnection), _pointer.X, _pointer.Y);
            InvalidateVisual();
        }
        else if (_mode == InteractionMode.MovePort && _connectionStart.HasValue)
        {
            var rect = WorkflowDesignerGeometry.GetNodeScreenRect(_session, _connectionStart.Value.Node);
            _portDropSide = WorkflowDesignerInteraction.NearestSide(rect, _pointer.X, _pointer.Y);
            if (_connectionStart.Value.Port.Direction == WorkflowPortDirection.Output
                && !WorkflowDesignerInteraction.IsNearRect(rect, _pointer.X, _pointer.Y, 42))
            {
                _mode = InteractionMode.Connect;
                _portDropSide = null;
            }
            Cursor = _mode == InteractionMode.MovePort ? Cursors.SizeAll : Cursors.Cross;
            InvalidateVisual();
        }
        else if (_mode is InteractionMode.Connect or InteractionMode.DataLink)
        {
            InvalidateVisual();
        }
        else if (_mode == InteractionMode.Marquee)
        {
            _marqueeCurrent = _pointer;
            InvalidateVisual();
        }
        else if (_mode == InteractionMode.None)
        {
            SetHoverNode(WorkflowDesignerInteraction.HitHoverNode(_session, _pointer.X, _pointer.Y)?.Node.Id);
        }
    }

    /// <inheritdoc />
    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        SetHoverNode(null);
    }

    /// <summary>记录鼠标所在节点；悬停节点的端口放大显示，便于拖线。</summary>
    private void SetHoverNode(string? nodeId)
    {
        if (string.Equals(_hoverNodeId, nodeId, StringComparison.Ordinal)) return;
        _hoverNodeId = nodeId;
        InvalidateVisual();
    }

    /// <summary>完成当前鼠标交互并将最终结果提交到设计会话。</summary>
    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);
        if (_session is null)
            return;
        var position = e.GetPosition(this);
        if (_mode == InteractionMode.MoveNode && _nodeDrag is not null)
        {
            _nodeDrag.Commit();
        }
        else if (_mode == InteractionMode.MoveWaypoint && _dragConnection is not null && _dragWaypointIndex >= 0)
        {
            var finalPoint = _dragConnection.Waypoints[_dragWaypointIndex];
            _dragConnection.Waypoints[_dragWaypointIndex] = _dragWaypointOrigin;
            _session.MoveConnectionWaypoint(_dragConnection, _dragWaypointIndex, finalPoint);
        }
        else if (_mode == InteractionMode.Marquee)
        {
            var marquee = NormalizeRectangle(_marqueeStart, _marqueeCurrent);
            _session.SelectNodes(
                WorkflowDesignerInteraction.GetNodeIdsInScreenRect(
                    _session, new WorkflowDesignerRect(marquee.X, marquee.Y, marquee.Width, marquee.Height)),
                _marqueeAdditive);
        }
        else if (_mode == InteractionMode.MoveConnectionLabel && _dragLabelConnection is not null)
        {
            var labelPosition = _dragLabelConnection.LabelPosition;
            _dragLabelConnection.LabelPosition = _dragLabelOrigin;
            _session.SetConnectionLabelPosition(_dragLabelConnection, labelPosition);
        }
        else if (_mode == InteractionMode.MovePort && _connectionStart.HasValue && _portDropSide.HasValue)
        {
            _session.SetPortSide(
                _connectionStart.Value.Node.Node.Id,
                _connectionStart.Value.Port.Direction,
                _connectionStart.Value.Port.Key,
                _portDropSide.Value);
        }
        else if (_mode == InteractionMode.Connect && _connectionStart.HasValue)
        {
            var target = HitPort(position.X, position.Y, WorkflowPortDirection.Input);
            if (target.HasValue)
            {
                try
                {
                    _session.Connect(
                        _connectionStart.Value.Node.Node.Id,
                        _connectionStart.Value.Port.Key,
                        target.Value.Node.Node.Id,
                        target.Value.Port.Key,
                        _connectionStart.Value.Side,
                        target.Value.Side);
                }
                catch (InvalidOperationException exception)
                {
                    InteractionError?.Invoke(this, exception.Message);
                }
            }
        }
        else if (_mode == InteractionMode.DataLink && _dataLinkStart is { } dataLink)
        {
            CompleteDataLink(dataLink.Node, dataLink.Member, position);
        }
        _dataLinkStart = null;
        _nodeDrag = null;
        _dragConnection = null;
        _dragLabelConnection = null;
        _dragWaypointIndex = -1;
        _connectionStart = null;
        _portDropSide = null;
        _mode = InteractionMode.None;
        ReleaseMouseCapture();
        Cursor = Cursors.Arrow;
        InvalidateVisual();
    }

    /// <summary>以指针位置为中心缩放工作流画布。</summary>
    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        if (_session is null)
            return;
        var position = e.GetPosition(this);
        var canvasPoint = WorkflowDesignerGeometry.ScreenToCanvas(_session, position.X, position.Y);
        var zoom = Polyfill.Clamp(_session.Zoom * (e.Delta > 0 ? 1.1 : 1 / 1.1), 0.25, 2.5);
        _session.SetViewport(zoom, position.X - canvasPoint.X * zoom, position.Y - canvasPoint.Y * zoom);
    }

    /// <summary>处理 WPF 左键双击节点或连接。</summary>
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            var position = e.GetPosition(this);
            var node = _session is null ? null : WorkflowDesignerInteraction.HitNode(_session, position.X, position.Y);
            if (node is not null)
            {
                NodeEditRequested?.Invoke(this, node.Node);
                e.Handled = true;
                return;
            }
            // 双击标签编辑备注；双击连线其它位置仍是添加拐点。
            if (_session is not null && HitConnectionLabel(position.X, position.Y) is { } labelled)
            {
                _session.SelectConnection(labelled);
                EditConnectionNote(labelled);
                e.Handled = true;
                return;
            }
            if (_session is not null && WorkflowDesignerInteraction.HitConnection(_session, position.X, position.Y) is { } connection)
            {
                _session.SelectConnection(connection);
                _session.AddConnectionWaypoint(
                    connection,
                    WorkflowDesignerGeometry.ScreenToCanvas(_session, position.X, position.Y));
                e.Handled = true;
                return;
            }
        }
        base.OnMouseLeftButtonDown(e);
    }

    /// <summary>处理设计器复制、粘贴、删除和撤销重做快捷键。</summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_session is null)
            return;
        if (e.Key == Key.A && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            _session.SelectNodes(_session.Canvas.Nodes.Select(node => node.Node.Id));
            e.Handled = true;
        }
        else if (e.Key == Key.C && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            e.Handled = _session.CopySelection();
        else if (e.Key == Key.V && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            e.Handled = _session.PasteSelection().Count > 0;
        else if (e.Key == Key.Delete && _session.SelectedNodeIds.Count > 0)
            e.Handled = _session.RemoveSelectedNodes();
        else if (e.Key == Key.Delete && _session.SelectedConnection is { } connection)
            e.Handled = _session.RemoveConnection(connection);
        else if (e.Key == Key.Z && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            e.Handled = _session.Undo();
        else if (e.Key == Key.Y && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            e.Handled = _session.Redo();
    }

    /// <summary>绘制随缩放和平移变化的淡细线栅格：每格一条细线、每 5 格一条主线；缩小后先隐藏细线，再按 2 的幂合并主线。</summary>
    /// <param name="context">绘图上下文或当前编辑上下文。</param>
    private void DrawGrid(DrawingContext context)
    {
        if (_session is null)
            return;
        var (minor, major) = WorkflowDesignerGeometry.GridLineSpacing(_session.Zoom);
        if (minor > 0)
            DrawGridLines(context, WorkflowDesignerPalette.GridMinor, minor);
        DrawGridLines(context, WorkflowDesignerPalette.GridMajor, major);
    }

    /// <summary>按像素对齐绘制一组等距栅格线（线宽 1 时落在像素中心），栅格原点跟随画布平移。</summary>
    private void DrawGridLines(DrawingContext context, WorkflowColor color, double spacing)
    {
        var pen = Freeze(new Pen(Brush(color), 1));
        for (var x = PositiveModulo(_session!.PanX, spacing); x < ActualWidth; x += spacing)
            context.DrawLine(pen, new Point(Math.Round(x) + 0.5, 0), new Point(Math.Round(x) + 0.5, ActualHeight));
        for (var y = PositiveModulo(_session.PanY, spacing); y < ActualHeight; y += spacing)
            context.DrawLine(pen, new Point(0, Math.Round(y) + 0.5), new Point(ActualWidth, Math.Round(y) + 0.5));
    }

    private static double PositiveModulo(double value, double modulus) => (value % modulus + modulus) % modulus;

    /// <summary>绘制画布中的全部正交连接。</summary>
    /// <param name="context">绘图上下文或当前编辑上下文。</param>
    private void DrawConnections(DrawingContext context)
    {
        if (_session is null)
            return;
        foreach (var connection in _session.Canvas.Connections)
        {
            var source = WorkflowDesignerInteraction.FindPortPoint(
                _session, connection.FromNodeId, connection.FromPort, WorkflowPortDirection.Output, connection.FromSide);
            var target = WorkflowDesignerInteraction.FindPortPoint(
                _session, connection.ToNodeId, connection.ToPort, WorkflowPortDirection.Input, connection.ToSide);
            if (source.HasValue && target.HasValue)
            {
                var selected = ReferenceEquals(_session.SelectedConnection, connection);
                // 停用的并行分支：暗灰虚线，线和下游节点保留，随时可以重新启用。
                var pen = new Pen(
                    Brush(selected ? WorkflowDesignerPalette.Selected
                        : connection.Enabled ? WorkflowDesignerPalette.Connection : WorkflowDesignerPalette.DisabledConnection),
                    selected ? 2.5 : 1.5) { DashStyle = connection.Enabled ? DashStyles.Solid : DashStyles.Dash };
                DrawConnectionPath(context, pen, connection, source.Value, target.Value);
                DrawConnectionLabel(context, connection, source.Value);
                if (selected)
                    DrawWaypointHandles(context, connection);
            }
        }
    }

    /// <summary>
    /// 绘制节点：标题行（状态点 · 图标 · 标题 · 执行序号 · 耗时）和按内容逐行排列的正文（端口名称、外置数据成员）。
    /// 边框按方案 A 着色：运行绿、暂停/停止黄、异常红、选中蓝，其余为中性色。
    /// </summary>
    /// <param name="context">绘图上下文或当前编辑上下文。</param>
    /// <param name="item">目标数据项。</param>
    private void DrawNode(DrawingContext context, WorkflowCanvasNode item)
    {
        if (_session is null)
            return;
        var zoom = _session.Zoom;
        var rect = WorkflowDesignerGeometry.GetNodeScreenRect(_session, item);
        var bounds = new Rect(rect.X, rect.Y, rect.Width, rect.Height);
        var selected = _session.SelectedNodeIds.Contains(item.Node.Id);
        var style = WorkflowDesignerPalette.GetNodeStyle(_session.GetNodeVisualState(item.Node.Id), selected);
        var corner = WorkflowDesignerGeometry.NodeCornerRadius(zoom);
        if (style.Glow.A > 0)
        {
            var glow = Math.Max(1, WorkflowDesignerPalette.GlowWidth * zoom);
            var outer = bounds;
            outer.Inflate(glow / 2, glow / 2);
            context.DrawRoundedRectangle(null, new Pen(Brush(style.Glow), glow), outer, corner + glow / 2, corner + glow / 2);
        }
        context.DrawRoundedRectangle(Brush(WorkflowDesignerPalette.NodeBody), null, bounds, corner, corner);
        var rows = WorkflowDesignerGeometry.GetNodeRows(_session, item);
        var headerHeight = Math.Min(rect.Height, WorkflowDesignerGeometry.HeaderHeight * zoom);
        context.PushClip(new RectangleGeometry(bounds, corner, corner));
        context.DrawRectangle(Brush(WorkflowDesignerPalette.NodeHeader), null, new Rect(rect.X, rect.Y, Math.Max(0, rect.Width), Math.Max(0, headerHeight)));
        context.Pop();
        DrawRowSeparators(context, rect, rows, zoom);
        context.DrawRoundedRectangle(
            null,
            new Pen(Brush(style.Border), WorkflowDesignerGeometry.NodeBorderWidth(zoom, style.BorderWidth)),
            bounds,
            corner,
            corner);

        var orderText = WorkflowDesignerInteraction.GetRuntimeOrderText(_session, item);
        var durationText = WorkflowDesignerInteraction.GetRuntimeDurationText(_session, item);
        var detailSize = Math.Max(3, WorkflowDesignerGeometry.DetailFontPixels * zoom);
        var layout = WorkflowDesignerGeometry.CalculateNodeHeaderLayout(
            rect,
            zoom,
            string.IsNullOrEmpty(orderText) ? 0 : MeasureTextWidth(orderText!, detailSize, FontWeights.Normal),
            string.IsNullOrEmpty(durationText) ? 0 : MeasureTextWidth(durationText!, detailSize, FontWeights.Normal));
        var indicator = layout.IndicatorBounds;
        context.DrawEllipse(Brush(style.Indicator), null, new Point(indicator.X + indicator.Width / 2, indicator.Y + indicator.Height / 2),
            indicator.Width / 2, indicator.Height / 2);
        DrawIcon(context, _session.GetNodeIcon(item.Node), layout.IconBounds, WorkflowDesignerPalette.Icon);
        DrawTextClipped(
            context,
            item.Node.Title,
            layout.TitleBounds.X,
            layout.TitleBounds.Y,
            layout.TitleBounds.Width,
            layout.TitleBounds.Height,
            layout.TitleFontSize,
            FontWeights.Normal,
            Brush(WorkflowDesignerPalette.Title));
        DrawRuntimeInfo(context, layout, style, orderText, durationText);

        var emphasized = _hoverNodeId == item.Node.Id || _mode is InteractionMode.Connect or InteractionMode.MovePort;
        DrawPorts(context, item, WorkflowPortDirection.Input, emphasized);
        DrawPorts(context, item, WorkflowPortDirection.Output, emphasized);
        DrawDataPorts(context, item, rect);
        DrawConnectionOverrideEndpoints(context, item);
        DrawConnectionInputTargets(context, item);
        if (selected)
            DrawPortSideTargets(context, item, bounds);
    }

    /// <summary>标题行与正文之间、侧边端口行与外置数据行之间的分隔线。</summary>
    private static void DrawRowSeparators(DrawingContext context, WorkflowDesignerRect rect, WorkflowNodeRows rows, double zoom)
    {
        if (rows.Count == 0) return;
        var pen = Freeze(new Pen(Brush(WorkflowDesignerPalette.RowSeparator), 1));
        var headerBottom = rect.Y + WorkflowDesignerGeometry.HeaderHeight * zoom;
        context.DrawLine(pen, new Point(rect.X + 1, headerBottom), new Point(rect.X + rect.Width - 1, headerBottom));
        if (rows.DataRows > 0 && rows.TopLabelRows + rows.SideRows > 0)
        {
            var (top, _) = WorkflowDesignerGeometry.GetDataBand(rect, zoom, rows);
            var inset = 8 * zoom;
            context.DrawLine(pen, new Point(rect.X + inset, top), new Point(rect.X + rect.Width - inset, top));
        }
    }

    /// <summary>以线框描边绘制 24×24 视图框的矢量图标，缩放到目标区域。</summary>
    private static void DrawIcon(DrawingContext context, WorkflowNodeIcon icon, WorkflowDesignerRect bounds, WorkflowColor color)
    {
        if (bounds.Width < 4) return;
        context.DrawGeometry(null, IconPen(color, bounds.Width), IconGeometry(icon, bounds));
    }

    /// <summary>把图标子路径转换为 WPF 几何，缩放到目标区域。</summary>
    internal static Geometry IconGeometry(WorkflowNodeIcon icon, WorkflowDesignerRect bounds)
    {
        var scale = bounds.Width / WorkflowNodeIcon.ViewBox;
        Point Map(WorkflowPoint point) => new(bounds.X + point.X * scale, bounds.Y + point.Y * scale);
        var geometry = new StreamGeometry();
        using (var figure = geometry.Open())
        {
            foreach (var item in icon.Figures)
            {
                figure.BeginFigure(Map(item.Start), false, item.Closed);
                if (item.Segments.Count == 0) figure.LineTo(Map(item.Start), true, true);
                foreach (var segment in item.Segments)
                {
                    if (segment.IsCubic) figure.BezierTo(Map(segment.Control1), Map(segment.Control2), Map(segment.End), true, true);
                    else figure.LineTo(Map(segment.End), true, true);
                }
            }
        }
        geometry.Freeze();
        return geometry;
    }

    /// <summary>图标描边画笔：圆头圆角，线宽随图标尺寸等比。</summary>
    internal static Pen IconPen(WorkflowColor color, double size) => Freeze(new Pen(Brush(color), Math.Max(1, WorkflowNodeIcon.StrokeWidth * size / WorkflowNodeIcon.ViewBox))
    {
        StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round
    });

    /// <summary>
    /// 在节点右边缘对应行绘制外置数据成员：成员名称右对齐，端口为淡紫空心菱形。
    /// </summary>
    private void DrawDataPorts(DrawingContext context, WorkflowCanvasNode node, WorkflowDesignerRect rect)
    {
        if (_session is null || node.ExposedOutputMembers.Count == 0) return;
        var ports = WorkflowDesignerGeometry.GetDataPortPoints(_session, node);
        if (ports.Count == 0) return;
        var zoom = _session.Zoom;
        var size = Math.Max(2.5, 4.5 * zoom);
        var rowHeight = WorkflowDesignerGeometry.RowHeight * zoom;
        var border = Freeze(new Pen(Brush(WorkflowDesignerPalette.DataPort), Math.Max(1, 1.5 * Math.Min(1, zoom))));
        var fill = Brush(WorkflowDesignerPalette.NodeBody);
        var text = Brush(WorkflowDesignerPalette.DataLabel);
        foreach (var (member, point) in ports)
        {
            var left = rect.X + 10 * zoom;
            DrawTextClipped(context, member.DisplayName, left, point.Y - rowHeight / 2,
                Math.Max(0, point.X - left - size - 6 * zoom), rowHeight,
                Math.Max(3, WorkflowDesignerGeometry.DetailFontPixels * zoom), FontWeights.Normal, text, TextAlignment.Right);
            var geometry = new StreamGeometry();
            using (var figure = geometry.Open())
            {
                figure.BeginFigure(new Point(point.X, point.Y - size), true, true);
                figure.PolyLineTo(new[] { new Point(point.X + size, point.Y), new Point(point.X, point.Y + size), new Point(point.X - size, point.Y) }, true, true);
            }
            geometry.Freeze();
            context.DrawGeometry(fill, border, geometry);
        }
    }

    private static Brush DataPortBrush => Brush(WorkflowDesignerPalette.DataPort);

    /// <summary>绘制数据端口到下游节点的虚线数据连线，表示该参数绑定到了这个输出成员。</summary>
    private void DrawDataLinks(DrawingContext context)
    {
        if (_session is null) return;
        var links = _session.GetDataLinks();
        if (links.Count == 0) return;
        var nodes = _session.Canvas.Nodes.ToDictionary(node => node.Node.Id, StringComparer.Ordinal);
        var pen = new Pen(Brush(WorkflowDesignerPalette.DataPort.WithAlpha(170)), 1.5) { DashStyle = DashStyles.Dash };
        foreach (var link in links.DistinctBy(link => (link.SourceNodeId, link.Member, link.ConsumerNodeId)))
        {
            if (!nodes.TryGetValue(link.SourceNodeId, out var source) || !nodes.TryGetValue(link.ConsumerNodeId, out var consumer)) continue;
            var from = WorkflowDesignerGeometry.GetDataPortPoints(_session, source).FirstOrDefault(port => port.Member.Name == link.Member);
            if (from.Member is null) continue;
            var to = WorkflowDesignerInteraction.GetSideCenter(
                WorkflowDesignerGeometry.GetNodeScreenRect(_session, consumer), WorkflowPortSide.Left, _session.Zoom,
                WorkflowDesignerGeometry.GetNodeRows(_session, consumer));
            DrawDataCurve(context, pen, from.Point, to);
            var radius = Math.Max(2, 3 * _session.Zoom);
            context.DrawEllipse(DataPortBrush, null, new Point(to.X, to.Y), radius, radius);
        }
    }

    private static void DrawDataCurve(DrawingContext context, Pen pen, WorkflowPoint from, WorkflowPoint to)
    {
        var offset = Math.Max(30, Math.Abs(to.X - from.X) / 2);
        var geometry = new StreamGeometry();
        using (var figure = geometry.Open())
        {
            figure.BeginFigure(new Point(from.X, from.Y), false, false);
            figure.BezierTo(new Point(from.X + offset, from.Y), new Point(to.X - offset, to.Y), new Point(to.X, to.Y), true, false);
        }
        geometry.Freeze();
        context.DrawGeometry(null, pen, geometry);
    }

    /// <summary>
    /// 松开数据端口拖线：落在可接收该数据的节点上时建立绑定；目标节点有多个兼容参数时弹出菜单选择。
    /// </summary>
    private void CompleteDataLink(WorkflowCanvasNode source, WorkflowOutputMember member, Point location)
    {
        if (_session is null) return;
        var target = WorkflowDesignerInteraction.HitNode(_session, location.X, location.Y);
        if (target is null || ReferenceEquals(target, source)) return;
        var targets = _session.GetDataPortTargets(source.Node.Id, member.Name, target.Node.Id);
        void Bind(WorkflowDataPortTarget item)
        {
            try { _session.BindDataPort(source.Node.Id, member.Name, item); }
            catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
            {
                InteractionError?.Invoke(this, exception.Message);
            }
        }
        if (targets.Count == 0)
        {
            InteractionError?.Invoke(this,
                $"“{target.Node.Title}”没有可绑定“{member.DisplayName}”的参数：类型不兼容，或该节点不在“{source.Node.Title}”之后执行。");
            return;
        }
        if (targets.Count == 1)
        {
            Bind(targets[0]);
            return;
        }
        var menu = new System.Windows.Controls.ContextMenu { PlacementTarget = this, Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint };
        menu.Items.Add(new System.Windows.Controls.MenuItem { Header = $"将“{member.DisplayName}”绑定到：", IsEnabled = false });
        foreach (var item in targets)
        {
            var entry = new System.Windows.Controls.MenuItem { Header = item.DisplayName };
            entry.Click += (_, _) => Bind(item);
            menu.Items.Add(entry);
        }
        menu.IsOpen = true;
    }

    // 连线右键菜单：编辑备注、启用/停用并行分支、删除连线。
    private void ShowConnectionMenu(WorkflowConnectionModel connection)
    {
        if (_session is null) return;
        var session = _session;
        var menu = new System.Windows.Controls.ContextMenu { PlacementTarget = this, Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint };
        menu.Items.Add(new System.Windows.Controls.MenuItem { Header = WorkflowDesignerInteraction.GetConnectionLabel(session, connection), IsEnabled = false });
        void Item(string header, Action action)
        {
            var item = new System.Windows.Controls.MenuItem { Header = header };
            item.Click += (_, _) => action();
            menu.Items.Add(item);
        }
        Item("编辑备注…", () => EditConnectionNote(connection));
        if (WorkflowDesignerInteraction.CanDisableConnection(session, connection))
            Item(connection.Enabled ? "停用分支" : "启用分支", () => Guard(() => session.SetConnectionEnabled(connection, !connection.Enabled)));
        menu.Items.Add(new System.Windows.Controls.Separator());
        Item("删除连线", () => session.RemoveConnection(connection));
        menu.IsOpen = true;
    }

    // 备注只用于显示；端口的固定语义不可编辑，留空恢复默认。
    private void EditConnectionNote(WorkflowConnectionModel connection)
    {
        if (_session is null) return;
        var session = _session;
        var input = new System.Windows.Controls.TextBox { Text = connection.Note ?? "", MaxLength = 64, Margin = new Thickness(0, 6, 0, 10) };
        var ok = new System.Windows.Controls.Button { Content = "确定", IsDefault = true, Width = 76, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new System.Windows.Controls.Button { Content = "取消", IsCancel = true, Width = 76 };
        var buttons = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(ok); buttons.Children.Add(cancel);
        var body = new System.Windows.Controls.StackPanel { Margin = new Thickness(12) };
        body.Children.Add(new System.Windows.Controls.TextBlock
        {
            Text = $"固定语义：{WorkflowPorts.GetDisplayName(connection.FromPort)}（不可修改）。备注只用于显示，留空恢复默认。",
            TextWrapping = TextWrapping.Wrap
        });
        body.Children.Add(input); body.Children.Add(buttons);
        var dialog = new Window
        {
            Title = "编辑连线备注", Content = body, Width = 400, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = Window.GetWindow(this), ShowInTaskbar = false
        };
        ok.Click += (_, _) => dialog.DialogResult = true;
        dialog.Loaded += (_, _) => { input.Focus(); input.SelectAll(); };
        if (dialog.ShowDialog() == true) Guard(() => session.SetConnectionNote(connection, input.Text));
    }

    private void Guard(Action action)
    {
        try { action(); }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException) { InteractionError?.Invoke(this, exception.Message); }
    }

    /// <summary>在标题行右侧绘制执行序号徽标（异常时为红底）和中性色耗时。</summary>
    private static void DrawRuntimeInfo(
        DrawingContext context,
        WorkflowNodeHeaderLayout layout,
        WorkflowNodeVisualStyle style,
        string? order,
        string? duration)
    {
        if (!string.IsNullOrEmpty(order) && layout.OrderBounds.Width > 0)
        {
            var badge = layout.OrderBounds;
            context.DrawRoundedRectangle(Brush(style.BadgeBackground), null,
                new Rect(badge.X, badge.Y, badge.Width, badge.Height), badge.Height / 2, badge.Height / 2);
            DrawTextClipped(context, order!, badge.X, badge.Y, badge.Width, badge.Height,
                layout.DetailFontSize, FontWeights.Normal, Brush(style.BadgeText), TextAlignment.Center);
        }
        if (!string.IsNullOrEmpty(duration) && layout.DurationBounds.Width > 0)
        {
            var bounds = layout.DurationBounds;
            DrawTextClipped(context, duration!, bounds.X, bounds.Y, bounds.Width, bounds.Height,
                layout.DetailFontSize, FontWeights.Normal, Brush(WorkflowDesignerPalette.Duration), TextAlignment.Right);
        }
    }

    /// <summary>连接拖动期间绘制所有可用输入端点。</summary>
    /// <param name="context">绘图上下文或当前编辑上下文。</param>
    /// <param name="node">目标画布节点。</param>
    private void DrawConnectionInputTargets(DrawingContext context, WorkflowCanvasNode node)
    {
        if (_session is null
            || _mode != InteractionMode.Connect
            || _connectionStart?.Port.Direction != WorkflowPortDirection.Output
            || _connectionStart?.Node == node)
        {
            return;
        }
        var ports = _session.GetPorts(node.Node.Id, WorkflowPortDirection.Input);
        if (ports.Count == 0) return;
        var fill = Brush(WorkflowDesignerPalette.NodeBody);
        var border = Freeze(new Pen(Brush(WorkflowDesignerPalette.PortHighlight), 1.5));
        var radius = WorkflowDesignerGeometry.PortScreenRadius(_session.Zoom, true);
        foreach (var port in ports)
        foreach (var side in Polyfill.GetEnumValues<WorkflowPortSide>())
        {
            var point = WorkflowDesignerGeometry.GetPortScreenPoint(_session, node, port, ports, side);
            context.DrawEllipse(fill, border, new Point(point.X, point.Y), radius, radius);
        }
    }

    /// <summary>在选中节点四边绘制端口移动目标。</summary>
    /// <param name="context">绘图上下文或当前编辑上下文。</param>
    /// <param name="node">目标画布节点。</param>
    /// <param name="bounds">绘制或命中边界。</param>
    private void DrawPortSideTargets(DrawingContext context, WorkflowCanvasNode node, Rect bounds)
    {
        var active = _mode == InteractionMode.MovePort && _connectionStart?.Node == node
            ? _portDropSide
            : null;
        foreach (var side in Polyfill.GetEnumValues<WorkflowPortSide>())
        {
            if (_session is not null && WorkflowDesignerInteraction.HasConnectedEndpointAtSide(_session, node, side))
                continue;
            var anchor = WorkflowDesignerInteraction.GetSideCenter(
                new WorkflowDesignerRect(bounds.X, bounds.Y, bounds.Width, bounds.Height), side, _session?.Zoom ?? 1,
                _session is null ? default : WorkflowDesignerGeometry.GetNodeRows(_session, node));
            var center = new Point(anchor.X, anchor.Y);
            var highlighted = active == side;
            // 选中节点四边的换边把手：空闲时为节点底色的小空心圈，拖到某一边时放大并以选中色填充。
            var radius = Math.Max(3, (highlighted ? 6 : 4.5) * (_session?.Zoom ?? 1));
            var fill = Brush(highlighted ? WorkflowDesignerPalette.Selected : WorkflowDesignerPalette.NodeBody);
            var border = new Pen(Brush(highlighted ? WorkflowDesignerPalette.Title : WorkflowDesignerPalette.Port), 1.5);
            context.DrawEllipse(fill, border, center, radius, radius);
        }
    }

    /// <summary>
    /// 绘制节点指定方向的端口及名称。已连线端口为直径 6 的小圆点，未连线端口平时不画，悬停节点或拖线时以空心圈放大显示；
    /// 失败端口为红色。同方向有多个端口时名称写在对应行内，未连线也显示，便于找到出口。
    /// </summary>
    /// <param name="context">绘图上下文或当前编辑上下文。</param>
    /// <param name="node">目标画布节点。</param>
    /// <param name="direction">端口方向。</param>
    /// <param name="emphasized">是否放大显示（悬停节点或拖线中）。</param>
    private void DrawPorts(DrawingContext context, WorkflowCanvasNode node, WorkflowPortDirection direction, bool emphasized)
    {
        if (_session is null)
            return;
        var ports = _session.GetPorts(node.Node.Id, direction);
        var rect = WorkflowDesignerGeometry.GetNodeScreenRect(_session, node);
        var labeled = WorkflowDesignerInteraction.ShouldDrawPortLabel(_session, node, direction);
        foreach (var port in ports)
        {
            var side = node.GetPortSide(port);
            var point = WorkflowDesignerGeometry.GetPortScreenPoint(_session, node, port, ports);
            var failed = direction == WorkflowPortDirection.Output && port.Key == WorkflowPorts.Failed;
            if (labeled)
                DrawPortLabel(context, failed ? WorkflowDesignerPalette.FailedPortLabel : WorkflowDesignerPalette.PortLabel,
                    rect, side, point, ports.Count(item => node.GetPortSide(item) == side), WorkflowPorts.GetDisplayName(port.Key));
            var connected = WorkflowDesignerInteraction.IsPortConnectedAtSide(_session, node, port, side);
            if (connected || emphasized)
                DrawPortDot(context, point, failed ? WorkflowDesignerPalette.FailedPort : WorkflowDesignerPalette.Port, connected, emphasized);
        }
    }

    /// <summary>绘制端口圆点：已连线为实心，未连线为空心圈。</summary>
    private void DrawPortDot(DrawingContext context, WorkflowPoint point, WorkflowColor color, bool filled, bool emphasized)
    {
        var radius = WorkflowDesignerGeometry.PortScreenRadius(_session?.Zoom ?? 1, emphasized);
        context.DrawEllipse(
            Brush(filled ? color : WorkflowDesignerPalette.NodeBody),
            new Pen(Brush(filled ? WorkflowDesignerPalette.Canvas : color), filled ? 1 : 1.5),
            new Point(point.X, point.Y),
            radius,
            radius);
    }

    /// <summary>绘制框选区域。</summary>
    /// <param name="context">绘图上下文或当前编辑上下文。</param>
    private void DrawMarquee(DrawingContext context)
    {
        if (_mode != InteractionMode.Marquee)
            return;
        var rect = NormalizeRectangle(_marqueeStart, _marqueeCurrent);
        context.DrawRectangle(
            Freeze(new SolidColorBrush(Color.FromArgb(35, 56, 189, 248))),
            new Pen(Freeze(new SolidColorBrush(Color.FromRgb(56, 189, 248))), 1)
            {
                DashStyle = DashStyles.Dash
            },
            rect);
    }

    /// <summary>返回指定屏幕坐标命中的端口；从输出端口拖出连接时，其他节点输入端口的四边均可命中。</summary>
    private WorkflowPortHit? HitPort(double x, double y, WorkflowPortDirection direction) =>
        _session is null
            ? null
            : WorkflowDesignerInteraction.HitPort(_session, x, y, direction,
                _mode == InteractionMode.Connect && _connectionStart?.Port.Direction == WorkflowPortDirection.Output
                    ? _connectionStart.Value.Node
                    : null);

    /// <summary>处理 WPF Session 依赖属性变化并切换事件订阅。</summary>
    private static void OnSessionPropertyChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs eventArgs)
    {
        var control = (WorkflowDesignerControl)dependencyObject;
        if (control._session is not null)
            control._session.Changed -= control.OnDesignerSessionChanged;
        control.CancelInteraction();
        control._session = (WorkflowDesignerSession?)eventArgs.NewValue;
        if (control._session is not null)
            control._session.Changed += control.OnDesignerSessionChanged;
        control.InvalidateVisual();
    }

    /// <summary>取消尚未提交的交互并还原临时修改。</summary>
    private void CancelInteraction()
    {
        if (_dragLabelConnection is not null)
            _dragLabelConnection.LabelPosition = _dragLabelOrigin;
        _nodeDrag?.Cancel();
        _dragLabelConnection = null;
        _nodeDrag = null;
        _dragConnection = null;
        _dragWaypointIndex = -1;
        _connectionStart = null;
        _portDropSide = null;
        _mode = InteractionMode.None;
        if (IsMouseCaptured)
            ReleaseMouseCapture();
        Cursor = Cursors.Arrow;
    }

    /// <summary>处理设计会话变化并请求 WPF 画布重绘。</summary>
    private void OnDesignerSessionChanged(object? sender, WorkflowDesignerChangedEventArgs e)
    {
        if (Dispatcher.CheckAccess())
            InvalidateVisual();
        else
            _ = Dispatcher.BeginInvoke(InvalidateVisual);
    }

    /// <summary>绘制单条正交连接及其方向箭头。</summary>
    /// <param name="context">绘图上下文或当前编辑上下文。</param>
    /// <param name="pen">绘制使用的画笔。</param>
    /// <param name="connection">目标连接。</param>
    /// <param name="start">路径起点。</param>
    /// <param name="end">路径终点。</param>
    private void DrawConnectionPath(
        DrawingContext context,
        Pen pen,
        WorkflowConnectionModel connection,
        WorkflowPoint start,
        WorkflowPoint end)
    {
        if (_session is null) return;
        var points = WorkflowDesignerInteraction.GetConnectionPath(_session, connection, start, end);
        DrawOrthogonalPath(context, pen, points);
        DrawInsetArrow(context, pen.Brush, points, _session.Zoom);
    }

    /// <summary>在连接终点前绘制方向箭头。</summary>
    /// <param name="context">绘图上下文或当前编辑上下文。</param>
    /// <param name="brush">绘制使用的画刷。</param>
    /// <param name="points">路径点集合。</param>
    /// <param name="zoom">当前缩放；箭头尺寸随缩放等比。</param>
    private static void DrawInsetArrow(DrawingContext context, Brush brush, IReadOnlyList<WorkflowPoint> points, double zoom)
    {
        if (points.Count < 2) return;
        var end = points[^1];
        var previous = points[^2];
        var dx = end.X - previous.X;
        var dy = end.Y - previous.Y;
        var length = Math.Sqrt(dx * dx + dy * dy);
        var arrow = WorkflowDesignerGeometry.ConnectionArrow(zoom);
        if (length < arrow.Inset + arrow.Length) return;
        var ux = dx / length;
        var uy = dy / length;
        var tip = new Point(end.X - ux * arrow.Inset, end.Y - uy * arrow.Inset);
        var center = new Point(tip.X - ux * arrow.Length, tip.Y - uy * arrow.Length);
        var geometry = new StreamGeometry();
        using (var drawing = geometry.Open())
        {
            drawing.BeginFigure(tip, true, true);
            drawing.LineTo(new Point(center.X - uy * arrow.HalfWidth, center.Y + ux * arrow.HalfWidth), true, false);
            drawing.LineTo(new Point(center.X + uy * arrow.HalfWidth, center.Y - ux * arrow.HalfWidth), true, false);
        }
        geometry.Freeze();
        context.DrawGeometry(brush, null, geometry);
    }

    /// <summary>返回指定屏幕坐标命中的连接标签。</summary>
    /// <param name="x">屏幕横坐标。</param>
    /// <param name="y">屏幕纵坐标。</param>
    private WorkflowConnectionModel? HitConnectionLabel(double x, double y) =>
        _session?.Canvas.Connections.Reverse().FirstOrDefault(connection =>
            WorkflowDesignerInteraction.ShouldDrawConnectionLabel(_session, connection)
            && GetConnectionLabelBounds(connection).Contains(x, y));

    /// <summary>在端口所在行内绘制端口名称，区域与对齐方式见 <see cref="WorkflowDesignerGeometry.GetPortLabelBounds"/>。</summary>
    private void DrawPortLabel(
        DrawingContext context,
        WorkflowColor color,
        WorkflowDesignerRect nodeRect,
        WorkflowPortSide side,
        WorkflowPoint point,
        int sameSideCount,
        string text)
    {
        var zoom = _session?.Zoom ?? 1;
        var (bounds, alignment) = WorkflowDesignerGeometry.GetPortLabelBounds(nodeRect, zoom, side, point, sameSideCount);
        DrawTextClipped(context, text, bounds.X, bounds.Y, bounds.Width, bounds.Height,
            Math.Max(3, WorkflowDesignerGeometry.DetailFontPixels * zoom), FontWeights.Normal, Brush(color),
            alignment switch { < 0 => TextAlignment.Left, > 0 => TextAlignment.Right, _ => TextAlignment.Center });
    }

    /// <summary>绘制具有单连接边覆盖的额外端点。</summary>
    /// <param name="context">绘图上下文或当前编辑上下文。</param>
    /// <param name="node">目标画布节点。</param>
    private void DrawConnectionOverrideEndpoints(DrawingContext context, WorkflowCanvasNode node)
    {
        if (_session is null) return;
        DrawConnectionOverrideEndpoints(context, node, WorkflowPortDirection.Input);
        DrawConnectionOverrideEndpoints(context, node, WorkflowPortDirection.Output);
    }

    /// <summary>绘制具有单连接边覆盖的额外端点。</summary>
    /// <param name="context">绘图上下文或当前编辑上下文。</param>
    /// <param name="node">目标画布节点。</param>
    /// <param name="direction">端口方向。</param>
    private void DrawConnectionOverrideEndpoints(
        DrawingContext context,
        WorkflowCanvasNode node,
        WorkflowPortDirection direction)
    {
        if (_session is null) return;
        var ports = _session.GetPorts(node.Node.Id, direction);
        foreach (var (port, side) in WorkflowDesignerInteraction.GetSideOverrideEndpoints(_session, node, direction))
        {
            // 换边端点只画圆点：端口语义由线上的标签表达。
            var target = WorkflowDesignerGeometry.GetPortScreenPoint(_session, node, port, ports, side);
            var failed = direction == WorkflowPortDirection.Output && port.Key == WorkflowPorts.Failed;
            DrawPortDot(context, target, failed ? WorkflowDesignerPalette.FailedPort : WorkflowDesignerPalette.Port, true, false);
        }
    }

    /// <summary>绘制连接输出语义标签。</summary>
    /// <param name="context">绘图上下文或当前编辑上下文。</param>
    /// <param name="connection">目标连接。</param>
    /// <param name="source">源路径点集合。</param>
    private void DrawConnectionLabel(
        DrawingContext context,
        WorkflowConnectionModel connection,
        WorkflowPoint source)
    {
        if (_session is null || !WorkflowDesignerInteraction.ShouldDrawConnectionLabel(_session, connection)) return;
        var rect = GetConnectionLabelBounds(connection);
        // 线上的小胶囊：深底细描边，可沿线拖动。
        context.DrawRoundedRectangle(Brush(WorkflowDesignerPalette.LabelBackground),
            new Pen(Brush(WorkflowDesignerPalette.LabelBorder), 1), rect, rect.Height / 2, rect.Height / 2);
        DrawTextClipped(
            context,
            WorkflowDesignerInteraction.GetConnectionLabelDisplay(_session, connection),
            rect.X,
            rect.Y,
            rect.Width,
            rect.Height,
            Math.Max(3, WorkflowDesignerGeometry.DetailFontPixels * _session.Zoom),
            FontWeights.Normal,
            Brush(connection.Enabled ? WorkflowDesignerPalette.LabelText : WorkflowDesignerPalette.DisabledLabelText),
            TextAlignment.Center);
    }

    /// <summary>计算连接标签的屏幕边界。</summary>
    /// <param name="connection">目标连接。</param>
    private Rect GetConnectionLabelBounds(WorkflowConnectionModel connection)
    {
        var zoom = _session?.Zoom ?? 1;
        var center = _session is null
            ? default
            : WorkflowDesignerInteraction.PointAlongPath(
                WorkflowDesignerInteraction.GetConnectionPath(_session, connection), connection.LabelPosition);
        var size = Math.Max(3, WorkflowDesignerGeometry.DetailFontPixels * zoom);
        var padding = Math.Max(3, 6 * zoom);
        var width = MeasureTextWidth(_session is null ? "" : WorkflowDesignerInteraction.GetConnectionLabelDisplay(_session, connection), size, FontWeights.Normal) + padding * 2;
        var height = size * 1.35 + 2;
        return new Rect(center.X - width / 2, center.Y - height / 2, width, height);
    }

    /// <summary>使用平台绘图 API 绘制正交折线路径。</summary>
    /// <param name="context">绘图上下文或当前编辑上下文。</param>
    /// <param name="pen">绘制使用的画笔。</param>
    /// <param name="points">路径点集合。</param>
    private static void DrawOrthogonalPath(
        DrawingContext context,
        Pen pen,
        IReadOnlyList<WorkflowPoint> points)
    {
        if (points.Count < 2) return;
        var geometry = new StreamGeometry();
        using (var drawing = geometry.Open())
        {
            drawing.BeginFigure(new Point(points[0].X, points[0].Y), false, false);
            foreach (var point in points.Skip(1))
                drawing.LineTo(new Point(point.X, point.Y), true, false);
        }
        geometry.Freeze();
        context.DrawGeometry(null, pen, geometry);
    }

    /// <summary>绘制选中连接的手工拐点控制柄。</summary>
    /// <param name="context">绘图上下文或当前编辑上下文。</param>
    /// <param name="connection">目标连接。</param>
    private void DrawWaypointHandles(DrawingContext context, WorkflowConnectionModel connection)
    {
        if (_session is null)
            return;
        var pen = new Pen(Freeze(new SolidColorBrush(Color.FromRgb(56, 189, 248))), 2);
        foreach (var waypoint in connection.Waypoints)
        {
            var point = WorkflowDesignerGeometry.CanvasToScreen(_session, waypoint.X, waypoint.Y);
            context.DrawRectangle(CanvasBrush, pen, new Rect(point.X - 5, point.Y - 5, 10, 10));
        }
    }

    private static Rect NormalizeRectangle(Point first, Point second) => new(
        Math.Min(first.X, second.X),
        Math.Min(first.Y, second.Y),
        Math.Abs(first.X - second.X),
        Math.Abs(first.Y - second.Y));

    /// <summary>测量指定文本的显示宽度。</summary>
    /// <param name="text">要显示或处理的文本。</param>
    /// <param name="size">字体大小。</param>
    /// <param name="weight">字体粗细。</param>
    private static double MeasureTextWidth(string text, double size, FontWeight weight) =>
        CreateFormattedText(text, size, weight, Brushes.White).WidthIncludingTrailingWhitespace;

    /// <summary>在给定边界内绘制裁剪文本。</summary>
    /// <param name="context">绘图上下文或当前编辑上下文。</param>
    /// <param name="text">要显示或处理的文本。</param>
    /// <param name="x">屏幕横坐标。</param>
    /// <param name="y">屏幕纵坐标。</param>
    /// <param name="width">“width”参数。</param>
    /// <param name="height">“height”参数。</param>
    /// <param name="size">字体大小。</param>
    /// <param name="weight">字体粗细。</param>
    /// <param name="brush">绘制使用的画刷。</param>
    /// <param name="alignment">区域内文本对齐方式。</param>
    private static void DrawTextClipped(
        DrawingContext context,
        string text,
        double x,
        double y,
        double width,
        double height,
        double size,
        FontWeight weight,
        Brush brush,
        TextAlignment alignment = TextAlignment.Left)
    {
        if (width <= 0 || height <= 0) return;
        var formatted = CreateFormattedText(text, size, weight, brush);
        formatted.MaxTextWidth = width;
        formatted.MaxTextHeight = Math.Max(size, height);
        formatted.Trimming = TextTrimming.CharacterEllipsis;
        formatted.TextAlignment = alignment;
        // 单行文字在给定区域内垂直居中，与标题栏指示灯对齐。
        context.DrawText(formatted, new Point(x, y + Math.Max(0, (height - formatted.Height) / 2)));
    }

    /// <summary>按指定字体和位置绘制文本。</summary>
    /// <param name="context">绘图上下文或当前编辑上下文。</param>
    /// <param name="text">要显示或处理的文本。</param>
    /// <param name="x">屏幕横坐标。</param>
    /// <param name="y">屏幕纵坐标。</param>
    /// <param name="size">字体大小。</param>
    /// <param name="weight">字体粗细。</param>
    /// <param name="brush">绘制使用的画刷。</param>
    private static void DrawText(
        DrawingContext context,
        string text,
        double x,
        double y,
        double size,
        FontWeight weight,
        Brush brush)
    {
        context.DrawText(CreateFormattedText(text, size, weight, brush), new Point(x, y));
    }

    /// <summary>创建用于 WPF 测量和绘制的格式化文本。</summary>
    /// <param name="text">要显示或处理的文本。</param>
    /// <param name="size">字体大小。</param>
    /// <param name="weight">字体粗细。</param>
    /// <param name="brush">绘制使用的画刷。</param>
    private static FormattedText CreateFormattedText(string text, double size, FontWeight weight, Brush brush) =>
        new(
            text,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, weight, FontStretches.Normal),
            size,
            brush,
            1);

    /// <summary>创建并冻结指定 RGB 颜色的 WPF 画刷。</summary>
    private static Brush Brush(byte red, byte green, byte blue) =>
        Freeze(new SolidColorBrush(Color.FromRgb(red, green, blue)));

    /// <summary>创建并冻结共享调色板颜色的 WPF 画刷。</summary>
    internal static Brush Brush(WorkflowColor color) =>
        Freeze(new SolidColorBrush(Color.FromArgb(color.A, color.R, color.G, color.B)));

    /// <summary>冻结 WPF Freezable 对象以降低绘制开销。</summary>
    /// <param name="freezable">需要冻结的 WPF 对象。</param>
    private static T Freeze<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }

    /// <summary>表示设计画布当前正在执行的鼠标交互。</summary>
    private enum InteractionMode
    {
        /// <summary>当前没有进行鼠标交互。</summary>
        None,
        /// <summary>正在拖动一个或多个选中节点。</summary>
        MoveNode,
        /// <summary>正在平移画布视口。</summary>
        Pan,
        /// <summary>正在从输出端口拖动并创建连接。</summary>
        Connect,
        /// <summary>正在从数据端口拖线，松开时把下游参数绑定到该输出成员。</summary>
        DataLink,
        /// <summary>正在调整端口默认所在边。</summary>
        MovePort,
        /// <summary>正在移动连接的手工拐点。</summary>
        MoveWaypoint,
        /// <summary>正在调整连接标签在线路上的位置。</summary>
        MoveConnectionLabel,
        /// <summary>正在拖动矩形框选择节点。</summary>
        Marquee
    }
}
