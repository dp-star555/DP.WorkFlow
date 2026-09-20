using System.ComponentModel;
using System.Runtime.InteropServices;

namespace ModernUI.WinForms;

/// <summary>
/// 保留原生 ListView 列、项目、虚拟模式、键盘、Designer 和 UIA 语义，并绘制现代表头与行状态。
/// </summary>
[DefaultProperty(nameof(Items))]
[DefaultEvent(nameof(SelectedIndexChanged))]
[Description("ModernListView 现代列表视图")]
[DisplayName("现代列表视图")]
[ToolboxBitmap(typeof(ModernListView), "Toolbox.Icons.DataDisplay.bmp")]
[ToolboxItem(true)]
public sealed partial class ModernListView : ListView
{
    private readonly ImageList _rowHeightImages = new();
    private readonly ModernNativeScrollBarOverlay _verticalScrollBar;
    private readonly ModernNativeScrollBarOverlay _horizontalScrollBar;
    private readonly Panel _scrollCorner = new() { TabStop = false, Visible = false };
    private HeaderWindow? _headerWindow;
    private ModernTheme _theme = ModernUiSettings.DefaultTheme;
    private int _hoveredIndex = -1;
    private int _rowHeight = 30;
    private int _horizontalPadding = 10;
    private int _cornerRadius = 8;
    private Size _roundedRegionSize;
    private int _roundedRegionDpi;
    private int _roundedRegionRadius = -1;
    private bool _dpiMetricsInvalid;
    private FinalLayoutTransaction? _layoutTransaction;

    private FinalLayoutTransaction LayoutTransaction =>
        _layoutTransaction ??= new FinalLayoutTransaction(this, ApplyFinalLayout);

    // Overlay HWNDs use final client coordinates and must not be scaled again as child controls.
    protected override bool ScaleChildren => false;

    /// <summary>初始化现代列表视图。</summary>
    public ModernListView()
    {
        BorderStyle = BorderStyle.None;
        View = View.Details;
        FullRowSelect = true;
        HideSelection = false;
        OwnerDraw = true;
        UseCompatibleStateImageBehavior = false;
        DoubleBuffered = true;
        _verticalScrollBar = new ModernNativeScrollBarOverlay(
            new NativeWindowScrollAdapter(this, vertical: true, setPosition: position =>
            {
                if (Items.Count > 0) TopItem = Items[ModernCompatibility.Clamp(position, 0, Items.Count - 1)];
            }), vertical: true)
        {
            AutoVisibility = true
        };
        _horizontalScrollBar = new ModernNativeScrollBarOverlay(
            new NativeWindowScrollAdapter(this, vertical: false, setPosition: ScrollHorizontallyTo,
                reverseInRightToLeft: true), vertical: false)
        {
            AutoVisibility = true
        };
        Controls.AddRange([_verticalScrollBar, _horizontalScrollBar, _scrollCorner]);
        ApplyMetrics();
        ApplyTheme();
        UpdateRoundedRegion();
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

    /// <summary>获取或设置外轮廓圆角半径（96 DPI 下的逻辑像素）。</summary>
    [Category("Appearance")]
    [DefaultValue(8)]
    public int CornerRadius
    {
        get => _cornerRadius;
        set
        {
            var normalized = Math.Max(0, value);
            if (_cornerRadius == normalized) return;
            _cornerRadius = normalized;
            UpdateRoundedRegion();
            Invalidate();
        }
    }

    /// <summary>
    /// 获取或设置 Details 行高（96 DPI 下的逻辑像素）。用户提供 SmallImageList 时由该图像列表决定原生行高。
    /// </summary>
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

    /// <summary>获取或设置单元格文本左右留白（96 DPI 下的逻辑像素）。</summary>
    [Category("Layout")]
    [DefaultValue(10)]
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
        UpdateRoundedRegion();
        ApplyNativeTheme();
        AttachHeaderWindow();
        LayoutTransaction.Request();
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        _headerWindow?.Release();
        _headerWindow = null;
        base.OnHandleDestroyed(e);
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        _dpiMetricsInvalid = true;
        LayoutTransaction.Request();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        LayoutScrollBars();
        LayoutTransaction.Request();
    }

