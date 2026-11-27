namespace DP.WorkFlow.UI;

/// <summary>
/// 子画布原地展开：复合节点（子画布、循环块等带子文档的节点）可以在本画布上就地展开成一块区域，
/// 里面按子文档原来的布局画出子节点和运行状态，也能直接编辑。
/// </summary>
/// <remarks>
/// 展开只改变显示：展开节点变高变宽，它右边和下边的节点按展开多出来的宽度 / 高度整体让位。
/// 保存的坐标不变，文档不会因此标成已修改；展开状态记在 <see cref="WorkflowCanvasExpansionMemory"/>，和文档分开。
/// 展开区里的编辑由子会话完成，撤销 / 重做记在本会话（再往上直到最外层画布）的同一个撤销栈里。
/// </remarks>
public sealed partial class WorkflowDesignerSession
{
    /// <summary>展开区内容与区域边框之间的留白（画布单位，1 格）。</summary>
    public const double ExpandedPadding = WorkflowDesignerGeometry.GridSize;

    private readonly HashSet<string> _expanded = new(StringComparer.Ordinal);
    private readonly Dictionary<string, WorkflowDesignerSession> _expandedSessions = new(StringComparer.Ordinal);
    private WorkflowDesignerSession? _owner;
    private string? _expansionScope;
    private bool _expansionEnabled;
    private ExpansionLayout? _expansionLayout;

    /// <summary>展开本会话的父会话；本会话不是某个展开区时为空。</summary>
    public WorkflowDesignerSession? Owner => _owner;

    /// <summary>本会话在父会话里对应的复合节点；不是展开区时为空。</summary>
    public string? OwnerNodeId { get; private set; }

    /// <summary>
    /// 展开状态的记忆范围：根画布为流程名，子画布为“上层范围/复合节点标识”。同一流程在设计页和运行页共用展开状态。
    /// </summary>
    public string ExpansionScope
    {
        get => _expansionScope ?? Document.Name;
        set => _expansionScope = value;
    }

    /// <summary>当前展开的复合节点。</summary>
    public IReadOnlyCollection<string> ExpandedNodeIds => _expanded.ToArray();

    /// <summary>
    /// 打开原地展开：按记忆恢复上次的展开状态。界面控件拿到会话时调用；没调用时（例如 WPF 画布）不展开，显示与以前一样。
    /// </summary>
    public void EnableExpansion()
    {
        if (_expansionEnabled) return;
        _expansionEnabled = true;
        foreach (var node in Document.Nodes)
            if (CanExpand(node) && WorkflowCanvasExpansionMemory.Contains(ExpansionScope, node.Node.Id))
                _expanded.Add(node.Node.Id);
        InvalidateExpansion();
    }

    /// <summary>节点能否原地展开：带子文档且子文档里有节点。</summary>
    /// <param name="node">画布节点。</param>
    public static bool CanExpand(WorkflowCanvasNode node) =>
        node?.Node is IWorkflowSubDocumentNode { SubDocument.Nodes.Count: > 0 };

    /// <summary>节点当前是否展开。</summary>
    /// <param name="nodeId">节点标识。</param>
    public bool IsExpanded(string nodeId) => _expanded.Contains(nodeId);

    /// <summary>展开或收起复合节点（只改变显示，不进撤销栈，不标记修改）。</summary>
    /// <param name="nodeId">节点标识。</param>
    /// <param name="expanded">是否展开。</param>
    /// <returns>状态有变化时为真。</returns>
    public bool SetExpanded(string nodeId, bool expanded)
    {
        var node = Document.Nodes.FirstOrDefault(item => item.Node.Id == nodeId);
        if (node is null || expanded && !CanExpand(node)) return false;
        if (!(expanded ? _expanded.Add(nodeId) : _expanded.Remove(nodeId))) return false;
        if (!expanded) _expandedSessions.Remove(nodeId);
        WorkflowCanvasExpansionMemory.Set(ExpansionScope, nodeId, expanded);
        InvalidateExpansion();
        RaiseChanged(WorkflowDesignerChangeKind.Viewport);
        return true;
    }

    /// <summary>丢掉展开区的缓存（子会话和布局），下次用到时按文档重建。在别处改过子文档（例如进入子画布编辑后返回）时调用。</summary>
    public void ResetExpansionCache()
    {
        _expandedSessions.Clear();
        InvalidateExpansion();
    }

