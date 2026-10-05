using DP.WorkFlow.Persistence.Json;

namespace DP.WorkFlow.UI;

/// <summary>节点对齐方式。</summary>
public enum WorkflowNodeAlignment
{
    /// <summary>按节点左边缘对齐。</summary>
    Left,
    /// <summary>按节点右边缘对齐。</summary>
    Right,
    /// <summary>按节点上边缘对齐。</summary>
    Top,
    /// <summary>按节点下边缘对齐。</summary>
    Bottom,
    /// <summary>使节点的水平中心坐标一致。</summary>
    HorizontalCenter,
    /// <summary>使节点的垂直中心坐标一致。</summary>
    VerticalCenter
}

/// <summary>节点等距分布方向。</summary>
public enum WorkflowNodeDistribution
{
    /// <summary>沿水平方向等距分布。</summary>
    Horizontal,
    /// <summary>沿垂直方向等距分布。</summary>
    Vertical
}

/// <summary>说明设计会话发生的变化类型。</summary>
public enum WorkflowDesignerChangeKind
{
    /// <summary>画布节点、连接或配置发生变化。</summary>
    Document,
    /// <summary>节点或连接选择发生变化。</summary>
    Selection,
    /// <summary>缩放或平移视口发生变化。</summary>
    Viewport,
    /// <summary>运行时快照及节点状态发生变化。</summary>
    Runtime
}

/// <summary>设计会话变化事件。</summary>
/// <param name="Kind">类型枚举值。</param>
public sealed record WorkflowDesignerChangedEventArgs(WorkflowDesignerChangeKind Kind);

/// <summary>表示工具箱中的节点类型。</summary>
/// <param name="NodeType">节点类型键。</param>
/// <param name="DisplayName">界面显示名称。</param>
/// <param name="Category">界面分类。</param>
/// <param name="Description">用途说明。</param>
public sealed record WorkflowToolboxItem(
    string NodeType,
    string DisplayName,
    string Category,
    string? Description);

/// <summary>
/// 画布编辑状态、节点操作和 Undo/Redo 行为。
/// </summary>
public sealed class WorkflowDesignerSession
{
    private static readonly object ClipboardSync = new();
    private static string? s_clipboardJson;
    private static int s_clipboardPasteCount;
    private readonly Stack<DesignerOperation> _undo = new();
    private readonly Stack<DesignerOperation> _redo = new();
    private string? _selectedNodeId;
    private readonly HashSet<string> _selectedNodeIds = new(StringComparer.Ordinal);
    private WorkflowConnectionModel? _selectedConnection;
    private WorkflowRuntimeSnapshot? _runtimeSnapshot;

    /// <summary>初始化正式工作流文档的设计会话；构造过程只读取文档，不执行布局修复。</summary>
    /// <param name="document">目标工作流文档。</param>
    /// <param name="catalog">用于创建和查询节点的节点目录。</param>
    /// <param name="publicDataCatalog">宿主可绑定公共数据目录。</param>
    public WorkflowDesignerSession(
        WorkflowDocument document,
        WorkflowNodeCatalog catalog,
        WorkflowPublicDataCatalog? publicDataCatalog = null)
    {
        Document = document ?? throw new ArgumentNullException(nameof(document));
        Catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        PublicDataCatalog = publicDataCatalog ?? new WorkflowPublicDataCatalog();
    }

    /// <summary>获取当前正式工作流文档。</summary>
    public WorkflowDocument Document { get; }

    /// <summary>获取当前设计器使用的画布投影。</summary>
    public WorkflowCanvasModel Canvas => Document.CanvasProjection;

    /// <summary>获取节点目录。</summary>
    public WorkflowNodeCatalog Catalog { get; }

    /// <summary>获取宿主声明的公共可绑定数据目录。</summary>
    public WorkflowPublicDataCatalog PublicDataCatalog { get; }

    /// <summary>获取或设置节点与手工路径点是否吸附到 24 单位设计栅格。</summary>
    public bool SnapToGrid { get; set; } = true;

    /// <summary>将画布坐标吸附到当前设计栅格。</summary>
    /// <param name="point">目标坐标。</param>
    public WorkflowPoint SnapPoint(WorkflowPoint point) => SnapToGrid
        ? new WorkflowPoint(Math.Round(point.X / 24) * 24, Math.Round(point.Y / 24) * 24)
        : point;

