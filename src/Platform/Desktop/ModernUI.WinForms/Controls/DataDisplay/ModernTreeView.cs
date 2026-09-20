using System.ComponentModel;
using System.Runtime.InteropServices;

namespace ModernUI.WinForms;

/// <summary>Specifies how a checked tree node synchronizes its ancestors and descendants.</summary>
public enum ModernTreeCheckPropagationMode
{
    Independent,
    Descendants,
    AncestorsAndDescendants
}

/// <summary>
/// 保留原生 TreeView 节点、键盘、滚动、Designer 和 UIA 语义，并提供现代主题行状态。
/// </summary>
[DefaultProperty(nameof(Nodes))]
[DefaultEvent(nameof(AfterSelect))]
[Description("ModernTreeView 现代树形控件")]
[DisplayName("现代树形视图")]
[ToolboxBitmap(typeof(ModernTreeView), "Toolbox.Icons.DataDisplay.bmp")]
[ToolboxItem(true)]
public sealed class ModernTreeView : TreeView
{
    private ModernTheme _theme = ModernUiSettings.DefaultTheme;
    private TreeNode? _hoveredNode;
    private int _nodeHeight = 30;
    private bool _synchronizingChecks;
    private bool _pointerCheckTransaction;
    private ModernTreeCheckPropagationMode _checkPropagationMode = ModernTreeCheckPropagationMode.AncestorsAndDescendants;
    private readonly ModernNativeScrollBarOverlay _verticalScrollBar;
    private readonly ModernNativeScrollBarOverlay _horizontalScrollBar;
    private readonly Panel _scrollCorner = new() { TabStop = false, Visible = false };
    private bool _dpiMetricsInvalid;
    private bool _nativeScrollBarsHidden;
    private bool _updatingNativeScrollStyles;
    private FinalLayoutTransaction? _layoutTransaction;

    private FinalLayoutTransaction LayoutTransaction =>
        _layoutTransaction ??= new FinalLayoutTransaction(this, ApplyFinalLayout);

    // Overlay HWNDs use final client coordinates and must not be scaled again as child controls.
    protected override bool ScaleChildren => false;

    /// <summary>初始化现代树形控件。</summary>
    public ModernTreeView()
    {
        BorderStyle = BorderStyle.None;
        DrawMode = TreeViewDrawMode.OwnerDrawAll;
        FullRowSelect = false;
        HideSelection = false;
        HotTracking = false;
        ShowLines = false;
        ShowRootLines = false;
        // 展开标记由现代层绘制，避免与原生 StateImage/CheckBox 共用缩进槽位。
        ShowPlusMinus = false;
        DoubleBuffered = true;
        _verticalScrollBar = new ModernNativeScrollBarOverlay(
            new NativeWindowScrollAdapter(this, vertical: true, hideNativeChrome: false,
                setPosition: ScrollToVisibleNode), vertical: true)
        {
            AutoVisibility = true
        };
        _horizontalScrollBar = new ModernNativeScrollBarOverlay(
            new NativeWindowScrollAdapter(this, vertical: false, hideNativeChrome: false), vertical: false)
        {
            AutoVisibility = true
        };
        Controls.AddRange([_verticalScrollBar, _horizontalScrollBar, _scrollCorner]);
        ApplyMetrics();
        ApplyTheme();
    }

    /// <summary>获取或设置控件主题。</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public ModernTheme Theme
    {
        get => _theme;
        set
        {
            _theme = value ?? throw new ArgumentNullException(nameof(value));
            ApplyTheme();
        }
    }

    /// <summary>获取或设置节点行高（96 DPI 下的逻辑像素）。</summary>
    [Category("Layout")]
    [DefaultValue(30)]
    public int NodeHeight
    {
        get => _nodeHeight;
        set
        {
            var normalized = Math.Max(18, value);
            if (_nodeHeight == normalized) return;
            _nodeHeight = normalized;
            ApplyMetrics();
        }
    }

    /// <summary>获取或设置节点勾选时对父节点和子节点的同步策略。</summary>
    [Category("Behavior"), DefaultValue(ModernTreeCheckPropagationMode.AncestorsAndDescendants)]
    public ModernTreeCheckPropagationMode CheckPropagationMode
    {
        get => _checkPropagationMode;
        set { if (_checkPropagationMode == value) return; _checkPropagationMode = value; Invalidate(); }
    }

