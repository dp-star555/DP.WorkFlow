using ModernUI.WinForms;
using DP.WorkFlow.UI;

namespace DP.WorkFlow.UI.WinForms;

/// <summary>
/// 节点窗口“输入输出”页：编辑复合节点子流程的输入和输出。
/// 输入的来源选父画布中本节点之前的节点输出（类型跟随来源），或使用手动值（整数、小数、布尔、文本）；
/// 子画布中通过“启动节点.名称”读取。输出的来源选子画布中的节点输出，父画布中通过“本节点.名称”读取。
/// </summary>
internal sealed class WorkflowSubflowInterfaceControl : UserControl
{
    private const string NameColumn = "Name";
    private const string SourceColumn = "Source";
    private const string TypeColumn = "Type";
    private const string ValueColumn = "Value";
    private const string RemoveColumn = "Remove";
    private static readonly E_WorkflowSubflowValueType[] ValueTypes = Polyfill.GetEnumValues<E_WorkflowSubflowValueType>();

    private readonly WorkflowSubflowInterfacePageModel _model;
    private readonly ModernDataGridView _inputs = CreateGrid();
    private readonly ModernDataGridView _outputs = CreateGrid();
    private bool _syncing;
    private bool _refreshQueued;

    public WorkflowSubflowInterfaceControl(WorkflowSubflowInterfacePageModel model)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
        _inputs.Columns.Add(new DataGridViewTextBoxColumn { Name = NameColumn, HeaderText = "名称", FillWeight = 30 });
        _inputs.Columns.Add(new DataGridViewButtonColumn { Name = SourceColumn, HeaderText = "来源", FillWeight = 50, FlatStyle = FlatStyle.Flat });
        _inputs.Columns.Add(new DataGridViewComboBoxColumn
        {
            Name = TypeColumn,
            HeaderText = "类型",
            FillWeight = 22,
            FlatStyle = FlatStyle.Flat,
            DataSource = ValueTypes.Select(WorkflowSubflowInput.DisplayName).ToArray()
        });
        _inputs.Columns.Add(new DataGridViewTextBoxColumn { Name = ValueColumn, HeaderText = "值", FillWeight = 28 });
        _inputs.Columns.Add(RemoveButtonColumn());
        _outputs.Columns.Add(new DataGridViewTextBoxColumn { Name = NameColumn, HeaderText = "名称", FillWeight = 30 });
        _outputs.Columns.Add(new DataGridViewButtonColumn { Name = SourceColumn, HeaderText = "来源", FillWeight = 60, FlatStyle = FlatStyle.Flat });
        _outputs.Columns.Add(new DataGridViewTextBoxColumn { Name = TypeColumn, HeaderText = "类型", FillWeight = 22, ReadOnly = true });
        _outputs.Columns.Add(RemoveButtonColumn());

