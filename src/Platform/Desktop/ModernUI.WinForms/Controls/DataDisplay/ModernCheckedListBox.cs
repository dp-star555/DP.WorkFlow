using System.ComponentModel;

namespace ModernUI.WinForms;

/// <summary>Preserves the native persistent checked-item list, keyboard, Designer and accessibility semantics.</summary>
[DefaultProperty(nameof(Items))]
[DefaultEvent(nameof(ItemCheck))]
[Description("ModernCheckedListBox 现代勾选列表")]
[DisplayName("现代勾选列表")]
[ToolboxBitmap(typeof(ModernCheckedListBox), "Toolbox.Icons.DataDisplay.bmp")]
[ToolboxItem(true)]
public sealed class ModernCheckedListBox : CheckedListBox
{
    private ModernTheme _theme = ModernUiSettings.DefaultTheme;
    private int _rowHeight = 30;
    private int _hoveredIndex = -1;
    private int _appliedItemHeight;
    private bool _readOnly;
    private bool _userCheckTransaction;
    private bool _bufferedPaintActive;
    private int _pendingCheckIndex = -1;
    private CheckState _pendingCheckState;

    public ModernCheckedListBox()
    {
        BorderStyle = BorderStyle.None;
        IntegralHeight = false;
        CheckOnClick = true;
        ThreeDCheckBoxes = false;
        DrawMode = DrawMode.OwnerDrawVariable;
        DoubleBuffered = true;
        ApplyMetrics();
        ApplyTheme();
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public ModernTheme Theme
    {
        get => _theme;
        set { _theme = value ?? throw new ArgumentNullException(nameof(value)); ApplyTheme(); }
    }

    [Category("Layout"), DefaultValue(30)]
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

    [Category("Behavior"), DefaultValue(false)]
    [Description("是否允许查看和移动焦点行但禁止用户改变勾选状态；程序仍可设置勾选项。")]
    public bool ReadOnly
    {
        get => _readOnly;
        set { if (_readOnly == value) return; _readOnly = value; Invalidate(); }
    }

    /// <summary>Occurs after a pending native ItemCheck change has been committed to CheckedItems.</summary>
    public event EventHandler? CheckedItemsChanged;

    /// <summary>Occurs when a user commits one checked-state change; programmatic changes do not raise it.</summary>
    public event EventHandler? CheckedItemsCommitted;

    protected override void OnItemCheck(ItemCheckEventArgs ice)
    {
        var userCommit = _userCheckTransaction && !ReadOnly && ice.NewValue != ice.CurrentValue;
        if (_userCheckTransaction && ReadOnly) ice.NewValue = ice.CurrentValue;
        var changed = ice.NewValue != ice.CurrentValue;
        if (changed)
        {
            _pendingCheckIndex = ice.Index;
            _pendingCheckState = ice.NewValue;
        }
        base.OnItemCheck(ice);
        if (!changed || !IsHandleCreated || IsDisposed) return;
        BeginInvoke((Action)(() =>
        {
            if (IsDisposed) return;
            _pendingCheckIndex = -1;
            CheckedItemsChanged?.Invoke(this, EventArgs.Empty);
            if (userCommit) CheckedItemsCommitted?.Invoke(this, EventArgs.Empty);
            InvalidateItem(ice.Index);
        }));
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
            message.Result = (IntPtr)1;
            return;
        }

        var userInput = message.Msg is 0x0201 or 0x0202 or 0x0203 or 0x0100 or 0x0102;
        var previous = _userCheckTransaction;
        if (userInput) _userCheckTransaction = true;
        try { base.WndProc(ref message); }
        finally { _userCheckTransaction = previous; }
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= Items.Count)
        {
            using var emptyBackground = new SolidBrush(BackColor);
            e.Graphics.FillRectangle(emptyBackground, e.Bounds);
            return;
        }

