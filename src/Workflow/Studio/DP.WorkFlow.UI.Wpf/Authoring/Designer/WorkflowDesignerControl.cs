using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using DP.WorkFlow.UI;

namespace DP.WorkFlow.UI.Wpf;

/// <summary>基于 WPF DrawingContext 的工作流画布，使用共享设计会话。</summary>
public sealed class WorkflowDesignerControl : FrameworkElement
{
    private static Brush CanvasBrush => Brush(30, 30, 30);
    private static Brush NodeBrush => Brush(37, 37, 38);
    private static Brush HeaderBrush => Brush(51, 51, 55);
    private static Brush ForegroundBrush => Brush(241, 241, 241);
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
        foreach (var node in _session.Canvas.Nodes)
            DrawNode(drawingContext, node);
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
            var contextConnection = WorkflowDesignerInteraction.HitConnection(_session, _pointer.X, _pointer.Y);
            if (contextConnection is not null)
            {
                if (ReferenceEquals(_session.SelectedConnection, contextConnection))
                    _session.RemoveConnection(contextConnection);
                else
                    _session.SelectConnection(contextConnection);
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
        else if (_mode == InteractionMode.Connect)
        {
            InvalidateVisual();
        }
        else if (_mode == InteractionMode.Marquee)
        {
            _marqueeCurrent = _pointer;
            InvalidateVisual();
        }
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
        var zoom = Math.Clamp(_session.Zoom * (e.Delta > 0 ? 1.1 : 1 / 1.1), 0.25, 2.5);
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

    /// <summary>绘制随缩放和平移变化的设计网格。</summary>
    /// <param name="context">绘图上下文或当前编辑上下文。</param>
    private void DrawGrid(DrawingContext context)
    {
        if (_session is null)
            return;
        var spacing = 24 * _session.Zoom;
        if (spacing < 8)
            return;
        var pen = new Pen(Brush(67, 67, 70), 1);
        for (var x = _session.PanX % spacing; x < ActualWidth; x += spacing)
            context.DrawLine(pen, new Point(x, 0), new Point(x, ActualHeight));
        for (var y = _session.PanY % spacing; y < ActualHeight; y += spacing)
            context.DrawLine(pen, new Point(0, y), new Point(ActualWidth, y));
    }

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
                var pen = new Pen(
                    Freeze(new SolidColorBrush(selected
                        ? Color.FromRgb(0, 120, 212)
                        : (Color.FromRgb(155, 155, 155)))),
                    selected ? 3 : 2);
                DrawConnectionPath(context, pen, connection, source.Value, target.Value);
                DrawConnectionLabel(context, connection, source.Value);
                if (selected)
                    DrawWaypointHandles(context, connection);
            }
        }
    }

    /// <summary>绘制节点背景、标题、状态、端口和运行信息。</summary>
    /// <param name="context">绘图上下文或当前编辑上下文。</param>
    /// <param name="item">目标数据项。</param>
    private void DrawNode(DrawingContext context, WorkflowCanvasNode item)
    {
        if (_session is null)
            return;
        var rect = WorkflowDesignerGeometry.GetNodeScreenRect(_session, item);
        var bounds = new Rect(rect.X, rect.Y, rect.Width, rect.Height);
        var state = _session.GetNodeState(item.Node.Id);
        var stateBrush = StateBrush(state);
        var selected = _session.SelectedNodeIds.Contains(item.Node.Id);
        context.DrawRoundedRectangle(NodeBrush, null, bounds, 8, 8);
        var headerHeight = Math.Min(rect.Height, WorkflowDesignerGeometry.HeaderHeight * _session.Zoom);
        context.PushClip(new RectangleGeometry(bounds, 8, 8));
        context.DrawRectangle(HeaderBrush, null, new Rect(rect.X, rect.Y, Math.Max(0, rect.Width), Math.Max(0, headerHeight)));
        context.Pop();
        context.DrawRoundedRectangle(
            null,
            new Pen(selected ? Freeze(new SolidColorBrush(Color.FromRgb(56, 189, 248))) : stateBrush, selected ? 3 : 2),
            bounds,
            8,
            8);
        var stateRadius = Math.Max(1, 4.5 * _session.Zoom);
        context.DrawEllipse(stateBrush, null, new Point(rect.X + 14 * _session.Zoom, rect.Y + 14 * _session.Zoom), stateRadius, stateRadius);
        var runtimeText = WorkflowDesignerInteraction.GetRuntimeDisplayText(_session, item);
        var runtimeMeasureSize = Math.Max(3, 7.5 * _session.Zoom);
        var measuredRuntimeWidth = string.IsNullOrEmpty(runtimeText)
            ? 0
            : MeasureTextWidth(runtimeText, runtimeMeasureSize, FontWeights.Normal);
        var headerLayout = WorkflowDesignerGeometry.CalculateNodeHeaderLayout(
            new WorkflowDesignerRect(bounds.X, bounds.Y, bounds.Width, bounds.Height),
            _session.Zoom,
            measuredRuntimeWidth);
        DrawTextClipped(
            context,
            item.Node.Title,
            headerLayout.TitleBounds.X,
            headerLayout.TitleBounds.Y,
            headerLayout.TitleBounds.Width,
            headerLayout.TitleBounds.Height,
            headerLayout.TitleFontSize,
            FontWeights.SemiBold,
            ForegroundBrush);
        DrawPorts(context, item, WorkflowPortDirection.Input);
        DrawPorts(context, item, WorkflowPortDirection.Output);
        DrawRuntimeInfo(context, headerLayout, runtimeText);
        DrawConnectionOverrideEndpoints(context, item);
        DrawConnectionInputTargets(context, item);
        if (selected)
            DrawPortSideTargets(context, item, bounds);
    }

    /// <summary>在节点标题区域绘制执行序号和耗时。</summary>
    /// <param name="context">绘图上下文或当前编辑上下文。</param>
    /// <param name="layout">标题和运行摘要的独立布局区域。</param>
    /// <param name="text">要显示或处理的文本。</param>
    private static void DrawRuntimeInfo(
        DrawingContext context,
        WorkflowNodeHeaderLayout layout,
        string? text)
    {
        if (string.IsNullOrEmpty(text) || layout.RuntimeBounds.Width <= 0) return;
        DrawTextClipped(
            context,
            text,
            layout.RuntimeBounds.X,
            layout.RuntimeBounds.Y,
            layout.RuntimeBounds.Width,
            layout.RuntimeBounds.Height,
            layout.RuntimeFontSize,
            FontWeights.Normal,
            Brush(200, 200, 200),
            TextAlignment.Right);
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
        var fill = Brush(167, 139, 250);
        var border = new Pen(ForegroundBrush, 1);
        foreach (var port in ports)
        foreach (var side in Enum.GetValues<WorkflowPortSide>())
        {
            var point = WorkflowDesignerGeometry.GetPortScreenPoint(_session, node, port, ports, side);
            context.DrawEllipse(fill, border, new Point(point.X, point.Y), 4, 4);
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
        foreach (var side in Enum.GetValues<WorkflowPortSide>())
        {
            if (_session is not null && WorkflowDesignerInteraction.HasConnectedEndpointAtSide(_session, node, side))
                continue;
            var center = side switch
            {
                WorkflowPortSide.Left => new Point(bounds.Left, bounds.Top + bounds.Height / 2),
                WorkflowPortSide.Top => new Point(bounds.Left + bounds.Width / 2, bounds.Top),
                WorkflowPortSide.Right => new Point(bounds.Right, bounds.Top + bounds.Height / 2),
                WorkflowPortSide.Bottom => new Point(bounds.Left + bounds.Width / 2, bounds.Bottom),
                _ => new Point()
            };
            var highlighted = active == side;
            var activeColor = _connectionStart?.Port.Direction == WorkflowPortDirection.Input
                ? Color.FromRgb(167, 139, 250)
                : Color.FromRgb(52, 211, 153);
            var radius = highlighted ? 4 : 2;
            var fill = Freeze(new SolidColorBrush(highlighted
                ? activeColor
                : Color.FromArgb(30, 15, 23, 42)));
            var border = new Pen(Freeze(new SolidColorBrush(highlighted
                ? Color.FromRgb(226, 232, 240)
                : Color.FromRgb(71, 85, 105))), highlighted ? 1.5 : 1);
            context.DrawEllipse(fill, border, center, radius, radius);
        }
    }

    /// <summary>绘制节点指定方向的可见端口及标签。</summary>
    /// <param name="context">绘图上下文或当前编辑上下文。</param>
    /// <param name="node">目标画布节点。</param>
    /// <param name="direction">端口方向。</param>
    private void DrawPorts(DrawingContext context, WorkflowCanvasNode node, WorkflowPortDirection direction)
    {
        if (_session is null)
            return;
        var ports = _session.GetPorts(node.Node.Id, direction);
        var brush = direction == WorkflowPortDirection.Input
            ? Freeze(new SolidColorBrush(Color.FromRgb(167, 139, 250)))
            : Freeze(new SolidColorBrush(Color.FromRgb(52, 211, 153)));
        for (var index = 0; index < ports.Count; index++)
        {
            var port = ports[index];
            var side = node.GetPortSide(port);
            var connected = WorkflowDesignerInteraction.IsPortConnectedAtSide(_session, node, port, side);
            var semanticHandle = !connected
                && direction == WorkflowPortDirection.Output
                && ports.Count > 1;
            if (!connected && !semanticHandle) continue;
            var point = WorkflowDesignerGeometry.GetPortScreenPoint(_session, node, port, ports);
            var radius = connected
                ? Math.Max(2, WorkflowDesignerGeometry.PortRadius * _session.Zoom * 0.72)
                : 2.5;
            context.DrawEllipse(
                semanticHandle ? Brush(71, 85, 105) : brush,
                new Pen(CanvasBrush, 1.25),
                new Point(point.X, point.Y),
                radius,
                radius);
            if ((connected || semanticHandle) && WorkflowDesignerInteraction.ShouldDrawPortLabel(_session, node, direction))
                DrawPortLabel(context, node, port, point, radius, direction);
        }
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
        DrawInsetArrow(context, pen.Brush, points);
    }

    /// <summary>在连接终点前绘制方向箭头。</summary>
    /// <param name="context">绘图上下文或当前编辑上下文。</param>
    /// <param name="brush">绘制使用的画刷。</param>
    /// <param name="points">路径点集合。</param>
    private static void DrawInsetArrow(DrawingContext context, Brush brush, IReadOnlyList<WorkflowPoint> points)
    {
        if (points.Count < 2) return;
        var end = points[^1];
        var previous = points[^2];
        var dx = end.X - previous.X;
        var dy = end.Y - previous.Y;
        var length = Math.Sqrt(dx * dx + dy * dy);
        if (length < 12) return;
        var ux = dx / length;
        var uy = dy / length;
        var tip = new Point(end.X - ux * 7, end.Y - uy * 7);
        var center = new Point(tip.X - ux * 8, tip.Y - uy * 8);
        var geometry = new StreamGeometry();
        using (var drawing = geometry.Open())
        {
            drawing.BeginFigure(tip, true, true);
            drawing.LineTo(new Point(center.X - uy * 4, center.Y + ux * 4), true, false);
            drawing.LineTo(new Point(center.X + uy * 4, center.Y - ux * 4), true, false);
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

    /// <summary>在端口内侧绘制端口键文本。</summary>
    /// <param name="context">绘图上下文或当前编辑上下文。</param>
    /// <param name="node">目标画布节点。</param>
    /// <param name="port">“port”参数。</param>
    /// <param name="point">目标坐标。</param>
    /// <param name="radius">端口或圆角半径。</param>
    /// <param name="direction">端口方向。</param>
    /// <param name="sideOverride">可选的连接端点边覆盖。</param>
    private void DrawPortLabel(
        DrawingContext context,
        WorkflowCanvasNode node,
        WorkflowPortDescriptor port,
        WorkflowPoint point,
        double radius,
        WorkflowPortDirection direction,
        WorkflowPortSide? sideOverride = null)
    {
        if (_session is null) return;
        var size = Math.Max(3, 9 * _session.Zoom);
        var brush = direction == WorkflowPortDirection.Input
            ? Brush(196, 181, 253)
            : Brush(110, 231, 183);
        var estimatedWidth = port.Key.Length * size * 0.58;
        var side = sideOverride ?? node.GetPortSide(port);
        var x = side switch
        {
            WorkflowPortSide.Left => point.X + radius + 4,
            WorkflowPortSide.Right => point.X - radius - estimatedWidth - 4,
            _ => point.X - estimatedWidth / 2
        };
        var nodeRect = WorkflowDesignerGeometry.GetNodeScreenRect(_session, node);
        var bodyTop = nodeRect.Y + WorkflowDesignerGeometry.HeaderHeight * _session.Zoom + 2;
        var y = side switch
        {
            WorkflowPortSide.Top => bodyTop,
            WorkflowPortSide.Bottom => point.Y - radius - size - 4,
            _ => point.Y - size / 2 - 2
        };
        DrawText(context, port.Key, x, y, size, FontWeights.Normal, brush);
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
            var target = WorkflowDesignerGeometry.GetPortScreenPoint(_session, node, port, ports, side);
            var radius = Math.Max(2, WorkflowDesignerGeometry.PortRadius * _session.Zoom * 0.72);
            var brush = direction == WorkflowPortDirection.Input ? Brush(167, 139, 250) : Brush(52, 211, 153);
            context.DrawEllipse(brush, new Pen(CanvasBrush, 1.25), new Point(target.X, target.Y), radius, radius);
            if (WorkflowDesignerInteraction.ShouldDrawPortLabel(_session, node, direction))
                DrawPortLabel(context, node, port, target, radius, direction, side);
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
        var background = Brush(37, 37, 38);
        var border = Brush(63, 63, 70);
        var foreground = Brush(0, 120, 212);
        context.DrawRectangle(background, new Pen(border, 1), rect);
        DrawText(
            context,
            connection.FromPort,
            rect.X + 4,
            rect.Y + 1,
            Math.Max(3, 10 * _session.Zoom),
            FontWeights.SemiBold,
            foreground);
    }

    /// <summary>计算连接标签的屏幕边界。</summary>
    /// <param name="connection">目标连接。</param>
    private Rect GetConnectionLabelBounds(WorkflowConnectionModel connection)
    {
        var center = _session is null
            ? default
            : WorkflowDesignerInteraction.PointAlongPath(
                WorkflowDesignerInteraction.GetConnectionPath(_session, connection), connection.LabelPosition);
        var size = Math.Max(3, 10 * (_session?.Zoom ?? 1));
        var width = MeasureTextWidth(connection.FromPort, size, FontWeights.SemiBold) + 8;
        return new Rect(center.X - width / 2, center.Y - (size + 6) / 2, width, size + 6);
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
        context.DrawText(formatted, new Point(x, y));
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

    /// <summary>取得节点运行状态对应的 WPF 画刷。</summary>
    /// <param name="state">节点运行状态。</param>
    private static Brush StateBrush(E_NodeState state) => state switch
    {
        E_NodeState.Running => Freeze(new SolidColorBrush(Color.FromRgb(56, 189, 248))),
        E_NodeState.Completed => Freeze(new SolidColorBrush(Color.FromRgb(52, 211, 153))),
        E_NodeState.Failed => Freeze(new SolidColorBrush(Color.FromRgb(248, 113, 113))),
        E_NodeState.Canceled => Freeze(new SolidColorBrush(Color.FromRgb(251, 191, 36))),
        _ => Freeze(new SolidColorBrush(Color.FromRgb(100, 116, 139)))
    };

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
