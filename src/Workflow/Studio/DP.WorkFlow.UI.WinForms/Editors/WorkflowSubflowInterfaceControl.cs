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
        // 全部是普通文字格：名称、值直接改；来源、类型点一下弹出菜单（带 ▾）；删除是红字。
        // 不用原生的下拉框和按钮列，样式和其他表格一致。
        _inputs.Columns.Add(TextColumn(NameColumn, "名称"));
        _inputs.Columns.Add(TextColumn(SourceColumn, "来源", readOnly: true));
        _inputs.Columns.Add(TextColumn(TypeColumn, "类型", readOnly: true));
        _inputs.Columns.Add(TextColumn(ValueColumn, "值"));
        _inputs.Columns.Add(RemoveColumnOf());
        WorkflowPage.ColumnWidths(_inputs, 1, 1, 0, 1, 0);
        _outputs.Columns.Add(TextColumn(NameColumn, "名称"));
        _outputs.Columns.Add(TextColumn(SourceColumn, "来源", readOnly: true));
        _outputs.Columns.Add(TextColumn(TypeColumn, "类型", readOnly: true));
        _outputs.Columns.Add(RemoveColumnOf());
        WorkflowPage.ColumnWidths(_outputs, 1, 2, 0, 0);

        _inputs.CellEndEdit += OnInputEdited;
        _inputs.CellClick += OnInputCellClicked;
        _inputs.DataError += (_, e) => e.ThrowException = false;
        _outputs.CellEndEdit += OnOutputEdited;
        _outputs.CellClick += OnOutputCellClicked;

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
                row.Cells[SourceColumn].Value = input.SourceText + Drop;
                if (input.IsBound)
                {
                    // 绑定来源时类型跟随来源，不能修改，也没有手动值。
                    row.Cells[TypeColumn].Value = input.TypeText;
                    row.Cells[ValueColumn].Value = string.Empty;
                    row.Cells[ValueColumn].ReadOnly = true;
                }
                else
                {
                    row.Cells[TypeColumn].Value = WorkflowSubflowInput.DisplayName(input.ValueType) + Drop;
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
                row.Cells[SourceColumn].Value = output.SourceText + Drop;
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

    private void OnInputCellClicked(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0 || _inputs.Rows[e.RowIndex].Tag is not int index) return;
        switch (_inputs.Columns[e.ColumnIndex].Name)
        {
            case SourceColumn:
                ShowMenu(_inputs, e, menu =>
                {
                    Add(menu, "选择父画布中的来源…", () =>
                    {
                        var current = _model.Inputs[index].Source;
                        using var dialog = new WorkflowBindingSelectorDialog(_model.GetInputCandidates(), current);
                        if (dialog.ShowDialog(FindForm()) == DialogResult.OK && dialog.SelectedCandidate is { } candidate)
                            _model.SetInputSource(index, candidate.ToBindingKey());
                    });
                    Add(menu, "使用手动值", () => _model.SetInputSource(index, null));
                });
                break;
            case TypeColumn when !_model.Inputs[index].IsBound:
                ShowMenu(_inputs, e, menu =>
                {
                    foreach (var type in ValueTypes)
                        Add(menu, WorkflowSubflowInput.DisplayName(type), () => _model.SetInputValueType(index, type), type == _model.Inputs[index].ValueType);
                });
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

    private void OnOutputCellClicked(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0 || _outputs.Rows[e.RowIndex].Tag is not int index) return;
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

    // 在被点的格子下面弹出主题菜单。
    private void ShowMenu(DataGridView grid, DataGridViewCellEventArgs e, Action<ModernContextMenu> fill)
    {
        var menu = new ModernContextMenu { Theme = WorkflowWinFormsTheme.Modern };
        fill(menu);
        menu.Closed += (_, _) => BeginInvoke(menu.Dispose);
        var cell = grid.GetCellDisplayRectangle(e.ColumnIndex, e.RowIndex, false);
        menu.Show(grid, new Point(cell.Left, cell.Bottom));
    }

    private static void Add(ToolStrip menu, string text, Action action, bool selected = false) =>
        menu.Items.Add(new ToolStripMenuItem(text, null, (_, _) => action()) { Checked = selected });

    // 标题行：粗体标题 + 文字按钮“添加…”；说明另起一行，灰字、随宽度换行。
    private Control Header(string title, string addText, string hint, Action add)
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, RowCount = 2, Margin = new Padding(0, 6, 0, 4) };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var label = new Label { Text = title, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, 8, 0) };
        label.Font = new Font(Font, FontStyle.Bold);
        var button = WorkflowSizing.Fit(new ModernButton
        {
            Text = addText,
            Theme = WorkflowWinFormsTheme.Modern,
            ButtonType = ModernButtonType.Text,
            Icon = ModernIconKind.Plus,
            Anchor = AnchorStyles.Left
        });
        button.Click += (_, _) => add();
        var note = new Label { Text = hint, AutoSize = true, Dock = DockStyle.Fill, ForeColor = WorkflowWinFormsTheme.Modern.TextSecondary, Margin = new Padding(0, 2, 0, 0) };
        panel.Controls.Add(label, 0, 0);
        panel.Controls.Add(button, 1, 0);
        panel.Controls.Add(note, 0, 1);
        panel.SetColumnSpan(note, 2);
        // 说明按面板宽度换行。
        panel.Resize += (_, _) => note.MaximumSize = new Size(Math.Max(0, panel.ClientSize.Width), 0);
        return panel;
    }

    private const string Drop = "  ▾";

    private static DataGridViewTextBoxColumn TextColumn(string name, string header, bool readOnly = false) =>
        new() { Name = name, HeaderText = header, ReadOnly = readOnly, SortMode = DataGridViewColumnSortMode.NotSortable };

    private static DataGridViewTextBoxColumn RemoveColumnOf()
    {
        var column = TextColumn(RemoveColumn, string.Empty, readOnly: true);
        column.DefaultCellStyle.ForeColor = WorkflowWinFormsTheme.Modern.Error;
        column.DefaultCellStyle.NullValue = "删除";
        return column;
    }

    private static ModernDataGridView CreateGrid() => new()
    {
        Dock = DockStyle.Fill,
        Theme = WorkflowWinFormsTheme.Modern,
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