    /// <summary>
    /// 展开节点的子会话：缩放、平移、运行快照和提示都跟本会话同步，所以可以直接用屏幕坐标绘制和命中。没展开时为空。
    /// </summary>
    /// <param name="nodeId">展开的复合节点。</param>
    public WorkflowDesignerSession? GetExpandedSession(string nodeId)
    {
        if (!_expanded.Contains(nodeId)) return null;
        var layout = GetExpansionLayout();
        if (!layout.Frames.TryGetValue(nodeId, out var frame)) return null;
        var child = frame.Session;
        child.Zoom = Zoom;
        child.PanX = PanX + Zoom * (frame.ContentOrigin.X - frame.ChildBounds.X);
        child.PanY = PanY + Zoom * (frame.ContentOrigin.Y - frame.ChildBounds.Y);
        child.SnapToGrid = SnapToGrid;
        child.Notices = Notices;
        child.Runtime = Runtime;
        if (child.RuntimePath.Count != RuntimePath.Count + 1 || child.RuntimePath[^1] != nodeId)
            child.RuntimePath = RuntimePath.Append(nodeId).ToArray();
        var snapshot = WorkflowDesignerNavigator.FindChildSnapshot(_runtimeSnapshot, nodeId);
        if (!ReferenceEquals(child._runtimeSnapshot, snapshot)) child._runtimeSnapshot = snapshot;
        return child;
    }

    /// <summary>展开节点里子流程所占的区域（本会话画布坐标）；没展开时为空。</summary>
    /// <param name="nodeId">展开的复合节点。</param>
    public WorkflowDesignerRect? GetExpandedContentRect(string nodeId)
    {
        if (!_expanded.Contains(nodeId) || !GetExpansionLayout().Frames.TryGetValue(nodeId, out var frame)) return null;
        return new WorkflowDesignerRect(frame.ContentOrigin.X - ExpandedPadding, frame.ContentOrigin.Y - ExpandedPadding,
            frame.ChildBounds.Width + ExpandedPadding * 2, frame.ChildBounds.Height + ExpandedPadding * 2);
    }

    /// <summary>
    /// 节点显示位置和大小（画布坐标）：保存的坐标加上让位偏移；展开的节点是展开后的大小。没有展开时就是保存的坐标。
    /// </summary>
    /// <param name="node">画布节点。</param>
    public WorkflowDesignerRect GetNodeDisplayRect(WorkflowCanvasNode node)
    {
        Guard.NotNull(node);
        if (_expanded.Count == 0) return new WorkflowDesignerRect(node.X, node.Y, node.Width, node.Height);
        var layout = GetExpansionLayout();
        var (dx, dy) = layout.Offset(node.X, node.Y, node.Width, node.Height, node.Node.Id);
        var width = node.Width;
        var height = node.Height;
        if (layout.Frames.TryGetValue(node.Node.Id, out var frame))
        {
            width = frame.Width;
            height = frame.Height;
        }
        return new WorkflowDesignerRect(node.X + dx, node.Y + dy, width, height);
    }

    /// <summary>把保存的坐标（例如连线拐点）换成显示坐标。</summary>
    /// <param name="point">保存的画布坐标。</param>
    public WorkflowPoint ToDisplayPoint(WorkflowPoint point)
    {
        if (_expanded.Count == 0) return point;
        var (dx, dy) = GetExpansionLayout().Offset(point.X, point.Y, 0, 0, null);
        return new WorkflowPoint(point.X + dx, point.Y + dy);
    }

    /// <summary>把显示坐标换回保存的坐标（拖动拐点、拖入节点时用）。</summary>
    /// <param name="point">显示的画布坐标。</param>
    public WorkflowPoint ToStoredPoint(WorkflowPoint point)
    {
        if (_expanded.Count == 0) return point;
        var layout = GetExpansionLayout();
        var stored = point;
        // 让位偏移按保存坐标分段取值，反推时迭代几次即可稳定。
        for (var i = 0; i < 4; i++)
        {
            var (dx, dy) = layout.Offset(stored.X, stored.Y, 0, 0, null);
            stored = new WorkflowPoint(point.X - dx, point.Y - dy);
        }
        return stored;
    }

    /// <summary>全部节点（含展开区）和连线拐点的显示范围（画布坐标）；没有节点时为空。</summary>
    public WorkflowDesignerRect? GetDisplayBounds()
    {
        if (Document.Nodes.Count == 0) return null;
        double left = double.MaxValue, top = double.MaxValue, right = double.MinValue, bottom = double.MinValue;
        void Include(double x, double y, double w, double h)
        {
            left = Math.Min(left, x); top = Math.Min(top, y);
            right = Math.Max(right, x + w); bottom = Math.Max(bottom, y + h);
        }
        foreach (var node in Document.Nodes)
        {
            var rect = GetNodeDisplayRect(node);
            Include(rect.X, rect.Y, rect.Width, rect.Height);
        }
        foreach (var point in Document.Connections.SelectMany(connection => connection.Waypoints))
        {
            var display = ToDisplayPoint(point);
            Include(display.X, display.Y, 0, 0);
        }
        return new WorkflowDesignerRect(left, top, right - left, bottom - top);
    }