    /// <summary>返回节点根据当前传播策略计算的勾选状态。</summary>
    public CheckState GetNodeCheckState(TreeNode node)
    {
        ModernCompatibility.ThrowIfNull(node, nameof(node));
        if (!ReferenceEquals(node.TreeView, this) && node.TreeView is not null)
            throw new ArgumentException("The node does not belong to this tree.", nameof(node));
        if (CheckPropagationMode == ModernTreeCheckPropagationMode.Independent || node.Nodes.Count == 0)
            return node.Checked ? CheckState.Checked : CheckState.Unchecked;

        var hasChecked = false;
        var hasUnchecked = false;
        foreach (TreeNode child in node.Nodes)
        {
            var state = GetNodeCheckState(child);
            hasChecked |= state is CheckState.Checked or CheckState.Indeterminate;
            hasUnchecked |= state is CheckState.Unchecked or CheckState.Indeterminate;
            if (hasChecked && hasUnchecked) return CheckState.Indeterminate;
        }
        return hasChecked ? CheckState.Checked : CheckState.Unchecked;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        _nativeScrollBarsHidden = false;
        ApplyMetrics();
        NativeControlTheme.ApplyExplorer(this, Theme.IsDark);
        EnableNativeDoubleBuffer();
        LayoutTransaction.Request();
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        _dpiMetricsInvalid = true;
        LayoutTransaction.Request();
    }

    protected override void WndProc(ref Message message)
    {
        const int windowNcCalculateSize = 0x0083;
        if (message.Msg == windowNcCalculateSize && message.LParam != IntPtr.Zero)
        {
            // BorderStyle=None means the whole HWND belongs to the managed tree surface. Preserve
            // that client rectangle even while native TreeView extent calculation temporarily
            // adds scrollbar styles; the styles are removed in the final-layout transaction.
            var fullClient = Marshal.PtrToStructure<NativeRect>(message.LParam);
            base.WndProc(ref message);
            Marshal.StructureToPtr(fullClient, message.LParam, false);
            return;
        }

        const int windowPaint = 0x000F;
        if (message.Msg == windowPaint && IsHandleCreated)
        {
            PaintBufferedWindow();
            // Extent calculation can restore native scroll styles immediately before WM_PAINT.
            // Never mutate non-client styles or child Z-order from inside the paint transaction;
            // coalesce that correction into the final-layout pass instead.
            if (HasNativeScrollStyles())
            {
                _nativeScrollBarsHidden = false;
                LayoutTransaction.Request();
            }
            message.Result = IntPtr.Zero;
            return;
        }

        const int windowEraseBackground = 0x0014;
        if (message.Msg == windowEraseBackground)
        {
            // OwnerDrawAll fills every visible row. Suppress the native erase pass so horizontal
            // tracking cannot compose an empty tree frame before the row draw notifications.
            message.Result = (IntPtr)1;
            return;
        }

        if (message.Msg == 0x020A && _verticalScrollBar is not null)
        {
            var delta = unchecked((short)((long)message.WParam >> 16));
            var scrollBar = (ModifierKeys & Keys.Shift) == Keys.Shift
                ? _horizontalScrollBar
                : _verticalScrollBar;
            // Once this Tree owns the requested axis, consume boundary notches as well. Passing a
            // downward wheel message to the native Tree at its vertical end makes it auto-scroll
            // horizontally to an indented node.
            var scrolled = scrollBar.TryScrollWheel(delta);
            if (scrolled || scrollBar.Visible)
            {
                if (!scrolled && MouseWheelRouting.TryRouteToAncestor(this, ref message))
                {
                    message.Result = IntPtr.Zero;
                    return;
                }
                scrollBar.RefreshFromTarget();
                scrollBar.Update();
                message.Result = IntPtr.Zero;
                return;
            }
        }
        if (MouseWheelRouting.TryRouteToAncestor(this, ref message))
        {
            message.Result = IntPtr.Zero;
            return;
        }
        base.WndProc(ref message);
        if (message.Msg is 0x0114 or 0x0115 or 0x020A)
        {
            _verticalScrollBar?.RefreshFromTarget();
            _horizontalScrollBar?.RefreshFromTarget();
            LayoutScrollBars();
        }
        if (message.Msg is 0x1100 or 0x1132 or 0x1101 or 0x1102 or 0x110D or 0x113F)
        {
            if (!_updatingNativeScrollStyles) _nativeScrollBarsHidden = false;
            LayoutTransaction.Request();
        }
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (!_updatingNativeScrollStyles) _nativeScrollBarsHidden = false;
        LayoutScrollBars();
        LayoutTransaction.Request();
    }

