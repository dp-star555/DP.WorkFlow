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
    private WorkflowCanvasNode? _dragNode;
    private WorkflowConnectionModel? _dragConnection;
    private WorkflowConnectionModel? _dragLabelConnection;
    private double _dragLabelOrigin;
    private int _dragWaypointIndex = -1;
    private WorkflowPoint _dragWaypointOrigin;
    private WorkflowPoint _dragStartCanvas;
    private readonly Dictionary<string, WorkflowPoint> _dragNodeOrigins = new(StringComparer.Ordinal);
    private readonly Dictionary<WorkflowConnectionModel, WorkflowPoint[]> _dragConnectionWaypointOrigins = new();
    private Point _marqueeStart;
    private Point _marqueeCurrent;
    private bool _marqueeAdditive;
    private Point _panStart;
    private WorkflowPoint _panOrigin;
    private (WorkflowCanvasNode Node, WorkflowPortDescriptor Port, WorkflowPoint Point, WorkflowPortSide Side)? _connectionStart;
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
                BuildOrthogonalPath(
                    _connectionStart.Value.Point,
                    new WorkflowPoint(_pointer.X, _pointer.Y),
                    Array.Empty<WorkflowPoint>()));
        }
        DrawMarquee(drawingContext);
    }

    /// <summary>验证拖入数据是否为工作流工具箱项目。</summary>
    /// <param name="sender">事件发送者。</param>
    /// <param name="e">事件参数。</param>
    private static void OnToolboxDrag(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(typeof(WorkflowToolboxItem))
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    /// <summary>处理工具箱项目放置，在空白处或连接线上创建节点。</summary>
    /// <param name="sender">事件发送者。</param>
    /// <param name="e">事件参数。</param>
    private void OnToolboxDrop(object sender, DragEventArgs e)
    {
        if (_session is null || e.Data.GetData(typeof(WorkflowToolboxItem)) is not WorkflowToolboxItem item)
            return;
        var point = e.GetPosition(this);
        var canvas = WorkflowDesignerGeometry.ScreenToCanvas(_session, point.X, point.Y);
        var insertion = HitConnectionInsertion(point.X, point.Y, 14);
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
    /// <param name="e">事件参数。</param>
    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        if (_session is null)
            return;
        _pointer = e.GetPosition(this);
        if (e.ChangedButton == MouseButton.Right)
        {
            var contextConnection = HitConnection(_pointer.X, _pointer.Y);
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
        var waypoint = HitWaypoint(_pointer.X, _pointer.Y);
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
        var portHit = HitSingleOutputSideTarget(_pointer.X, _pointer.Y)
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
        var node = HitNode(_pointer.X, _pointer.Y);
        if (node is null)
        {
            var connection = HitConnection(_pointer.X, _pointer.Y);
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
            _dragNode = node;
            _dragStartCanvas = WorkflowDesignerGeometry.ScreenToCanvas(_session, _pointer.X, _pointer.Y);
            _dragNodeOrigins.Clear();
            foreach (var selected in _session.Canvas.Nodes.Where(item => _session.SelectedNodeIds.Contains(item.Node.Id)))
                _dragNodeOrigins[selected.Node.Id] = new WorkflowPoint(selected.X, selected.Y);
            _dragConnectionWaypointOrigins.Clear();
            foreach (var connection in _session.Canvas.Connections.Where(connection =>
                         (_session.SelectedNodeIds.Contains(connection.FromNodeId)
                          || _session.SelectedNodeIds.Contains(connection.ToNodeId))
                         && connection.Waypoints.Count > 0))
            {
                _dragConnectionWaypointOrigins[connection] = connection.Waypoints.ToArray();
            }
            _mode = InteractionMode.MoveNode;
            CaptureMouse();
        }
    }

    /// <summary>处理指针移动并实时更新当前交互状态。</summary>
    /// <param name="e">事件参数。</param>
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
        else if (_mode == InteractionMode.MoveNode && _dragNode is not null)
        {
            var current = WorkflowDesignerGeometry.ScreenToCanvas(_session, _pointer.X, _pointer.Y);
            var deltaX = current.X - _dragStartCanvas.X;
            var deltaY = current.Y - _dragStartCanvas.Y;
            foreach (var selected in _session.Canvas.Nodes.Where(item => _dragNodeOrigins.ContainsKey(item.Node.Id)))
            {
                var origin = _dragNodeOrigins[selected.Node.Id];
                var snapped = _session.SnapNodePosition(selected, new WorkflowPoint(origin.X + deltaX, origin.Y + deltaY));
                selected.X = snapped.X;
                selected.Y = snapped.Y;
            }
            foreach (var pair in _dragConnectionWaypointOrigins)
            {
                var movesBothEnds = _dragNodeOrigins.ContainsKey(pair.Key.FromNodeId)
                    && _dragNodeOrigins.ContainsKey(pair.Key.ToNodeId);
                SetLiveWaypoints(pair.Key, movesBothEnds
                    ? pair.Value.Select(point => new WorkflowPoint(point.X + deltaX, point.Y + deltaY))
                    : Array.Empty<WorkflowPoint>());
            }
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
            _dragLabelConnection.LabelPosition = ProjectPathPosition(
                GetConnectionPathForHit(_dragLabelConnection), _pointer.X, _pointer.Y);
            InvalidateVisual();
        }
        else if (_mode == InteractionMode.MovePort && _connectionStart.HasValue)
        {
            var rect = WorkflowDesignerGeometry.GetNodeScreenRect(_session, _connectionStart.Value.Node);
            _portDropSide = NearestSide(rect, _pointer.X, _pointer.Y);
            if (_connectionStart.Value.Port.Direction == WorkflowPortDirection.Output
                && !IsNearNode(rect, _pointer.X, _pointer.Y, 42))
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
    /// <param name="e">事件参数。</param>
    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);
        if (_session is null)
            return;
        var position = e.GetPosition(this);
        if (_mode == InteractionMode.MoveNode && _dragNode is not null)
        {
            var positions = _dragNodeOrigins.Keys.ToDictionary(
                id => id,
                id =>
                {
                    var selected = _session.Canvas.Nodes.First(item => item.Node.Id == id);
                    return new WorkflowPoint(selected.X, selected.Y);
                },
                StringComparer.Ordinal);
            foreach (var pair in _dragNodeOrigins)
            {
                var selected = _session.Canvas.Nodes.First(item => item.Node.Id == pair.Key);
                selected.X = pair.Value.X;
                selected.Y = pair.Value.Y;
            }
            foreach (var pair in _dragConnectionWaypointOrigins)
                SetLiveWaypoints(pair.Key, pair.Value);
            _session.MoveNodes(positions);
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
            var selectedIds = _session.Canvas.Nodes
                .Where(node => Intersects(WorkflowDesignerGeometry.GetNodeScreenRect(_session, node), marquee))
                .Select(node => node.Node.Id)
                .ToArray();
            _session.SelectNodes(selectedIds, _marqueeAdditive);
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
        _dragNode = null;
        _dragNodeOrigins.Clear();
        _dragConnectionWaypointOrigins.Clear();
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
    /// <param name="e">事件参数。</param>
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
    /// <param name="e">事件参数。</param>
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            var position = e.GetPosition(this);
            var node = HitNode(position.X, position.Y);
            if (node is not null)
            {
                NodeEditRequested?.Invoke(this, node.Node);
                e.Handled = true;
                return;
            }
            if (_session is not null && HitConnection(position.X, position.Y) is { } connection)
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
    /// <param name="e">事件参数。</param>
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
            var source = FindPortPoint(connection.FromNodeId, connection.FromPort, WorkflowPortDirection.Output, connection.FromSide);
            var target = FindPortPoint(connection.ToNodeId, connection.ToPort, WorkflowPortDirection.Input, connection.ToSide);
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
        var runtimeText = GetRuntimeDisplayText(item);
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

    /// <summary>生成节点执行序号和耗时显示文本。</summary>
    /// <param name="node">目标画布节点。</param>
    /// <returns>返回处理结果。</returns>
    private string? GetRuntimeDisplayText(WorkflowCanvasNode node)
    {
        var info = _session?.GetNodeRuntimeInfo(node.Node.Id);
        if (info is not { ExecutionCount: > 0 }) return null;
        var elapsed = info.State == E_NodeState.Running && info.StartedAt.HasValue
            ? DateTimeOffset.UtcNow - info.StartedAt.Value
            : info.Elapsed;
        return $"#{info.ExecutionSequence}  {FormatElapsed(elapsed)}";
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
            var hasConnectedEndpoint = _session is not null
                && _session.GetPorts(node.Node.Id, WorkflowPortDirection.Input)
                    .Concat(_session.GetPorts(node.Node.Id, WorkflowPortDirection.Output))
                    .Any(port => IsPortConnectedAtSide(node, port, side));
            if (hasConnectedEndpoint) continue;
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

    /// <summary>将运行耗时格式化为合适精度的文本。</summary>
    /// <param name="elapsed">“elapsed”参数。</param>
    /// <returns>返回处理结果。</returns>
    private static string FormatElapsed(TimeSpan elapsed) => elapsed.TotalSeconds >= 1
        ? $"{elapsed.TotalSeconds:F2}s"
        : elapsed.TotalMilliseconds >= 10
            ? $"{elapsed.TotalMilliseconds:F0}ms"
            : elapsed.TotalMilliseconds >= 1
                ? $"{elapsed.TotalMilliseconds:F1}ms"
                : $"{elapsed.TotalMilliseconds:F3}ms";

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
            var connected = IsPortConnectedAtSide(node, port, side);
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
            if ((connected || semanticHandle) && ShouldDrawPortLabel(node, direction))
                DrawPortLabel(context, node, port, point, radius, direction);
        }
    }

    /// <summary>判断节点指定方向是否需要显示端口标签。</summary>
    /// <param name="node">目标画布节点。</param>
    /// <param name="direction">端口方向。</param>
    /// <returns>返回处理结果。</returns>
    private bool ShouldDrawPortLabel(WorkflowCanvasNode node, WorkflowPortDirection direction) =>
        _session?.GetPorts(node.Node.Id, direction).Count > 1;

    /// <summary>判断端口在指定边是否存在连接。</summary>
    /// <param name="node">目标画布节点。</param>
    /// <param name="port">“port”参数。</param>
    /// <param name="side">端口所在边。</param>
    /// <returns>返回处理结果。</returns>
    private bool IsPortConnectedAtSide(
        WorkflowCanvasNode node,
        WorkflowPortDescriptor port,
        WorkflowPortSide side) =>
        _session?.Canvas.Connections.Any(connection => port.Direction == WorkflowPortDirection.Output
            ? connection.FromNodeId == node.Node.Id && connection.FromPort == port.Key
              && (connection.FromSide ?? node.GetPortSide(port)) == side
            : connection.ToNodeId == node.Node.Id && connection.ToPort == port.Key
              && (connection.ToSide ?? node.GetPortSide(port)) == side) == true;

    /// <summary>命中只有一个输出端口节点的四边快捷连接目标。</summary>
    /// <param name="x">屏幕横坐标。</param>
    /// <param name="y">屏幕纵坐标。</param>
    /// <returns>返回处理结果。</returns>
    private (WorkflowCanvasNode Node, WorkflowPortDescriptor Port, WorkflowPoint Point, WorkflowPortSide Side)?
        HitSingleOutputSideTarget(double x, double y)
    {
        if (_session is null || _session.SelectedNodeIds.Count != 1) return null;
        var node = _session.Canvas.Nodes.FirstOrDefault(item => _session.SelectedNodeIds.Contains(item.Node.Id));
        if (node is null) return null;
        var outputs = _session.GetPorts(node.Node.Id, WorkflowPortDirection.Output);
        if (outputs.Count != 1) return null;
        var bounds = WorkflowDesignerGeometry.GetNodeScreenRect(_session, node);
        foreach (var side in Enum.GetValues<WorkflowPortSide>())
        {
            var point = side switch
            {
                WorkflowPortSide.Left => new WorkflowPoint(bounds.X, bounds.Y + bounds.Height / 2),
                WorkflowPortSide.Top => new WorkflowPoint(bounds.X + bounds.Width / 2, bounds.Y),
                WorkflowPortSide.Right => new WorkflowPoint(bounds.X + bounds.Width, bounds.Y + bounds.Height / 2),
                WorkflowPortSide.Bottom => new WorkflowPoint(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height),
                _ => default
            };
            if (Math.Pow(point.X - x, 2) + Math.Pow(point.Y - y, 2) <= 36)
                return (node, outputs[0], point, side);
        }
        return null;
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

    /// <summary>返回指定屏幕坐标命中的最上层节点。</summary>
    /// <param name="x">屏幕横坐标。</param>
    /// <param name="y">屏幕纵坐标。</param>
    /// <returns>返回处理结果。</returns>
    private WorkflowCanvasNode? HitNode(double x, double y) => _session?.Canvas.Nodes
        .Reverse()
        .FirstOrDefault(node => WorkflowDesignerGeometry.GetNodeScreenRect(_session, node).Contains(x, y));

    /// <summary>返回指定屏幕坐标命中的端口及其实际边。</summary>
    /// <param name="x">屏幕横坐标。</param>
    /// <param name="y">屏幕纵坐标。</param>
    /// <param name="direction">端口方向。</param>
    /// <returns>返回处理结果。</returns>
    private (WorkflowCanvasNode Node, WorkflowPortDescriptor Port, WorkflowPoint Point, WorkflowPortSide Side)? HitPort(
        double x,
        double y,
        WorkflowPortDirection direction)
    {
        if (_session is null)
            return null;
        foreach (var node in _session.Canvas.Nodes.Reverse())
        {
            var ports = _session.GetPorts(node.Node.Id, direction);
            for (var index = 0; index < ports.Count; index++)
            {
                var port = ports[index];
                var side = node.GetPortSide(port);
                var point = WorkflowDesignerGeometry.GetPortScreenPoint(_session, node, port, ports);
                if (Math.Sqrt(Math.Pow(point.X - x, 2) + Math.Pow(point.Y - y, 2)) <= 10)
                    return (node, port, point, side);
                if (direction == WorkflowPortDirection.Input
                    && _mode == InteractionMode.Connect
                    && _connectionStart?.Port.Direction == WorkflowPortDirection.Output
                    && _connectionStart?.Node != node)
                {
                    foreach (var candidateSide in Enum.GetValues<WorkflowPortSide>())
                    {
                        var candidate = WorkflowDesignerGeometry.GetPortScreenPoint(_session, node, port, ports, candidateSide);
                        if (Math.Sqrt(Math.Pow(candidate.X - x, 2) + Math.Pow(candidate.Y - y, 2)) <= 10)
                            return (node, port, candidate, candidateSide);
                    }
                }
            }
        }
        return null;
    }

    /// <summary>查找拖放位置附近可插入节点的连接线段。</summary>
    /// <param name="x">屏幕横坐标。</param>
    /// <param name="y">屏幕纵坐标。</param>
    /// <param name="tolerance">命中测试允许的像素距离。</param>
    /// <returns>返回处理结果。</returns>
    private (WorkflowConnectionModel Connection, WorkflowPortSide InputSide, WorkflowPortSide OutputSide)?
        HitConnectionInsertion(double x, double y, double tolerance)
    {
        if (_session is null) return null;
        foreach (var connection in _session.Canvas.Connections.Reverse())
        {
            var start = FindPortPoint(connection.FromNodeId, connection.FromPort, WorkflowPortDirection.Output, connection.FromSide);
            var end = FindPortPoint(connection.ToNodeId, connection.ToPort, WorkflowPortDirection.Input, connection.ToSide);
            if (!start.HasValue || !end.HasValue) continue;
            var points = GetConnectionPath(connection, start.Value, end.Value);
            for (var index = 0; index < points.Count - 1; index++)
            {
                var first = points[index];
                var second = points[index + 1];
                if (DistanceToSegment(x, y, first, second) > tolerance) continue;
                if (Math.Abs(second.X - first.X) >= Math.Abs(second.Y - first.Y))
                    return second.X >= first.X
                        ? (connection, WorkflowPortSide.Left, WorkflowPortSide.Right)
                        : (connection, WorkflowPortSide.Right, WorkflowPortSide.Left);
                return second.Y >= first.Y
                    ? (connection, WorkflowPortSide.Top, WorkflowPortSide.Bottom)
                    : (connection, WorkflowPortSide.Bottom, WorkflowPortSide.Top);
            }
        }
        return null;
    }

    /// <summary>返回指定屏幕坐标附近的最上层连接。</summary>
    /// <param name="x">屏幕横坐标。</param>
    /// <param name="y">屏幕纵坐标。</param>
    /// <param name="tolerance">命中测试允许的像素距离。</param>
    /// <returns>返回处理结果。</returns>
    private WorkflowConnectionModel? HitConnection(double x, double y, double tolerance = 7)
    {
        if (_session is null)
            return null;
        foreach (var connection in _session.Canvas.Connections.Reverse())
        {
            var start = FindPortPoint(connection.FromNodeId, connection.FromPort, WorkflowPortDirection.Output, connection.FromSide);
            var end = FindPortPoint(connection.ToNodeId, connection.ToPort, WorkflowPortDirection.Input, connection.ToSide);
            if (!start.HasValue || !end.HasValue)
                continue;
            var points = GetConnectionPath(connection, start.Value, end.Value);
            if (points.Zip(points.Skip(1), (first, second) => DistanceToSegment(x, y, first, second))
                .Any(distance => distance <= tolerance))
                return connection;
        }
        return null;
    }

    /// <summary>返回指定屏幕坐标命中的连接拐点。</summary>
    /// <param name="x">屏幕横坐标。</param>
    /// <param name="y">屏幕纵坐标。</param>
    /// <returns>返回处理结果。</returns>
    private (WorkflowConnectionModel Connection, int Index)? HitWaypoint(double x, double y)
    {
        if (_session?.SelectedConnection is not { } connection)
            return null;
        for (var index = 0; index < connection.Waypoints.Count; index++)
        {
            var point = WorkflowDesignerGeometry.CanvasToScreen(
                _session, connection.Waypoints[index].X, connection.Waypoints[index].Y);
            if (Math.Abs(point.X - x) <= 9 && Math.Abs(point.Y - y) <= 9)
                return (connection, index);
        }
        return null;
    }

    /// <summary>查找连接端口当前使用的节点边。</summary>
    /// <param name="nodeId">节点标识。</param>
    /// <param name="portKey">端口键。</param>
    /// <param name="direction">端口方向。</param>
    /// <returns>返回处理结果。</returns>
    private WorkflowPortSide FindPortSide(
        string nodeId,
        string portKey,
        WorkflowPortDirection direction)
    {
        if (_session is null) return direction == WorkflowPortDirection.Input
            ? WorkflowPortSide.Left
            : WorkflowPortSide.Right;
        var node = _session.Canvas.Nodes.FirstOrDefault(item => item.Node.Id == nodeId);
        if (node is null)
            return direction == WorkflowPortDirection.Input ? WorkflowPortSide.Left : WorkflowPortSide.Right;
        var port = _session.GetPorts(nodeId, direction).FirstOrDefault(item => item.Key == portKey);
        return port is null ? (direction == WorkflowPortDirection.Input ? WorkflowPortSide.Left : WorkflowPortSide.Right) : node.GetPortSide(port);
    }

    /// <summary>计算指定节点端口的屏幕坐标。</summary>
    /// <param name="nodeId">节点标识。</param>
    /// <param name="portKey">端口键。</param>
    /// <param name="direction">端口方向。</param>
    /// <param name="sideOverride">可选的连接端点边覆盖。</param>
    /// <returns>返回处理结果。</returns>
    private WorkflowPoint? FindPortPoint(
        string nodeId,
        string portKey,
        WorkflowPortDirection direction,
        WorkflowPortSide? sideOverride = null)
    {
        if (_session is null)
            return null;
        var node = _session.Canvas.Nodes.FirstOrDefault(item => item.Node.Id == nodeId);
        if (node is null)
            return null;
        var ports = _session.GetPorts(nodeId, direction);
        var port = ports.FirstOrDefault(item => item.Key == portKey);
        return port is null ? null : WorkflowDesignerGeometry.GetPortScreenPoint(_session, node, port, ports, sideOverride);
    }

    /// <summary>处理 WPF Session 依赖属性变化并切换事件订阅。</summary>
    /// <param name="dependencyObject">“dependencyObject”参数。</param>
    /// <param name="eventArgs">“eventArgs”参数。</param>
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
        if (_session is not null)
        {
            foreach (var pair in _dragNodeOrigins)
            {
                var node = _session.Canvas.Nodes.FirstOrDefault(item => item.Node.Id == pair.Key);
                if (node is not null) { node.X = pair.Value.X; node.Y = pair.Value.Y; }
            }
            foreach (var pair in _dragConnectionWaypointOrigins)
                SetLiveWaypoints(pair.Key, pair.Value);
        }
        _dragLabelConnection = null;
        _dragNode = null;
        _dragNodeOrigins.Clear();
        _dragConnectionWaypointOrigins.Clear();
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
    /// <param name="sender">事件发送者。</param>
    /// <param name="e">事件参数。</param>
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
        var points = GetConnectionPath(connection, start, end);
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

    /// <summary>取得用于命中测试的连接屏幕路径。</summary>
    /// <param name="connection">目标连接。</param>
    /// <returns>返回处理结果。</returns>
    private IReadOnlyList<WorkflowPoint> GetConnectionPathForHit(WorkflowConnectionModel connection)
    {
        var start = FindPortPoint(connection.FromNodeId, connection.FromPort, WorkflowPortDirection.Output, connection.FromSide);
        var end = FindPortPoint(connection.ToNodeId, connection.ToPort, WorkflowPortDirection.Input, connection.ToSide);
        return start.HasValue && end.HasValue
            ? GetConnectionPath(connection, start.Value, end.Value)
            : Array.Empty<WorkflowPoint>();
    }

    /// <summary>返回指定屏幕坐标命中的连接标签。</summary>
    /// <param name="x">屏幕横坐标。</param>
    /// <param name="y">屏幕纵坐标。</param>
    /// <returns>返回处理结果。</returns>
    private WorkflowConnectionModel? HitConnectionLabel(double x, double y) =>
        _session?.Canvas.Connections.Reverse().FirstOrDefault(connection =>
            ShouldDrawConnectionLabel(connection) && GetConnectionLabelBounds(connection).Contains(x, y));

    /// <summary>计算连接路径指定相对位置处的坐标。</summary>
    /// <param name="points">路径点集合。</param>
    /// <param name="position">“position”参数。</param>
    /// <returns>返回处理结果。</returns>
    private static WorkflowPoint PointAlongPath(IReadOnlyList<WorkflowPoint> points, double position)
    {
        if (points.Count == 0) return default;
        if (points.Count == 1) return points[0];
        var lengths = points.Zip(points.Skip(1), (first, second) => PathDistance(first, second)).ToArray();
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

    /// <summary>将屏幕坐标投影为连接路径上的相对位置。</summary>
    /// <param name="points">路径点集合。</param>
    /// <param name="x">屏幕横坐标。</param>
    /// <param name="y">屏幕纵坐标。</param>
    /// <returns>返回处理结果。</returns>
    private static double ProjectPathPosition(IReadOnlyList<WorkflowPoint> points, double x, double y)
    {
        if (points.Count < 2) return 0.5;
        var lengths = points.Zip(points.Skip(1), (first, second) => PathDistance(first, second)).ToArray();
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

    /// <summary>计算两个路径点之间的欧氏距离。</summary>
    /// <param name="first">第一个坐标或矩形。</param>
    /// <param name="second">第二个坐标或矩形。</param>
    /// <returns>返回处理结果。</returns>
    private static double PathDistance(WorkflowPoint first, WorkflowPoint second) =>
        Math.Sqrt(Math.Pow(second.X - first.X, 2) + Math.Pow(second.Y - first.Y, 2));

    /// <summary>计算连接经过视口转换和障碍物路由后的屏幕路径。</summary>
    /// <param name="connection">目标连接。</param>
    /// <param name="start">路径起点。</param>
    /// <param name="end">路径终点。</param>
    /// <returns>返回处理结果。</returns>
    private IReadOnlyList<WorkflowPoint> GetConnectionPath(
        WorkflowConnectionModel connection,
        WorkflowPoint start,
        WorkflowPoint end)
    {
        if (_session is null) return Array.Empty<WorkflowPoint>();
        var waypoints = connection.Waypoints
            .Select(point => WorkflowDesignerGeometry.CanvasToScreen(_session, point.X, point.Y))
            .ToArray();
        const double searchMargin = 160;
        var search = new WorkflowDesignerRect(
            Math.Min(start.X, end.X) - searchMargin,
            Math.Min(start.Y, end.Y) - searchMargin,
            Math.Abs(end.X - start.X) + searchMargin * 2,
            Math.Abs(end.Y - start.Y) + searchMargin * 2);
        var obstacles = _session.Canvas.Nodes
            .Where(node => node.Node.Id != connection.FromNodeId && node.Node.Id != connection.ToNodeId)
            .Select(node =>
            {
                var rect = WorkflowDesignerGeometry.GetNodeScreenRect(_session, node);
                return new WorkflowDesignerRect(rect.X - 14, rect.Y - 14, rect.Width + 28, rect.Height + 28);
            })
            .Where(rect => rect.X <= search.X + search.Width && rect.X + rect.Width >= search.X
                && rect.Y <= search.Y + search.Height && rect.Y + rect.Height >= search.Y)
            .ToArray();
        return WorkflowOrthogonalRouter.Route(
            start,
            end,
            connection.FromSide ?? FindPortSide(connection.FromNodeId, connection.FromPort, WorkflowPortDirection.Output),
            connection.ToSide ?? FindPortSide(connection.ToNodeId, connection.ToPort, WorkflowPortDirection.Input),
            waypoints,
            obstacles);
    }

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
        var endpoints = _session.Canvas.Connections
            .Select(connection => direction == WorkflowPortDirection.Input
                ? (NodeId: connection.ToNodeId, PortKey: connection.ToPort, Side: connection.ToSide)
                : (NodeId: connection.FromNodeId, PortKey: connection.FromPort, Side: connection.FromSide))
            .Where(item => item.NodeId == node.Node.Id && item.Side.HasValue)
            .Distinct()
            .ToArray();
        foreach (var endpoint in endpoints)
        {
            var port = ports.FirstOrDefault(item => item.Key == endpoint.PortKey);
            if (port is null || node.GetPortSide(port) == endpoint.Side!.Value) continue;
            var target = WorkflowDesignerGeometry.GetPortScreenPoint(
                _session, node, port, ports, endpoint.Side.Value);
            var radius = Math.Max(2, WorkflowDesignerGeometry.PortRadius * _session.Zoom * 0.72);
            var brush = direction == WorkflowPortDirection.Input ? Brush(167, 139, 250) : Brush(52, 211, 153);
            context.DrawEllipse(brush, new Pen(CanvasBrush, 1.25), new Point(target.X, target.Y), radius, radius);
            if (ShouldDrawPortLabel(node, direction))
                DrawPortLabel(context, node, port, target, radius, direction, endpoint.Side);
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
        if (_session is null || !ShouldDrawConnectionLabel(connection)) return;
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

    /// <summary>判断连接是否需要显示输出语义标签。</summary>
    /// <param name="connection">目标连接。</param>
    /// <returns>返回处理结果。</returns>
    private bool ShouldDrawConnectionLabel(WorkflowConnectionModel connection) =>
        _session is not null
        && (_session.GetPorts(connection.FromNodeId, WorkflowPortDirection.Output).Count > 1
            || connection.FromPort != WorkflowPorts.Success);

    /// <summary>计算连接标签的屏幕边界。</summary>
    /// <param name="connection">目标连接。</param>
    /// <returns>返回处理结果。</returns>
    private Rect GetConnectionLabelBounds(WorkflowConnectionModel connection)
    {
        var center = PointAlongPath(GetConnectionPathForHit(connection), connection.LabelPosition);
        var size = Math.Max(3, 10 * (_session?.Zoom ?? 1));
        var width = MeasureTextWidth(connection.FromPort, size, FontWeights.SemiBold) + 8;
        return new Rect(center.X - width / 2, center.Y - (size + 6) / 2, width, size + 6);
    }

    /// <summary>构建包含可选手工拐点的基础正交折线路径。</summary>
    /// <param name="start">路径起点。</param>
    /// <param name="end">路径终点。</param>
    /// <param name="waypoints">手工连接拐点集合。</param>
    /// <param name="startSide">起点端口所在边。</param>
    /// <param name="endSide">终点端口所在边。</param>
    /// <returns>返回处理结果。</returns>
    private static IReadOnlyList<WorkflowPoint> BuildOrthogonalPath(
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

    /// <summary>沿指定端口边的外法线偏移坐标。</summary>
    /// <param name="point">目标坐标。</param>
    /// <param name="side">端口所在边。</param>
    /// <param name="distance">偏移距离。</param>
    /// <returns>返回处理结果。</returns>
    private static WorkflowPoint Offset(WorkflowPoint point, WorkflowPortSide side, double distance) => side switch
    {
        WorkflowPortSide.Left => new WorkflowPoint(point.X - distance, point.Y),
        WorkflowPortSide.Top => new WorkflowPoint(point.X, point.Y - distance),
        WorkflowPortSide.Right => new WorkflowPoint(point.X + distance, point.Y),
        WorkflowPortSide.Bottom => new WorkflowPoint(point.X, point.Y + distance),
        _ => point
    };

    /// <summary>直接替换连接拐点，用于拖动期间的实时预览。</summary>
    /// <param name="connection">目标连接。</param>
    /// <param name="points">路径点集合。</param>
    private static void SetLiveWaypoints(WorkflowConnectionModel connection, IEnumerable<WorkflowPoint> points)
    {
        connection.Waypoints.Clear();
        foreach (var point in points)
            connection.Waypoints.Add(point);
    }

    /// <summary>仅在坐标不同于最后一点时追加路径点。</summary>
    /// <param name="points">路径点集合。</param>
    /// <param name="point">目标坐标。</param>
    private static void AddDistinct(ICollection<WorkflowPoint> points, WorkflowPoint point)
    {
        if (points.LastOrDefault() != point) points.Add(point);
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

    /// <summary>判断坐标是否位于节点扩展区域内。</summary>
    /// <param name="rect">目标矩形。</param>
    /// <param name="x">屏幕横坐标。</param>
    /// <param name="y">屏幕纵坐标。</param>
    /// <param name="margin">扩展命中区域大小。</param>
    /// <returns>返回处理结果。</returns>
    private static bool IsNearNode(WorkflowDesignerRect rect, double x, double y, double margin) =>
        x >= rect.X - margin && x <= rect.X + rect.Width + margin
        && y >= rect.Y - margin && y <= rect.Y + rect.Height + margin;

    /// <summary>计算指定坐标距离节点最近的边。</summary>
    /// <param name="rect">目标矩形。</param>
    /// <param name="x">屏幕横坐标。</param>
    /// <param name="y">屏幕纵坐标。</param>
    /// <returns>返回处理结果。</returns>
    private static WorkflowPortSide NearestSide(WorkflowDesignerRect rect, double x, double y)
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

    private static Rect NormalizeRectangle(Point first, Point second) => new(
        Math.Min(first.X, second.X),
        Math.Min(first.Y, second.Y),
        Math.Abs(first.X - second.X),
        Math.Abs(first.Y - second.Y));

    /// <summary>判断节点矩形是否与框选矩形相交。</summary>
    /// <param name="node">目标画布节点。</param>
    /// <param name="marquee">“marquee”参数。</param>
    /// <returns>返回处理结果。</returns>
    private static bool Intersects(WorkflowDesignerRect node, Rect marquee) =>
        node.X < marquee.Right && node.X + node.Width > marquee.Left
        && node.Y < marquee.Bottom && node.Y + node.Height > marquee.Top;

    /// <summary>计算点到有限线段的最短距离。</summary>
    /// <param name="x">屏幕横坐标。</param>
    /// <param name="y">屏幕纵坐标。</param>
    /// <param name="start">路径起点。</param>
    /// <param name="end">路径终点。</param>
    /// <returns>返回处理结果。</returns>
    private static double DistanceToSegment(double x, double y, WorkflowPoint start, WorkflowPoint end)
    {
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        if (dx == 0 && dy == 0)
            return Math.Sqrt(Math.Pow(x - start.X, 2) + Math.Pow(y - start.Y, 2));
        var t = Math.Clamp(((x - start.X) * dx + (y - start.Y) * dy) / (dx * dx + dy * dy), 0, 1);
        var nearestX = start.X + t * dx;
        var nearestY = start.Y + t * dy;
        return Math.Sqrt(Math.Pow(x - nearestX, 2) + Math.Pow(y - nearestY, 2));
    }

    /// <summary>测量指定文本的显示宽度。</summary>
    /// <param name="text">要显示或处理的文本。</param>
    /// <param name="size">字体大小。</param>
    /// <param name="weight">字体粗细。</param>
    /// <returns>返回处理结果。</returns>
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
    /// <returns>返回处理结果。</returns>
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
    /// <param name="red">“red”参数。</param>
    /// <param name="green">“green”参数。</param>
    /// <param name="blue">“blue”参数。</param>
    /// <returns>返回处理结果。</returns>
    private static Brush Brush(byte red, byte green, byte blue) =>
        Freeze(new SolidColorBrush(Color.FromRgb(red, green, blue)));

    /// <summary>取得节点运行状态对应的 WPF 画刷。</summary>
    /// <param name="state">节点运行状态。</param>
    /// <returns>返回处理结果。</returns>
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
    /// <returns>返回处理结果。</returns>
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