    /// <summary>本会话和全部展开区（逐层往下）的会话，最外层在前。</summary>
    public IEnumerable<WorkflowDesignerSession> SelfAndExpandedSessions()
    {
        yield return this;
        foreach (var node in Document.Nodes)
        {
            if (GetExpandedSession(node.Node.Id) is not { } child) continue;
            foreach (var nested in child.SelfAndExpandedSessions())
                yield return nested;
        }
    }

    /// <summary>撤销 / 重做实际记在哪个会话：展开区记在最外层画布上。</summary>
    private WorkflowDesignerSession UndoOwner => _owner?.UndoOwner ?? this;

    private void InvalidateExpansion()
    {
        _expansionLayout = null;
        _owner?.InvalidateExpansion();
    }

    private ExpansionLayout GetExpansionLayout()
    {
        if (_expansionLayout is { } cached) return cached;
        var frames = new Dictionary<string, ExpansionFrame>(StringComparer.Ordinal);
        foreach (var node in Document.Nodes)
        {
            if (!_expanded.Contains(node.Node.Id) || node.Node is not IWorkflowSubDocumentNode composite || composite.SubDocument.Nodes.Count == 0)
                continue;
            if (!_expandedSessions.TryGetValue(node.Node.Id, out var child) || !ReferenceEquals(child.Document, composite.SubDocument))
            {
                child = new WorkflowDesignerSession(composite.SubDocument, Catalog, PublicData)
                {
                    _owner = this,
                    OwnerNodeId = node.Node.Id,
                    ExpansionScope = ExpansionScope + "/" + node.Node.Id
                };
                if (_expansionEnabled) child.EnableExpansion();
                _expandedSessions[node.Node.Id] = child;
            }
            var bounds = child.GetDisplayBounds() ?? new WorkflowDesignerRect(0, 0, 0, 0);
            var width = Math.Max(node.Width, WorkflowDesignerGeometry.QuantizeNodeWidth(bounds.Width + ExpandedPadding * 2));
            var height = node.Height + WorkflowDesignerGeometry.QuantizeNodeHeight(bounds.Height + ExpandedPadding * 2);
            frames[node.Node.Id] = new ExpansionFrame(child, node.X, node.Y, node.Width, node.Height, width, height, bounds);
        }
        return _expansionLayout = new ExpansionLayout(frames, Document.Nodes.Select(node => new WorkflowDesignerRect(node.X, node.Y, node.Width, node.Height)).ToArray());
    }

    /// <summary>一个展开区：保存的卡片位置和大小、展开后的大小、子流程显示范围。</summary>
    private sealed record ExpansionFrame(
        WorkflowDesignerSession Session,
        double X,
        double Y,
        double CardWidth,
        double CardHeight,
        double Width,
        double Height,
        WorkflowDesignerRect ChildBounds)
    {
        public double GrowX => Width - CardWidth;
        public double GrowY => Height - CardHeight;

        /// <summary>右边节点的移动量；由布局填入。</summary>
        public double ShiftX { get; set; }

        /// <summary>下边节点的移动量；由布局填入。</summary>
        public double ShiftY { get; set; }

        /// <summary>子流程左上角（不含让位偏移，偏移在 <see cref="ExpansionLayout"/> 里加）。</summary>
        public WorkflowPoint StoredContentOrigin => new(X + ExpandedPadding, Y + CardHeight + ExpandedPadding);

        /// <summary>子流程左上角（显示坐标）；由布局填入。</summary>
        public WorkflowPoint ContentOrigin { get; set; }
    }

