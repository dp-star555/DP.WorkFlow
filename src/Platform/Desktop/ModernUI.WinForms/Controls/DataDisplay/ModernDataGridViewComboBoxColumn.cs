using System.ComponentModel;

namespace ModernUI.WinForms;

/// <summary>使用 <see cref="ModernSelect"/> 作为编辑器的现代 DataGridView 下拉列。</summary>
[ToolboxItem(false)]
public sealed class ModernDataGridViewComboBoxColumn : DataGridViewComboBoxColumn
{
    public ModernDataGridViewComboBoxColumn()
    {
        CellTemplate = new ModernDataGridViewComboBoxCell();
        FlatStyle = FlatStyle.Flat;
        DefaultCellStyle = new DataGridViewCellStyle { Padding = Padding.Empty };
    }
}

/// <summary>为 DataGridView 的组合框单元格提供现代选择编辑器。</summary>
[ToolboxItem(false)]
public sealed class ModernDataGridViewComboBoxCell : DataGridViewComboBoxCell
{
    public override Type EditType => typeof(ModernDataGridViewSelectEditingControl);

    protected override void OnMouseDown(DataGridViewCellMouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left || DataGridView is null || e.RowIndex < 0 || e.ColumnIndex < 0) return;

        DataGridView.CurrentCell = DataGridView.Rows[e.RowIndex].Cells[e.ColumnIndex];
        if (!DataGridView.IsCurrentCellInEditMode && !DataGridView.BeginEdit(true)) return;
        if (DataGridView.EditingControl is ModernDataGridViewSelectEditingControl editor && !editor.IsDisposed)
            editor.DroppedDown = true;
    }

    public override void InitializeEditingControl(int rowIndex, object? initialFormattedValue,
        DataGridViewCellStyle dataGridViewCellStyle)
    {
        base.InitializeEditingControl(rowIndex, initialFormattedValue, dataGridViewCellStyle);
        if (DataGridView?.EditingControl is not ModernDataGridViewSelectEditingControl editor) return;

        editor.BeginInitialization();
        try
        {
            editor.Theme = DataGridView is ModernDataGridView modernGrid
                ? modernGrid.Theme
                : ModernUiSettings.DefaultTheme;
            editor.DisplayMember = DisplayMember;
            editor.ValueMember = ValueMember;
            editor.MaxDropDownItems = MaxDropDownItems;
            editor.DropDownAnimationDuration = 120;

            if (DataSource is not null)
            {
                editor.DataSource = DataSource;
            }
            else
            {
                editor.DataSource = null;
                editor.Items.Clear();
                foreach (var item in Items.Cast<object>()) editor.Items.Add(item);
            }

            object? cellValue = rowIndex >= 0 && DataGridView is not null && rowIndex < DataGridView.Rows.Count
                ? GetValue(rowIndex)
                : null;
            editor.SelectCellValue(cellValue, Convert.ToString(initialFormattedValue));
        }
        finally
        {
            editor.EndInitialization();
        }
    }
}

/// <summary>将 ModernSelect 适配到 DataGridView 原生编辑生命周期。</summary>
[ToolboxItem(false)]
public sealed class ModernDataGridViewSelectEditingControl : ModernSelect, IDataGridViewEditingControl
{
    private bool _initializing;

    public ModernDataGridViewSelectEditingControl()
    {
        // DataGridView owns editor bounds and applies the inherited cell font. Inherit would run a
        // second parent DPI autoscale when this temporary control is attached after a monitor move.
        AutoScaleMode = AutoScaleMode.None;
        Margin = Padding.Empty;
        MinimumSize = Size.Empty;
        ShowFocusBorder = false;
        EmbeddedCellAppearance = true;
        SelectedIndexChanged += (_, _) =>
        {
            if (_initializing) return;
            EditingControlValueChanged = true;
            EditingControlDataGridView?.NotifyCurrentCellDirty(true);
        };
    }

    public DataGridView? EditingControlDataGridView { get; set; }

    public object EditingControlFormattedValue
    {
        get => GetItemText(SelectedItem);
        set => SelectCellValue(null, Convert.ToString(value));
    }

    public int EditingControlRowIndex { get; set; }

    public bool EditingControlValueChanged { get; set; }

    public Cursor EditingPanelCursor => Cursors.Default;

    public bool RepositionEditingControlOnValueChange => false;

    public void ApplyCellStyleToEditingControl(DataGridViewCellStyle dataGridViewCellStyle)
    {
        Font = dataGridViewCellStyle.Font ?? EditingControlDataGridView?.Font ?? Font;
        EmbeddedBackColor = dataGridViewCellStyle.SelectionBackColor;
        EmbeddedForeColor = dataGridViewCellStyle.SelectionForeColor;
        if (EditingControlDataGridView is ModernDataGridView modernGrid) Theme = modernGrid.Theme;
    }

    public bool EditingControlWantsInputKey(Keys keyData, bool dataGridViewWantsInputKey) =>
        (keyData & Keys.KeyCode) is Keys.Up or Keys.Down or Keys.Left or Keys.Right or Keys.Home or Keys.End
            or Keys.Enter or Keys.Space or Keys.Escape or Keys.PageUp or Keys.PageDown
        || !dataGridViewWantsInputKey;

    public object GetEditingControlFormattedValue(DataGridViewDataErrorContexts context) =>
        EditingControlFormattedValue;

    public void PrepareEditingControlForEdit(bool selectAll)
    {
        Focus();
    }

    internal void BeginInitialization() => _initializing = true;

    internal void EndInitialization()
    {
        EditingControlValueChanged = false;
        _initializing = false;
    }

    internal void SelectCellValue(object? value, string? formattedValue)
    {
        if (value is not null)
        {
            if (!string.IsNullOrWhiteSpace(ValueMember)) SelectedValue = value;
            else SelectedItem = Items.Cast<object>().FirstOrDefault(item => Equals(item, value));
        }
        else
        {
            SelectedIndex = -1;
        }

        if (SelectedIndex >= 0 || string.IsNullOrEmpty(formattedValue)) return;
        SelectedItem = Items.Cast<object>()
            .FirstOrDefault(item => string.Equals(GetItemText(item), formattedValue, StringComparison.CurrentCulture));
    }
}
