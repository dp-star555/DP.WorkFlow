using System.Collections;
using System.ComponentModel;
using System.Globalization;

namespace ModernUI.WinForms;

/// <summary>
/// 使用 <see cref="ModernSelectMultiple"/> 编辑集合值，并在非编辑状态下以标签展示所选项。
/// 单元格值使用 <see cref="Separator"/> 分隔，候选项由 DataSource 或 Items 提供。
/// </summary>
[ToolboxItem(false)]
public sealed class ModernDataGridViewMultiSelectColumn : DataGridViewColumn
{
    private readonly List<object> _items = new();

    public ModernDataGridViewMultiSelectColumn() : base(new ModernDataGridViewMultiSelectCell())
    {
        DefaultCellStyle = new DataGridViewCellStyle { Padding = Padding.Empty };
    }

    [DefaultValue(null)]
    public object? DataSource { get; set; }

    [DefaultValue("")]
    public string DisplayMember { get; set; } = string.Empty;

    [DefaultValue("")]
    public string ValueMember { get; set; } = string.Empty;

    [DefaultValue(",")]
    public string Separator { get; set; } = ",";

    [DefaultValue(3)]
    public int MaxVisibleTags { get; set; } = 3;

    /// <summary>是否忽略固定标签数量上限，改为铺满单元格实际可用空间。</summary>
    [DefaultValue(false)]
    public bool AutoFitVisibleTags { get; set; }

    /// <summary>单个标签允许使用的最大逻辑像素宽度。</summary>
    [DefaultValue(170)]
    public int MaximumTagWidth { get; set; } = 170;