    /// <summary>按节点中心点吸附位置，避免不同尺寸节点无法居中对齐。</summary>
    /// <param name="node">目标画布节点或节点模型。</param>
    /// <param name="position">目标坐标或相对位置。</param>
    public WorkflowPoint SnapNodePosition(WorkflowCanvasNode node, WorkflowPoint position)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (!SnapToGrid) return position;
        var center = SnapPoint(new WorkflowPoint(
            position.X + node.Width / 2,
            position.Y + node.Height / 2));
        return new WorkflowPoint(center.X - node.Width / 2, center.Y - node.Height / 2);
    }

    /// <summary>获取或设置当前选中节点。</summary>
    public string? SelectedNodeId
    {
        get => _selectedNodeId;
        set
        {
            var normalized = value is not null && Canvas.Nodes.Any(item => item.Node.Id == value) ? value : null;
            if (_selectedNodeId == normalized
                && ((normalized is null && _selectedNodeIds.Count == 0)
                    || (normalized is not null && _selectedNodeIds.Count == 1 && _selectedNodeIds.Contains(normalized))))
            {
                return;
            }
            _selectedNodeId = normalized;
            _selectedNodeIds.Clear();
            if (normalized is not null)
            {
                _selectedNodeIds.Add(normalized);
                _selectedConnection = null;
            }
            RaiseChanged(WorkflowDesignerChangeKind.Selection);
        }
    }

    /// <summary>获取当前选中的全部节点 ID。</summary>
    public IReadOnlyCollection<string> SelectedNodeIds => _selectedNodeIds.ToArray();

    /// <summary>选择或切换一个节点。</summary>
    /// <param name="nodeId">节点标识。</param>
    /// <param name="additive">是否保留现有选择并增选。</param>
    public bool SelectNode(string nodeId, bool additive = false)
    {
        if (!Canvas.Nodes.Any(item => item.Node.Id == nodeId))
            return false;
        if (!additive)
            _selectedNodeIds.Clear();
        if (additive && !_selectedNodeIds.Add(nodeId))
            _selectedNodeIds.Remove(nodeId);
        else
            _selectedNodeIds.Add(nodeId);
        _selectedNodeId = _selectedNodeIds.Contains(nodeId)
            ? nodeId
            : _selectedNodeIds.LastOrDefault();
        _selectedConnection = null;
        RaiseChanged(WorkflowDesignerChangeKind.Selection);
        return true;
    }

    /// <summary>使用给定集合替换节点选择。</summary>
    /// <param name="nodeIds">要选择或处理的节点标识集合。</param>
    /// <param name="additive">是否保留现有选择并增选。</param>
    public void SelectNodes(IEnumerable<string> nodeIds, bool additive = false)
    {
        ArgumentNullException.ThrowIfNull(nodeIds);
        if (!additive)
            _selectedNodeIds.Clear();
        var validIds = Canvas.Nodes.Select(item => item.Node.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var nodeId in nodeIds.Where(validIds.Contains))
            _selectedNodeIds.Add(nodeId);
        _selectedNodeId = _selectedNodeIds.LastOrDefault();
        _selectedConnection = null;
        RaiseChanged(WorkflowDesignerChangeKind.Selection);
    }

    /// <summary>获取当前选中连接。</summary>
    public WorkflowConnectionModel? SelectedConnection => _selectedConnection;

    /// <summary>选择连接并清除节点选择。</summary>
    /// <param name="connection">目标连接。</param>
    public bool SelectConnection(WorkflowConnectionModel? connection)
    {
        if (connection is not null && !Canvas.Connections.Contains(connection))
            return false;
        if (ReferenceEquals(_selectedConnection, connection) && _selectedNodeId is null)
            return true;
        _selectedConnection = connection;
        _selectedNodeId = null;
        _selectedNodeIds.Clear();
        RaiseChanged(WorkflowDesignerChangeKind.Selection);
        return true;
    }

    /// <summary>获取当前缩放比例。</summary>
    public double Zoom { get; private set; } = 1;

    /// <summary>获取水平平移量（屏幕像素）。</summary>
    public double PanX { get; private set; } = 24;

    /// <summary>获取垂直平移量（屏幕像素）。</summary>
    public double PanY { get; private set; } = 24;

    /// <summary>获取最近一次运行快照。</summary>
    public WorkflowRuntimeSnapshot? RuntimeSnapshot => _runtimeSnapshot;

    /// <summary>获取是否可以撤销。</summary>
    public bool CanUndo => _undo.Count > 0;

    /// <summary>获取是否可以重做。</summary>
    public bool CanRedo => _redo.Count > 0;

    /// <summary>设计状态变化时发生。</summary>
    public event EventHandler<WorkflowDesignerChangedEventArgs>? Changed;

    /// <summary>返回按分类和显示名称排序的工具箱项目。</summary>
    public IReadOnlyList<WorkflowToolboxItem> GetToolboxItems() => Catalog.Snapshot().Values
        .Where(descriptor => descriptor.ModelType.GetCustomAttributes(typeof(System.ComponentModel.BrowsableAttribute), true)
            .OfType<System.ComponentModel.BrowsableAttribute>().All(attribute => attribute.Browsable))
        .Select(descriptor => new WorkflowToolboxItem(
            descriptor.NodeType,
            descriptor.DisplayName ?? descriptor.NodeType,
            descriptor.Category ?? "Other",
            descriptor.Description))
        .OrderBy(item => item.Category, StringComparer.Ordinal)
        .ThenBy(item => item.DisplayName, StringComparer.Ordinal)
        .ToArray();

    /// <summary>创建节点并加入画布。</summary>
    /// <param name="nodeType">节点类型键。</param>
    /// <param name="x">横坐标。</param>
    /// <param name="y">纵坐标。</param>
    public WorkflowCanvasNode AddNode(string nodeType, double x, double y)
    {
        var descriptor = Catalog.GetOrThrow(nodeType);
        var node = descriptor.Factory();
        node.Id = CreateUniqueNodeId(nodeType);
        node.Title = descriptor.DisplayName ?? nodeType;
        var maximumPortsOnSide = descriptor.GetPorts(node)
            .Where(port => port.Side is WorkflowPortSide.Left or WorkflowPortSide.Right)
            .GroupBy(port => port.Side)
            .Select(group => group.Count())
            .DefaultIfEmpty(0)
            .Max();
        var canvasNode = new WorkflowCanvasNode
        {
            Node = node,
            X = x,
            Y = y,
            Height = CalculateRequiredNodeHeight(maximumPortsOnSide)
        };
        EnsureNodeDisplaySize(canvasNode);
        var position = SnapNodePosition(canvasNode, new WorkflowPoint(canvasNode.X, canvasNode.Y));
        canvasNode.X = position.X;
        canvasNode.Y = position.Y;
        var previousEntryNodeId = Document.EntryNodeId;
        var assignAsEntry = string.IsNullOrWhiteSpace(previousEntryNodeId)
            && string.Equals(node.NodeType, "Start", StringComparison.Ordinal);
        Execute(new DesignerOperation(
            () =>
            {
                Canvas.Nodes.Add(canvasNode);
                if (assignAsEntry)
                    Document.EntryNodeId = node.Id;
            },
            () =>
            {
                Canvas.Nodes.Remove(canvasNode);
                if (assignAsEntry)
                    Document.EntryNodeId = previousEntryNodeId;
            }));
        SelectedNodeId = node.Id;
        return canvasNode;
    }

    /// <summary>
    /// 创建节点并插入现有连接；新节点必须同时具有输入和输出端口。
    /// 原连接会被替换为“原起点 → 新节点 → 原终点”，并作为一次 Undo 操作提交。
    /// </summary>
    /// <param name="nodeType">节点类型键。</param>
    /// <param name="x">横坐标。</param>
    /// <param name="y">纵坐标。</param>
    /// <param name="connection">目标连接。</param>
    /// <param name="inputSide">插入节点输入端口应使用的边。</param>
    /// <param name="outputSide">插入节点输出端口应使用的边。</param>
    public WorkflowCanvasNode? AddNodeOnConnection(
        string nodeType,
        double x,
        double y,
        WorkflowConnectionModel connection,
        WorkflowPortSide? inputSide = null,
        WorkflowPortSide? outputSide = null)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (!Canvas.Connections.Contains(connection))
            return null;
        var descriptor = Catalog.GetOrThrow(nodeType);
        var node = descriptor.Factory();
        node.Id = CreateUniqueNodeId(nodeType);
        node.Title = descriptor.DisplayName ?? nodeType;
        var ports = descriptor.GetPorts(node);
        var input = ports.FirstOrDefault(port => port.Direction == WorkflowPortDirection.Input);
        var output = ports.FirstOrDefault(port => port.Direction == WorkflowPortDirection.Output);
        if (input is null || output is null)
            return null;
        var maximumPortsOnSide = ports
            .Where(port => port.Side is WorkflowPortSide.Left or WorkflowPortSide.Right)
            .GroupBy(port => port.Side)
            .Select(group => group.Count())
            .DefaultIfEmpty(0)
            .Max();
        var canvasNode = new WorkflowCanvasNode
        {
            Node = node,
            X = x,
            Y = y,
            Height = CalculateRequiredNodeHeight(maximumPortsOnSide)
        };
        EnsureNodeDisplaySize(canvasNode);
        var position = SnapNodePosition(canvasNode, new WorkflowPoint(canvasNode.X, canvasNode.Y));
        canvasNode.X = position.X;
        canvasNode.Y = position.Y;
        if (inputSide.HasValue)
            canvasNode.SetPortSide(WorkflowPortDirection.Input, input.Key, inputSide.Value);
        if (outputSide.HasValue)
            canvasNode.SetPortSide(WorkflowPortDirection.Output, output.Key, outputSide.Value);
        var connectionIndex = Canvas.Connections.IndexOf(connection);
        var incoming = new WorkflowConnectionModel
        {
            FromNodeId = connection.FromNodeId,
            FromPort = connection.FromPort,
            ToNodeId = node.Id,
            ToPort = input.Key,
            FromSide = connection.FromSide,
            ToSide = inputSide
        };
        var outgoing = new WorkflowConnectionModel
        {
            FromNodeId = node.Id,
            FromPort = output.Key,
            ToNodeId = connection.ToNodeId,
            ToPort = connection.ToPort,
            FromSide = outputSide,
            ToSide = connection.ToSide
        };
        Execute(new DesignerOperation(
            () =>
            {
                Canvas.Connections.Remove(connection);
                Canvas.Nodes.Add(canvasNode);
                Canvas.Connections.Insert(Math.Min(connectionIndex, Canvas.Connections.Count), incoming);
                Canvas.Connections.Insert(Math.Min(connectionIndex + 1, Canvas.Connections.Count), outgoing);
            },
            () =>
            {
                Canvas.Connections.Remove(incoming);
                Canvas.Connections.Remove(outgoing);
                Canvas.Nodes.Remove(canvasNode);
                Canvas.Connections.Insert(Math.Min(connectionIndex, Canvas.Connections.Count), connection);
            }));
        SelectedNodeId = node.Id;
        return canvasNode;
    }

    /// <summary>删除节点及其全部连接。</summary>
    /// <param name="nodeId">节点标识。</param>
    public bool RemoveNode(string nodeId)
    {
        var canvasNode = Canvas.Nodes.FirstOrDefault(item => item.Node.Id == nodeId);
        if (canvasNode is null)
            return false;
        var nodeIndex = Canvas.Nodes.IndexOf(canvasNode);
        var connections = Canvas.Connections
            .Select((connection, index) => (Connection: connection, Index: index))
            .Where(item => item.Connection.FromNodeId == nodeId || item.Connection.ToNodeId == nodeId)
            .ToArray();
        var previousEntryNodeId = Document.EntryNodeId;
        var removesEntry = string.Equals(previousEntryNodeId, nodeId, StringComparison.Ordinal);
        Execute(new DesignerOperation(
            () =>
            {
                foreach (var item in connections.OrderByDescending(item => item.Index))
                    Canvas.Connections.Remove(item.Connection);
                Canvas.Nodes.Remove(canvasNode);
                if (removesEntry)
                    Document.EntryNodeId = string.Empty;
            },
            () =>
            {
                Canvas.Nodes.Insert(Math.Min(nodeIndex, Canvas.Nodes.Count), canvasNode);
                foreach (var item in connections.OrderBy(item => item.Index))
                    Canvas.Connections.Insert(Math.Min(item.Index, Canvas.Connections.Count), item.Connection);
                if (removesEntry)
                    Document.EntryNodeId = previousEntryNodeId;
            }));
        if (_selectedNodeIds.Remove(nodeId))
        {
            _selectedNodeId = _selectedNodeIds.LastOrDefault();
            RaiseChanged(WorkflowDesignerChangeKind.Selection);
        }
        if (_selectedConnection is not null
            && connections.Any(item => ReferenceEquals(item.Connection, _selectedConnection)))
        {
            SelectConnection(null);
        }
        return true;
    }

    /// <summary>将现有节点设置为文档入口，并记录 Undo/Redo。</summary>
    /// <param name="nodeId">必须存在于当前文档语义图中的节点 ID。</param>
    /// <returns>入口发生变化时返回 true。</returns>
    public bool SetEntryNode(string nodeId)
    {
        _ = GetCanvasNodeOrThrow(nodeId);
        if (string.Equals(Document.EntryNodeId, nodeId, StringComparison.Ordinal))
            return false;
        var previous = Document.EntryNodeId;
        Execute(new DesignerOperation(
            () => Document.EntryNodeId = nodeId,
            () => Document.EntryNodeId = previous));
        return true;
    }

    /// <summary>移动节点并记录一次可撤销操作。</summary>
    /// <param name="nodeId">节点标识。</param>
    /// <param name="x">横坐标。</param>
    /// <param name="y">纵坐标。</param>
    public bool MoveNode(string nodeId, double x, double y)
    {
        var item = Canvas.Nodes.FirstOrDefault(node => node.Node.Id == nodeId);
        if (item is null)
            return false;
        var snapped = SnapNodePosition(item, new WorkflowPoint(x, y));
        x = snapped.X;
        y = snapped.Y;
        if (item.X.Equals(x) && item.Y.Equals(y))
            return false;
        var oldX = item.X;
        var oldY = item.Y;
        Execute(new DesignerOperation(
            () => { item.X = x; item.Y = y; },
            () => { item.X = oldX; item.Y = oldY; }));
        return true;
    }

    /// <summary>以单个 Undo 操作移动多个节点。</summary>
    /// <param name="positions">节点标识到目标位置的映射。</param>
    public bool MoveNodes(IReadOnlyDictionary<string, WorkflowPoint> positions)
    {
        ArgumentNullException.ThrowIfNull(positions);
        var changes = Canvas.Nodes
            .Where(item => positions.ContainsKey(item.Node.Id))
            .Select(item => (Node: item, Old: new WorkflowPoint(item.X, item.Y), New: positions[item.Node.Id]))
            .Where(item => item.Old != item.New)
            .ToArray();
        if (changes.Length == 0)
            return false;
        var deltas = changes.ToDictionary(
            item => item.Node.Node.Id,
            item => new WorkflowPoint(item.New.X - item.Old.X, item.New.Y - item.Old.Y),
            StringComparer.Ordinal);
        var waypointChanges = Canvas.Connections
            .Where(connection => connection.Waypoints.Count > 0
                && (deltas.ContainsKey(connection.FromNodeId) || deltas.ContainsKey(connection.ToNodeId)))
            .Select(connection =>
            {
                var oldPoints = connection.Waypoints.ToArray();
                var moveFrom = deltas.TryGetValue(connection.FromNodeId, out var fromDelta);
                var moveTo = deltas.TryGetValue(connection.ToNodeId, out var toDelta);
                var newPoints = moveFrom && moveTo && NearlyEqual(fromDelta, toDelta)
                    ? oldPoints.Select(point => new WorkflowPoint(point.X + fromDelta.X, point.Y + fromDelta.Y)).ToArray()
                    : Array.Empty<WorkflowPoint>();
                return (Connection: connection, Old: oldPoints, New: newPoints);
            })
            .ToArray();
        Execute(new DesignerOperation(
            () =>
            {
                foreach (var change in changes) { change.Node.X = change.New.X; change.Node.Y = change.New.Y; }
                foreach (var change in waypointChanges) SetWaypoints(change.Connection, change.New);
            },
            () =>
            {
                foreach (var change in changes) { change.Node.X = change.Old.X; change.Node.Y = change.Old.Y; }
                foreach (var change in waypointChanges) SetWaypoints(change.Connection, change.Old);
            }));
        return true;
    }

    /// <summary>删除全部选中节点，并作为一次 Undo 操作。</summary>
    public bool RemoveSelectedNodes()
    {
        var selected = new HashSet<string>(_selectedNodeIds, StringComparer.Ordinal);
        if (selected.Count == 0)
            return false;
        var nodes = Canvas.Nodes
            .Select((node, index) => (Node: node, Index: index))
            .Where(item => selected.Contains(item.Node.Node.Id))
            .ToArray();
        var connections = Canvas.Connections
            .Select((connection, index) => (Connection: connection, Index: index))
            .Where(item => selected.Contains(item.Connection.FromNodeId) || selected.Contains(item.Connection.ToNodeId))
            .ToArray();
        var previousEntryNodeId = Document.EntryNodeId;
        var removesEntry = selected.Contains(previousEntryNodeId);
        Execute(new DesignerOperation(
            () =>
            {
                foreach (var item in connections.OrderByDescending(item => item.Index)) Canvas.Connections.Remove(item.Connection);
                foreach (var item in nodes.OrderByDescending(item => item.Index)) Canvas.Nodes.Remove(item.Node);
                if (removesEntry) Document.EntryNodeId = string.Empty;
            },
            () =>
            {
                foreach (var item in nodes.OrderBy(item => item.Index)) Canvas.Nodes.Insert(Math.Min(item.Index, Canvas.Nodes.Count), item.Node);
                foreach (var item in connections.OrderBy(item => item.Index)) Canvas.Connections.Insert(Math.Min(item.Index, Canvas.Connections.Count), item.Connection);
                if (removesEntry) Document.EntryNodeId = previousEntryNodeId;
            }));
        _selectedNodeIds.Clear();
        _selectedNodeId = null;
        RaiseChanged(WorkflowDesignerChangeKind.Selection);
        return true;
    }

    /// <summary>复制当前单选或框选节点，以及选中节点之间的内部连接。</summary>
    public bool CopySelection()
    {
        var selected = new HashSet<string>(_selectedNodeIds, StringComparer.Ordinal);
        if (selected.Count == 0 && _selectedNodeId is not null)
            selected.Add(_selectedNodeId);
        if (selected.Count == 0) return false;
        var clipboardDocument = new WorkflowDocument { Name = "Clipboard" };
        var clipboardCanvas = clipboardDocument.CanvasProjection;
        foreach (var node in Canvas.Nodes.Where(item => selected.Contains(item.Node.Id)))
            clipboardCanvas.Nodes.Add(node);
        foreach (var connection in Canvas.Connections.Where(item =>
                     selected.Contains(item.FromNodeId) && selected.Contains(item.ToNodeId)))
        {
            clipboardCanvas.Connections.Add(connection);
        }
        clipboardDocument.EntryNodeId = clipboardCanvas.Nodes[0].Node.Id;
        var json = new WorkflowDocumentJsonStore(Catalog).Serialize(clipboardDocument);
        lock (ClipboardSync)
        {
            s_clipboardJson = json;
            s_clipboardPasteCount = 0;
        }
        return true;
    }

    /// <summary>粘贴最近复制的节点，并将整组内容偏移一个栅格。</summary>
    public IReadOnlyList<WorkflowCanvasNode> PasteSelection()
    {
        string? json;
        int pasteCount;
        lock (ClipboardSync)
        {
            json = s_clipboardJson;
            pasteCount = ++s_clipboardPasteCount;
        }
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<WorkflowCanvasNode>();
        var source = new WorkflowDocumentJsonStore(Catalog).Deserialize(json).Canvas;
        var offset = 24d * pasteCount;
        var reserved = new HashSet<string>(Canvas.Nodes.Select(item => item.Node.Id), StringComparer.Ordinal);
        var idMap = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var node in source.Nodes)
        {
            var oldId = node.Node.Id;
            var newId = CreateUniqueNodeId(node.Node.NodeType, reserved);
            reserved.Add(newId);
            idMap[oldId] = newId;
            node.Node.Id = newId;
            // 粘贴产生的是新的逻辑脚本，必须重新生成标识，避免两个节点共享热版本槽。
            if (node.Node is IWorkflowScriptNode scriptNode)
                scriptNode.ScriptId = Guid.NewGuid().ToString("N");
            node.X += offset;
            node.Y += offset;
        }
        foreach (var connection in source.Connections)
        {
            connection.FromNodeId = idMap[connection.FromNodeId];
            connection.ToNodeId = idMap[connection.ToNodeId];
            for (var index = 0; index < connection.Waypoints.Count; index++)
            {
                var point = connection.Waypoints[index];
                connection.Waypoints[index] = new WorkflowPoint(point.X + offset, point.Y + offset);
            }
        }
        var nodes = source.Nodes.ToArray();
        var connections = source.Connections.ToArray();
        Execute(new DesignerOperation(
            () =>
            {
                foreach (var node in nodes) Canvas.Nodes.Add(node);
                foreach (var connection in connections) Canvas.Connections.Add(connection);
            },
            () =>
            {
                foreach (var connection in connections) Canvas.Connections.Remove(connection);
                foreach (var node in nodes) Canvas.Nodes.Remove(node);
            }));
        SelectNodes(nodes.Select(item => item.Node.Id));
        return nodes;
    }

    /// <summary>按指定方式对齐选中节点。</summary>
    /// <param name="alignment">对齐方式。</param>
    public bool AlignSelectedNodes(WorkflowNodeAlignment alignment)
    {
        var nodes = Canvas.Nodes.Where(item => _selectedNodeIds.Contains(item.Node.Id)).ToArray();
        if (nodes.Length < 2)
            return false;
        var left = nodes.Min(node => node.X);
        var right = nodes.Max(node => node.X + node.Width);
        var top = nodes.Min(node => node.Y);
        var bottom = nodes.Max(node => node.Y + node.Height);
        var positions = nodes.ToDictionary(
            node => node.Node.Id,
            node => alignment switch
            {
                WorkflowNodeAlignment.Left => new WorkflowPoint(left, node.Y),
                WorkflowNodeAlignment.Right => new WorkflowPoint(right - node.Width, node.Y),
                WorkflowNodeAlignment.Top => new WorkflowPoint(node.X, top),
                WorkflowNodeAlignment.Bottom => new WorkflowPoint(node.X, bottom - node.Height),
                WorkflowNodeAlignment.HorizontalCenter => new WorkflowPoint((left + right - node.Width) / 2, node.Y),
                WorkflowNodeAlignment.VerticalCenter => new WorkflowPoint(node.X, (top + bottom - node.Height) / 2),
                _ => throw new NotSupportedException($"不支持的对齐方式：{alignment}。")
            },
            StringComparer.Ordinal);
        return MoveNodes(positions);
    }

    /// <summary>在首尾节点之间等距分布选中节点。</summary>
    /// <param name="distribution">分布方向。</param>
    public bool DistributeSelectedNodes(WorkflowNodeDistribution distribution)
    {
        var nodes = Canvas.Nodes.Where(item => _selectedNodeIds.Contains(item.Node.Id)).ToArray();
        if (nodes.Length < 3)
            return false;
        var positions = nodes.ToDictionary(node => node.Node.Id, node => new WorkflowPoint(node.X, node.Y), StringComparer.Ordinal);
        if (distribution == WorkflowNodeDistribution.Horizontal)
        {
            var ordered = nodes.OrderBy(node => node.X + node.Width / 2).ToArray();
            var first = ordered[0].X + ordered[0].Width / 2;
            var last = ordered[^1].X + ordered[^1].Width / 2;
            for (var index = 1; index < ordered.Length - 1; index++)
                positions[ordered[index].Node.Id] = new WorkflowPoint(first + (last - first) * index / (ordered.Length - 1) - ordered[index].Width / 2, ordered[index].Y);
        }
        else
        {
            var ordered = nodes.OrderBy(node => node.Y + node.Height / 2).ToArray();
            var first = ordered[0].Y + ordered[0].Height / 2;
            var last = ordered[^1].Y + ordered[^1].Height / 2;
            for (var index = 1; index < ordered.Length - 1; index++)
                positions[ordered[index].Node.Id] = new WorkflowPoint(ordered[index].X, first + (last - first) * index / (ordered.Length - 1) - ordered[index].Height / 2);
        }
        return MoveNodes(positions);
    }

    /// <summary>按连接方向执行稳定的分层自动布局。</summary>
    /// <param name="layerSpacing">自动布局的层间距。</param>
    /// <param name="nodeSpacing">同层节点间距。</param>
    public bool AutoLayout(double layerSpacing = 260, double nodeSpacing = 110)
    {
        if (Canvas.Nodes.Count == 0)
            return false;
        var nodeIds = Canvas.Nodes.Select(item => item.Node.Id).ToArray();
        var incoming = nodeIds.ToDictionary(
            id => id,
            id => Canvas.Connections.Count(connection => connection.ToNodeId == id),
            StringComparer.Ordinal);
        var layers = new Dictionary<string, int>(StringComparer.Ordinal);
        var roots = nodeIds.Where(id => incoming[id] == 0).OrderBy(id => id, StringComparer.Ordinal).ToArray();
        foreach (var root in roots)
            layers[root] = 0;
        var queue = new Queue<string>(roots);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            var layer = layers.GetValueOrDefault(current);
            foreach (var target in Canvas.Connections.Where(connection => connection.FromNodeId == current).Select(connection => connection.ToNodeId).Distinct(StringComparer.Ordinal))
            {
                layers[target] = Math.Max(layers.GetValueOrDefault(target), layer + 1);
                incoming[target]--;
                if (incoming[target] == 0) queue.Enqueue(target);
            }
        }
        var fallbackLayer = layers.Count == 0 ? 0 : layers.Values.Max() + 1;
        foreach (var id in nodeIds.Where(id => !layers.ContainsKey(id)).OrderBy(id => id, StringComparer.Ordinal))
            layers[id] = fallbackLayer++;
        var positions = new Dictionary<string, WorkflowPoint>(StringComparer.Ordinal);
        foreach (var group in Canvas.Nodes.GroupBy(node => layers[node.Node.Id]).OrderBy(group => group.Key))
        {
            var row = 0;
            foreach (var node in group.OrderBy(item => item.Node.Id, StringComparer.Ordinal))
                positions[node.Node.Id] = new WorkflowPoint(group.Key * layerSpacing, row++ * nodeSpacing);
        }
        return MoveNodes(positions);
    }

    /// <summary>创建经过端口方向、基数和重复检查的连接。</summary>
    /// <param name="fromNodeId">源节点标识。</param>
    /// <param name="fromPort">源输出端口键。</param>
    /// <param name="toNodeId">目标节点标识。</param>
    /// <param name="toPort">目标输入端口键。</param>
    /// <param name="fromSide">源连接端点所在边。</param>
    /// <param name="toSide">目标连接端点所在边。</param>
    public WorkflowConnectionModel Connect(
        string fromNodeId,
        string fromPort,
        string toNodeId,
        string toPort = WorkflowPorts.Input,
        WorkflowPortSide? fromSide = null,
        WorkflowPortSide? toSide = null)
    {
        if (fromNodeId == toNodeId)
            throw new InvalidOperationException("当前编辑器不允许节点连接到自身。");
        var source = GetCanvasNodeOrThrow(fromNodeId);
        var target = GetCanvasNodeOrThrow(toNodeId);
        var sourcePort = GetPortOrThrow(source.Node, fromPort, WorkflowPortDirection.Output);
        var targetPort = GetPortOrThrow(target.Node, toPort, WorkflowPortDirection.Input);
        if (Canvas.Connections.Any(connection =>
                connection.FromNodeId == fromNodeId && connection.FromPort == fromPort
                && connection.ToNodeId == toNodeId && connection.ToPort == toPort))
        {
            throw new InvalidOperationException("该连接已经存在。");
        }
        var sourceCount = Canvas.Connections.Count(connection =>
            connection.State == WorkflowConnectionState.Active
            && connection.FromNodeId == fromNodeId && connection.FromPort == fromPort);
        var targetCount = Canvas.Connections.Count(connection =>
            connection.State == WorkflowConnectionState.Active
            && connection.ToNodeId == toNodeId && connection.ToPort == toPort);
        if (sourceCount >= sourcePort.MaxConnections)
            throw new InvalidOperationException($"输出端口 {fromNodeId}:{fromPort} 已达到连接上限。");
        if (targetCount >= targetPort.MaxConnections)
            throw new InvalidOperationException($"输入端口 {toNodeId}:{toPort} 已达到连接上限。");

        var connection = new WorkflowConnectionModel
        {
            FromNodeId = fromNodeId,
            FromPort = fromPort,
            ToNodeId = toNodeId,
            ToPort = toPort,
            FromSide = fromSide,
            ToSide = toSide
        };
        Execute(new DesignerOperation(
            () => Canvas.Connections.Add(connection),
            () => Canvas.Connections.Remove(connection)));
        return connection;
    }

    /// <summary>在连接中追加手工路径点。</summary>
    /// <param name="connection">目标连接。</param>
    /// <param name="point">目标坐标。</param>
    public void AddConnectionWaypoint(WorkflowConnectionModel connection, WorkflowPoint point)
    {
        ValidateConnection(connection);
        var before = connection.Waypoints.ToArray();
        var after = NormalizeWaypoints(before.Append(SnapPoint(point)));
        Execute(new DesignerOperation(
            () => SetWaypoints(connection, after),
            () => SetWaypoints(connection, before)));
        SelectConnection(connection);
    }

    /// <summary>移动一个手工路径点。</summary>
    /// <param name="connection">目标连接。</param>
    /// <param name="index">目标元素索引。</param>
    /// <param name="point">目标坐标。</param>
    public bool MoveConnectionWaypoint(
        WorkflowConnectionModel connection,
        int index,
        WorkflowPoint point)
    {
        ValidateConnection(connection);
        if (index < 0 || index >= connection.Waypoints.Count)
            return false;
        var before = connection.Waypoints.ToArray();
        if (before[index] == point)
            return false;
        var changed = before.ToArray();
        changed[index] = SnapPoint(point);
        var after = NormalizeWaypoints(changed);
        Execute(new DesignerOperation(
            () => SetWaypoints(connection, after),
            () => SetWaypoints(connection, before)));
        SelectConnection(connection);
        return true;
    }

    /// <summary>删除一个手工路径点。</summary>
    /// <param name="connection">目标连接。</param>
    /// <param name="index">目标元素索引。</param>
    public bool RemoveConnectionWaypoint(WorkflowConnectionModel connection, int index)
    {
        ValidateConnection(connection);
        if (index < 0 || index >= connection.Waypoints.Count)
            return false;
        var before = connection.Waypoints.ToArray();
        var after = NormalizeWaypoints(before.Where((_, itemIndex) => itemIndex != index));
        Execute(new DesignerOperation(
            () => SetWaypoints(connection, after),
            () => SetWaypoints(connection, before)));
        SelectConnection(connection);
        return true;
    }

    /// <summary>设置连线标签沿路径的位置，并记录一次可撤销操作。</summary>
    /// <param name="connection">目标连接。</param>
    /// <param name="position">目标坐标或相对位置。</param>
    public bool SetConnectionLabelPosition(WorkflowConnectionModel connection, double position)
    {
        ValidateConnection(connection);
        var normalized = Math.Clamp(position, 0.05, 0.95);
        if (Math.Abs(connection.LabelPosition - normalized) < 0.0001)
            return false;
        var old = connection.LabelPosition;
        Execute(new DesignerOperation(
            () => connection.LabelPosition = normalized,
            () => connection.LabelPosition = old));
        return true;
    }

    /// <summary>删除连接。</summary>
    /// <param name="connection">目标连接。</param>
    public bool RemoveConnection(WorkflowConnectionModel connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        var index = Canvas.Connections.IndexOf(connection);
        if (index < 0)
            return false;
        Execute(new DesignerOperation(
            () => Canvas.Connections.Remove(connection),
            () => Canvas.Connections.Insert(Math.Min(index, Canvas.Connections.Count), connection)));
        if (ReferenceEquals(_selectedConnection, connection))
            SelectConnection(null);
        return true;
    }

    /// <summary>设置缩放和平移视口。</summary>
    /// <param name="zoom">缩放比例。</param>
    /// <param name="panX">水平平移量。</param>
    /// <param name="panY">垂直平移量。</param>
    public void SetViewport(double zoom, double panX, double panY)
    {
        var normalizedZoom = Math.Clamp(zoom, 0.25, 2.5);
        if (Zoom.Equals(normalizedZoom) && PanX.Equals(panX) && PanY.Equals(panY))
            return;
        Zoom = normalizedZoom;
        PanX = panX;
        PanY = panY;
        RaiseChanged(WorkflowDesignerChangeKind.Viewport);
    }

    /// <summary>适合画布时允许的最大缩放；节点较少时保持 100%，避免标题和端口被放大到与界面其他文字不协调。</summary>
    public const double MaximumFitZoom = 1.0;

    /// <summary>调整视口，使全部节点适合指定屏幕区域；只缩小不放大超过 <see cref="MaximumFitZoom"/>。</summary>
    /// <param name="viewportWidth">视口宽度。</param>
    /// <param name="viewportHeight">视口高度。</param>
    /// <param name="padding">内容与视口边缘的预留距离。</param>
    public void FitToView(double viewportWidth, double viewportHeight, double padding = 48)
    {
        if (viewportWidth <= 0 || viewportHeight <= 0 || Canvas.Nodes.Count == 0)
            return;
        var left = Canvas.Nodes.Min(node => node.X);
        var top = Canvas.Nodes.Min(node => node.Y);
        var right = Canvas.Nodes.Max(node => node.X + node.Width);
        var bottom = Canvas.Nodes.Max(node => node.Y + node.Height);
        var contentWidth = Math.Max(1, right - left);
        var contentHeight = Math.Max(1, bottom - top);
        var zoom = Math.Clamp(
            Math.Min(
                Math.Max(1, viewportWidth - padding * 2) / contentWidth,
                Math.Max(1, viewportHeight - padding * 2) / contentHeight),
            0.25,
            MaximumFitZoom);
        var panX = (viewportWidth - contentWidth * zoom) / 2 - left * zoom;
        var panY = (viewportHeight - contentHeight * zoom) / 2 - top * zoom;
        SetViewport(zoom, panX, panY);
    }

    /// <summary>整体替换连接折线路径点，并加入 Undo/Redo。</summary>
    /// <param name="connection">目标连接。</param>
    /// <param name="waypoints">新的连接拐点集合。</param>
    public void ReplaceConnectionWaypoints(
        WorkflowConnectionModel connection,
        IEnumerable<WorkflowPoint> waypoints)
    {
        ValidateConnection(connection);
        ArgumentNullException.ThrowIfNull(waypoints);
        var before = connection.Waypoints.ToArray();
        var after = NormalizeWaypoints(waypoints.Select(SnapPoint));
        Execute(new DesignerOperation(
            () => SetWaypoints(connection, after),
            () => SetWaypoints(connection, before)));
    }

    /// <summary>
    /// 提交一次节点配置事务。节点全部字段、派生显示尺寸和连接有效性作为一个 Undo/Redo 操作提交。
    /// </summary>
    /// <param name="nodeId">要修改的节点 ID。</param>
    /// <param name="change">针对现有节点实例执行的配置修改。</param>
    /// <param name="hiddenOutputPorts">可选的设计器隐藏端口集合；为空时保持现状。</param>
    public void ExecuteNodeConfigurationChange(
        string nodeId,
        Action<IWorkflowNodeModel> change,
        IReadOnlyCollection<string>? hiddenOutputPorts = null)
    {
        ArgumentNullException.ThrowIfNull(change);
        var canvasNode = GetCanvasNodeOrThrow(nodeId);
        var before = CaptureConfigurationState(canvasNode);
        ConfigurationState after;
        try
        {
            change(canvasNode.Node);
            if (hiddenOutputPorts is not null)
            {
                canvasNode.HiddenOutputPorts.Clear();
                foreach (var portKey in hiddenOutputPorts)
                    canvasNode.HiddenOutputPorts.Add(portKey);
            }
            EnsureNodeDisplaySize(canvasNode);
            RefreshConnectionStates();
            after = CaptureConfigurationState(canvasNode);
        }
        catch
        {
            ApplyConfigurationState(canvasNode, before);
            throw;
        }

        ApplyConfigurationState(canvasNode, before);
        Execute(new DesignerOperation(
            () => ApplyConfigurationState(canvasNode, after),
            () => ApplyConfigurationState(canvasNode, before)));
    }

    /// <summary>
    /// 刷新派生显示状态但不创建历史记录。正式属性编辑应调用
    /// <see cref="ExecuteNodeConfigurationChange"/>。
    /// </summary>
    public void NotifyNodeConfigurationChanged()
    {
        foreach (var node in Canvas.Nodes)
            EnsureNodeDisplaySize(node);
        RefreshConnectionStates();
        RaiseChanged(WorkflowDesignerChangeKind.Document);
    }

    /// <summary>设置节点实例的端口边，并加入 Undo/Redo。</summary>
    /// <param name="nodeId">节点标识。</param>
    /// <param name="direction">端口方向或路径方向。</param>
    /// <param name="portKey">端口键。</param>
    /// <param name="side">端口所在边。</param>
    public void SetPortSide(
        string nodeId,
        WorkflowPortDirection direction,
        string portKey,
        WorkflowPortSide side)
    {
        var node = GetCanvasNodeOrThrow(nodeId);
        var port = GetPorts(nodeId, direction).FirstOrDefault(item => item.Key == portKey)
            ?? throw new KeyNotFoundException($"节点 {nodeId} 不存在端口 {direction}:{portKey}。");
        var key = $"{direction}:{portKey}";
        var hadOverride = node.PortSides.TryGetValue(key, out var oldOverride);
        var oldSide = node.GetPortSide(port);
        if (oldSide == side)
            return;
        var affectedConnections = Canvas.Connections
            .Where(connection => direction == WorkflowPortDirection.Output
                ? connection.FromNodeId == nodeId && connection.FromPort == portKey
                  && (connection.FromSide ?? oldSide) == oldSide
                : connection.ToNodeId == nodeId && connection.ToPort == portKey
                  && (connection.ToSide ?? oldSide) == oldSide)
            .Select(connection => (
                Connection: connection,
                OldSide: direction == WorkflowPortDirection.Output ? connection.FromSide : connection.ToSide))
            .ToArray();
        Execute(new DesignerOperation(
            () =>
            {
                node.SetPortSide(direction, portKey, side);
                foreach (var item in affectedConnections)
                {
                    if (direction == WorkflowPortDirection.Output) item.Connection.FromSide = side;
                    else item.Connection.ToSide = side;
                }
                EnsureNodeDisplaySize(node);
            },
            () =>
            {
                if (hadOverride) node.SetPortSide(direction, portKey, oldOverride);
                else node.PortSides.Remove(key);
                foreach (var item in affectedConnections)
                {
                    if (direction == WorkflowPortDirection.Output) item.Connection.FromSide = item.OldSide;
                    else item.Connection.ToSide = item.OldSide;
                }
                EnsureNodeDisplaySize(node);
            }));
    }

    /// <summary>更新运行时覆盖层。</summary>
    /// <param name="snapshot">运行时快照。</param>
    public void SetRuntimeSnapshot(WorkflowRuntimeSnapshot? snapshot)
    {
        _runtimeSnapshot = snapshot;
        RaiseChanged(WorkflowDesignerChangeKind.Runtime);
    }

    /// <summary>
    /// 宿主提供的最近一次节点输出查询，由运行绑定在应用快照时设置；未接入运行时为空。
    /// </summary>
    public Func<string, WorkflowNodeOutput?>? NodeOutputProvider { get; set; }

    /// <summary>获取节点在当前运行快照所属运行中的最近一次输出；不属于本画布当前运行的输出不会返回。</summary>
    /// <param name="nodeId">节点标识。</param>
    public WorkflowNodeOutput? GetLatestNodeOutput(string nodeId) =>
        _runtimeSnapshot is { } snapshot && NodeOutputProvider?.Invoke(nodeId) is { } output && output.RunId == snapshot.RunId
            ? output
            : null;

    /// <summary>获取节点最近运行状态。</summary>
    /// <param name="nodeId">节点标识。</param>
    public E_NodeState GetNodeState(string nodeId) =>
        GetNodeRuntimeInfo(nodeId)?.State ?? E_NodeState.Idle;

    /// <summary>获取节点最近一次执行的运行信息。</summary>
    /// <param name="nodeId">节点标识。</param>
    public WorkflowNodeRuntimeInfo? GetNodeRuntimeInfo(string nodeId) =>
        _runtimeSnapshot?.Nodes.TryGetValue(nodeId, out var info) == true ? info : null;

    /// <summary>获取当前停留在指定节点的活动 Token ID。</summary>
    /// <param name="nodeId">节点标识。</param>
    public IReadOnlyList<long> GetActiveTokenIds(string nodeId) =>
        _runtimeSnapshot?.ActiveTokens.Values
            .Where(token => string.Equals(token.CurrentNodeId, nodeId, StringComparison.Ordinal))
            .Select(token => token.TokenId)
            .OrderBy(id => id)
            .ToArray() ?? Array.Empty<long>();

    /// <summary>显式规范化当前文档布局，并作为一个可撤销操作提交。</summary>
    /// <returns>至少一个节点尺寸、位置或连接路径发生变化时返回 true。</returns>
    public bool NormalizeLayout()
    {
        var beforeNodes = Canvas.Nodes.ToDictionary(
            item => item.Node.Id,
            item => new NodeLayoutState(item.X, item.Y, item.Width, item.Height),
            StringComparer.Ordinal);
        var beforeRoutes = Canvas.Connections.ToDictionary(
            connection => connection,
            connection => connection.Waypoints.ToArray());

        foreach (var node in Canvas.Nodes)
        {
            EnsureNodeDisplaySize(node);
            var position = SnapNodePosition(node, new WorkflowPoint(node.X, node.Y));
            node.X = position.X;
            node.Y = position.Y;
        }
        foreach (var connection in Canvas.Connections)
            SetWaypoints(connection, NormalizeWaypoints(connection.Waypoints));

        var afterNodes = Canvas.Nodes.ToDictionary(
            item => item.Node.Id,
            item => new NodeLayoutState(item.X, item.Y, item.Width, item.Height),
            StringComparer.Ordinal);
        var afterRoutes = Canvas.Connections.ToDictionary(
            connection => connection,
            connection => connection.Waypoints.ToArray());
        var changed = beforeNodes.Any(pair => !afterNodes.TryGetValue(pair.Key, out var value) || value != pair.Value)
            || beforeRoutes.Any(pair => !pair.Value.SequenceEqual(afterRoutes[pair.Key]));
        RestoreLayout(beforeNodes, beforeRoutes);
        if (!changed)
            return false;

        Execute(new DesignerOperation(
            () => RestoreLayout(afterNodes, afterRoutes),
            () => RestoreLayout(beforeNodes, beforeRoutes)));
        return true;
    }

    /// <summary>执行由专用属性编辑器提供的可撤销文档操作。</summary>
    /// <param name="execute">执行文档修改的委托。</param>
    /// <param name="undo">撤销文档修改的委托。</param>
    public void ExecuteDocumentOperation(Action execute, Action undo)
    {
        ArgumentNullException.ThrowIfNull(execute);
        ArgumentNullException.ThrowIfNull(undo);
        Execute(new DesignerOperation(execute, undo));
    }

    /// <summary>撤销最近一次文档操作。</summary>
    public bool Undo()
    {
        if (!_undo.TryPop(out var operation))
            return false;
        operation.Undo();
        _redo.Push(operation);
        RaiseChanged(WorkflowDesignerChangeKind.Document);
        return true;
    }

    /// <summary>重做最近一次撤销操作。</summary>
    public bool Redo()
    {
        if (!_redo.TryPop(out var operation))
            return false;
        operation.Do();
        _undo.Push(operation);
        RaiseChanged(WorkflowDesignerChangeKind.Document);
        return true;
    }

    /// <summary>获取节点指定方向且在设计器中启用的端口。</summary>
    /// <param name="nodeId">节点标识。</param>
    /// <param name="direction">端口方向或路径方向。</param>
    public IReadOnlyList<WorkflowPortDescriptor> GetPorts(string nodeId, WorkflowPortDirection direction)
    {
        var canvasNode = GetCanvasNodeOrThrow(nodeId);
        return GetDeclaredPorts(nodeId, direction)
            .Where(port => direction != WorkflowPortDirection.Output
                || !canvasNode.HiddenOutputPorts.Contains(port.Key))
            .ToArray();
    }

    /// <summary>获取节点声明的全部端口，包括在设计器中禁用的输出端口。</summary>
    /// <param name="nodeId">节点标识。</param>
    /// <param name="direction">端口方向或路径方向。</param>
    public IReadOnlyList<WorkflowPortDescriptor> GetDeclaredPorts(string nodeId, WorkflowPortDirection direction)
    {
        var node = GetCanvasNodeOrThrow(nodeId).Node;
        return Catalog.TryGet(node.NodeType, out var descriptor)
            ? descriptor!.GetPorts(node).Where(port => port.Direction == direction).ToArray()
            : Array.Empty<WorkflowPortDescriptor>();
    }

    /// <summary>显示或隐藏多输出节点的指定端口；布局状态不得删除或改变语义连接。</summary>
    /// <param name="nodeId">节点标识。</param>
    /// <param name="portKey">端口键。</param>
    /// <param name="visible">是否显示端口。</param>
    public bool SetOutputPortVisible(string nodeId, string portKey, bool visible)
    {
        var node = GetCanvasNodeOrThrow(nodeId);
        var outputs = GetDeclaredPorts(nodeId, WorkflowPortDirection.Output);
        if (outputs.Count <= 1 || outputs.All(port => port.Key != portKey)) return false;
        var wasVisible = !node.HiddenOutputPorts.Contains(portKey);
        if (wasVisible == visible) return false;
        Execute(new DesignerOperation(
            () =>
            {
                if (visible) node.HiddenOutputPorts.Remove(portKey);
                else node.HiddenOutputPorts.Add(portKey);
                EnsureNodeDisplaySize(node);
            },
            () =>
            {
                if (wasVisible) node.HiddenOutputPorts.Remove(portKey);
                else node.HiddenOutputPorts.Add(portKey);
                EnsureNodeDisplaySize(node);
            }));
        return true;
    }

    /// <summary>执行设计器操作、维护撤销栈并发出变更通知。</summary>
    /// <param name="operation">要执行并登记的设计器操作。</param>
    private void Execute(DesignerOperation operation)
    {
        operation.Do();
        _undo.Push(operation);
        _redo.Clear();
        RaiseChanged(WorkflowDesignerChangeKind.Document);
    }

    private ConfigurationState CaptureConfigurationState(WorkflowCanvasNode node) => new(
        WorkflowNodeConfigurationSnapshotter.Capture(node.Node),
        new NodeLayoutState(node.X, node.Y, node.Width, node.Height),
        node.HiddenOutputPorts.OrderBy(key => key, StringComparer.Ordinal).ToArray(),
        Canvas.Connections.ToDictionary(
            connection => connection,
            connection => new ConnectionContractState(connection.State, connection.Diagnostic)));

    private static void ApplyConfigurationState(WorkflowCanvasNode node, ConfigurationState state)
    {
        WorkflowNodeConfigurationSnapshotter.Restore(node.Node, state.NodeSnapshot);
        node.X = state.Layout.X;
        node.Y = state.Layout.Y;
        node.Width = state.Layout.Width;
        node.Height = state.Layout.Height;
        node.HiddenOutputPorts.Clear();
        foreach (var portKey in state.HiddenOutputPorts)
            node.HiddenOutputPorts.Add(portKey);
        foreach (var pair in state.ConnectionStates)
        {
            pair.Key.State = pair.Value.State;
            pair.Key.Diagnostic = pair.Value.Diagnostic;
        }
    }

    private void RefreshConnectionStates()
    {
        var nodeMap = Canvas.Nodes
            .GroupBy(item => item.Node.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        foreach (var connection in Canvas.Connections)
        {
            string? diagnostic = null;
            if (!nodeMap.TryGetValue(connection.FromNodeId, out var source))
                diagnostic = $"连接来源节点不存在：{connection.FromNodeId}。";
            else if (!GetDeclaredPorts(source.Node.Id, WorkflowPortDirection.Output)
                         .Any(port => string.Equals(port.Key, connection.FromPort, StringComparison.Ordinal)))
                diagnostic = $"节点 {connection.FromNodeId} 不再声明输出端口 {connection.FromPort}。";
            else if (!nodeMap.TryGetValue(connection.ToNodeId, out var target))
                diagnostic = $"连接目标节点不存在：{connection.ToNodeId}。";
            else if (!GetDeclaredPorts(target.Node.Id, WorkflowPortDirection.Input)
                         .Any(port => string.Equals(port.Key, connection.ToPort, StringComparison.Ordinal)))
                diagnostic = $"节点 {connection.ToNodeId} 不再声明输入端口 {connection.ToPort}。";

            connection.State = diagnostic is null
                ? WorkflowConnectionState.Active
                : WorkflowConnectionState.Detached;
            connection.Diagnostic = diagnostic;
        }
    }

    private void RestoreLayout(
        IReadOnlyDictionary<string, NodeLayoutState> nodes,
        IReadOnlyDictionary<WorkflowConnectionModel, WorkflowPoint[]> routes)
    {
        foreach (var node in Canvas.Nodes)
        {
            if (!nodes.TryGetValue(node.Node.Id, out var state))
                continue;
            node.X = state.X;
            node.Y = state.Y;
            node.Width = state.Width;
            node.Height = state.Height;
        }
        foreach (var pair in routes)
            SetWaypoints(pair.Key, pair.Value);
    }

    /// <summary>查找指定画布节点；不存在时抛出异常。</summary>
    /// <param name="nodeId">节点标识。</param>
    private WorkflowCanvasNode GetCanvasNodeOrThrow(string nodeId) =>
        Canvas.Nodes.FirstOrDefault(item => item.Node.Id == nodeId)
        ?? throw new KeyNotFoundException($"画布中不存在节点 {nodeId}。");

    /// <summary>清理连接拐点，移除重复点和不必要的共线点。</summary>
    /// <param name="points">路径点或候选点集合。</param>
    /// <param name="tolerance">比较或命中测试允许的误差。</param>
    private static WorkflowPoint[] NormalizeWaypoints(
        IEnumerable<WorkflowPoint> points,
        double tolerance = 6)
    {
        var normalized = new List<WorkflowPoint>();
        foreach (var source in points)
        {
            var point = source;
            var nearbyIndex = normalized.FindIndex(item => Distance(item, point) <= tolerance);
            if (nearbyIndex >= 0)
                point = normalized[nearbyIndex];
            if (normalized.Count > 0)
            {
                var previous = normalized[^1];
                if (Distance(previous, point) <= tolerance)
                    continue;
                if (Math.Abs(previous.X - point.X) <= tolerance)
                    point = new WorkflowPoint(previous.X, point.Y);
                if (Math.Abs(previous.Y - point.Y) <= tolerance)
                    point = new WorkflowPoint(point.X, previous.Y);
            }
            normalized.Add(point);
        }

        for (var index = normalized.Count - 2; index > 0; index--)
        {
            var previous = normalized[index - 1];
            var current = normalized[index];
            var next = normalized[index + 1];
            if ((Math.Abs(previous.X - current.X) <= tolerance && Math.Abs(current.X - next.X) <= tolerance)
                || (Math.Abs(previous.Y - current.Y) <= tolerance && Math.Abs(current.Y - next.Y) <= tolerance))
            {
                normalized.RemoveAt(index);
            }
        }
        return normalized.ToArray();
    }

    /// <summary>判断两个值或坐标是否在允许误差内相等。</summary>
    /// <param name="first">第一个值或坐标。</param>
    /// <param name="second">第二个值或坐标。</param>
    /// <param name="tolerance">比较或命中测试允许的误差。</param>
    private static bool NearlyEqual(WorkflowPoint first, WorkflowPoint second, double tolerance = 0.001) =>
        Math.Abs(first.X - second.X) <= tolerance && Math.Abs(first.Y - second.Y) <= tolerance;

    /// <summary>计算两个点之间的欧氏距离。</summary>
    /// <param name="first">第一个值或坐标。</param>
    /// <param name="second">第二个值或坐标。</param>
    private static double Distance(WorkflowPoint first, WorkflowPoint second) =>
        Math.Sqrt(Math.Pow(first.X - second.X, 2) + Math.Pow(first.Y - second.Y, 2));

    /// <summary>用给定坐标集合重建连接的拐点列表。</summary>
    /// <param name="connection">目标连接。</param>
    /// <param name="points">路径点或候选点集合。</param>
    private static void SetWaypoints(
        WorkflowConnectionModel connection,
        IEnumerable<WorkflowPoint> points)
    {
        connection.Waypoints.Clear();
        foreach (var point in points)
            connection.Waypoints.Add(point);
    }

    /// <summary>验证连接引用的节点、端口和方向是否合法。</summary>
    /// <param name="connection">目标连接。</param>
    private void ValidateConnection(WorkflowConnectionModel connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (!Canvas.Connections.Contains(connection))
            throw new InvalidOperationException("连接不属于当前画布。");
    }

    /// <summary>查找节点端口；端口不存在时抛出异常。</summary>
    /// <param name="node">目标画布节点或节点模型。</param>
    /// <param name="portKey">端口键。</param>
    /// <param name="direction">端口方向或路径方向。</param>
    private WorkflowPortDescriptor GetPortOrThrow(
        IWorkflowNodeModel node,
        string portKey,
        WorkflowPortDirection direction)
    {
        var descriptor = Catalog.GetOrThrow(node.NodeType);
        return descriptor.GetPorts(node).FirstOrDefault(port => port.Direction == direction && port.Key == portKey)
            ?? throw new InvalidOperationException($"节点 {node.Id} 未声明{direction}端口 {portKey}。");
    }

    /// <summary>根据标题和端口数量扩展节点的最小显示尺寸。</summary>
    /// <param name="node">目标画布节点或节点模型。</param>
    private void EnsureNodeDisplaySize(WorkflowCanvasNode node)
    {
        static double TextWidth(string text) => Math.Max(12, text.Sum(character => character > 255 ? 14d : 8d));
        var ports = Catalog.GetOrThrow(node.Node.NodeType).GetPorts(node.Node)
            .Where(port => port.Direction != WorkflowPortDirection.Output || !node.HiddenOutputPorts.Contains(port.Key))
            .ToArray();
        var groups = ports.GroupBy(node.GetPortSide).ToDictionary(group => group.Key, group => group.ToArray());
        var titleWidth = TextWidth(node.Node.Title);
        var horizontalPortWidth = groups
            .Where(group => group.Key is WorkflowPortSide.Top or WorkflowPortSide.Bottom)
            .Select(group => group.Value.Sum(port => TextWidth(port.Key) + 24))
            .DefaultIfEmpty(0)
            .Max();
        var sideLabelWidth = groups
            .Where(group => group.Key is WorkflowPortSide.Left or WorkflowPortSide.Right)
            .SelectMany(group => group.Value)
            .Select(port => TextWidth(port.Key))
            .DefaultIfEmpty(0)
            .Max();
        var verticalPortCount = groups
            .Where(group => group.Key is WorkflowPortSide.Left or WorkflowPortSide.Right)
            .Select(group => group.Value.Length)
            .DefaultIfEmpty(0)
            .Max();
        var requiredWidth = Math.Max(180, Math.Max(titleWidth + 100, Math.Max(horizontalPortWidth + 20, titleWidth + sideLabelWidth * 2 + 72)));
        var requiredHeight = CalculateRequiredNodeHeight(verticalPortCount);
        if (Math.Abs(node.Width - requiredWidth) < 0.01 && Math.Abs(node.Height - requiredHeight) < 0.01)
            return;
        var centerX = node.X + node.Width / 2;
        var centerY = node.Y + node.Height / 2;
        node.Width = requiredWidth;
        node.Height = requiredHeight;
        centerX = SnapToGrid ? Math.Round(centerX / 24) * 24 : centerX;
        centerY = SnapToGrid ? Math.Round(centerY / 24) * 24 : centerY;
        node.X = centerX - node.Width / 2;
        node.Y = centerY - node.Height / 2;
    }

    /// <summary>根据垂直边上的最大端口数计算节点所需高度。</summary>
    /// <param name="maximumVerticalPorts">节点任一垂直边上的最大端口数量。</param>
    private static double CalculateRequiredNodeHeight(int maximumVerticalPorts) => Math.Max(
        WorkflowDesignerGeometry.MinimumNodeHeight,
        WorkflowDesignerGeometry.HeaderHeight + WorkflowDesignerGeometry.NodeBodyVerticalPadding
        + maximumVerticalPorts * WorkflowDesignerGeometry.VerticalPortSpacing);

    /// <summary>根据节点类型生成画布内不重复的节点标识。</summary>
    /// <param name="nodeType">节点类型键。</param>
    private string CreateUniqueNodeId(string nodeType) =>
        CreateUniqueNodeId(nodeType, new HashSet<string>(Canvas.Nodes.Select(item => item.Node.Id), StringComparer.Ordinal));

    /// <summary>根据节点类型生成画布内不重复的节点标识。</summary>
    /// <param name="nodeType">节点类型键。</param>
    /// <param name="reserved">已经占用、不可重复使用的标识集合。</param>
    private static string CreateUniqueNodeId(string nodeType, IReadOnlySet<string> reserved)
    {
        var prefix = new string(nodeType.Where(char.IsLetterOrDigit).ToArray());
        if (prefix.Length == 0)
            prefix = "Node";
        var index = 1;
        string candidate;
        do candidate = $"{prefix}{index++}";
        while (reserved.Contains(candidate));
        return candidate;
    }

    /// <summary>发布指定类型的设计器变更事件。</summary>
    /// <param name="kind">节点、页面或变更类型。</param>
    private void RaiseChanged(WorkflowDesignerChangeKind kind) =>
        Changed?.Invoke(this, new WorkflowDesignerChangedEventArgs(kind));

    private sealed record ConfigurationState(
        IWorkflowNodeModel NodeSnapshot,
        NodeLayoutState Layout,
        IReadOnlyList<string> HiddenOutputPorts,
        IReadOnlyDictionary<WorkflowConnectionModel, ConnectionContractState> ConnectionStates);

    private readonly record struct NodeLayoutState(double X, double Y, double Width, double Height);

    private readonly record struct ConnectionContractState(WorkflowConnectionState State, string? Diagnostic);

    /// <summary>封装一对执行与撤销委托。</summary>
    /// <param name="Do">执行委托。</param>
    /// <param name="Undo">撤销委托。</param>
    private sealed record DesignerOperation(Action Do, Action Undo);
}
