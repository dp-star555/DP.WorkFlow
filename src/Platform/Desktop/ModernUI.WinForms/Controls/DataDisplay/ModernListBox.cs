using System.ComponentModel;
using System.Runtime.InteropServices;

namespace ModernUI.WinForms;

/// <summary>
/// 保留原生 ListBox 数据绑定、选择、键盘、滚动、Designer 和 UIA 语义，并绘制现代列表行状态。
/// </summary>
[DefaultProperty(nameof(Items))]
[DefaultEvent(nameof(SelectedIndexChanged))]
[Description("ModernListBox 现代列表控件")]
[DisplayName("现代列表框")]
[ToolboxBitmap(typeof(ModernListBox), "Toolbox.Icons.DataDisplay.bmp")]
[ToolboxItem(true)]
public sealed class ModernListBox : ListBox
{
    private ModernTheme _theme = ModernUiSettings.DefaultTheme;
    private int _hoveredIndex = NoMatches;
    private int _rowHeight = 30;
    private int _horizontalPadding = 12;
    private readonly ModernNativeScrollBarOverlay _verticalScrollBar;
    private readonly ModernNativeScrollBarOverlay _horizontalScrollBar;
    private readonly Panel _scrollCorner = new() { TabStop = false, Visible = false };
    private bool _dpiMetricsInvalid;
    private bool _bufferedPaintActive;
    private FinalLayoutTransaction? _layoutTransaction;

    private FinalLayoutTransaction LayoutTransaction =>
        _layoutTransaction ??= new FinalLayoutTransaction(this, ApplyFinalLayout);

    // Scroll overlays are positioned from the final client rectangle. Letting WinForms scale them
    // after the owner has already handled OnResize applies the DPI factor a second time.
    protected override bool ScaleChildren => false;

    /// <summary>初始化现代列表控件。</summary>
    protected override CreateParams CreateParams
    {
        get
        {
            const int verticalScrollStyle = 0x00200000;
            var parameters = base.CreateParams;
            // Keep WS_HSCROLL so the native ListBox remains the single horizontal-position source.
            // Its non-client chrome is hidden by the horizontal adapter after ScrollInfo exists.
            parameters.Style &= ~verticalScrollStyle;
            return parameters;
        }
    }

