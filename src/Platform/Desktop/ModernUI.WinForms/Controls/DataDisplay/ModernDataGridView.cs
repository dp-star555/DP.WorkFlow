using System.ComponentModel;

namespace ModernUI.WinForms;

/// <summary>
/// 保留原生 DataGridView 数据绑定、编辑、排序、虚拟模式、Designer 和 UIA，并映射现代表格主题。
/// </summary>
[DefaultProperty(nameof(DataSource))]
[DefaultEvent(nameof(CellValueChanged))]
[Description("ModernDataGridView 现代数据表格")]
[DisplayName("现代数据表格")]
[ToolboxBitmap(typeof(ModernDataGridView), "Toolbox.Icons.DataDisplay.bmp")]
[ToolboxItem(true)]
public sealed class ModernDataGridView : DataGridView
{
    private ModernTheme _theme = ModernUiSettings.DefaultTheme;
    private int _rowHeight = 32;
    private int _headerHeight = 36;
    private int _cornerRadius = 8;
    private readonly ModernNativeScrollBarOverlay _verticalScrollBar;
    private readonly ModernNativeScrollBarOverlay _horizontalScrollBar;
    private readonly Panel _scrollCorner = new() { TabStop = false, Visible = false };
    private Size _roundedRegionSize;
    private int _roundedRegionDpi;
    private int _roundedRegionRadius = -1;
    private Size _lastResizeClientSize;
    private bool _dpiMetricsInvalid;
    private int _editingAppearanceGeneration;
    private FinalLayoutTransaction? _layoutTransaction;

    private FinalLayoutTransaction LayoutTransaction =>
        _layoutTransaction ??= new FinalLayoutTransaction(this, ApplyFinalLayout);

    /// <summary>初始化现代数据表格。</summary>
    // Overlay HWNDs use final client coordinates and must not be scaled again as child controls.
    protected override bool ScaleChildren => false;

