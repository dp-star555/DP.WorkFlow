using System.Drawing.Drawing2D;
using DP.WorkFlow.UI;

namespace DP.WorkFlow.UI.WinForms;

/// <summary>
/// 基于 GDI+ 的工作流画布。静态控件属性位于同名 Designer.cs；节点、端口和连线必须运行时自绘。
/// </summary>
public sealed partial class WorkflowDesignerControl : Control
{
    private WorkflowDesignerSession? _session;
    private WorkflowNodeDragOperation? _nodeDrag;
    private WorkflowConnectionModel? _dragConnection;
    private WorkflowConnectionModel? _dragLabelConnection;
    private double _dragLabelOrigin;
    private int _dragWaypointIndex = -1;
    private WorkflowPoint _dragWaypointOrigin;
    private WorkflowPoint[] _segmentOriginalWaypoints = Array.Empty<WorkflowPoint>();
    private WorkflowPoint[] _segmentWorkingWaypoints = Array.Empty<WorkflowPoint>();
    private int _segmentFirstWaypointIndex = -1;
    private int _segmentSecondWaypointIndex = -1;
    private bool _segmentIsHorizontal;
    private WorkflowConnectionModel? _segmentConnection;
    private WorkflowPoint _dragStartCanvas;
    private Point _marqueeStart;
    private Point _marqueeCurrent;
    private bool _marqueeAdditive;
    private Point _panStart;
    private WorkflowPoint _panOrigin;
    private WorkflowPortHit? _connectionStart;
    private WorkflowPortSide? _portDropSide;
    private Point _pointer;
    private InteractionMode _mode;
    private WorkflowDesignerRect? _overviewAnchorViewport;
    private double _overviewAnchorZoom;
    private Size _overviewAnchorClientSize;
    private readonly System.Windows.Forms.Timer _runtimeTimer;

