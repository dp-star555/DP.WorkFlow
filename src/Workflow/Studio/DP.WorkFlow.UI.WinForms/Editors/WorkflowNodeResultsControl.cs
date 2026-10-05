using ModernUI.WinForms;
using DP.WorkFlow.UI;

namespace DP.WorkFlow.UI.WinForms;

/// <summary>
/// 节点“运行结果”页：顶部一行显示最近一次运行状态，下方表格列出标准输出的全部成员。
/// 尚未运行时成员也全部列出（值为占位符）；勾选“数据端口”后该成员显示在节点上，可直接拖线绑定到下游参数。
/// </summary>
internal sealed class WorkflowNodeResultsControl : UserControl
{
    private const string ExposedColumn = "Exposed";
    private readonly WorkflowNodeResultPageModel _model;
    private readonly Label _state = new()
    {
        Dock = DockStyle.Top,
        AutoSize = false,
        Height = 48,
        Padding = new Padding(8, 6, 8, 6),
        AutoEllipsis = true
    };
    private readonly ModernDataGridView _grid = new()
    {
        Dock = DockStyle.Fill,
        Theme = ModernTheme.Dark,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        AllowUserToResizeRows = false,
        MultiSelect = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        RowHeadersVisible = false,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        EditMode = DataGridViewEditMode.EditOnEnter
    };
    private string _signature = string.Empty;
    private bool _syncing;

    public WorkflowNodeResultsControl(WorkflowNodeResultPageModel model)
    {
        _model = model;
        _grid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            Name = ExposedColumn,
            HeaderText = "数据端口",
            FillWeight = 22,
            ToolTipText = "勾选后该结果显示在节点上，可直接拖线绑定到下游参数；不勾选时仍可在参数中手动选择绑定。"
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Name", HeaderText = "名称", FillWeight = 38, ReadOnly = true });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Value", HeaderText = "值", FillWeight = 90, ReadOnly = true });
        _grid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_grid.IsCurrentCellDirty && _grid.CurrentCell?.OwningColumn.Name == ExposedColumn)
                _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        _grid.CellValueChanged += OnCellValueChanged;
        Controls.Add(_grid);
        Controls.Add(_state);
        _model.Changed += OnModelChanged;
        RefreshItems();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _model.Changed -= OnModelChanged;
        base.Dispose(disposing);
    }

    private void OnModelChanged(object? sender, EventArgs e)
    {
        if (IsDisposed) return;
        if (InvokeRequired) BeginInvoke(RefreshItems);
        else RefreshItems();
    }

    private void OnCellValueChanged(object? sender, DataGridViewCellEventArgs e)
    {
        if (_syncing || e.RowIndex < 0 || _grid.Columns[e.ColumnIndex].Name != ExposedColumn) return;
        var row = _grid.Rows[e.RowIndex];
        if (row.Tag is string member && row.Cells[ExposedColumn].Value is bool exposed)
            _model.SetMemberExposed(member, exposed);
    }

    /// <summary>行结构不变时只刷新值与勾选，保留滚动与选择；结构变化时整体重建。</summary>
    private void RefreshItems()
    {
        if (IsDisposed) return;
        var items = _model.GetItems();
        var states = items.Where(item => item.Category == WorkflowNodeResultPageModel.StateCategory).ToArray();
        _state.Text = string.Join("    ", states.Select(item => item.Name == "状态" ? item.Value : $"{item.Name}：{item.Value}"));
        var members = _model.GetMembers();
        var outputs = items.Where(item => item.Category != WorkflowNodeResultPageModel.StateCategory).ToArray();
        var rows = members.Select(member => (Key: (object?)member.Name, Exposed: (bool?)member.Exposed, member.DisplayName, member.Value))
            .Concat(outputs.Select(item => (Key: (object?)null, Exposed: (bool?)null, DisplayName: item.Name, item.Value)))
            .ToArray();
        var signature = string.Join("|", rows.Select(row => $"{row.Key}:{row.DisplayName}"));
        _syncing = true;
        try
        {
            if (signature != _signature)
            {
                _signature = signature;
                _grid.Rows.Clear();
                foreach (var row in rows)
                {
                    var index = _grid.Rows.Add(row.Exposed ?? false, row.DisplayName, row.Value);
                    var gridRow = _grid.Rows[index];
                    gridRow.Tag = row.Key;
                    // 未声明输出类型时展开的实际输出没有对应成员，不能作为数据端口。
                    if (row.Exposed is null) gridRow.Cells[ExposedColumn].ReadOnly = true;
                }
            }
            else
                for (var index = 0; index < rows.Length; index++)
                {
                    var cells = _grid.Rows[index].Cells;
                    if (!Equals(cells["Value"].Value, rows[index].Value)) cells["Value"].Value = rows[index].Value;
                    if (rows[index].Exposed is { } exposed && !Equals(cells[ExposedColumn].Value, exposed)) cells[ExposedColumn].Value = exposed;
                }
        }
        finally { _syncing = false; }
    }
}