    /// <summary>是否在非编辑状态的标签上显示可直接删除该值的按钮。</summary>
    [DefaultValue(false)]
    public bool ShowTagRemoveButtons { get; set; }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Content)]
    public IList<object> Items => _items;

    public override object Clone()
    {
        var clone = (ModernDataGridViewMultiSelectColumn)base.Clone();
        clone.DataSource = DataSource;
        clone.DisplayMember = DisplayMember;
        clone.ValueMember = ValueMember;
        clone.Separator = Separator;
        clone.MaxVisibleTags = MaxVisibleTags;
        clone.AutoFitVisibleTags = AutoFitVisibleTags;
        clone.MaximumTagWidth = MaximumTagWidth;
        clone.ShowTagRemoveButtons = ShowTagRemoveButtons;
        clone._items.AddRange(_items);
        return clone;
    }

    internal IEnumerable<object> EnumerateItems()
    {
        if (DataSource is IListSource listSource)
            return listSource.GetList().Cast<object>();
        if (DataSource is IEnumerable enumerable)
            return enumerable.Cast<object>();
        return _items;
    }

    internal object? GetItemValue(object item) => DataMemberResolver.Resolve(item, ValueMember);

    internal string GetItemText(object item)
    {
        object? value = DataMemberResolver.Resolve(item, DisplayMember);
        return Convert.ToString(value, CultureInfo.CurrentCulture) ?? string.Empty;
    }

    internal IReadOnlyList<string> SplitValue(object? value)
    {
        string raw = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(raw)) return Array.Empty<string>();
        char separator = string.IsNullOrEmpty(Separator) ? ',' : Separator[0];
        return raw.Split(new[] { separator, ';', '|' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(item => item.Trim())
            .Where(item => item.Length > 0)
            .ToArray();
    }

    internal string FormatValues(IEnumerable<object?> values)
    {
        string separator = string.IsNullOrEmpty(Separator) ? "," : Separator;
        return string.Join(separator, values.Select(value =>
            Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty));
    }

    internal string ResolveDisplayText(string token)
    {
        object? match = EnumerateItems().FirstOrDefault(item => string.Equals(
            Convert.ToString(GetItemValue(item), CultureInfo.InvariantCulture),
            token,
            StringComparison.OrdinalIgnoreCase));
        return match is null ? token : GetItemText(match);
    }

    internal void PaintCell(
        DataGridViewCellPaintingEventArgs e,
        ModernTheme theme,
        bool gridEnabled,
        bool gridFocused,
        int dpi)
    {
        bool selected = (e.State & DataGridViewElementStates.Selected) != 0;
        e.PaintBackground(e.ClipBounds, selected);
        e.Paint(e.ClipBounds, DataGridViewPaintParts.Border);

        int Scale(int value) => ModernDpi.ScaleToInt(value, dpi);
        var arrowSize = Scale(12);
        var arrowBounds = new Rectangle(
            e.CellBounds.Right - Scale(9) - arrowSize,
            e.CellBounds.Top + (e.CellBounds.Height - arrowSize) / 2,
            arrowSize,
            arrowSize);
        var contentBounds = new Rectangle(
            e.CellBounds.Left + Scale(8),
            e.CellBounds.Top + Scale(5),
            Math.Max(0, arrowBounds.Left - e.CellBounds.Left - Scale(12)),
            Math.Max(0, e.CellBounds.Height - Scale(10)));

        IReadOnlyList<string> tokens = SplitValue(e.FormattedValue);
        int x = contentBounds.Left;
        int drawn = 0;
        using var canvas = new GdiCanvas(e.Graphics!);
        int visibleLimit = AutoFitVisibleTags ? tokens.Count : Math.Max(1, MaxVisibleTags);
        foreach (string token in tokens.Take(visibleLimit))
        {
            string text = ResolveDisplayText(token);
            int measured = TextRenderer.MeasureText(
                text,
                e.CellStyle?.Font ?? DataGridView?.Font ?? Control.DefaultFont,
                Size.Empty,
                TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width;
            int width = Math.Min(
                Scale(Math.Max(60, MaximumTagWidth)),
                measured + Scale(ShowTagRemoveButtons ? 36 : 18));
            if (x + width > contentBounds.Right) break;

            var tagBounds = new Rectangle(x, contentBounds.Top, width, contentBounds.Height);
            canvas.Fill(theme.PrimaryBackground, tagBounds, Scale(4));
            canvas.Draw(Geometry.Blend(theme.Border, theme.Primary, .25f), Scale(1), tagBounds, Scale(4));
            Rectangle textBounds = Rectangle.Inflate(tagBounds, -Scale(7), 0);
            if (ShowTagRemoveButtons)
            {
                var closeBounds = new Rectangle(
                    tagBounds.Right - Scale(18),
                    tagBounds.Top + (tagBounds.Height - Scale(12)) / 2,
                    Scale(12),
                    Scale(12));
                textBounds = new Rectangle(
                    tagBounds.Left + Scale(7),
                    tagBounds.Top,
                    Math.Max(0, closeBounds.Left - tagBounds.Left - Scale(10)),
                    tagBounds.Height);
                canvas.DrawIcon(ModernIconKind.Close, theme.TextSecondary, closeBounds, Math.Max(1f, dpi / 96f));
            }
            TextRenderer.DrawText(
                e.Graphics!,
                text,
                e.CellStyle?.Font ?? DataGridView?.Font ?? Control.DefaultFont,
                textBounds,
                gridEnabled ? theme.Text : theme.TextDisabled,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
                TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            x = tagBounds.Right + Scale(4);
            drawn++;
        }

        int hidden = tokens.Count - drawn;
        if (hidden > 0 && x < contentBounds.Right)
        {
            TextRenderer.DrawText(
                e.Graphics!,
                "+" + hidden.ToString(CultureInfo.InvariantCulture),
                e.CellStyle?.Font ?? DataGridView?.Font ?? Control.DefaultFont,
                new Rectangle(x, e.CellBounds.Top, contentBounds.Right - x, e.CellBounds.Height),
                theme.TextSecondary,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
                TextFormatFlags.NoPrefix);
        }

        canvas.DrawIcon(
            ModernIconKind.ChevronDown,
            gridEnabled ? theme.TextSecondary : theme.TextDisabled,
            arrowBounds,
            Math.Max(1f, dpi / 96f));

        if ((e.PaintParts & DataGridViewPaintParts.Focus) != 0 && gridFocused)
        {
            var focusBounds = Rectangle.Inflate(e.CellBounds, -Scale(2), -Scale(2));
            ModernFocusVisual.Draw(canvas, theme, focusBounds, Scale(2), dpi / 96f);
        }
        e.Handled = true;
    }

    internal bool TryRemoveTagAt(
        object? value,
        Size cellSize,
        Point point,
        Font font,
        int dpi,
        out string nextValue)
    {
        IReadOnlyList<string> tokens = SplitValue(value);
        nextValue = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        if (!ShowTagRemoveButtons || tokens.Count == 0) return false;

        int Scale(int logical) => ModernDpi.ScaleToInt(logical, dpi);
        int arrowSize = Scale(12);
        int contentRight = Math.Max(0, cellSize.Width - Scale(9) - arrowSize - Scale(12));
        int x = Scale(8);
        int top = Scale(5);
        int height = Math.Max(0, cellSize.Height - Scale(10));
        int visibleLimit = AutoFitVisibleTags ? tokens.Count : Math.Max(1, MaxVisibleTags);
        for (int index = 0; index < tokens.Count && index < visibleLimit; index++)
        {
            string text = ResolveDisplayText(tokens[index]);
            int measured = TextRenderer.MeasureText(text, font, Size.Empty,
                TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width;
            int width = Math.Min(Scale(Math.Max(60, MaximumTagWidth)), measured + Scale(36));
            if (x + width > contentRight) break;
            var tagBounds = new Rectangle(x, top, width, height);
            var closeBounds = new Rectangle(
                tagBounds.Right - Scale(18),
                tagBounds.Top + (tagBounds.Height - Scale(12)) / 2,
                Scale(12),
                Scale(12));
            if (closeBounds.Contains(point))
            {
                nextValue = FormatValues(tokens.Where((_, tokenIndex) => tokenIndex != index));
                return true;
            }
            x = tagBounds.Right + Scale(4);
        }
        return false;
    }
}

[ToolboxItem(false)]
public sealed class ModernDataGridViewMultiSelectCell : DataGridViewTextBoxCell
{
    public override Type EditType => typeof(ModernDataGridViewMultiSelectEditingControl);
    public override Type FormattedValueType => typeof(string);
    public override Type ValueType => typeof(string);
    public override object DefaultNewRowValue => string.Empty;

    protected override void OnMouseDown(DataGridViewCellMouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left || DataGridView is null || e.RowIndex < 0 || e.ColumnIndex < 0) return;

        DataGridViewCell targetCell = DataGridView.Rows[e.RowIndex].Cells[e.ColumnIndex];
        DataGridView.CurrentCell = targetCell;
        if (OwningColumn is ModernDataGridViewMultiSelectColumn column &&
            column.TryRemoveTagAt(
                targetCell.Value,
                targetCell.Size,
                new Point(e.X, e.Y),
                targetCell.InheritedStyle.Font ?? DataGridView.Font,
                DataGridView.DeviceDpi,
                out string nextValue))
        {
            targetCell.Value = nextValue;
            DataGridView.InvalidateCell(targetCell);
            return;
        }
        if (!DataGridView.IsCurrentCellInEditMode && !DataGridView.BeginEdit(true)) return;
        if (DataGridView.EditingControl is ModernDataGridViewMultiSelectEditingControl editor)
            editor.DroppedDown = true;
    }

    public override void InitializeEditingControl(
        int rowIndex,
        object? initialFormattedValue,
        DataGridViewCellStyle dataGridViewCellStyle)
    {
        base.InitializeEditingControl(rowIndex, initialFormattedValue, dataGridViewCellStyle);
        if (DataGridView?.EditingControl is not ModernDataGridViewMultiSelectEditingControl editor ||
            OwningColumn is not ModernDataGridViewMultiSelectColumn column)
            return;

        object? cellValue = rowIndex >= 0 && DataGridView is not null && rowIndex < DataGridView.Rows.Count
            ? GetValue(rowIndex)
            : initialFormattedValue;
        editor.Configure(
            column,
            DataGridView is ModernDataGridView modernGrid
                ? modernGrid.Theme
                : ModernUiSettings.DefaultTheme,
            cellValue ?? initialFormattedValue);
    }
}

/// <summary>将 ModernSelectMultiple 适配到 DataGridView 编辑生命周期。</summary>
[ToolboxItem(false)]
public sealed class ModernDataGridViewMultiSelectEditingControl : UserControl, IDataGridViewEditingControl
{
    private readonly ModernSelectMultiple _selector;
    private readonly List<string> _selectedValueOrder = new();
    private ModernDataGridViewMultiSelectColumn? _column;
    private bool _initializing;

    public ModernDataGridViewMultiSelectEditingControl()
    {
        AutoScaleMode = AutoScaleMode.Inherit;
        Margin = Padding.Empty;
        Padding = Padding.Empty;
        MinimumSize = Size.Empty;
        _selector = new ModernSelectMultiple
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            MinimumSize = Size.Empty,
            DisplayMode = MultipleSelectionDisplayMode.Tags,
            ShowFocusBorder = false
        };
        _selector.SelectionChanged += (_, _) =>
        {
            if (_initializing) return;
            SynchronizeSelectedValueOrder();
            EditingControlValueChanged = true;
            EditingControlDataGridView?.NotifyCurrentCellDirty(true);
        };
        Controls.Add(_selector);
    }

    public bool DroppedDown
    {
        get => _selector.DroppedDown;
        set => _selector.DroppedDown = value;
    }

    public DataGridView? EditingControlDataGridView { get; set; }

    public object EditingControlFormattedValue
    {
        get
        {
            SynchronizeSelectedValueOrder();
            return _column?.FormatValues(_selectedValueOrder) ?? string.Empty;
        }
        set => SelectCellValue(value);
    }

    public int EditingControlRowIndex { get; set; }
    public bool EditingControlValueChanged { get; set; }
    public Cursor EditingPanelCursor => Cursors.Default;
    public bool RepositionEditingControlOnValueChange => false;

    internal void Configure(
        ModernDataGridViewMultiSelectColumn column,
        ModernTheme theme,
        object? value)
    {
        _initializing = true;
        try
        {
            _column = column;
            _selector.Theme = theme;
            _selector.DisplayMember = column.DisplayMember;
            _selector.ValueMember = column.ValueMember;
            _selector.DisplayMode = MultipleSelectionDisplayMode.Tags;
            _selector.MaxVisibleTags = column.AutoFitVisibleTags ? int.MaxValue : column.MaxVisibleTags;
            _selector.DataSource = null;
            _selector.Items.Clear();
            // Never bind the editor to a BindingSource already owned by another grid. Sharing its
            // currency manager can move the source grid while DataGridView is still initializing
            // this row, producing an out-of-range rowIndex exception. A cell editor only needs a
            // stable candidate snapshot for the lifetime of the popup.
            foreach (object item in column.EnumerateItems().ToArray())
                _selector.Items.Add(item);
            SelectCellValue(value);
            EditingControlValueChanged = false;
        }
        finally
        {
            _initializing = false;
        }
    }

    public void ApplyCellStyleToEditingControl(DataGridViewCellStyle dataGridViewCellStyle)
    {
        Font = dataGridViewCellStyle.Font ?? EditingControlDataGridView?.Font ?? Font;
        if (EditingControlDataGridView is ModernDataGridView modernGrid)
            _selector.Theme = modernGrid.Theme;
    }

    public bool EditingControlWantsInputKey(Keys keyData, bool dataGridViewWantsInputKey) =>
        (keyData & Keys.KeyCode) is Keys.Up or Keys.Down or Keys.Left or Keys.Right or Keys.Home or Keys.End
            or Keys.Enter or Keys.Space or Keys.Escape or Keys.PageUp or Keys.PageDown
        || !dataGridViewWantsInputKey;

    public object GetEditingControlFormattedValue(DataGridViewDataErrorContexts context) =>
        EditingControlFormattedValue;

    public void PrepareEditingControlForEdit(bool selectAll) => _selector.Focus();

    private void SelectCellValue(object? value)
    {
        if (_column is null) return;
        _selectedValueOrder.Clear();
        _selectedValueOrder.AddRange(_column.SplitValue(value));
        var requested = new HashSet<string>(
            _selectedValueOrder,
            StringComparer.OrdinalIgnoreCase);
        object[] selected = _selector.Items.Cast<object>()
            .Where(item => requested.Contains(
                Convert.ToString(_column.GetItemValue(item), CultureInfo.InvariantCulture) ?? string.Empty))
            .ToArray();
        _selector.SetSelectedItems(selected);
    }

    private void SynchronizeSelectedValueOrder()
    {
        if (_column is null) return;
        string[] selectedValues = _selector.SelectedValues
            .Select(value => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty)
            .Where(value => value.Length > 0)
            .ToArray();
        var selected = new HashSet<string>(selectedValues, StringComparer.OrdinalIgnoreCase);
        _selectedValueOrder.RemoveAll(value => !selected.Contains(value));
        foreach (string value in selectedValues)
        {
            if (!_selectedValueOrder.Contains(value, StringComparer.OrdinalIgnoreCase))
                _selectedValueOrder.Add(value);
        }
    }
}