    public ModernDataGridView()
    {
        BorderStyle = BorderStyle.None;
        CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
        RowHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
        EnableHeadersVisualStyles = false;
        DoubleBuffered = true;
        _verticalScrollBar = new ModernNativeScrollBarOverlay(
            new ManagedControlScrollAdapter(this,
                () => Math.Max(0, FirstDisplayedScrollingRowIndex),
                GetMaximumFirstDisplayedRowIndex,
                () => Math.Max(1, DisplayedRowCount(includePartialRow: false)),
                position =>
                {
                    if (RowCount > 0)
                        FirstDisplayedScrollingRowIndex = ModernCompatibility.Clamp(position, 0, GetMaximumFirstDisplayedRowIndex());
                }), vertical: true)
        {
            AutoVisibility = true
        };
        _horizontalScrollBar = new ModernNativeScrollBarOverlay(
            new ManagedControlScrollAdapter(this, () => HorizontalScrollingOffset,
                GetMaximumHorizontalOffset, () => Math.Max(1, DisplayRectangle.Width),
                position => HorizontalScrollingOffset = ModernCompatibility.Clamp(position, 0, GetMaximumHorizontalOffset()),
                reverseInRightToLeft: true),
            vertical: false)
        {
            AutoVisibility = true
        };
        Controls.AddRange([_verticalScrollBar, _horizontalScrollBar, _scrollCorner]);
        ApplyMetrics(updateExistingRows: false);
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
            UpdateRoundedSurface();
            Invalidate();
        }
    }

    /// <summary>获取或设置数据行高（96 DPI 下的逻辑像素）。</summary>
    [Category("Layout")]
    [DefaultValue(32)]
    public int RowHeight
    {
        get => _rowHeight;
        set
        {
            var normalized = ModernCompatibility.Clamp(value, 20, 160);
            if (_rowHeight == normalized) return;
            _rowHeight = normalized;
            ApplyMetrics(updateExistingRows: true);
        }
    }

    /// <summary>获取或设置列表头高度（96 DPI 下的逻辑像素）。</summary>
    [Category("Layout")]
    [DefaultValue(36)]
    public int HeaderHeight
    {
        get => _headerHeight;
        set
        {
            var normalized = ModernCompatibility.Clamp(value, 22, 160);
            if (_headerHeight == normalized) return;
            _headerHeight = normalized;
            ApplyMetrics(updateExistingRows: false);
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyMetrics(updateExistingRows: false);
        UpdateRoundedSurface();
        NativeControlTheme.ApplyExplorer(this, Theme.IsDark);
        _lastResizeClientSize = ClientSize;
        LayoutTransaction.Request();
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        _dpiMetricsInvalid = true;
        LayoutTransaction.Request();
    }

    protected override void OnResize(EventArgs e)
    {
        var previousSize = _lastResizeClientSize;
        base.OnResize(e);
        // Drop the old HWND clipping shape before Windows composes the resized child. Repaint only
        // the former and current border bands; a synchronous full-grid Update here stalls the
        // SplitContainer input transaction on large tables.
        ResetRoundedRegion();
        InvalidateResizeBands(previousSize, ClientSize);
        _lastResizeClientSize = ClientSize;
        LayoutScrollBars();
        LayoutTransaction.Request();
    }

    protected override void OnRightToLeftChanged(EventArgs e)
    {
        base.OnRightToLeftChanged(e);
        LayoutScrollBars();
        LayoutTransaction.Request();
    }

    protected override void OnScroll(ScrollEventArgs e)
    {
        base.OnScroll(e);
        // DataGridView uses ScrollWindow to shift its client pixels in both directions. The modern
        // rounded outline is painted in that same client surface, so after a vertical shift the copied
        // outline would stay inside the rows as a stray line; repaint completely after every shift.
        Invalidate(true);
        Update();
    }

    protected override void OnRowsAdded(DataGridViewRowsAddedEventArgs e)
    {
        base.OnRowsAdded(e);
        if (_horizontalScrollBar is not null) LayoutTransaction.Request();
    }

    protected override void OnRowsRemoved(DataGridViewRowsRemovedEventArgs e)
    {
        base.OnRowsRemoved(e);
        if (_horizontalScrollBar is not null) LayoutTransaction.Request();
    }

    protected override void OnDataBindingComplete(DataGridViewBindingCompleteEventArgs e)
    {
        base.OnDataBindingComplete(e);
        if (_horizontalScrollBar is not null) LayoutTransaction.Request();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (CornerRadius <= 0) return;
        if (UsesManagedSplitterRounding())
        {
            using var path = Geometry.CreateRoundedRectangle(ClientRectangle,
                ModernDpi.Scale(CornerRadius, DeviceDpi));
            using var outside = new Region(ClientRectangle);
            outside.Exclude(path);
            using var background = new SolidBrush(ResolveOpaqueParentBackground());
            e.Graphics.FillRegion(background, outside);
        }
        using var canvas = new GdiCanvas(e.Graphics);
        var inset = Math.Max(1f, DeviceDpi / 96f);
        canvas.Draw(Theme.Border, inset,
            RectangleF.Inflate(ClientRectangle, -inset, -inset),
            ModernDpi.Scale(CornerRadius, DeviceDpi));
    }

    protected override void OnColumnAdded(DataGridViewColumnEventArgs e)
    {
        base.OnColumnAdded(e);
        ApplyColumnTheme(e.Column);
        LayoutTransaction.Request();
    }

    protected override void OnColumnRemoved(DataGridViewColumnEventArgs e)
    {
        base.OnColumnRemoved(e);
        LayoutTransaction.Request();
    }

    protected override void OnColumnWidthChanged(DataGridViewColumnEventArgs e)
    {
        base.OnColumnWidthChanged(e);
        LayoutTransaction.Request();
    }

    protected override void OnEditingControlShowing(DataGridViewEditingControlShowingEventArgs e)
    {
        base.OnEditingControlShowing(e);
        SynchronizeEditingControlAppearance(e.Control);
        QueueFinalEditingControlAppearance(e.Control);
        if (e.Control is ComboBox comboBox) NativeControlTheme.ApplyComboBox(comboBox, Theme.IsDark);
        else NativeControlTheme.ApplyExplorer(e.Control, Theme.IsDark);
    }

    protected override void WndProc(ref Message message)
    {
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
        if (message.Msg == 0x0047 && IsHandleCreated && UsesManagedSplitterRounding())
            Update();
        if (message.Msg is 0x0114 or 0x0115 or 0x020A)
        {
            _verticalScrollBar?.RefreshFromTarget();
            _horizontalScrollBar?.RefreshFromTarget();
            LayoutScrollBars();
        }
    }

    protected override void OnCellPainting(DataGridViewCellPaintingEventArgs e)
    {
        if (e.RowIndex >= 0 && e.ColumnIndex >= 0 && e.Graphics is not null &&
            Columns[e.ColumnIndex] is ModernDataGridViewMultiSelectColumn multiSelectColumn)
        {
            multiSelectColumn.PaintCell(e, Theme, Enabled, Focused && ShowFocusCues, DeviceDpi);
            return;
        }
        if (e.RowIndex >= 0 && e.ColumnIndex >= 0
            && Columns[e.ColumnIndex] is DataGridViewComboBoxColumn && e.Graphics is not null)
        {
            PaintComboBoxCell(e);
            return;
        }

        if ((e.PaintParts & DataGridViewPaintParts.Focus) == 0)
        {
            base.OnCellPainting(e);
            return;
        }

        e.Paint(e.ClipBounds, e.PaintParts & ~DataGridViewPaintParts.Focus);
        if (Focused && ShowFocusCues && e.RowIndex >= 0 && e.ColumnIndex >= 0 && e.Graphics is not null)
        {
            var focusBounds = Rectangle.Inflate(e.CellBounds, -ScaleLogical(2), -ScaleLogical(2));
            using var canvas = new GdiCanvas(e.Graphics);
            ModernFocusVisual.Draw(canvas, Theme, focusBounds, ScaleLogical(2), DeviceDpi / 96f);
        }
        e.Handled = true;
    }

    private void PaintComboBoxCell(DataGridViewCellPaintingEventArgs e)
    {
        var selected = (e.State & DataGridViewElementStates.Selected) != 0;
        var cellStyle = e.CellStyle ?? DefaultCellStyle;
        e.PaintBackground(e.ClipBounds, selected);
        e.Paint(e.ClipBounds, DataGridViewPaintParts.Border);

        var arrowSize = ScaleLogical(12);
        var arrowBounds = new Rectangle(
            e.CellBounds.Right - ScaleLogical(8) - arrowSize,
            e.CellBounds.Top + (e.CellBounds.Height - arrowSize) / 2,
            arrowSize,
            arrowSize);
        var textBounds = new Rectangle(
            e.CellBounds.Left + ScaleLogical(8),
            e.CellBounds.Top,
            Math.Max(0, arrowBounds.Left - e.CellBounds.Left - ScaleLogical(12)),
            e.CellBounds.Height);
        var foreground = !Enabled
            ? Theme.TextDisabled
            : selected ? Theme.Primary : cellStyle.ForeColor;
        TextRenderer.DrawText(e.Graphics!, Convert.ToString(e.FormattedValue) ?? string.Empty,
            cellStyle.Font ?? Font, textBounds, foreground,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
            TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        using (var canvas = new GdiCanvas(e.Graphics!))
            canvas.DrawIcon(ModernIconKind.ChevronDown,
                !Enabled ? Theme.TextDisabled : Theme.TextSecondary, arrowBounds, Math.Max(1f, DeviceDpi / 96f));

        if ((e.PaintParts & DataGridViewPaintParts.Focus) != 0 && Focused && ShowFocusCues)
        {
            var focusBounds = Rectangle.Inflate(e.CellBounds, -ScaleLogical(2), -ScaleLogical(2));
            using var canvas = new GdiCanvas(e.Graphics!);
            ModernFocusVisual.Draw(canvas, Theme, focusBounds, ScaleLogical(2), DeviceDpi / 96f);
        }
        e.Handled = true;
    }

    private void ApplyTheme()
    {
        BackColor = Theme.Control;
        ForeColor = Theme.Text;
        _verticalScrollBar.Theme = Theme;
        _horizontalScrollBar.Theme = Theme;
        _scrollCorner.BackColor = Theme.Control;
        BackgroundColor = Theme.Control;
        GridColor = Theme.BorderSecondary;

        ApplyCellStyle(DefaultCellStyle, Theme.Control, Theme.Text, Theme.PrimaryBackground, Theme.Primary);
        ApplyCellStyle(RowsDefaultCellStyle, Theme.Control, Theme.Text, Theme.PrimaryBackground, Theme.Primary);
        ApplyCellStyle(AlternatingRowsDefaultCellStyle, Theme.Container, Theme.Text,
            Theme.PrimaryBackground, Theme.Primary);
        ApplyCellStyle(ColumnHeadersDefaultCellStyle, Theme.Container, Theme.TextSecondary,
            Theme.Container, Theme.TextSecondary);
        ApplyCellStyle(RowHeadersDefaultCellStyle, Theme.Container, Theme.TextSecondary,
            Theme.PrimaryBackground, Theme.Primary);

        var horizontalPadding = ScaleLogical(8);
        var cellPadding = new Padding(horizontalPadding, 0, horizontalPadding, 0);
        ColumnHeadersDefaultCellStyle.Padding = cellPadding;
        DefaultCellStyle.Padding = cellPadding;
        RowsDefaultCellStyle.Padding = cellPadding;
        AlternatingRowsDefaultCellStyle.Padding = cellPadding;
        foreach (DataGridViewColumn column in Columns) ApplyColumnTheme(column);
        NativeControlTheme.ApplyExplorer(this, Theme.IsDark);
        Invalidate();
    }

    private static void ApplyColumnTheme(DataGridViewColumn column)
    {
        if (column is DataGridViewComboBoxColumn comboBoxColumn)
            comboBoxColumn.FlatStyle = FlatStyle.Flat;
    }

    private static void ApplyCellStyle(DataGridViewCellStyle style, Color background, Color foreground,
        Color selectionBackground, Color selectionForeground)
    {
        style.BackColor = background;
        style.ForeColor = foreground;
        style.SelectionBackColor = selectionBackground;
        style.SelectionForeColor = selectionForeground;
    }

    private bool UsesManagedSplitterRounding() =>
        Parent is SplitterPanel && Parent.Parent is ModernSplitter;

    private Color ResolveOpaqueParentBackground()
    {
        for (Control? ancestor = Parent; ancestor is not null; ancestor = ancestor.Parent)
            if (ancestor.BackColor.A == byte.MaxValue) return ancestor.BackColor;
        return Theme.Background;
    }

    private void ApplyFinalLayout()
    {
        if (_dpiMetricsInvalid)
        {
            _dpiMetricsInvalid = false;
            ApplyMetrics(updateExistingRows: true);
        }
        // A native rounded HWND region is unsafe while a SplitContainer repeatedly moves this
        // child: Windows copies the former clipped edge into the enlarged surface. Splitter-hosted
        // grids mask their corners in OnPaint and keep a rectangular composition surface instead.
        UpdateRoundedSurface();
        // DataGridView creates its temporary editor during the parent DPI transaction. WinForms can
        // scale that child once more after EditingControlShowing, so restore the final inherited
        // cell font only after the coalesced DPI/layout transaction has completed.
        SynchronizeEditingControlAppearance(EditingControl);
        _verticalScrollBar.RefreshFromTarget();
        _horizontalScrollBar.RefreshFromTarget();
        LayoutScrollBars();
    }

    private void QueueFinalEditingControlAppearance(Control editor)
    {
        if (!IsHandleCreated || IsDisposed || Disposing) return;
        var generation = ++_editingAppearanceGeneration;
        BeginInvoke((Action)(() =>
        {
            if (generation != _editingAppearanceGeneration ||
                IsDisposed || editor.IsDisposed || !ReferenceEquals(editor, EditingControl)) return;
            SynchronizeEditingControlAppearance(editor);
            editor.Invalidate();
        }));
    }

    private void SynchronizeEditingControlAppearance(Control? editor)
    {
        if (editor is null || editor.IsDisposed) return;
        var inherited = CurrentCell?.InheritedStyle;
        var expectedFont = inherited?.Font ?? Font;
        if (!Equals(editor.Font, expectedFont)) editor.Font = expectedFont;
        editor.BackColor = Theme.Elevated;
        editor.ForeColor = Theme.Text;
    }

    private void ApplyMetrics(bool updateExistingRows)
    {
        var rowHeight = ScaleLogical(RowHeight);
        var changed = false;
        if (RowTemplate.Height != rowHeight)
        {
            RowTemplate.Height = rowHeight;
            changed = true;
        }
        var headerHeight = ScaleLogical(HeaderHeight);
        if (ColumnHeadersHeight != headerHeight)
        {
            ColumnHeadersHeight = headerHeight;
            changed = true;
        }

        if (updateExistingRows && !VirtualMode && AutoSizeRowsMode == DataGridViewAutoSizeRowsMode.None)
        {
            foreach (DataGridViewRow row in Rows)
            {
                if (row.IsNewRow || row.Height == rowHeight) continue;
                row.Height = rowHeight;
                changed = true;
            }
        }
        if (changed) Invalidate();
    }

    private void UpdateRoundedSurface()
    {
        if (UsesManagedSplitterRounding()) ResetRoundedRegion();
        else UpdateRoundedRegion();
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

    private void InvalidateResizeBands(Size previous, Size current)
    {
        var band = Math.Max(3, ScaleLogical(3));
        if (previous.Height > 0)
            Invalidate(new Rectangle(0, Math.Max(0, previous.Height - band), current.Width,
                Math.Min(current.Height, band * 2)));
        if (previous.Width > 0)
            Invalidate(new Rectangle(Math.Max(0, previous.Width - band), 0,
                Math.Min(current.Width, band * 2), current.Height));
        Invalidate(new Rectangle(0, Math.Max(0, current.Height - band), current.Width, band));
        Invalidate(new Rectangle(Math.Max(0, current.Width - band), 0, band, current.Height));
    }

    private void ResetRoundedRegion()
    {
        if (Region is not null)
        {
            var previous = Region;
            Region = null;
            previous.Dispose();
        }
        _roundedRegionSize = Size.Empty;
        _roundedRegionDpi = 0;
        _roundedRegionRadius = -1;
    }

    private int GetMaximumFirstDisplayedRowIndex() =>
        Math.Max(0, RowCount - Math.Max(1, DisplayedRowCount(includePartialRow: false)));

    private int GetMaximumHorizontalOffset()
    {
        var contentWidth = Columns.Cast<DataGridViewColumn>()
            .Where(column => column.Visible)
            .Sum(column => column.Width) + (RowHeadersVisible ? RowHeadersWidth : 0);
        return Math.Max(0, contentWidth - Math.Max(1, DisplayRectangle.Width));
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

    protected override void Dispose(bool disposing)
    {
        if (disposing && !IsDisposed && IsCurrentCellInEditMode)
        {
            // DataGridView.Dispose clears Columns before it fully detaches EditingControl. If a
            // modern popup editor is still active, the base layout path can address the removed
            // column and throw from GetColumnXFromIndex. Close and terminate the edit first.
            if (EditingControl is ModernSelect select) select.DroppedDown = false;
            _ = CancelEdit();
            _ = EndEdit();
        }
        base.Dispose(disposing);
    }

    private int ScaleLogical(int logicalPixels) => ModernDpi.ScaleToInt(logicalPixels, DeviceDpi);
}