    protected override void OnRightToLeftChanged(EventArgs e)
    {
        base.OnRightToLeftChanged(e);
        LayoutScrollBars();
        LayoutTransaction.Request();
    }

    protected override void OnAfterCheck(TreeViewEventArgs e)
    {
        if (!_synchronizingChecks && e.Node is not null)
        {
            _synchronizingChecks = true;
            try
            {
                if (CheckPropagationMode is ModernTreeCheckPropagationMode.Descendants or
                    ModernTreeCheckPropagationMode.AncestorsAndDescendants)
                    SetDescendantChecks(e.Node, e.Node.Checked);
                if (CheckPropagationMode == ModernTreeCheckPropagationMode.AncestorsAndDescendants)
                    SynchronizeAncestors(e.Node.Parent);
            }
            finally { _synchronizingChecks = false; }
            if (!_pointerCheckTransaction) InvalidateNodeAndRelations(e.Node);
        }
        base.OnAfterCheck(e);
    }

    protected override void OnDrawNode(DrawTreeNodeEventArgs e)
    {
        if (e.Node is null || e.Bounds.IsEmpty) return;
        DrawNodeSurface(e.Graphics, e.Node,
            new Rectangle(0, e.Bounds.Top, ClientSize.Width, Math.Max(ItemHeight, e.Bounds.Height)));
    }

    private void DrawNodeSurface(Graphics graphics, TreeNode node, Rectangle rowBounds)
    {
        var drawingState = graphics.Save();
        try
        {
            graphics.SetClip(ClientRectangle, System.Drawing.Drawing2D.CombineMode.Replace);
            var selected = ReferenceEquals(node, SelectedNode);
            var hovered = ReferenceEquals(node, _hoveredNode);
            var nodeBackColor = node.BackColor.IsEmpty ? BackColor : node.BackColor;
            var background = selected
                ? Theme.PrimaryBackground
                : hovered ? Theme.ControlHover : nodeBackColor;
            using (var brush = new SolidBrush(background)) graphics.FillRectangle(brush, rowBounds);

            var layout = GetNodeLayout(node, rowBounds);
            using var canvas = new GdiCanvas(graphics);
            if (node.Nodes.Count > 0)
            {
                canvas.DrawIcon(node.IsExpanded ? ModernIconKind.ChevronDown : ModernIconKind.ChevronRight,
                    Theme.TextSecondary, layout.ExpanderBounds, Math.Max(1f, DeviceDpi / 96f * 1.4f));
            }

            if (CheckBoxes)
            {
                var checkState = GetNodeCheckState(node);
                ModernCheckboxRenderer.Draw(canvas, layout.CheckBoxBounds, Theme,
                    checkState == CheckState.Unchecked ? 0f : 1f,
                    checkState == CheckState.Indeterminate, hovered, Enabled, DeviceDpi / 96f);
            }

            DrawNodeImage(graphics, node, layout.ImageBounds);
            var nodeForeColor = node.ForeColor.IsEmpty ? ForeColor : node.ForeColor;
            var foreground = selected ? Theme.Primary : nodeForeColor;
            TextRenderer.DrawText(graphics, node.Text, node.NodeFont ?? Font, layout.TextBounds, foreground,
                (RightToLeft == RightToLeft.Yes ? TextFormatFlags.Right | TextFormatFlags.RightToLeft : TextFormatFlags.Left) |
                TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
                TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

            if (selected && Focused && ShowFocusCues)
            {
                var focusBounds = Rectangle.Inflate(rowBounds, -ScaleLogical(3), -ScaleLogical(3));
                ControlPaint.DrawFocusRectangle(graphics, focusBounds, Theme.Primary, background);
            }
        }
        finally { graphics.Restore(drawingState); }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        var node = GetNodeAtRow(e.Y);
        if (e.Button == MouseButtons.Left && node is not null)
        {
            var row = new Rectangle(0, node.Bounds.Top, ClientSize.Width, Math.Max(ItemHeight, node.Bounds.Height));
            var layout = GetNodeLayout(node, row);
            if (node.Nodes.Count > 0 && layout.ExpanderBounds.Contains(e.Location))
            {
                if (node.IsExpanded) node.Collapse(); else node.Expand();
                InvalidateNode(node);
                return;
            }
            // OwnerDrawAll owns label/image/state geometry completely. Asking the native control to
            // hit-test those slots during WM_LBUTTONDOWN can trigger a synchronous custom-draw pass
            // and deadlock before this handler returns.
            var textOrImageHit = layout.TextBounds.Contains(e.Location) || layout.ImageBounds.Contains(e.Location);
            if (CheckBoxes && (layout.CheckBoxBounds.Contains(e.Location) || textOrImageHit))
            {
                _pointerCheckTransaction = true;
                try
                {
                    // Toggle before selection. Setting SelectedNode first makes the native TreeView
                    // synchronously paint the newly selected owner-drawn row while still inside
                    // WM_LBUTTONDOWN; changing its state image in that re-entrant draw can deadlock.
                    node.Checked = GetNodeCheckState(node) != CheckState.Checked;
                    SelectedNode = node;
                }
                finally { _pointerCheckTransaction = false; }
                InvalidateNodeAndRelations(node);
                return;
            }
        }
        base.OnMouseDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        // Complete owner draw uses full-row interaction, so avoid the native TVM_HITTEST path here
        // as well. It can trigger an extra custom-draw cycle for every pointer move.
        var node = GetNodeAtRow(e.Y);
        if (ReferenceEquals(node, _hoveredNode)) return;
        InvalidateNode(_hoveredNode);
        _hoveredNode = node;
        InvalidateNode(_hoveredNode);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        InvalidateNode(_hoveredNode);
        _hoveredNode = null;
        base.OnMouseLeave(e);
    }

    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        InvalidateNode(SelectedNode);
    }