        var selected = (e.State & DrawItemState.Selected) != 0;
        var hovered = e.Index == _hoveredIndex && Enabled;
        var background = selected ? Theme.PrimaryBackground : hovered ? Theme.ControlHover : BackColor;
        using (var brush = new SolidBrush(background)) e.Graphics.FillRectangle(brush, e.Bounds);
        var checkSize = ScaleLogical(18);
        var rightToLeft = RightToLeft == RightToLeft.Yes;
        var checkBounds = new RectangleF(
            rightToLeft ? e.Bounds.Right - ScaleLogical(10) - checkSize : e.Bounds.Left + ScaleLogical(10),
            e.Bounds.Top + (e.Bounds.Height - checkSize) / 2f, checkSize, checkSize);
        var checkState = e.Index == _pendingCheckIndex ? _pendingCheckState : GetItemCheckState(e.Index);
        using (var canvas = new GdiCanvas(e.Graphics))
            ModernCheckboxRenderer.Draw(canvas, checkBounds, Theme,
                checkState == CheckState.Unchecked ? 0f : 1f, checkState == CheckState.Indeterminate,
                hovered, Enabled && !ReadOnly, DeviceDpi / 96f);
        var textLeft = rightToLeft ? e.Bounds.Left + ScaleLogical(10) : (int)checkBounds.Right + ScaleLogical(8);
        var textRight = rightToLeft ? (int)checkBounds.Left - ScaleLogical(8) : e.Bounds.Right - ScaleLogical(10);
        TextRenderer.DrawText(e.Graphics, GetItemText(Items[e.Index]), Font,
            new Rectangle(textLeft, e.Bounds.Top, Math.Max(0, textRight - textLeft), e.Bounds.Height),
            Enabled ? selected ? Theme.Primary : ForeColor : Theme.TextDisabled,
            (rightToLeft ? TextFormatFlags.Right | TextFormatFlags.RightToLeft : TextFormatFlags.Left) |
            TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis |
            TextFormatFlags.NoPrefix);
        if ((e.State & DrawItemState.Focus) != 0 && Focused && ShowFocusCues)
            ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(e.Bounds, -3, -2), Theme.Primary, background);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var index = IndexFromPoint(e.Location);
        if (_hoveredIndex == index) return;
        var previous = _hoveredIndex;
        _hoveredIndex = index;
        InvalidateItem(previous);
        InvalidateItem(index);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        var previous = _hoveredIndex;
        _hoveredIndex = -1;
        InvalidateItem(previous);
        base.OnMouseLeave(e);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyMetrics();
        NativeControlTheme.ApplyExplorer(this, Theme.IsDark);
    }

    protected override void OnDataSourceChanged(EventArgs e)
    {
        base.OnDataSourceChanged(e);
        ApplyMetrics();
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        ApplyMetrics();
    }

    protected override void OnMeasureItem(MeasureItemEventArgs e)
    {
        base.OnMeasureItem(e);
        e.ItemHeight = Math.Max(1, _appliedItemHeight);
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
        finally { _ = EndPaint(Handle, ref paint); }
    }

    private static int ColorRef(Color color) => color.R | color.G << 8 | color.B << 16;

    private void ApplyMetrics()
    {
        _appliedItemHeight = Math.Min(255,
            ModernDpi.ScaleToInt(RowHeight, DeviceDpi > 0 ? DeviceDpi : 96));
        if (base.ItemHeight != _appliedItemHeight) base.ItemHeight = _appliedItemHeight;
        if (IsHandleCreated && Items.Count > 0)
        {
            const int listBoxSetItemHeight = 0x01A0;
            _ = SendMessage(Handle, listBoxSetItemHeight, IntPtr.Zero, (IntPtr)_appliedItemHeight);
        }
        Invalidate();
    }

    private void ApplyTheme()
    {
        BackColor = Theme.Control;
        ForeColor = Theme.Text;
        NativeControlTheme.ApplyExplorer(this, Theme.IsDark);
        Invalidate();
    }

    private int ScaleLogical(int logicalPixels) =>
        ModernDpi.ScaleToInt(logicalPixels, DeviceDpi > 0 ? DeviceDpi : 96);

    private void InvalidateItem(int index)
    {
        if (index < 0 || index >= Items.Count || !IsHandleCreated) return;
        Invalidate(GetItemRectangle(index));
    }

    private const int SourceCopy = 0x00CC0020;

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct NativeRect { public int Left; public int Top; public int Right; public int Bottom; }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct PaintStruct
    {
        public IntPtr DeviceContext;
        [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)] public bool Erase;
        public NativeRect Paint;
        [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)] public bool Restore;
        [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)] public bool IncUpdate;
        [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.ByValArray, SizeConst = 32)] public byte[] Reserved;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr parameter, IntPtr data);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr BeginPaint(IntPtr window, out PaintStruct paint);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool EndPaint(IntPtr window, ref PaintStruct paint);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int FillRect(IntPtr deviceContext, ref NativeRect rectangle, IntPtr brush);

    [System.Runtime.InteropServices.DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr deviceContext);

    [System.Runtime.InteropServices.DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleBitmap(IntPtr deviceContext, int width, int height);

    [System.Runtime.InteropServices.DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr deviceContext, IntPtr value);

    [System.Runtime.InteropServices.DllImport("gdi32.dll")]
    private static extern IntPtr CreateSolidBrush(int color);

    [System.Runtime.InteropServices.DllImport("gdi32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr value);

    [System.Runtime.InteropServices.DllImport("gdi32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool DeleteDC(IntPtr deviceContext);

    [System.Runtime.InteropServices.DllImport("gdi32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool BitBlt(IntPtr destination, int x, int y, int width, int height,
        IntPtr source, int sourceX, int sourceY, int operation);
}