        _inputs.CellEndEdit += OnInputEdited;
        _inputs.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_inputs.IsCurrentCellDirty && _inputs.CurrentCell?.OwningColumn.Name == TypeColumn)
                _inputs.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        _inputs.CellValueChanged += OnInputTypeChanged;
        _inputs.CellContentClick += OnInputButtonClicked;
        _inputs.DataError += (_, e) => e.ThrowException = false;
        _outputs.CellEndEdit += OnOutputEdited;
        _outputs.CellContentClick += OnOutputButtonClicked;

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(4) };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        layout.Controls.Add(Header("输入", "添加输入",
            "来源选父画布中本节点之前的节点输出，类型自动跟随来源；不选来源时使用手动值。子画布中绑定“启动节点.名称”读取。",
            () => _model.AddInput()), 0, 0);
        layout.Controls.Add(_inputs, 0, 1);
        layout.Controls.Add(Header("输出", "添加输出",
            "来源选子画布中的节点输出，类型自动跟随来源。父画布中绑定“本节点.名称”读取。",
            () => _model.AddOutput()), 0, 2);
        layout.Controls.Add(_outputs, 0, 3);
        Controls.Add(layout);

        _model.Changed += OnModelChanged;
        RefreshRows();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _model.Changed -= OnModelChanged;
        base.Dispose(disposing);
    }

    private void OnModelChanged(object? sender, EventArgs e)
    {
        // 修改常由表格自身的编辑事件触发，推迟到事件结束后重建，避免表格重入。
        if (IsDisposed || _refreshQueued) return;
        _refreshQueued = true;
        if (IsHandleCreated) BeginInvoke(RefreshRows);
        else RefreshRows();
    }

    private void RefreshRows()
    {
        _refreshQueued = false;
        if (IsDisposed) return;
        _syncing = true;
        try
        {
            _inputs.Rows.Clear();
            foreach (var input in _model.Inputs)
            {
                var index = _inputs.Rows.Add();
                var row = _inputs.Rows[index];
                row.Tag = input.Index;
                row.Cells[NameColumn].Value = input.Name;
                row.Cells[SourceColumn].Value = input.SourceText;
                if (input.IsBound)
                {
                    // 绑定来源时类型跟随来源，不能修改，也没有手动值。
                    row.Cells[TypeColumn] = new DataGridViewTextBoxCell { Value = input.TypeText };
                    row.Cells[TypeColumn].ReadOnly = true;
                    row.Cells[ValueColumn].Value = string.Empty;
                    row.Cells[ValueColumn].ReadOnly = true;
                }
                else
                {
                    row.Cells[TypeColumn].Value = WorkflowSubflowInput.DisplayName(input.ValueType);
                    row.Cells[ValueColumn].Value = input.ValueText;
                }
            }
            _outputs.Rows.Clear();
            foreach (var output in _model.Outputs)
            {
                var index = _outputs.Rows.Add();
                var row = _outputs.Rows[index];
                row.Tag = output.Index;
                row.Cells[NameColumn].Value = output.Name;
                row.Cells[SourceColumn].Value = output.SourceText;
                row.Cells[TypeColumn].Value = output.TypeText;
            }
        }
        finally
        {
            _syncing = false;
        }
    }

    private void OnInputEdited(object? sender, DataGridViewCellEventArgs e)
    {
        if (_syncing || e.RowIndex < 0 || _inputs.Rows[e.RowIndex].Tag is not int index) return;
        var cell = _inputs.Rows[e.RowIndex].Cells[e.ColumnIndex];
        var text = Convert.ToString(cell.Value) ?? string.Empty;
        switch (_inputs.Columns[e.ColumnIndex].Name)
        {
            case NameColumn:
                _model.RenameInput(index, text);
                break;
            case ValueColumn when !cell.ReadOnly:
                _model.SetInputValue(index, text);
                break;
        }
    }

    private void OnInputTypeChanged(object? sender, DataGridViewCellEventArgs e)
    {
        if (_syncing || e.RowIndex < 0 || _inputs.Columns[e.ColumnIndex].Name != TypeColumn
            || _inputs.Rows[e.RowIndex].Tag is not int index) return;
        var text = Convert.ToString(_inputs.Rows[e.RowIndex].Cells[e.ColumnIndex].Value);
        var valueType = ValueTypes.FirstOrDefault(type => WorkflowSubflowInput.DisplayName(type) == text);
        _model.SetInputValueType(index, valueType);
    }

    private void OnInputButtonClicked(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || _inputs.Rows[e.RowIndex].Tag is not int index) return;
        switch (_inputs.Columns[e.ColumnIndex].Name)
        {
            case SourceColumn:
                var menu = new ContextMenuStrip();
                menu.Items.Add("选择父画布中的来源…", null, (_, _) =>
                {
                    var current = _model.Inputs[index].Source;
                    using var dialog = new WorkflowBindingSelectorDialog(_model.GetInputCandidates(), current);
                    if (dialog.ShowDialog(FindForm()) == DialogResult.OK && dialog.SelectedCandidate is { } candidate)
                        _model.SetInputSource(index, candidate.ToBindingKey());
                });
                menu.Items.Add("使用手动值", null, (_, _) => _model.SetInputSource(index, null));
                menu.Closed += (_, _) => BeginInvoke(menu.Dispose);
                menu.Show(_inputs, _inputs.PointToClient(Cursor.Position));
                break;
            case RemoveColumn:
                _model.RemoveInput(index);
                break;
        }
    }

    private void OnOutputEdited(object? sender, DataGridViewCellEventArgs e)
    {
        if (_syncing || e.RowIndex < 0 || _outputs.Columns[e.ColumnIndex].Name != NameColumn
            || _outputs.Rows[e.RowIndex].Tag is not int index) return;
        _model.RenameOutput(index, Convert.ToString(_outputs.Rows[e.RowIndex].Cells[e.ColumnIndex].Value) ?? string.Empty);
    }

    private void OnOutputButtonClicked(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || _outputs.Rows[e.RowIndex].Tag is not int index) return;
        switch (_outputs.Columns[e.ColumnIndex].Name)
        {
            case SourceColumn:
                using (var dialog = new WorkflowBindingSelectorDialog(_model.GetOutputCandidates(), _model.Outputs[index].Source))
                {
                    if (dialog.ShowDialog(FindForm()) == DialogResult.OK && dialog.SelectedCandidate is { } candidate)
                        _model.SetOutputSource(index, candidate.ToBindingKey());
                }
                break;
            case RemoveColumn:
                _model.RemoveOutput(index);
                break;
        }
    }

    private static Control Header(string title, string addText, string hint, Action add)
    {
        var header = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            WrapContents = true,
            Margin = new Padding(0, 6, 0, 2)
        };
        header.Controls.Add(new Label
        {
            Text = title,
            AutoSize = true,
            Font = new Font(DefaultFont, FontStyle.Bold),
            Margin = new Padding(0, 8, 8, 0)
        });
        var button = new ModernButton
        {
            Text = addText,
            Theme = ModernTheme.Dark,
            Size = new Size(TextRenderer.MeasureText(addText, DefaultFont).Width + 32, 30),
            Margin = new Padding(0, 2, 8, 2)
        };
        button.Click += (_, _) => add();
        header.Controls.Add(button);
        header.Controls.Add(new Label
        {
            Text = hint,
            AutoSize = true,
            MaximumSize = new Size(560, 0),
            ForeColor = Color.FromArgb(148, 163, 184),
            Margin = new Padding(0, 4, 0, 0)
        });
        return header;
    }

    private static DataGridViewButtonColumn RemoveButtonColumn() => new()
    {
        Name = RemoveColumn,
        HeaderText = string.Empty,
        Text = "删除",
        UseColumnTextForButtonValue = true,
        FillWeight = 14,
        FlatStyle = FlatStyle.Flat
    };

    private static ModernDataGridView CreateGrid() => new()
    {
        Dock = DockStyle.Fill,
        Theme = ModernTheme.Dark,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        AllowUserToResizeRows = false,
        MultiSelect = false,
        SelectionMode = DataGridViewSelectionMode.CellSelect,
        RowHeadersVisible = false,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        EditMode = DataGridViewEditMode.EditOnEnter
    };
}