    protected override void OnLostFocus(EventArgs e)
    {
        base.OnLostFocus(e);
        InvalidateNode(SelectedNode);
    }

    private void PaintBufferedWindow()
    {
        var paint = new PaintStruct();
        var target = BeginPaint(Handle, out paint);
        if (target == IntPtr.Zero) return;
        try { PaintBufferedClient(target); }
        finally { _ = EndPaint(Handle, ref paint); }
    }

    private void PaintBufferedClient(IntPtr target)
    {
        var memory = CreateCompatibleDC(target);
        if (memory == IntPtr.Zero) return;
        var bitmap = CreateCompatibleBitmap(target, Math.Max(1, ClientSize.Width), Math.Max(1, ClientSize.Height));
        if (bitmap == IntPtr.Zero)
        {
            _ = DeleteDC(memory);
            return;
        }
        var previous = SelectObject(memory, bitmap);
        try
        {
            var fill = CreateSolidBrush(ColorRef(BackColor));
            if (fill != IntPtr.Zero)
            {
                var client = new NativeRect { Left = 0, Top = 0, Right = ClientSize.Width, Bottom = ClientSize.Height };
                _ = FillRect(memory, ref client, fill);
                _ = DeleteObject(fill);
            }
            using (var graphics = Graphics.FromHdc(memory))
            {
                var rowTop = 0;
                for (var node = TopNode; node is not null && rowTop < ClientSize.Height;
                     node = GetNextExpandedNode(node), rowTop += ItemHeight)
                {
                    DrawNodeSurface(graphics, node,
                        new Rectangle(0, rowTop, ClientSize.Width, ItemHeight));
                }
            }
            _ = BitBlt(target, 0, 0, ClientSize.Width, ClientSize.Height,
                memory, 0, 0, SourceCopy);
        }
        finally
        {
            _ = SelectObject(memory, previous);
            _ = DeleteObject(bitmap);
            _ = DeleteDC(memory);
        }
    }

    private static int ColorRef(Color color) => color.R | color.G << 8 | color.B << 16;