    /// <summary>
    /// 让位规则（保存坐标里判断）：左边越过卡片中线、且不是整个在展开区上方的节点算“右边”，统一右移；
    /// 上边越过卡片中线、且不是整个在展开区左方的节点算“下边”，统一下移。
    /// 移动量是展开多出来的宽 / 高，再加上原来就和卡片重叠的那部分（卡片较宽时右边的节点可能本来就压着它），
    /// 所以让位后一定不压着展开区，右边 / 下边节点之间的相对位置也不变。几个展开区的偏移相加。
    /// </summary>
    private sealed class ExpansionLayout
    {
        public ExpansionLayout(Dictionary<string, ExpansionFrame> frames, IReadOnlyList<WorkflowDesignerRect> nodes)
        {
            Frames = frames;
            foreach (var frame in frames.Values)
            {
                double minLeft = double.MaxValue, minTop = double.MaxValue;
                foreach (var node in nodes)
                {
                    // 只看和卡片同一行 / 同一列、真的可能压着它的节点。
                    if (IsRight(frame, node.X, node.Y, node.Height) && node.Y < frame.Y + frame.CardHeight) minLeft = Math.Min(minLeft, node.X);
                    if (IsBelow(frame, node.X, node.Y, node.Width) && node.X < frame.X + frame.CardWidth) minTop = Math.Min(minTop, node.Y);
                }
                var right = frame.X + frame.CardWidth;
                var bottom = frame.Y + frame.CardHeight;
                frame.ShiftX = frame.GrowX + (minLeft < right ? right - minLeft : 0);
                frame.ShiftY = frame.GrowY + (minTop < bottom ? bottom - minTop : 0);
            }
            foreach (var pair in frames)
            {
                var frame = pair.Value;
                var (dx, dy) = Offset(frame.X, frame.Y, frame.CardWidth, frame.CardHeight, pair.Key);
                var origin = frame.StoredContentOrigin;
                frame.ContentOrigin = new WorkflowPoint(origin.X + dx, origin.Y + dy);
            }
        }

        public Dictionary<string, ExpansionFrame> Frames { get; }

        private static bool IsRight(ExpansionFrame frame, double x, double y, double height) =>
            x >= frame.X + frame.CardWidth / 2 && y + height > frame.Y + 0.5;

        private static bool IsBelow(ExpansionFrame frame, double x, double y, double width) =>
            y >= frame.Y + frame.CardHeight / 2 && x + width > frame.X + 0.5;

        public (double X, double Y) Offset(double x, double y, double width, double height, string? self)
        {
            double dx = 0, dy = 0;
            foreach (var pair in Frames)
            {
                if (pair.Key == self) continue;
                var frame = pair.Value;
                if (IsRight(frame, x, y, height)) dx += frame.ShiftX;
                if (IsBelow(frame, x, y, width)) dy += frame.ShiftY;
            }
            return (dx, dy);
        }
    }
}

/// <summary>
/// 画布展开状态的记忆，和流程文档分开保存：展开 / 收起不会让文档变成已修改。
/// 设了 <see cref="FilePath"/> 时写到文件，下次打开程序还是上次的展开状态；没设时只在本次运行里记住。
/// </summary>
public static class WorkflowCanvasExpansionMemory
{
    private static readonly object Sync = new();
    private static readonly HashSet<string> Keys = new(StringComparer.Ordinal);
    private static string? s_filePath;

    /// <summary>记忆文件（每行一个“范围\t节点”）；设置时读入已有内容。为空时不写文件。</summary>
    public static string? FilePath
    {
        get => s_filePath;
        set
        {
            lock (Sync)
            {
                s_filePath = string.IsNullOrWhiteSpace(value) ? null : value;
                if (s_filePath is null || !File.Exists(s_filePath)) return;
                try
                {
                    foreach (var line in File.ReadAllLines(s_filePath))
                        if (line.IndexOf('\t') > 0) Keys.Add(line);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }

    /// <summary>节点是否记为展开。</summary>
    /// <param name="scope">画布范围，见 <see cref="WorkflowDesignerSession.ExpansionScope"/>。</param>
    /// <param name="nodeId">节点标识。</param>
    public static bool Contains(string scope, string nodeId)
    {
        lock (Sync) return Keys.Contains(Key(scope, nodeId));
    }

    /// <summary>记下展开或收起。</summary>
    /// <param name="scope">画布范围。</param>
    /// <param name="nodeId">节点标识。</param>
    /// <param name="expanded">是否展开。</param>
    public static void Set(string scope, string nodeId, bool expanded)
    {
        lock (Sync)
        {
            if (!(expanded ? Keys.Add(Key(scope, nodeId)) : Keys.Remove(Key(scope, nodeId))) || s_filePath is null) return;
            try
            {
                var directory = Path.GetDirectoryName(s_filePath);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                File.WriteAllLines(s_filePath, Keys.OrderBy(key => key, StringComparer.Ordinal));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static string Key(string scope, string nodeId) => (scope ?? string.Empty).Replace('\t', ' ') + "\t" + nodeId;
}