    protected override void OnRightToLeftChanged(EventArgs e)
    {
        base.OnRightToLeftChanged(e);
        LayoutScrollBars();
        LayoutTransaction.Request();
    }

    protected override void OnDrawColumnHeader(DrawListViewColumnHeaderEventArgs e)
    {
        var backgroundBounds = e.ColumnIndex == Columns.Count - 1
            ? new Rectangle(e.Bounds.Left, e.Bounds.Top, Math.Max(e.Bounds.Width, ClientSize.Width - e.Bounds.Left), e.Bounds.Height)
            : e.Bounds;
        using (var background = new SolidBrush(Theme.Container)) e.Graphics.FillRectangle(background, backgroundBounds);
        using (var divider = new Pen(Theme.BorderSecondary))
        {
            e.Graphics.DrawLine(divider, e.Bounds.Right - 1, e.Bounds.Top + ScaleLogical(5),
                e.Bounds.Right - 1, e.Bounds.Bottom - ScaleLogical(5));
            e.Graphics.DrawLine(divider, backgroundBounds.Left, backgroundBounds.Bottom - 1,
                backgroundBounds.Right, backgroundBounds.Bottom - 1);
        }

        var alignment = e.Header?.TextAlign switch
        {
            HorizontalAlignment.Center => TextFormatFlags.HorizontalCenter,
            HorizontalAlignment.Right => RightToLeft == RightToLeft.Yes ? TextFormatFlags.Left : TextFormatFlags.Right,
            _ => RightToLeft == RightToLeft.Yes ? TextFormatFlags.Right : TextFormatFlags.Left
        };
        if (RightToLeft == RightToLeft.Yes) alignment |= TextFormatFlags.RightToLeft;
        var textBounds = Rectangle.Inflate(e.Bounds, -ScaleLogical(HorizontalPadding), 0);
        TextRenderer.DrawText(e.Graphics, e.Header?.Text ?? string.Empty, Font, textBounds,
            Theme.TextSecondary, alignment | TextFormatFlags.VerticalCenter |
            TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
    }

    protected override void OnDrawItem(DrawListViewItemEventArgs e)
    {
        var selected = e.Item.Selected;
        var disabled = !Enabled;
        var hovered = e.ItemIndex == _hoveredIndex && !disabled;
        var itemBackColor = e.Item.BackColor.IsEmpty ? BackColor : e.Item.BackColor;
        var background = disabled
            ? BackColor
            : selected ? Theme.PrimaryBackground
            : hovered ? Theme.ControlHover : itemBackColor;
        var rowBounds = View == View.Details
            ? new Rectangle(0, e.Bounds.Top, ClientSize.Width, e.Bounds.Height)
            : e.Bounds;
        using (var brush = new SolidBrush(background)) e.Graphics.FillRectangle(brush, rowBounds);

        if (View == View.Details) return;

        var foreground = ResolveForeground(e.Item, e.Item.ForeColor, selected, disabled);
        var textBounds = Rectangle.Inflate(e.Bounds, -ScaleLogical(HorizontalPadding), 0);
        TextRenderer.DrawText(e.Graphics, e.Item.Text, Font, textBounds, foreground,
            (RightToLeft == RightToLeft.Yes ? TextFormatFlags.Right | TextFormatFlags.RightToLeft : TextFormatFlags.Left) |
            TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis |
            TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
    }

    protected override void OnDrawSubItem(DrawListViewSubItemEventArgs e)
    {
        var item = e.Item;
        var subItem = e.SubItem;
        if (item is null || subItem is null) return;
        var selected = item.Selected;
        var disabled = !Enabled;
        var itemBackColor = item.BackColor.IsEmpty ? BackColor : item.BackColor;
        var background = disabled
            ? BackColor
            : selected ? Theme.PrimaryBackground
            : item.Index == _hoveredIndex ? Theme.ControlHover : itemBackColor;
        using (var backgroundBrush = new SolidBrush(background))
        {
            e.Graphics.FillRectangle(backgroundBrush, e.Bounds);
            if (e.ColumnIndex == Columns.Count - 1 && e.Bounds.Right < ClientSize.Width)
                e.Graphics.FillRectangle(backgroundBrush,
                    new Rectangle(e.Bounds.Right, e.Bounds.Top, ClientSize.Width - e.Bounds.Right, e.Bounds.Height));
        }
        var foreground = ResolveForeground(item, subItem.ForeColor, selected, disabled);
        var rightToLeft = RightToLeft == RightToLeft.Yes;
        var textLeft = e.Bounds.Left + ScaleLogical(HorizontalPadding);
        var textRight = e.Bounds.Right - ScaleLogical(HorizontalPadding);

        if (e.ColumnIndex == 0)
        {
            if (CheckBoxes)
            {
                var checkBounds = GetCheckBoxBounds(item);
                using (var canvas = new GdiCanvas(e.Graphics))
                    ModernCheckboxRenderer.Draw(canvas, checkBounds, Theme, item.Checked ? 1f : 0f,
                        false, item.Index == _hoveredIndex, Enabled, DeviceDpi / 96f);
                if (rightToLeft) textRight = checkBounds.Left - ScaleLogical(7);
                else textLeft = checkBounds.Right + ScaleLogical(7);
            }

            if (SmallImageList is not null && !ReferenceEquals(SmallImageList, _rowHeightImages)
                && item.ImageIndex >= 0 && item.ImageIndex < SmallImageList.Images.Count)
            {
                var imageSize = SmallImageList.ImageSize;
                var imageBounds = new Rectangle(rightToLeft ? textRight - imageSize.Width : textLeft,
                    e.Bounds.Top + (e.Bounds.Height - imageSize.Height) / 2,
                    imageSize.Width, imageSize.Height);
                SmallImageList.Draw(e.Graphics, imageBounds.Location, item.ImageIndex);
                if (rightToLeft) textRight = imageBounds.Left - ScaleLogical(6);
                else textLeft = imageBounds.Right + ScaleLogical(6);
            }
        }

        var alignment = e.Header?.TextAlign switch
        {
            HorizontalAlignment.Center => TextFormatFlags.HorizontalCenter,
            HorizontalAlignment.Right => rightToLeft ? TextFormatFlags.Left : TextFormatFlags.Right,
            _ => rightToLeft ? TextFormatFlags.Right : TextFormatFlags.Left
        };
        if (rightToLeft) alignment |= TextFormatFlags.RightToLeft;
        var textBounds = new Rectangle(textLeft, e.Bounds.Top,
            Math.Max(0, textRight - textLeft), e.Bounds.Height);
        TextRenderer.DrawText(e.Graphics, subItem.Text, Font, textBounds, foreground,
            alignment | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis |
            TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);

        if (selected && Focused && ShowFocusCues && e.ColumnIndex == Columns.Count - 1)
        {
            var focusBounds = Rectangle.Inflate(new Rectangle(0, e.Bounds.Top, ClientSize.Width, e.Bounds.Height),
                -ScaleLogical(3), -ScaleLogical(3));
            ControlPaint.DrawFocusRectangle(e.Graphics, focusBounds, Theme.Primary,
                disabled ? BackColor : Theme.PrimaryBackground);
        }
    }

    protected override void OnSelectedIndexChanged(EventArgs e)
    {
        base.OnSelectedIndexChanged(e);
        Invalidate();
    }

    protected override void OnColumnWidthChanged(ColumnWidthChangedEventArgs e)
    {
        base.OnColumnWidthChanged(e);
        LayoutTransaction.Request();
        Invalidate();
    }

    protected override void WndProc(ref Message message)
    {
        const int mouseLeftButtonDown = 0x0201;
        if (message.Msg == mouseLeftButtonDown && CheckBoxes && View == View.Details)
        {
            var packed = unchecked((long)message.LParam);
            var point = new Point(unchecked((short)(packed & 0xffff)),
                unchecked((short)((packed >> 16) & 0xffff)));
            var hitTestX = RightToLeft == RightToLeft.Yes
                ? Math.Min(point.X, ClientSize.Width - ScaleLogical(HorizontalPadding) - 1)
                : Math.Max(point.X, ScaleLogical(HorizontalPadding) + 1);
            var item = GetItemAt(hitTestX, point.Y) ??
                       Items.Cast<ListViewItem>().FirstOrDefault(candidate =>
                           point.Y >= candidate.Bounds.Top && point.Y < candidate.Bounds.Bottom);
            if (item is not null && GetCheckBoxBounds(item).Contains(point))
            {
                item.Checked = !item.Checked;
                item.Selected = true;
                Focus();
                Invalidate(item.Bounds);
                message.Result = IntPtr.Zero;
                return;
            }
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
        if (message.Msg is 0x0114 or 0x0115 or 0x020A)
        {
            _verticalScrollBar?.RefreshFromTarget();
            _horizontalScrollBar?.RefreshFromTarget();
            LayoutScrollBars();
        }
        if (message.Msg is 0x1007 or 0x104D or 0x1008 or 0x1009 or 0x102F or
            0x101B or 0x1061 or 0x101C or 0x101E)
            LayoutTransaction.Request();
        if (message.Msg == WindowPaint && IsHandleCreated)
        {
            if (ModernNativeScrollProtocol.HasNativeBar(this, vertical: true) ||
                ModernNativeScrollProtocol.HasNativeBar(this, vertical: false))
                LayoutTransaction.Request();
            if (CornerRadius > 0) PaintOuterBorder();
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var index = GetItemAt(e.X, e.Y)?.Index ?? -1;
        if (_hoveredIndex == index) return;
        InvalidateItem(_hoveredIndex);
        _hoveredIndex = index;
        InvalidateItem(_hoveredIndex);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        InvalidateItem(_hoveredIndex);
        _hoveredIndex = -1;
        base.OnMouseLeave(e);
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _headerWindow?.Release();
            _headerWindow = null;
            if (ReferenceEquals(SmallImageList, _rowHeightImages)) SmallImageList = null;
            _rowHeightImages.Dispose();
        }
        base.Dispose(disposing);
    }

    private Rectangle GetCheckBoxBounds(ListViewItem item)
    {
        var size = ScaleLogical(18);
        var row = item.Bounds;
        var x = RightToLeft == RightToLeft.Yes
            ? Math.Max(0, ClientSize.Width - ScaleLogical(HorizontalPadding) - size)
            : ScaleLogical(HorizontalPadding);
        return new Rectangle(x, row.Top + (row.Height - size) / 2, size, size);
    }

    private Color ResolveForeground(ListViewItem item, Color requested, bool selected, bool disabled)
    {
        if (disabled) return Theme.TextDisabled;
        if (selected) return Theme.Primary;
        if (!requested.IsEmpty) return requested;
        return item.ForeColor.IsEmpty ? ForeColor : item.ForeColor;
    }

    private void ApplyTheme()
    {
        BackColor = Theme.Control;
        ForeColor = Theme.Text;
        _verticalScrollBar.Theme = Theme;
        _horizontalScrollBar.Theme = Theme;
        _scrollCorner.BackColor = Theme.Control;
        ApplyNativeTheme();
        Invalidate();
    }

    private void ApplyNativeTheme()
    {
        NativeControlTheme.ApplyExplorer(this, Theme.IsDark);
        if (!IsHandleCreated) return;
        var headerHandle = SendMessage(Handle, ListViewGetHeader, 0, 0);
        NativeControlTheme.ApplyExplorer(headerHandle, Theme.IsDark);
        if (headerHandle != IntPtr.Zero) InvalidateRect(headerHandle, 0, true);
    }

    private void AttachHeaderWindow()
    {
        var headerHandle = SendMessage(Handle, ListViewGetHeader, 0, 0);
        if (headerHandle == IntPtr.Zero) return;
        _headerWindow?.Release();
        _headerWindow = new HeaderWindow(this, headerHandle);
    }

    private void PaintHeaderRemainder(nint headerHandle)
    {
        if (!GetClientRect(headerHandle, out var clientRect)) return;
        var remainderLeft = 0;
        if (Columns.Count > 0)
        {
            var itemRect = new NativeRect();
            if (SendMessage(headerHandle, HeaderGetItemRect, Columns.Count - 1, ref itemRect) != 0)
                remainderLeft = itemRect.Right;
        }
        if (remainderLeft >= clientRect.Right) return;

        using var graphics = Graphics.FromHwnd(headerHandle);
        var bounds = new Rectangle(remainderLeft, 0, clientRect.Right - remainderLeft, clientRect.Bottom);
        using (var background = new SolidBrush(Theme.Container)) graphics.FillRectangle(background, bounds);
        using var divider = new Pen(Theme.BorderSecondary);
        graphics.DrawLine(divider, bounds.Left, Math.Max(0, bounds.Bottom - 1), bounds.Right, Math.Max(0, bounds.Bottom - 1));
    }

    private void PaintOuterBorder()
    {
        using var graphics = Graphics.FromHwnd(Handle);
        using var canvas = new GdiCanvas(graphics);
        var inset = Math.Max(1f, DeviceDpi / 96f);
        canvas.Draw(Theme.Border, inset,
            RectangleF.Inflate(ClientRectangle, -inset, -inset),
            ModernDpi.Scale(CornerRadius, DeviceDpi));
    }

    private void ApplyFinalLayout()
    {
        if (_dpiMetricsInvalid)
        {
            _dpiMetricsInvalid = false;
            ApplyMetrics();
        }
        UpdateRoundedRegion();
        _verticalScrollBar.RefreshFromTarget();
        _horizontalScrollBar.RefreshFromTarget();
        LayoutScrollBars();
        Invalidate();
        Update();
    }

    private void UpdateRoundedRegion()
    {
        if (CornerRadius > 0)
        {
            if (_roundedRegionSize == ClientSize && _roundedRegionDpi == DeviceDpi &&
                _roundedRegionRadius == CornerRadius) return;
            RoundedNativeControlRegion.Apply(this, CornerRadius);
            _roundedRegionSize = ClientSize;
            _roundedRegionDpi = DeviceDpi;
            _roundedRegionRadius = CornerRadius;
            return;
        }

        if (Region is not null)
        {
            var previous = Region;
            Region = null;
            previous.Dispose();
        }
        _roundedRegionSize = Size.Empty;
        _roundedRegionDpi = 0;
        _roundedRegionRadius = 0;
    }

    private void ApplyMetrics()
    {
        if (SmallImageList is not null && !ReferenceEquals(SmallImageList, _rowHeightImages))
        {
            Invalidate();
            return;
        }

        var scaledHeight = Math.Min(256, ScaleLogical(RowHeight));
        if (_rowHeightImages.ImageSize.Height != scaledHeight)
        {
            if (ReferenceEquals(SmallImageList, _rowHeightImages)) SmallImageList = null;
            _rowHeightImages.ImageSize = new Size(1, scaledHeight);
        }
        if (SmallImageList is null) SmallImageList = _rowHeightImages;
        Invalidate();
    }

    private const int ListViewGetHeader = 0x101F;
    private const int HeaderGetItemRect = 0x1207;
    private const int WindowPaint = 0x000F;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    private static extern nint SendMessage(nint window, int message, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    private static extern nint SendMessage(nint window, int message, nint wParam, ref NativeRect lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(nint window, out NativeRect rectangle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool InvalidateRect(nint window, nint rectangle, bool erase);
}