    private void ScrollToVisibleNode(int position)
    {
        var node = GetVisibleNodeAt(position);
        if (node is null || ReferenceEquals(TopNode, node)) return;

        // TopNode performs native ScrollWindow pixel moves before managed owner drawing runs. Keep
        // that intermediate composition off-screen, restore the retained horizontal origin and
        // native styles, then publish one complete buffered frame after redraw is re-enabled.
        const int setRedraw = 0x000B;
        var horizontalPosition = GetScrollPos(Handle, 0);
        _ = SendMessage(Handle, setRedraw, IntPtr.Zero, IntPtr.Zero);
        try
        {
            TopNode = node;
            if (GetScrollPos(Handle, 0) != horizontalPosition)
            {
                ModernNativeScrollProtocol.SendScroll(this, vertical: false,
                    horizontalPosition == 0 ? ModernNativeScrollProtocol.First : ModernNativeScrollProtocol.ThumbTrack,
                    horizontalPosition);
                ModernNativeScrollProtocol.SendScroll(this, vertical: false,
                    ModernNativeScrollProtocol.EndScroll, 0);
            }
            HideNativeScrollBars(force: true);
            LayoutScrollBars();
        }
        finally { _ = SendMessage(Handle, setRedraw, (IntPtr)1, IntPtr.Zero); }
        Invalidate();
        Update();
    }

    private void EnableNativeDoubleBuffer()
    {
        const int treeSetExtendedStyle = 0x112C;
        const int treeExtendedDoubleBuffer = 0x0004;
        _ = SendMessage(Handle, treeSetExtendedStyle,
            (IntPtr)treeExtendedDoubleBuffer, (IntPtr)treeExtendedDoubleBuffer);
    }

    private void SetDescendantChecks(TreeNode node, bool value)
    {
        foreach (TreeNode child in node.Nodes)
        {
            child.Checked = value;
            SetDescendantChecks(child, value);
        }
    }

    private void SynchronizeAncestors(TreeNode? node)
    {
        while (node is not null)
        {
            node.Checked = GetNodeCheckState(node) == CheckState.Checked;
            node = node.Parent;
        }
    }

    private NodeLayout GetNodeLayout(TreeNode node, Rectangle rowBounds)
    {
        var horizontalScroll = IsHandleCreated ? GetScrollPos(Handle, 0) : 0;
        var expanderSize = ScaleLogical(12);
        var expanderSlot = ScaleLogical(18);
        var checkSize = CheckBoxes ? ScaleLogical(18) : 0;
        if (RightToLeft == RightToLeft.Yes)
        {
            var x = ClientSize.Width - ScaleLogical(4) - node.Level * Indent + horizontalScroll;
            var expander = new RectangleF(x - expanderSize,
                rowBounds.Top + (rowBounds.Height - expanderSize) / 2f, expanderSize, expanderSize);
            x -= expanderSlot;
            var checkBox = new RectangleF(x - checkSize,
                rowBounds.Top + (rowBounds.Height - checkSize) / 2f, checkSize, checkSize);
            if (CheckBoxes) x -= checkSize + ScaleLogical(7);
            var imageBounds = Rectangle.Empty;
            if (ImageList is not null)
            {
                imageBounds = new Rectangle(x - ImageList.ImageSize.Width,
                    rowBounds.Top + (rowBounds.Height - ImageList.ImageSize.Height) / 2,
                    ImageList.ImageSize.Width, ImageList.ImageSize.Height);
                x -= ImageList.ImageSize.Width + ScaleLogical(5);
            }
            var textBounds = new Rectangle(ScaleLogical(8), rowBounds.Top,
                Math.Max(0, x - ScaleLogical(8)), rowBounds.Height);
            return new NodeLayout(expander, checkBox, imageBounds, textBounds);
        }

        var left = ScaleLogical(4) + node.Level * Indent - horizontalScroll;
        var leftExpander = new RectangleF(left, rowBounds.Top + (rowBounds.Height - expanderSize) / 2f,
            expanderSize, expanderSize);
        left += expanderSlot;
        var leftCheckBox = new RectangleF(left, rowBounds.Top + (rowBounds.Height - checkSize) / 2f,
            checkSize, checkSize);
        if (CheckBoxes) left += checkSize + ScaleLogical(7);
        var leftImageBounds = Rectangle.Empty;
        if (ImageList is not null)
        {
            leftImageBounds = new Rectangle(left,
                rowBounds.Top + (rowBounds.Height - ImageList.ImageSize.Height) / 2,
                ImageList.ImageSize.Width, ImageList.ImageSize.Height);
            left += ImageList.ImageSize.Width + ScaleLogical(5);
        }
        var leftTextBounds = new Rectangle(left, rowBounds.Top,
            Math.Max(0, ClientSize.Width - left - ScaleLogical(8)), rowBounds.Height);
        return new NodeLayout(leftExpander, leftCheckBox, leftImageBounds, leftTextBounds);
    }