    /// <summary>初始化控件。</summary>
    public WorkflowDesignerControl()
    {
        InitializeComponent();
        SetStyle(ControlStyles.Selectable | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
        _runtimeTimer = new System.Windows.Forms.Timer { Interval = 100 };
        _runtimeTimer.Tick += (_, _) =>
        {
            if (_session?.RuntimeSnapshot?.ExecutionState is E_WorkflowExecutionState.Running)
                Invalidate();
        };
        _runtimeTimer.Start();
    }

    /// <summary>获取或设置共享设计会话。</summary>
    public WorkflowDesignerSession? Session
    {
        get => _session;
        set
        {
            if (ReferenceEquals(_session, value))
                return;
            if (_session is not null)
                _session.Changed -= OnSessionChanged;
            CancelInteraction();
            _overviewAnchorViewport = null;
            _session = value;
            if (_session is not null)
                _session.Changed += OnSessionChanged;
            Invalidate();
        }
    }

    /// <summary>获取当前是否因节点超出可视区域而显示右上角概览窗口。</summary>
    public bool IsOverviewMapVisible => TryCreateOverviewMapLayout(out _);

    /// <summary>双击节点时发生，由宿主打开属性编辑器。</summary>
    public event EventHandler<IWorkflowNodeModel>? NodeEditRequested;

    /// <summary>交互产生无法创建的连接时发生。</summary>
    public event EventHandler<string>? InteractionError;

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (_session is not null)
                _session.Changed -= OnSessionChanged;
            _runtimeTimer.Dispose();
        }
        base.Dispose(disposing);
    }

    /// <inheritdoc />
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        DrawGrid(e.Graphics);
        if (_session is null)
            return;
        DrawConnections(e.Graphics);
        foreach (var node in _session.Canvas.Nodes)
            DrawNode(e.Graphics, node);
        DrawPendingConnection(e.Graphics);
        DrawMarquee(e.Graphics);
        DrawOverviewMap(e.Graphics);
    }

    /// <inheritdoc />
    protected override void OnDragEnter(DragEventArgs drgevent)
    {
        base.OnDragEnter(drgevent);
        drgevent.Effect = ToolboxDragEffect(drgevent);
    }

    /// <inheritdoc />
    protected override void OnDragOver(DragEventArgs drgevent)
    {
        base.OnDragOver(drgevent);
        drgevent.Effect = ToolboxDragEffect(drgevent);
    }

    private static DragDropEffects ToolboxDragEffect(DragEventArgs e) =>
        e.Data?.GetDataPresent(typeof(WorkflowToolboxItem)) == true ? DragDropEffects.Copy : DragDropEffects.None;

    /// <inheritdoc />
    protected override void OnDragDrop(DragEventArgs drgevent)
    {
        base.OnDragDrop(drgevent);
        if (_session is null || drgevent.Data?.GetData(typeof(WorkflowToolboxItem)) is not WorkflowToolboxItem item)
            return;
        var client = PointToClient(new Point(drgevent.X, drgevent.Y));
        var canvas = WorkflowDesignerGeometry.ScreenToCanvas(_session, client.X, client.Y);
        var insertion = WorkflowDesignerInteraction.HitConnectionInsertion(_session, client.X, client.Y, 14);
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
    }

    /// <inheritdoc />
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        if (_session is null)
            return;
        _pointer = e.Location;
        if (e.Button == MouseButtons.Right)
        {
            var contextConnection = WorkflowDesignerInteraction.HitConnection(_session, e.X, e.Y);
            if (contextConnection is not null)
            {
                if (ReferenceEquals(_session.SelectedConnection, contextConnection))
                    _session.RemoveConnection(contextConnection);
                else
                    _session.SelectConnection(contextConnection);
                return;
            }
        }
        if (e.Button == MouseButtons.Middle || e.Button == MouseButtons.Right)
        {
            _mode = InteractionMode.Pan;
            _panStart = e.Location;
            _panOrigin = new WorkflowPoint(_session.PanX, _session.PanY);
            Cursor = Cursors.Hand;
            return;
        }
        if (e.Button != MouseButtons.Left)
            return;
        if (TryNavigateOverview(e.Location))
        {
            _mode = InteractionMode.NavigateOverview;
            Capture = true;
            Cursor = Cursors.Hand;
            return;
        }
        var waypoint = WorkflowDesignerInteraction.HitWaypoint(_session, e.X, e.Y);
        if (waypoint.HasValue)
        {
            if ((ModifierKeys & Keys.Shift) != 0)
            {
                _session.RemoveConnectionWaypoint(waypoint.Value.Connection, waypoint.Value.Index);
                return;
            }
            _dragConnection = waypoint.Value.Connection;
            _dragWaypointIndex = waypoint.Value.Index;
            _dragWaypointOrigin = waypoint.Value.Connection.Waypoints[waypoint.Value.Index];
            _mode = InteractionMode.MoveWaypoint;
            Capture = true;
            return;
        }
        var labelConnection = HitConnectionLabel(e.X, e.Y);
        if (labelConnection is not null)
        {
            _session.SelectConnection(labelConnection);
            _dragLabelConnection = labelConnection;
            _dragLabelOrigin = labelConnection.LabelPosition;
            _mode = InteractionMode.MoveConnectionLabel;
            Capture = true;
            return;
        }
        var portHit = WorkflowDesignerInteraction.HitSingleOutputSideTarget(_session, e.X, e.Y)
            ?? HitPort(e.X, e.Y, WorkflowPortDirection.Output)
            ?? HitPort(e.X, e.Y, WorkflowPortDirection.Input);
        if (portHit.HasValue)
        {
            _session.SelectNode(portHit.Value.Node.Node.Id);
            _connectionStart = portHit;
            _portDropSide = portHit.Value.Side;
            _mode = InteractionMode.MovePort;
            Capture = true;
            Invalidate();
            return;
        }
        var node = WorkflowDesignerInteraction.HitNode(_session, e.X, e.Y);
        if (node is null)
        {
            if (TryBeginSegmentDrag(e.X, e.Y))
                return;
            var connection = WorkflowDesignerInteraction.HitConnection(_session, e.X, e.Y);
            if (connection is not null)
                _session.SelectConnection(connection);
            else
            {
                _marqueeStart = e.Location;
                _marqueeCurrent = e.Location;
                _marqueeAdditive = (ModifierKeys & Keys.Control) != 0;
                if (!_marqueeAdditive)
                    _session.SelectNodes(Array.Empty<string>());
                _mode = InteractionMode.Marquee;
                Capture = true;
            }
        }
        else
        {
            var additive = (ModifierKeys & Keys.Control) != 0;
            if (additive || !_session.SelectedNodeIds.Contains(node.Node.Id))
                _session.SelectNode(node.Node.Id, additive);
            if (!_session.SelectedNodeIds.Contains(node.Node.Id))
                return;
            _nodeDrag = new WorkflowNodeDragOperation(
                _session, WorkflowDesignerGeometry.ScreenToCanvas(_session, e.X, e.Y));
            _mode = InteractionMode.MoveNode;
            Capture = true;
        }
    }

    /// <inheritdoc />
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        _pointer = e.Location;
        if (_session is null)
            return;
        if (_mode == InteractionMode.Pan)
        {
            _session.SetViewport(
                _session.Zoom,
                _panOrigin.X + e.X - _panStart.X,
                _panOrigin.Y + e.Y - _panStart.Y);
        }
        else if (_mode == InteractionMode.NavigateOverview)
        {
            TryNavigateOverview(e.Location);
        }
        else if (_mode == InteractionMode.MoveNode && _nodeDrag is not null)
        {
            _nodeDrag.Update(WorkflowDesignerGeometry.ScreenToCanvas(_session, e.X, e.Y));
            Invalidate();
        }
        else if (_mode == InteractionMode.MoveWaypoint && _dragConnection is not null && _dragWaypointIndex >= 0)
        {
            _dragConnection.Waypoints[_dragWaypointIndex] = _session.SnapPoint(
                WorkflowDesignerGeometry.ScreenToCanvas(_session, e.X, e.Y));
            Invalidate();
        }
        else if (_mode == InteractionMode.MoveConnectionLabel && _dragLabelConnection is not null)
        {
            _dragLabelConnection.LabelPosition = WorkflowDesignerInteraction.ProjectPathPosition(
                WorkflowDesignerInteraction.GetConnectionPath(_session, _dragLabelConnection), e.X, e.Y);
            Invalidate();
        }
        else if (_mode == InteractionMode.MoveSegment && _segmentConnection is not null)
        {
            var current = _session.SnapPoint(WorkflowDesignerGeometry.ScreenToCanvas(_session, e.X, e.Y));
            var delta = _segmentIsHorizontal
                ? new WorkflowPoint(0, current.Y - _dragStartCanvas.Y)
                : new WorkflowPoint(current.X - _dragStartCanvas.X, 0);
            var updated = _segmentWorkingWaypoints.ToArray();
            updated[_segmentFirstWaypointIndex] = new WorkflowPoint(
                updated[_segmentFirstWaypointIndex].X + delta.X,
                updated[_segmentFirstWaypointIndex].Y + delta.Y);
            updated[_segmentSecondWaypointIndex] = new WorkflowPoint(
                updated[_segmentSecondWaypointIndex].X + delta.X,
                updated[_segmentSecondWaypointIndex].Y + delta.Y);
            WorkflowDesignerInteraction.SetLiveWaypoints(_segmentConnection, updated);
            Invalidate();
        }
        else if (_mode == InteractionMode.MovePort && _connectionStart.HasValue)
        {
            var rect = WorkflowDesignerGeometry.GetNodeScreenRect(_session, _connectionStart.Value.Node);
            _portDropSide = WorkflowDesignerInteraction.NearestSide(rect, e.X, e.Y);
            if (_connectionStart.Value.Port.Direction == WorkflowPortDirection.Output
                && !WorkflowDesignerInteraction.IsNearRect(rect, e.X, e.Y, 42))
            {
                _mode = InteractionMode.Connect;
                _portDropSide = null;
            }
            Cursor = _mode == InteractionMode.MovePort ? Cursors.SizeAll : Cursors.Cross;
            Invalidate();
        }
        else if (_mode == InteractionMode.Connect)
        {
            Invalidate();
        }
        else if (_mode == InteractionMode.Marquee)
        {
            _marqueeCurrent = e.Location;
            Invalidate();
        }
    }

    /// <inheritdoc />
    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (_session is null)
            return;
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
        else if (_mode == InteractionMode.MoveConnectionLabel && _dragLabelConnection is not null)
        {
            var position = _dragLabelConnection.LabelPosition;
            _dragLabelConnection.LabelPosition = _dragLabelOrigin;
            _session.SetConnectionLabelPosition(_dragLabelConnection, position);
        }
        else if (_mode == InteractionMode.MoveSegment && _segmentConnection is not null)
        {
            var final = _segmentConnection.Waypoints.ToArray();
            WorkflowDesignerInteraction.SetLiveWaypoints(_segmentConnection, _segmentOriginalWaypoints);
            _session.ReplaceConnectionWaypoints(_segmentConnection, final);
        }
        else if (_mode == InteractionMode.Marquee)
        {
            var marquee = NormalizeRectangle(_marqueeStart, _marqueeCurrent);
            _session.SelectNodes(
                WorkflowDesignerInteraction.GetNodeIdsInScreenRect(
                    _session, new WorkflowDesignerRect(marquee.X, marquee.Y, marquee.Width, marquee.Height)),
                _marqueeAdditive);
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
            var target = HitPort(e.X, e.Y, WorkflowPortDirection.Input);
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
        _segmentConnection = null;
        _segmentOriginalWaypoints = Array.Empty<WorkflowPoint>();
        _segmentWorkingWaypoints = Array.Empty<WorkflowPoint>();
        _segmentFirstWaypointIndex = -1;
        _segmentSecondWaypointIndex = -1;
        _connectionStart = null;
        _portDropSide = null;
        _mode = InteractionMode.None;
        Capture = false;
        Cursor = Cursors.Default;
        Invalidate();
    }

    /// <inheritdoc />
    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if (_session is null)
            return;
        var canvasPoint = WorkflowDesignerGeometry.ScreenToCanvas(_session, e.X, e.Y);
        var zoom = Math.Clamp(_session.Zoom * (e.Delta > 0 ? 1.1 : 1 / 1.1), 0.25, 2.5);
        _session.SetViewport(zoom, e.X - canvasPoint.X * zoom, e.Y - canvasPoint.Y * zoom);
    }

    /// <inheritdoc />
    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        if (_session is null)
            return;
        var node = WorkflowDesignerInteraction.HitNode(_session, e.X, e.Y);
        if (node is not null)
        {
            NodeEditRequested?.Invoke(this, node.Node);
            return;
        }
        if (WorkflowDesignerInteraction.HitConnection(_session, e.X, e.Y) is { } connection)
        {
            _session.SelectConnection(connection);
            _session.AddConnectionWaypoint(
                connection,
                WorkflowDesignerGeometry.ScreenToCanvas(_session, e.X, e.Y));
        }
    }

    /// <inheritdoc />
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (_session is not null)
        {
            if (keyData == (Keys.Control | Keys.A))
            {
                _session.SelectNodes(_session.Canvas.Nodes.Select(node => node.Node.Id));
                return true;
            }
            if (keyData == (Keys.Control | Keys.C))
                return _session.CopySelection();
            if (keyData == (Keys.Control | Keys.V))
                return _session.PasteSelection().Count > 0;
            if (keyData == Keys.Delete && _session.SelectedNodeIds.Count > 0)
                return _session.RemoveSelectedNodes();
            if (keyData == Keys.Delete && _session.SelectedConnection is { } connection)
                return _session.RemoveConnection(connection);
            if (keyData == (Keys.Control | Keys.Z))
                return _session.Undo();
            if (keyData == (Keys.Control | Keys.Y))
                return _session.Redo();
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    /// <summary>绘制随缩放和平移变化的设计网格。</summary>
    /// <param name="graphics">GDI+ 绘图表面。</param>
    private void DrawGrid(Graphics graphics)
    {
        graphics.Clear(BackColor);
        if (_session is null)
            return;
        var spacing = 24 * _session.Zoom;
        if (spacing < 8)
            return;
        using var pen = new Pen(WorkflowWinFormsStyle.Get().Grid, 1);
        var startX = _session.PanX % spacing;
        var startY = _session.PanY % spacing;
        for (var x = startX; x < Width; x += spacing)
            graphics.DrawLine(pen, (float)x, 0, (float)x, Height);
        for (var y = startY; y < Height; y += spacing)
            graphics.DrawLine(pen, 0, (float)y, Width, (float)y);
    }

    /// <summary>绘制画布中的全部正交连接。</summary>
    /// <param name="graphics">GDI+ 绘图表面。</param>
    private void DrawConnections(Graphics graphics)
    {
        if (_session is null)
            return;
        foreach (var connection in _session.Canvas.Connections)
        {
            var source = WorkflowDesignerInteraction.FindPortPoint(
                _session, connection.FromNodeId, connection.FromPort, WorkflowPortDirection.Output, connection.FromSide);
            var target = WorkflowDesignerInteraction.FindPortPoint(
                _session, connection.ToNodeId, connection.ToPort, WorkflowPortDirection.Input, connection.ToSide);
            if (!source.HasValue || !target.HasValue)
                continue;
            var selected = ReferenceEquals(_session.SelectedConnection, connection);
            var palette = WorkflowWinFormsStyle.Get();
            using var pen = new Pen(
                selected ? palette.Accent : Color.FromArgb(155, 155, 155),
                selected ? 3 : 2);
            DrawConnectionPath(graphics, pen, connection, source.Value, target.Value);
            DrawConnectionLabel(graphics, connection, source.Value);
            if (selected)
                DrawWaypointHandles(graphics, connection);
        }
    }

    /// <summary>绘制节点背景、标题、状态、端口和运行信息。</summary>
    /// <param name="graphics">GDI+ 绘图表面。</param>
    /// <param name="item">目标数据项。</param>
    private void DrawNode(Graphics graphics, WorkflowCanvasNode item)
    {
        if (_session is null)
            return;
        var rect = WorkflowDesignerGeometry.GetNodeScreenRect(_session, item);
        var bounds = RectangleF.FromLTRB((float)rect.X, (float)rect.Y, (float)(rect.X + rect.Width), (float)(rect.Y + rect.Height));
        var selected = _session.SelectedNodeIds.Contains(item.Node.Id);
        var state = _session.GetNodeState(item.Node.Id);
        var stateColor = StateColor(state);
        using var path = RoundedRectangle(bounds, 8);
        using var fill = new SolidBrush(WorkflowWinFormsStyle.Get().Surface);
        using var border = new Pen(selected ? Color.FromArgb(56, 189, 248) : stateColor, selected ? 3f : 2f);
        graphics.FillPath(fill, path);
        var headerHeight = (float)Math.Min(rect.Height, WorkflowDesignerGeometry.HeaderHeight * _session.Zoom);
        using var header = new SolidBrush(Color.FromArgb(51, 51, 55));
        var graphicsState = graphics.Save();
        graphics.SetClip(path);
        graphics.FillRectangle(header, bounds.X, bounds.Y, bounds.Width, Math.Max(0, headerHeight));
        graphics.Restore(graphicsState);
        graphics.DrawPath(border, path);
        var runtimeText = WorkflowDesignerInteraction.GetRuntimeDisplayText(_session, item);
        using var runtimeFont = CanvasFont(WorkflowDesignerGeometry.DetailFontPixels * _session.Zoom);
        var measuredRuntimeWidth = string.IsNullOrEmpty(runtimeText)
            ? 0
            : graphics.MeasureString(runtimeText, runtimeFont).Width;
        var headerLayout = WorkflowDesignerGeometry.CalculateNodeHeaderLayout(
            new WorkflowDesignerRect(bounds.X, bounds.Y, bounds.Width, bounds.Height),
            _session.Zoom,
            measuredRuntimeWidth);
        using var stateBrush = new SolidBrush(stateColor);
        graphics.FillEllipse(stateBrush, ToRectangle(headerLayout.IndicatorBounds));
        using var titleFont = CanvasFont(headerLayout.TitleFontSize);
        using var titleBrush = new SolidBrush(ForeColor);
        using var titleFormat = new StringFormat
        {
            LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = StringFormatFlags.NoWrap
        };
        graphics.DrawString(
            item.Node.Title,
            titleFont,
            titleBrush,
            ToRectangle(headerLayout.TitleBounds),
            titleFormat);
        DrawPorts(graphics, item, WorkflowPortDirection.Input);
        DrawPorts(graphics, item, WorkflowPortDirection.Output);
        DrawRuntimeInfo(graphics, headerLayout, runtimeText, runtimeFont);
        DrawConnectionOverrideEndpoints(graphics, item);
        DrawConnectionInputTargets(graphics, item);
        if (selected)
            DrawPortSideTargets(graphics, item, bounds);
    }

    /// <summary>在节点标题栏右侧绘制执行序号和耗时。</summary>
    private static void DrawRuntimeInfo(Graphics graphics, WorkflowNodeHeaderLayout layout, string? text, Font font)
    {
        if (string.IsNullOrEmpty(text) || layout.RuntimeBounds.Width <= 0) return;
        using var brush = new SolidBrush(WorkflowWinFormsStyle.Get().MutedText);
        using var format = new StringFormat
        {
            Alignment = StringAlignment.Far,
            LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = StringFormatFlags.NoWrap
        };
        graphics.DrawString(text, font, brush, ToRectangle(layout.RuntimeBounds), format);
    }

    /// <summary>
    /// 画布文字使用像素字号并随缩放变化：画布几何不随系统 DPI 放大，若用磅值字号，
    /// 高 DPI 下文字会相对卡片被额外放大。
    /// </summary>
    private Font CanvasFont(double pixels, FontStyle style = FontStyle.Regular) =>
        new(Font.FontFamily, (float)Math.Max(3, pixels), style, GraphicsUnit.Pixel);

    private static RectangleF ToRectangle(WorkflowDesignerRect rect) => new(
        (float)rect.X,
        (float)rect.Y,
        (float)rect.Width,
        (float)rect.Height);

    /// <summary>连接拖动期间绘制所有可用输入端点。</summary>
    /// <param name="graphics">GDI+ 绘图表面。</param>
    /// <param name="node">目标画布节点。</param>
    private void DrawConnectionInputTargets(Graphics graphics, WorkflowCanvasNode node)
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
        using var fill = new SolidBrush(Color.FromArgb(167, 139, 250));
        using var border = new Pen(ForeColor, 1);
        foreach (var port in ports)
        foreach (var side in Enum.GetValues<WorkflowPortSide>())
        {
            var point = WorkflowDesignerGeometry.GetPortScreenPoint(_session, node, port, ports, side);
            const float radius = 4;
            graphics.FillEllipse(fill, (float)point.X - radius, (float)point.Y - radius, radius * 2, radius * 2);
            graphics.DrawEllipse(border, (float)point.X - radius, (float)point.Y - radius, radius * 2, radius * 2);
        }
    }

    /// <summary>在选中节点四边绘制端口移动目标。</summary>
    /// <param name="graphics">GDI+ 绘图表面。</param>
    /// <param name="node">目标画布节点。</param>
    /// <param name="bounds">绘制或命中边界。</param>
    private void DrawPortSideTargets(Graphics graphics, WorkflowCanvasNode node, RectangleF bounds)
    {
        var active = _mode == InteractionMode.MovePort
            && _connectionStart?.Node == node
            ? _portDropSide
            : null;
        foreach (var side in Enum.GetValues<WorkflowPortSide>())
        {
            if (_session is not null && WorkflowDesignerInteraction.HasConnectedEndpointAtSide(_session, node, side))
                continue;
            var center = side switch
            {
                WorkflowPortSide.Left => new PointF(bounds.Left, bounds.Top + bounds.Height / 2),
                WorkflowPortSide.Top => new PointF(bounds.Left + bounds.Width / 2, bounds.Top),
                WorkflowPortSide.Right => new PointF(bounds.Right, bounds.Top + bounds.Height / 2),
                WorkflowPortSide.Bottom => new PointF(bounds.Left + bounds.Width / 2, bounds.Bottom),
                _ => PointF.Empty
            };
            var highlighted = active == side;
            var activeColor = _connectionStart?.Port.Direction == WorkflowPortDirection.Input
                ? Color.FromArgb(167, 139, 250)
                : Color.FromArgb(52, 211, 153);
            var radius = highlighted ? 4f : 2f;
            using var fill = new SolidBrush(highlighted
                ? activeColor
                : Color.FromArgb(30, 15, 23, 42));
            using var border = new Pen(highlighted
                ? Color.FromArgb(226, 232, 240)
                : Color.FromArgb(71, 85, 105), highlighted ? 1.5f : 1);
            graphics.FillEllipse(fill, center.X - radius, center.Y - radius, radius * 2, radius * 2);
            graphics.DrawEllipse(border, center.X - radius, center.Y - radius, radius * 2, radius * 2);
        }
    }

    /// <summary>绘制节点指定方向的可见端口及标签。</summary>
    /// <param name="graphics">GDI+ 绘图表面。</param>
    /// <param name="node">目标画布节点。</param>
    /// <param name="direction">端口方向。</param>
    private void DrawPorts(Graphics graphics, WorkflowCanvasNode node, WorkflowPortDirection direction)
    {
        if (_session is null)
            return;
        var ports = _session.GetPorts(node.Node.Id, direction);
        using var fill = new SolidBrush(direction == WorkflowPortDirection.Input
            ? Color.FromArgb(167, 139, 250)
            : Color.FromArgb(52, 211, 153));
        using var border = new Pen(Color.FromArgb(15, 23, 42), 1.5f);
        using var labelFont = CanvasFont(WorkflowDesignerGeometry.DetailFontPixels * _session.Zoom);
        using var labelBrush = new SolidBrush(direction == WorkflowPortDirection.Input
            ? Color.FromArgb(196, 181, 253)
            : Color.FromArgb(110, 231, 183));
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
                ? (float)Math.Max(2, WorkflowDesignerGeometry.PortRadius * _session.Zoom * 0.72)
                : 2.5f;
            if (semanticHandle)
            {
                using var neutralFill = new SolidBrush(Color.FromArgb(71, 85, 105));
                graphics.FillEllipse(neutralFill, (float)point.X - radius, (float)point.Y - radius, radius * 2, radius * 2);
            }
            else
            {
                graphics.FillEllipse(fill, (float)point.X - radius, (float)point.Y - radius, radius * 2, radius * 2);
            }
            graphics.DrawEllipse(border, (float)point.X - radius, (float)point.Y - radius, radius * 2, radius * 2);
            if ((connected || semanticHandle) && WorkflowDesignerInteraction.ShouldDrawPortLabel(_session, node, direction))
                DrawPortLabel(graphics, labelFont, labelBrush, node, port, point, radius);
        }
    }

    /// <summary>绘制框选区域。</summary>
    /// <param name="graphics">GDI+ 绘图表面。</param>
    private void DrawMarquee(Graphics graphics)
    {
        if (_mode != InteractionMode.Marquee)
            return;
        var rect = NormalizeRectangle(_marqueeStart, _marqueeCurrent);
        using var fill = new SolidBrush(Color.FromArgb(35, 56, 189, 248));
        using var border = new Pen(Color.FromArgb(56, 189, 248), 1) { DashStyle = DashStyle.Dash };
        graphics.FillRectangle(fill, rect);
        graphics.DrawRectangle(border, rect);
    }

    /// <summary>
    /// 当任一节点超出当前可视区域时，在右上角绘制流程概览。
    /// 概览只表达节点位置、连接关系和当前视口，不绘制节点业务文字。
    /// </summary>
    /// <param name="graphics">GDI+ 绘图表面。</param>
    private void DrawOverviewMap(Graphics graphics)
    {
        if (!TryCreateOverviewMapLayout(out var layout) || _session is null)
            return;

        using var background = new SolidBrush(Color.FromArgb(235, 24, 24, 27));
        using var border = new Pen(Color.FromArgb(82, 82, 91), 1);
        using var connectionPen = new Pen(Color.FromArgb(113, 113, 122), 1);
        using var nodeBrush = new SolidBrush(Color.FromArgb(82, 82, 91));
        using var selectedNodeBrush = new SolidBrush(Color.FromArgb(37, 99, 235));
        using var viewportBrush = new SolidBrush(Color.FromArgb(30, 59, 130, 246));
        using var viewportPen = new Pen(Color.FromArgb(96, 165, 250), 1.5f);
        using var titleBrush = new SolidBrush(Color.FromArgb(212, 212, 216));
        using var titleFont = CanvasFont(WorkflowDesignerGeometry.DetailFontPixels);

        graphics.FillRectangle(background, layout.MapBounds);
        graphics.DrawRectangle(border, layout.MapBounds.X, layout.MapBounds.Y, layout.MapBounds.Width, layout.MapBounds.Height);
        graphics.DrawString("概览", titleFont, titleBrush, layout.MapBounds.X + 7, layout.MapBounds.Y + 4);

        var nodes = _session.Canvas.Nodes.ToDictionary(item => item.Node.Id, StringComparer.Ordinal);
        foreach (var connection in _session.Canvas.Connections)
        {
            if (!nodes.TryGetValue(connection.FromNodeId, out var source)
                || !nodes.TryGetValue(connection.ToNodeId, out var target))
                continue;
            var points = new List<WorkflowPoint>
            {
                new(source.X + source.Width / 2d, source.Y + source.Height / 2d)
            };
            points.AddRange(connection.Waypoints);
            points.Add(new WorkflowPoint(target.X + target.Width / 2d, target.Y + target.Height / 2d));
            if (points.Count > 1)
                graphics.DrawLines(connectionPen, points.Select(point => OverviewPoint(layout, point.X, point.Y)).ToArray());
        }

        foreach (var node in _session.Canvas.Nodes)
        {
            var topLeft = OverviewPoint(layout, node.X, node.Y);
            var width = Math.Max(3f, (float)(node.Width * layout.Scale));
            var height = Math.Max(2f, (float)(node.Height * layout.Scale));
            graphics.FillRectangle(
                _session.SelectedNodeIds.Contains(node.Node.Id) ? selectedNodeBrush : nodeBrush,
                topLeft.X,
                topLeft.Y,
                width,
                height);
        }

        var viewport = CurrentViewportCanvasRect();
        var viewportTopLeft = OverviewPoint(layout, viewport.X, viewport.Y);
        var viewportRectangle = new RectangleF(
            viewportTopLeft.X,
            viewportTopLeft.Y,
            Math.Max(2f, (float)(viewport.Width * layout.Scale)),
            Math.Max(2f, (float)(viewport.Height * layout.Scale)));
        // 中键平移可以暂时把视口移出流程总边界，因此将视口框裁剪在概览内容区内，
        // 避免它覆盖标题或跑出概览窗口。
        viewportRectangle = RectangleF.Intersect(viewportRectangle, layout.ContentBounds);
        if (viewportRectangle.Width > 0 && viewportRectangle.Height > 0)
        {
            graphics.FillRectangle(viewportBrush, viewportRectangle);
            graphics.DrawRectangle(viewportPen, viewportRectangle.X, viewportRectangle.Y, viewportRectangle.Width, viewportRectangle.Height);
        }
    }

    /// <summary>点击或拖动概览时，将点击位置移动到主画布中心。</summary>
    /// <param name="location">菜单显示位置。</param>
    private bool TryNavigateOverview(Point location)
    {
        if (_session is null || !TryCreateOverviewMapLayout(out var layout) || !layout.MapBounds.Contains(location))
            return false;
        var x = Math.Clamp(location.X, layout.ContentBounds.Left, layout.ContentBounds.Right);
        var y = Math.Clamp(location.Y, layout.ContentBounds.Top, layout.ContentBounds.Bottom);
        var canvasX = layout.WorldBounds.X + (x - layout.ContentBounds.X) / layout.Scale;
        var canvasY = layout.WorldBounds.Y + (y - layout.ContentBounds.Y) / layout.Scale;
        var viewport = CurrentViewportCanvasRect();
        // 概览拖动只能在固定的流程世界边界内定位。这样拖到边缘时世界边界不会
        // 跟随当前视口继续扩张，概览比例和蓝色视口框尺寸也就不会发生抖动。
        canvasX = Math.Clamp(
            canvasX,
            layout.WorldBounds.X + viewport.Width / 2d,
            layout.WorldBounds.X + layout.WorldBounds.Width - viewport.Width / 2d);
        canvasY = Math.Clamp(
            canvasY,
            layout.WorldBounds.Y + viewport.Height / 2d,
            layout.WorldBounds.Y + layout.WorldBounds.Height - viewport.Height / 2d);
        _session.SetViewport(
            _session.Zoom,
            ClientSize.Width / 2d - canvasX * _session.Zoom,
            ClientSize.Height / 2d - canvasY * _session.Zoom);
        return true;
    }

    /// <summary>计算概览窗口、世界边界及缩放比例；内容未溢出时返回 false。</summary>
    private bool TryCreateOverviewMapLayout(out OverviewMapLayout layout)
    {
        layout = default;
        if (_session is null || _session.Canvas.Nodes.Count == 0 || ClientSize.Width < 240 || ClientSize.Height < 180)
            return false;

        var overflowing = _session.Canvas.Nodes.Any(node =>
        {
            var rect = WorkflowDesignerGeometry.GetNodeScreenRect(_session, node);
            return rect.X < 0 || rect.Y < 0 || rect.X + rect.Width > ClientSize.Width || rect.Y + rect.Height > ClientSize.Height;
        });
        if (!overflowing)
        {
            _overviewAnchorViewport = null;
            return false;
        }

        var viewport = CurrentViewportCanvasRect();
        // 首次出现概览时记录视口锚点，后续平移不能把实时 Pan 重新并入世界边界。
        // 这样既能在概览中保留最初可视区域与远端节点的位置关系，也能保证拖动期间比例固定。
        if (!_overviewAnchorViewport.HasValue
            || !_overviewAnchorZoom.Equals(_session.Zoom)
            || _overviewAnchorClientSize != ClientSize)
        {
            _overviewAnchorViewport = viewport;
            _overviewAnchorZoom = _session.Zoom;
            _overviewAnchorClientSize = ClientSize;
        }
        var anchorViewport = _overviewAnchorViewport.Value;
        var minX = Math.Min(anchorViewport.X, _session.Canvas.Nodes.Min(node => node.X));
        var minY = Math.Min(anchorViewport.Y, _session.Canvas.Nodes.Min(node => node.Y));
        var maxX = Math.Max(anchorViewport.X + anchorViewport.Width, _session.Canvas.Nodes.Max(node => node.X + node.Width));
        var maxY = Math.Max(anchorViewport.Y + anchorViewport.Height, _session.Canvas.Nodes.Max(node => node.Y + node.Height));
        var waypoints = _session.Canvas.Connections.SelectMany(connection => connection.Waypoints).ToArray();
        if (waypoints.Length > 0)
        {
            minX = Math.Min(minX, waypoints.Min(point => point.X));
            minY = Math.Min(minY, waypoints.Min(point => point.Y));
            maxX = Math.Max(maxX, waypoints.Max(point => point.X));
            maxY = Math.Max(maxY, waypoints.Max(point => point.Y));
        }
        var padding = Math.Max(24d, Math.Max(maxX - minX, maxY - minY) * 0.035d);
        var horizontalPadding = padding + viewport.Width / 2d;
        var verticalPadding = padding + viewport.Height / 2d;
        var world = new WorkflowDesignerRect(
            minX - horizontalPadding,
            minY - verticalPadding,
            Math.Max(1, maxX - minX + horizontalPadding * 2),
            Math.Max(1, maxY - minY + verticalPadding * 2));

        const int margin = 12;
        var mapWidth = Math.Min(220, Math.Max(170, ClientSize.Width / 4));
        var mapHeight = Math.Min(155, Math.Max(120, ClientSize.Height / 4));
        var map = new RectangleF(ClientSize.Width - mapWidth - margin, margin, mapWidth, mapHeight);
        var inner = new RectangleF(map.X + 7, map.Y + 23, map.Width - 14, map.Height - 30);
        var scale = (float)Math.Min(inner.Width / world.Width, inner.Height / world.Height);
        var contentWidth = (float)(world.Width * scale);
        var contentHeight = (float)(world.Height * scale);
        var content = new RectangleF(
            inner.X + (inner.Width - contentWidth) / 2f,
            inner.Y + (inner.Height - contentHeight) / 2f,
            contentWidth,
            contentHeight);
        layout = new OverviewMapLayout(map, content, world, scale);
        return true;
    }

    /// <summary>执行 Current Viewport Canvas Rect 相关处理。</summary>
    private WorkflowDesignerRect CurrentViewportCanvasRect()
    {
        var topLeft = WorkflowDesignerGeometry.ScreenToCanvas(_session!, 0, 0);
        return new WorkflowDesignerRect(
            topLeft.X,
            topLeft.Y,
            ClientSize.Width / _session!.Zoom,
            ClientSize.Height / _session.Zoom);
    }

    private static PointF OverviewPoint(OverviewMapLayout layout, double x, double y) => new(
        layout.ContentBounds.X + (float)((x - layout.WorldBounds.X) * layout.Scale),
        layout.ContentBounds.Y + (float)((y - layout.WorldBounds.Y) * layout.Scale));

    /// <summary>定义 OverviewMapLayout 类型。</summary>
    private readonly record struct OverviewMapLayout(
        RectangleF MapBounds,
        RectangleF ContentBounds,
        WorkflowDesignerRect WorldBounds,
        float Scale);

    /// <summary>绘制正在拖动但尚未提交的连接预览。</summary>
    /// <param name="graphics">GDI+ 绘图表面。</param>
    private void DrawPendingConnection(Graphics graphics)
    {
        if (!_connectionStart.HasValue)
            return;
        using var pen = new Pen(Color.FromArgb(56, 189, 248), 2) { DashStyle = DashStyle.Dash };
        var points = WorkflowDesignerInteraction.BuildOrthogonalPath(
            _connectionStart.Value.Point,
            new WorkflowPoint(_pointer.X, _pointer.Y),
            Array.Empty<WorkflowPoint>());
        graphics.DrawLines(pen, points.Select(point => new PointF((float)point.X, (float)point.Y)).ToArray());
    }

    /// <summary>返回指定屏幕坐标命中的端口；从输出端口拖出连接时，其他节点输入端口的四边均可命中。</summary>
    private WorkflowPortHit? HitPort(double x, double y, WorkflowPortDirection direction) =>
        _session is null
            ? null
            : WorkflowDesignerInteraction.HitPort(_session, x, y, direction,
                _mode == InteractionMode.Connect && _connectionStart?.Port.Direction == WorkflowPortDirection.Output
                    ? _connectionStart.Value.Node
                    : null);

    /// <summary>尝试启动连接中间线段的整体拖动。</summary>
    /// <param name="x">屏幕横坐标。</param>
    /// <param name="y">屏幕纵坐标。</param>
    private bool TryBeginSegmentDrag(double x, double y)
    {
        if (_session is null) return false;
        foreach (var connection in _session.Canvas.Connections.Reverse())
        {
            var points = WorkflowDesignerInteraction.GetConnectionPath(_session, connection);
            for (var index = 1; index < points.Count - 2; index++)
            {
                if (WorkflowDesignerInteraction.DistanceToSegment(x, y, points[index], points[index + 1]) > 7)
                    continue;
                _session.SelectConnection(connection);
                _segmentConnection = connection;
                _segmentOriginalWaypoints = connection.Waypoints.ToArray();
                _segmentWorkingWaypoints = points.Skip(1)
                    .Take(points.Count - 2)
                    .Select(point => WorkflowDesignerGeometry.ScreenToCanvas(_session, point.X, point.Y))
                    .ToArray();
                _segmentFirstWaypointIndex = index - 1;
                _segmentSecondWaypointIndex = index;
                _segmentIsHorizontal = Math.Abs(points[index].Y - points[index + 1].Y) < 0.1;
                _dragStartCanvas = WorkflowDesignerGeometry.ScreenToCanvas(_session, x, y);
                WorkflowDesignerInteraction.SetLiveWaypoints(connection, _segmentWorkingWaypoints);
                _mode = InteractionMode.MoveSegment;
                Capture = true;
                Cursor = _segmentIsHorizontal ? Cursors.HSplit : Cursors.VSplit;
                return true;
            }
        }
        return false;
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
        _segmentConnection = null;
        _segmentOriginalWaypoints = Array.Empty<WorkflowPoint>();
        _segmentWorkingWaypoints = Array.Empty<WorkflowPoint>();
        _segmentFirstWaypointIndex = -1;
        _segmentSecondWaypointIndex = -1;
        _connectionStart = null;
        _portDropSide = null;
        _mode = InteractionMode.None;
        Capture = false;
        Cursor = Cursors.Default;
    }

    /// <summary>处理设计会话变化并刷新当前控件。</summary>
    private void OnSessionChanged(object? sender, WorkflowDesignerChangedEventArgs e)
    {
        if (e.Kind == WorkflowDesignerChangeKind.Document)
            _overviewAnchorViewport = null;
        if (IsDisposed)
            return;
        if (InvokeRequired)
            BeginInvoke(Invalidate);
        else
            Invalidate();
    }

    /// <summary>绘制单条正交连接及其方向箭头。</summary>
    /// <param name="graphics">GDI+ 绘图表面。</param>
    /// <param name="pen">绘制使用的画笔。</param>
    /// <param name="connection">目标连接。</param>
    /// <param name="start">路径起点。</param>
    /// <param name="end">路径终点。</param>
    private void DrawConnectionPath(
        Graphics graphics,
        Pen pen,
        WorkflowConnectionModel connection,
        WorkflowPoint start,
        WorkflowPoint end)
    {
        if (_session is null) return;
        var points = WorkflowDesignerInteraction.GetConnectionPath(_session, connection, start, end)
            .Select(point => new PointF((float)point.X, (float)point.Y))
            .ToArray();
        graphics.DrawLines(pen, points);
        DrawInsetArrow(graphics, pen.Color, points);
    }

    /// <summary>在连接终点前绘制方向箭头。</summary>
    /// <param name="graphics">GDI+ 绘图表面。</param>
    /// <param name="color">“color”参数。</param>
    /// <param name="points">路径点集合。</param>
    private static void DrawInsetArrow(Graphics graphics, Color color, IReadOnlyList<PointF> points)
    {
        if (points.Count < 2) return;
        var end = points[^1];
        var previous = points[^2];
        var dx = end.X - previous.X;
        var dy = end.Y - previous.Y;
        var length = Math.Sqrt(dx * dx + dy * dy);
        if (length < 12) return;
        var ux = (float)(dx / length);
        var uy = (float)(dy / length);
        var tip = new PointF(end.X - ux * 7, end.Y - uy * 7);
        var center = new PointF(tip.X - ux * 8, tip.Y - uy * 8);
        var perpendicularX = -uy * 4;
        var perpendicularY = ux * 4;
        using var brush = new SolidBrush(color);
        graphics.FillPolygon(brush, new[]
        {
            tip,
            new PointF(center.X + perpendicularX, center.Y + perpendicularY),
            new PointF(center.X - perpendicularX, center.Y - perpendicularY)
        });
    }

    /// <summary>在端口内侧绘制端口键文本。</summary>
    /// <param name="graphics">GDI+ 绘图表面。</param>
    /// <param name="font">绘制使用的字体。</param>
    /// <param name="brush">绘制使用的画刷。</param>
    /// <param name="node">目标画布节点。</param>
    /// <param name="port">“port”参数。</param>
    /// <param name="point">目标坐标。</param>
    /// <param name="radius">端口或圆角半径。</param>
    /// <param name="sideOverride">可选的连接端点边覆盖。</param>
    private void DrawPortLabel(
        Graphics graphics,
        Font font,
        Brush brush,
        WorkflowCanvasNode node,
        WorkflowPortDescriptor port,
        WorkflowPoint point,
        float radius,
        WorkflowPortSide? sideOverride = null)
    {
        var size = graphics.MeasureString(port.Key, font);
        var side = sideOverride ?? node.GetPortSide(port);
        var x = side switch
        {
            WorkflowPortSide.Left => (float)point.X + radius + 4,
            WorkflowPortSide.Right => (float)point.X - radius - size.Width - 4,
            _ => (float)point.X - size.Width / 2
        };
        var nodeRect = _session is null
            ? default
            : WorkflowDesignerGeometry.GetNodeScreenRect(_session, node);
        var bodyTop = (float)(nodeRect.Y + WorkflowDesignerGeometry.HeaderHeight * (_session?.Zoom ?? 1) + 2);
        var y = side switch
        {
            WorkflowPortSide.Top => bodyTop,
            WorkflowPortSide.Bottom => (float)point.Y - radius - size.Height - 2,
            _ => (float)point.Y - size.Height / 2
        };
        graphics.DrawString(port.Key, font, brush, x, y);
    }

    /// <summary>绘制具有单连接边覆盖的额外端点。</summary>
    /// <param name="graphics">GDI+ 绘图表面。</param>
    /// <param name="node">目标画布节点。</param>
    private void DrawConnectionOverrideEndpoints(Graphics graphics, WorkflowCanvasNode node)
    {
        if (_session is null) return;
        DrawConnectionOverrideEndpoints(graphics, node, WorkflowPortDirection.Input);
        DrawConnectionOverrideEndpoints(graphics, node, WorkflowPortDirection.Output);
    }

    /// <summary>绘制具有单连接边覆盖的额外端点。</summary>
    /// <param name="graphics">GDI+ 绘图表面。</param>
    /// <param name="node">目标画布节点。</param>
    /// <param name="direction">端口方向。</param>
    private void DrawConnectionOverrideEndpoints(
        Graphics graphics,
        WorkflowCanvasNode node,
        WorkflowPortDirection direction)
    {
        if (_session is null) return;
        var ports = _session.GetPorts(node.Node.Id, direction);
        foreach (var (port, side) in WorkflowDesignerInteraction.GetSideOverrideEndpoints(_session, node, direction))
        {
            var target = WorkflowDesignerGeometry.GetPortScreenPoint(_session, node, port, ports, side);
            var radius = (float)Math.Max(2, WorkflowDesignerGeometry.PortRadius * _session.Zoom * 0.72);
            using var fill = new SolidBrush(direction == WorkflowPortDirection.Input
                ? Color.FromArgb(167, 139, 250)
                : Color.FromArgb(52, 211, 153));
            using var endpointBorder = new Pen(BackColor, 1.25f);
            graphics.FillEllipse(fill, (float)target.X - radius, (float)target.Y - radius, radius * 2, radius * 2);
            graphics.DrawEllipse(endpointBorder, (float)target.X - radius, (float)target.Y - radius, radius * 2, radius * 2);
            if (WorkflowDesignerInteraction.ShouldDrawPortLabel(_session, node, direction))
            {
                using var font = CanvasFont(WorkflowDesignerGeometry.DetailFontPixels * _session.Zoom);
                using var brush = new SolidBrush(direction == WorkflowPortDirection.Input
                    ? Color.FromArgb(196, 181, 253)
                    : Color.FromArgb(110, 231, 183));
                DrawPortLabel(graphics, font, brush, node, port, target, radius, side);
            }
        }
    }

    /// <summary>绘制连接输出语义标签。</summary>
    /// <param name="graphics">GDI+ 绘图表面。</param>
    /// <param name="connection">目标连接。</param>
    /// <param name="source">源路径点集合。</param>
    private void DrawConnectionLabel(
        Graphics graphics,
        WorkflowConnectionModel connection,
        WorkflowPoint source)
    {
        if (_session is null || !WorkflowDesignerInteraction.ShouldDrawConnectionLabel(_session, connection)) return;
        using var font = CanvasFont(WorkflowDesignerGeometry.DetailFontPixels * _session.Zoom);
        var rect = GetConnectionLabelBounds(graphics, font, connection);
        var palette = WorkflowWinFormsStyle.Get();
        using var background = new SolidBrush(Color.FromArgb(235, palette.Surface));
        using var foreground = new SolidBrush(palette.Accent);
        graphics.FillRectangle(background, rect);
        using var labelBorder = new Pen(palette.Border);
        graphics.DrawRectangle(labelBorder, rect.X, rect.Y, rect.Width, rect.Height);
        graphics.DrawString(connection.FromPort, font, foreground, rect.X + 4, rect.Y + 1);
    }

    /// <summary>计算连接标签的屏幕边界。</summary>
    /// <param name="graphics">GDI+ 绘图表面。</param>
    /// <param name="font">绘制使用的字体。</param>
    /// <param name="connection">目标连接。</param>
    private RectangleF GetConnectionLabelBounds(Graphics graphics, Font font, WorkflowConnectionModel connection)
    {
        var center = _session is null
            ? default
            : WorkflowDesignerInteraction.PointAlongPath(
                WorkflowDesignerInteraction.GetConnectionPath(_session, connection), connection.LabelPosition);
        var size = graphics.MeasureString(connection.FromPort, font);
        return new RectangleF(
            (float)center.X - size.Width / 2 - 4,
            (float)center.Y - size.Height / 2 - 2,
            size.Width + 8,
            size.Height + 4);
    }

    /// <summary>返回指定屏幕坐标命中的连接标签。</summary>
    /// <param name="x">屏幕横坐标。</param>
    /// <param name="y">屏幕纵坐标。</param>
    private WorkflowConnectionModel? HitConnectionLabel(double x, double y)
    {
        if (_session is null) return null;
        using var graphics = CreateGraphics();
        using var font = CanvasFont(WorkflowDesignerGeometry.DetailFontPixels * _session.Zoom);
        return _session.Canvas.Connections.Reverse().FirstOrDefault(connection =>
            WorkflowDesignerInteraction.ShouldDrawConnectionLabel(_session, connection)
            && GetConnectionLabelBounds(graphics, font, connection).Contains((float)x, (float)y));
    }

    /// <summary>绘制选中连接的手工拐点控制柄。</summary>
    /// <param name="graphics">GDI+ 绘图表面。</param>
    /// <param name="connection">目标连接。</param>
    private void DrawWaypointHandles(Graphics graphics, WorkflowConnectionModel connection)
    {
        if (_session is null)
            return;
        using var fill = new SolidBrush(Color.FromArgb(15, 23, 42));
        using var border = new Pen(Color.FromArgb(56, 189, 248), 2);
        foreach (var waypoint in connection.Waypoints)
        {
            var point = WorkflowDesignerGeometry.CanvasToScreen(_session, waypoint.X, waypoint.Y);
            graphics.FillRectangle(fill, (float)point.X - 5, (float)point.Y - 5, 10, 10);
            graphics.DrawRectangle(border, (float)point.X - 5, (float)point.Y - 5, 10, 10);
        }
    }

    /// <summary>执行 From LTRB 相关处理。</summary>
    private static Rectangle NormalizeRectangle(Point first, Point second) => Rectangle.FromLTRB(
        Math.Min(first.X, second.X),
        Math.Min(first.Y, second.Y),
        Math.Max(first.X, second.X),
        Math.Max(first.Y, second.Y));

    /// <summary>创建用于绘制圆角矩形的 GDI+ 路径。</summary>
    /// <param name="bounds">绘制或命中边界。</param>
    /// <param name="radius">端口或圆角半径。</param>
    private static GraphicsPath RoundedRectangle(RectangleF bounds, float radius)
    {
        var diameter = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    /// <summary>取得节点运行状态对应的 GDI+ 颜色。</summary>
    /// <param name="state">节点运行状态。</param>
    private static Color StateColor(E_NodeState state) => state switch
    {
        E_NodeState.Running => Color.FromArgb(56, 189, 248),
        E_NodeState.Completed => Color.FromArgb(52, 211, 153),
        E_NodeState.Failed => Color.FromArgb(248, 113, 113),
        E_NodeState.Canceled => Color.FromArgb(251, 191, 36),
        _ => Color.FromArgb(100, 116, 139)
    };

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
        /// <summary>正在整体拖动连接的水平或垂直线段。</summary>
        MoveSegment,
        /// <summary>正在拖动矩形框选择节点。</summary>
        Marquee,
        /// <summary>正在通过缩略总览图定位画布视口。</summary>
        NavigateOverview
    }
}