    public ModernListBox()
    {
        BorderStyle = BorderStyle.None;
        DrawMode = DrawMode.OwnerDrawFixed;
        IntegralHeight = false;
        DoubleBuffered = true;
        _verticalScrollBar = new ModernNativeScrollBarOverlay(
            new ManagedControlScrollAdapter(this, () => TopIndex, GetMaximumTopIndex, GetPageSize,
                position => TopIndex = ModernCompatibility.Clamp(position, 0, GetMaximumTopIndex())), vertical: true)
        {
            AutoVisibility = true
        };
        _horizontalScrollBar = new ModernNativeScrollBarOverlay(
            new NativeWindowScrollAdapter(this, vertical: false), vertical: false)
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

    /// <summary>获取或设置列表行高（96 DPI 下的逻辑像素）。</summary>
    [Category("Layout")]
    [DefaultValue(30)]
    public int RowHeight
    {
        get => _rowHeight;
        set
        {
            var normalized = ModernCompatibility.Clamp(value, 18, 128);
            if (_rowHeight == normalized) return;
            _rowHeight = normalized;
            ApplyMetrics();
        }
    }

    /// <summary>获取或设置文本左右留白（96 DPI 下的逻辑像素）。</summary>
    [Category("Layout")]
    [DefaultValue(12)]
    public int HorizontalPadding
    {
        get => _horizontalPadding;
        set
        {
            var normalized = Math.Max(4, value);
            if (_horizontalPadding == normalized) return;
            _horizontalPadding = normalized;
            Invalidate();
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyMetrics();
        NativeControlTheme.ApplyExplorer(this, Theme.IsDark);
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
        const int windowPaint = 0x000F;
        if (message.Msg == windowPaint && !_bufferedPaintActive && IsHandleCreated)
        {
            PaintBufferedWindow();
            message.Result = IntPtr.Zero;
            return;
        }

        const int windowEraseBackground = 0x0014;
        if (message.Msg == windowEraseBackground)
        {
            // Every owner-drawn row paints its complete bounds. Letting the native ListBox erase
            // first exposes a blank frame during horizontal thumb tracking.
            message.Result = (IntPtr)1;
            return;
        }

        const int windowStyleChanging = 0x007C;
        if (message.Msg == windowStyleChanging && message.WParam.ToInt32() == -16 && message.LParam != IntPtr.Zero)
        {
            const int horizontalScrollStyle = 0x00100000;
            const int verticalScrollStyle = 0x00200000;
            var style = Marshal.PtrToStructure<StyleStruct>(message.LParam);
            // CreateParams keeps WS_HSCROLL long enough for the native ListBox to initialize its
            // range and position. Once the managed overlays own chrome, native range updates such
            // as LB_SETHORIZONTALEXTENT must not re-expose either non-client scrollbar.
            style.NewStyle &= ~(horizontalScrollStyle | verticalScrollStyle);
            Marshal.StructureToPtr(style, message.LParam, false);
        }

        const int windowHorizontalScroll = 0x0114;
        if (message.Msg == windowHorizontalScroll && IsHandleCreated)
        {
            ProcessHorizontalScroll(ref message);
            return;
        }

        if (message.Msg == 0x020A && _verticalScrollBar is not null &&
            (((ModifierKeys & Keys.Shift) == Keys.Shift && _horizontalScrollBar.TryScrollWheel(unchecked((short)((long)message.WParam >> 16)))) ||
             _verticalScrollBar.TryScrollWheel(unchecked((short)((long)message.WParam >> 16)))))
        {
            message.Result = IntPtr.Zero;
            return;
        }
        if (MouseWheelRouting.TryRouteToAncestor(this, ref message))
        {
            message.Result = IntPtr.Zero;
            return;
        }
        base.WndProc(ref message);
        // Refresh metrics for explicit native scroll messages. Bounds are also verified below,
        // because keyboard navigation can scroll the child HWNDs without emitting these messages.
        if (message.Msg is 0x0115 or 0x0194 or 0x0197 or 0x020A)
        {
            _verticalScrollBar?.RefreshFromTarget();
            _horizontalScrollBar?.RefreshFromTarget();
            LayoutScrollBars();
        }
        if (message.Msg is 0x0180 or 0x0181 or 0x0182 or 0x0184 or 0x0194 or 0x01A7)
            LayoutTransaction.Request();

        // The native ListBox scrolls child HWNDs with its item surface. Keyboard navigation,
        // character search, selection changes and capture auto-scroll do not consistently emit
        // WM_VSCROLL, so repair chrome after only the messages that can move that surface.
        if (message.Msg is 0x0100 or 0x0102 or 0x0113 or 0x0185 or 0x0186 or 0x018C or 0x019E or 0x0200 or 0x0201)
            RestoreScrollBarBounds();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        // Native ListBox recalculates and can re-show WS_HSCROLL inside WM_SIZE. Remove its chrome
        // before positioning the managed overlay so no composed frame can contain both bars.
        ModernNativeScrollProtocol.HideNativeBar(this, vertical: false);
        // Bounds are visible immediately after WM_SIZE. Move existing chrome in this frame, while
        // range/visibility queries stay coalesced in the final layout transaction.
        LayoutScrollBars();
        LayoutTransaction.Request();
    }

    protected override void OnRightToLeftChanged(EventArgs e)
    {
        base.OnRightToLeftChanged(e);
        LayoutScrollBars();
        LayoutTransaction.Request();
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= Items.Count)
        {
            using var background = new SolidBrush(BackColor);
            e.Graphics.FillRectangle(background, e.Bounds);
            return;
        }

        var selected = (e.State & DrawItemState.Selected) != 0;
        var disabled = !Enabled || (e.State & DrawItemState.Disabled) != 0;
        var hovered = e.Index == _hoveredIndex && !disabled;
        var rowBounds = new Rectangle(0, e.Bounds.Top, ClientSize.Width, e.Bounds.Height);
        using (var background = new SolidBrush(BackColor)) e.Graphics.FillRectangle(background, rowBounds);

        var stateBounds = Rectangle.Inflate(rowBounds, -ScaleLogical(2), -ScaleLogical(1));
        var stateBackground = disabled
            ? BackColor
            : selected ? Theme.PrimaryBackground
            : hovered ? Theme.ControlHover : BackColor;
        if (stateBackground != BackColor)
        {
            using var canvas = new GdiCanvas(e.Graphics);
            canvas.Fill(stateBackground, stateBounds, ScaleLogical(Theme.Radius));
        }

        var foreground = disabled
            ? Theme.TextDisabled
            : selected ? Theme.Primary : ForeColor;
        var rightToLeft = RightToLeft == RightToLeft.Yes;
        var scrollPosition = IsHandleCreated ? GetScrollPos(Handle, 0) : 0;
        var contentWidth = Math.Max(ClientSize.Width, HorizontalExtent);
        var textBounds = new Rectangle(
            -scrollPosition + ScaleLogical(HorizontalPadding),
            rowBounds.Top,
            Math.Max(0, contentWidth - ScaleLogical(HorizontalPadding * 2)),
            rowBounds.Height);
        TextRenderer.DrawText(e.Graphics, GetItemText(Items[e.Index]), Font, textBounds, foreground,
            (rightToLeft ? TextFormatFlags.Right | TextFormatFlags.RightToLeft : TextFormatFlags.Left) |
            TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
            TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

        if ((e.State & DrawItemState.Focus) != 0 && Focused && ShowFocusCues)
        {
            using var canvas = new GdiCanvas(e.Graphics);
            ModernFocusVisual.Draw(canvas, Theme, stateBounds, ScaleLogical(Theme.Radius), DeviceDpi / 96f);
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var index = IndexFromPoint(e.Location);
        if (_hoveredIndex == index) return;
        InvalidateItem(_hoveredIndex);
        _hoveredIndex = index;
        InvalidateItem(_hoveredIndex);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        InvalidateItem(_hoveredIndex);
        _hoveredIndex = NoMatches;
        base.OnMouseLeave(e);
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        Invalidate();
    }

    private void ProcessHorizontalScroll(ref Message message)
    {
        if (!ModernNativeScrollProtocol.TryGetState(this, vertical: false, out var state))
        {
            message.Result = IntPtr.Zero;
            return;
        }

        var command = unchecked((ushort)(long)message.WParam);
        var requestedPosition = unchecked((ushort)((long)message.WParam >> 16));
        var lineStep = Math.Max(1, ScaleLogical(16));
        var next = command switch
        {
            0 => state.Position - lineStep,
            1 => state.Position + lineStep,
            2 => state.Position - state.PageSize,
            3 => state.Position + state.PageSize,
            4 or 5 => requestedPosition,
            6 => state.Minimum,
            7 => state.MaximumPosition,
            _ => state.Position
        };
        next = ModernCompatibility.Clamp(next, state.Minimum, state.MaximumPosition);

        // Calling the native ListBox WM_HSCROLL implementation invokes ScrollWindowEx, which
        // visibly moves old row pixels and every overlay child HWND before OwnerDraw can repair the
        // frame. Keep the native SCROLLINFO as the position source, but own the atomic visual commit.
        SetNativeHorizontalPosition(next);
        _verticalScrollBar.RefreshFromTarget();
        _horizontalScrollBar.RefreshFromTarget();
        LayoutScrollBars();
        PaintBufferedClientNow();
        message.Result = IntPtr.Zero;
    }

    private void SetNativeHorizontalPosition(int position)
    {
        const uint scrollInfoPosition = 0x0004;
        var info = new NativeScrollInfo
        {
            Size = (uint)Marshal.SizeOf<NativeScrollInfo>(),
            Mask = scrollInfoPosition,
            Position = position
        };
        _ = SetScrollInfo(Handle, 0, ref info, redraw: false);
    }

    private void PaintBufferedWindow()
    {
        var paint = new PaintStruct();
        var target = BeginPaint(Handle, out paint);
        if (target == IntPtr.Zero) return;
        try
        {
            var bounds = Rectangle.FromLTRB(paint.Paint.Left, paint.Paint.Top,
                paint.Paint.Right, paint.Paint.Bottom);
            if (bounds.IsEmpty) bounds = ClientRectangle;
            PaintBufferedClient(target, bounds);
        }
        finally { _ = EndPaint(Handle, ref paint); }
    }

    private void PaintBufferedClientNow()
    {
        var target = GetDC(Handle);
        if (target == IntPtr.Zero) return;
        try { PaintBufferedClient(target, ClientRectangle); }
        finally { _ = ReleaseDC(Handle, target); }
    }

    private void PaintBufferedClient(IntPtr target, Rectangle bounds)
    {
        if (ClientSize.Width <= 0 || ClientSize.Height <= 0 || bounds.IsEmpty) return;
        var memory = CreateCompatibleDC(target);
        if (memory == IntPtr.Zero) return;
        var bitmap = CreateCompatibleBitmap(target, ClientSize.Width, ClientSize.Height);
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
            _bufferedPaintActive = true;
            try
            {
                const int windowPrintClient = 0x0318;
                const int printClient = 0x00000004;
                _ = SendMessage(Handle, windowPrintClient, memory, (IntPtr)printClient);
            }
            finally { _bufferedPaintActive = false; }
            _ = BitBlt(target, bounds.Left, bounds.Top, bounds.Width, bounds.Height,
                memory, bounds.Left, bounds.Top, SourceCopy);
        }
        finally
        {
            _ = SelectObject(memory, previous);
            _ = DeleteObject(bitmap);
            _ = DeleteDC(memory);
        }
    }

    private static int ColorRef(Color color) => color.R | color.G << 8 | color.B << 16;

    private void ApplyTheme()
    {
        BackColor = Theme.Control;
        ForeColor = Theme.Text;
        NativeControlTheme.ApplyExplorer(this, Theme.IsDark);
        _verticalScrollBar.Theme = Theme;
        _horizontalScrollBar.Theme = Theme;
        _scrollCorner.BackColor = Theme.Control;
        Invalidate();
    }

    private void ApplyFinalLayout()
    {
        if (_dpiMetricsInvalid)
        {
            _dpiMetricsInvalid = false;
            ApplyMetrics();
        }
        _verticalScrollBar.RefreshFromTarget();
        _horizontalScrollBar.RefreshFromTarget();
        LayoutScrollBars();
    }

    private void ApplyMetrics()
    {
        var itemHeight = Math.Min(255, ScaleLogical(RowHeight));
        if (ItemHeight == itemHeight) return;
        ItemHeight = itemHeight;
        Invalidate();
    }

    private void LayoutScrollBars()
    {
        if (_verticalScrollBar is null || _horizontalScrollBar is null) return;
        RestoreScrollBarBounds();
        _verticalScrollBar.BringToFront();
        _horizontalScrollBar.BringToFront();
        _scrollCorner.BringToFront();
    }

    private void RestoreScrollBarBounds()
    {
        if (_verticalScrollBar is null || _horizontalScrollBar is null) return;
        var vertical = _verticalScrollBar.Visible;
        var horizontal = _horizontalScrollBar.Visible;
        var verticalWidth = SystemInformation.VerticalScrollBarWidth;
        var horizontalHeight = SystemInformation.HorizontalScrollBarHeight;
        var verticalLeft = RightToLeft == RightToLeft.Yes ? 0 : Math.Max(0, ClientSize.Width - verticalWidth);
        var verticalBounds = new Rectangle(verticalLeft, 0,
            verticalWidth, Math.Max(0, ClientSize.Height - (horizontal ? horizontalHeight : 0)));
        var horizontalLeft = vertical && RightToLeft == RightToLeft.Yes ? verticalWidth : 0;
        var horizontalBounds = new Rectangle(horizontalLeft,
            Math.Max(0, ClientSize.Height - horizontalHeight),
            Math.Max(0, ClientSize.Width - (vertical ? verticalWidth : 0)), horizontalHeight);
        var showCorner = vertical && horizontal;
        var cornerBounds = new Rectangle(verticalLeft,
            ClientSize.Height - horizontalHeight, verticalWidth, horizontalHeight);

        if (_verticalScrollBar.Bounds != verticalBounds) _verticalScrollBar.Bounds = verticalBounds;
        if (_horizontalScrollBar.Bounds != horizontalBounds) _horizontalScrollBar.Bounds = horizontalBounds;
        if (_scrollCorner.Visible != showCorner) _scrollCorner.Visible = showCorner;
        if (showCorner && _scrollCorner.Bounds != cornerBounds) _scrollCorner.Bounds = cornerBounds;
    }

    private const int SourceCopy = 0x00CC0020;

    [StructLayout(LayoutKind.Sequential)]
    private struct StyleStruct
    {
        public int OldStyle;
        public int NewStyle;
    }

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

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeScrollInfo
    {
        public uint Size;
        public uint Mask;
        public int Minimum;
        public int Maximum;
        public uint Page;
        public int Position;
        public int TrackPosition;
    }

    [DllImport("user32.dll")]
    private static extern int SetScrollInfo(IntPtr window, int bar, ref NativeScrollInfo info,
        [MarshalAs(UnmanagedType.Bool)] bool redraw);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr parameter, IntPtr data);

    [DllImport("user32.dll")]
    private static extern int GetScrollPos(IntPtr window, int bar);

    [DllImport("user32.dll")]
    private static extern IntPtr BeginPaint(IntPtr window, out PaintStruct paint);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EndPaint(IntPtr window, ref PaintStruct paint);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr window);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr window, IntPtr deviceContext);

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

    private int GetPageSize() => Math.Max(1, ClientSize.Height / Math.Max(1, ItemHeight));

    private int GetMaximumTopIndex() => Math.Max(0, Items.Count - GetPageSize());

    private int ScaleLogical(int logicalPixels) => ModernDpi.ScaleToInt(logicalPixels, DeviceDpi);

    private void InvalidateItem(int index)
    {
        if (index < 0 || index >= Items.Count || !IsHandleCreated) return;
        Invalidate(GetItemRectangle(index));
    }
}