    private void DrawNodeImage(Graphics graphics, TreeNode node, Rectangle bounds)
    {
        if (ImageList is null || bounds.IsEmpty) return;
        Image? image = null;
        var key = node.IsSelected ? node.SelectedImageKey : node.ImageKey;
        var index = node.IsSelected ? node.SelectedImageIndex : node.ImageIndex;
        if (!string.IsNullOrEmpty(key) && ImageList.Images.ContainsKey(key)) image = ImageList.Images[key];
        else if ((uint)index < (uint)ImageList.Images.Count) image = ImageList.Images[index];
        if (image is not null) graphics.DrawImage(image, bounds);
    }

    private TreeNode? GetNodeAtRow(int y)
    {
        if (y < 0) return null;
        var index = GetScrollPos(Handle, 1) + y / Math.Max(1, ItemHeight);
        return GetVisibleNodeAt(index);
    }

    private TreeNode? GetVisibleNodeAt(int position)
    {
        TreeNode? node = Nodes.Count > 0 ? Nodes[0] : null;
        for (var index = 0; index < position && node is not null; index++)
            node = GetNextExpandedNode(node);
        return node;
    }

    private static TreeNode? GetNextExpandedNode(TreeNode node)
    {
        if (node.IsExpanded && node.Nodes.Count > 0) return node.Nodes[0];
        for (var current = node; current is not null; current = current.Parent)
        {
            if (current.NextNode is not null) return current.NextNode;
        }
        return null;
    }

    private void ApplyTheme()
    {
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
        NativeControlTheme.ApplyExplorer(this, Theme.IsDark);
        _verticalScrollBar.Theme = Theme;
        _horizontalScrollBar.Theme = Theme;
        _scrollCorner.BackColor = Theme.Background;
        Invalidate();
    }

    private void ApplyFinalLayout()
    {
        if (_dpiMetricsInvalid)
        {
            _dpiMetricsInvalid = false;
            ApplyMetrics();
        }
        HideNativeScrollBars();
        _verticalScrollBar.RefreshFromTarget();
        _horizontalScrollBar.RefreshFromTarget();
        LayoutScrollBars();
        Invalidate();
        Update();
    }

    private bool HasNativeScrollStyles() =>
        (GetWindowLong(Handle, -16) & (0x00100000 | 0x00200000)) != 0;

    private void HideNativeScrollBars(bool force = false)
    {
        const int windowStyle = -16;
        const int scrollStyles = 0x00100000 | 0x00200000;
        var style = GetWindowLong(Handle, windowStyle);
        var clientUsesFullBounds = ClientSize.Width == Width && ClientSize.Height == Height;
        // Treat the flag as a cache only while both the style and non-client geometry agree.
        if (_nativeScrollBarsHidden && !force && (style & scrollStyles) == 0 && clientUsesFullBounds) return;
        if ((style & scrollStyles) != 0 || !clientUsesFullBounds)
        {
            _updatingNativeScrollStyles = true;
            try
            {
                _ = SetWindowLong(Handle, windowStyle, style & ~scrollStyles);
                const uint frameChanged = 0x0020;
                const uint noMove = 0x0002;
                const uint noSize = 0x0001;
                const uint noActivate = 0x0010;
                _ = SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0,
                    frameChanged | noMove | noSize | noActivate);
                _ = ShowScrollBar(Handle, 3, false);
                var restoredStyle = GetWindowLong(Handle, windowStyle);
                if ((restoredStyle & scrollStyles) != 0)
                    _ = SetWindowLong(Handle, windowStyle, restoredStyle & ~scrollStyles);
            }
            finally { _updatingNativeScrollStyles = false; }
        }
        _nativeScrollBarsHidden = (GetWindowLong(Handle, windowStyle) & scrollStyles) == 0 &&
                                  ClientSize.Width == Width && ClientSize.Height == Height;
    }

    private void ApplyMetrics()
    {
        var itemHeight = ScaleLogical(NodeHeight);
        var indent = ScaleLogical(20);
        if (ItemHeight == itemHeight && Indent == indent) return;
        ItemHeight = itemHeight;
        Indent = indent;
        Invalidate();
    }

    private void LayoutScrollBars()
    {
        if (_verticalScrollBar is null || _horizontalScrollBar is null) return;
        var vertical = _verticalScrollBar.Visible;
        var horizontal = _horizontalScrollBar.Visible;
        var verticalWidth = SystemInformation.VerticalScrollBarWidth;
        var horizontalHeight = SystemInformation.HorizontalScrollBarHeight;
        var verticalLeft = RightToLeft == RightToLeft.Yes ? 0 : Math.Max(0, ClientSize.Width - verticalWidth);
        _verticalScrollBar.Bounds = new Rectangle(verticalLeft, 0,
            verticalWidth, Math.Max(0, ClientSize.Height - (horizontal ? horizontalHeight : 0)));
        var horizontalLeft = vertical && RightToLeft == RightToLeft.Yes ? verticalWidth : 0;
        _horizontalScrollBar.Bounds = new Rectangle(horizontalLeft,
            Math.Max(0, ClientSize.Height - horizontalHeight),
            Math.Max(0, ClientSize.Width - (vertical ? verticalWidth : 0)), horizontalHeight);
        _scrollCorner.Visible = vertical && horizontal;
        if (_scrollCorner.Visible)
            _scrollCorner.Bounds = new Rectangle(verticalLeft,
                ClientSize.Height - horizontalHeight, verticalWidth, horizontalHeight);
        _verticalScrollBar.BringToFront();
        _horizontalScrollBar.BringToFront();
        _scrollCorner.BringToFront();
    }

    private int ScaleLogical(int logicalPixels) => ModernDpi.ScaleToInt(logicalPixels, DeviceDpi);

    private void InvalidateNodeAndRelations(TreeNode node)
    {
        InvalidateNode(node);
        foreach (TreeNode child in node.Nodes) InvalidateNodeAndRelations(child);
        for (var parent = node.Parent; parent is not null; parent = parent.Parent) InvalidateNode(parent);
    }

    private void InvalidateNode(TreeNode? node)
    {
        if (node is null) return;
        // Match PaintBufferedClient's visible-row coordinates. TreeNode.Bounds is the native label
        // rectangle and can diverge from the full managed row after native scrollbar styles change.
        var rowTop = 0;
        for (var visible = TopNode; visible is not null && rowTop < ClientSize.Height;
             visible = GetNextExpandedNode(visible), rowTop += ItemHeight)
        {
            if (!ReferenceEquals(visible, node)) continue;
            Invalidate(new Rectangle(0, rowTop, ClientSize.Width,
                Math.Min(ItemHeight, ClientSize.Height - rowTop)), false);
            return;
        }
    }

    private const int SourceCopy = 0x00CC0020;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left; public int Top; public int Right; public int Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct PaintStruct
    {
        public IntPtr DeviceContext;
        [MarshalAs(UnmanagedType.Bool)] public bool Erase;
        public NativeRect Paint;
        [MarshalAs(UnmanagedType.Bool)] public bool Restore;
        [MarshalAs(UnmanagedType.Bool)] public bool IncUpdate;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] Reserved;
    }

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr window, int index);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr window, int index, int value);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y,
        int width, int height, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowScrollBar(IntPtr window, int bar, bool show);

    [DllImport("user32.dll")]
    private static extern int GetScrollPos(IntPtr window, int bar);

    [DllImport("user32.dll")]
    private static extern IntPtr BeginPaint(IntPtr window, out PaintStruct paint);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EndPaint(IntPtr window, ref PaintStruct paint);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr parameter, IntPtr data);

    [DllImport("user32.dll")]
    private static extern int FillRect(IntPtr deviceContext, ref NativeRect rectangle, IntPtr brush);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr deviceContext);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleBitmap(IntPtr deviceContext, int width, int height);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr deviceContext, IntPtr value);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateSolidBrush(int color);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr value);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteDC(IntPtr deviceContext);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BitBlt(IntPtr destination, int x, int y, int width, int height,
        IntPtr source, int sourceX, int sourceY, int operation);

    private readonly record struct NodeLayout(RectangleF ExpanderBounds, RectangleF CheckBoxBounds,
        Rectangle ImageBounds, Rectangle TextBounds);
}
