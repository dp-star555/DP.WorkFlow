using System.Globalization;
using ModernUI.WinForms;

namespace DP.WorkFlow.UI.WinForms;

/// <summary>
/// 公共数据清单页（R2-d）：流程登记的 bool 信号、数值、队列，以及节点实际的使用。
/// 可以新增、修改、删除登记，把节点用到但没登记的项一键补进清单；选中一项在下方列出使用它的节点，双击定位。
/// </summary>
/// <remarks>修改经过设计器会话，可以撤销。清单登记在根文档上，子画布共用。</remarks>
public sealed class WorkflowPublicDataPage : UserControl
{
    private readonly ModernDataGridView _list = WorkflowPage.Table("键", "显示名", "形态", "值类型", "默认值", "保持", "恢复时复位", "使用", "状态");
    private readonly ModernDataGridView _uses = WorkflowPage.Table("节点", "所在画布", "用作");
    private readonly Label _summary = WorkflowPage.Label();
    private readonly ModernButton _add = new() { Text = "新增", Icon = ModernIconKind.Plus };
    private readonly ModernButton _edit = new() { Text = "修改" };
    private readonly ModernButton _remove = new() { Text = "删除" };
    private readonly ModernButton _declareMissing = new() { Text = "补齐未登记的", ButtonType = ModernButtonType.Primary };
    private readonly WorkflowCardHeader _usesHeader = new("使用它的节点", "选中左边一项");
    private WorkflowPublicDataManifestModel? _model;

    /// <summary>初始化页面。</summary>
    public WorkflowPublicDataPage()
    {
        float[] weights = { 18, 14, 10, 10, 10, 6, 9, 6, 17 };
        for (var i = 0; i < weights.Length; i++) _list.Columns[i].FillWeight = weights[i];
        _list.Columns[7].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
        _uses.Columns[0].FillWeight = 30;
        _uses.Columns[1].FillWeight = 35;
        _uses.Columns[2].FillWeight = 35;

        _list.SelectionChanged += (_, _) => { ShowUses(); UpdateCommands(); };
        _list.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) EditSelected(); };
        _uses.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex >= 0 && _uses.Rows[e.RowIndex].Tag is WorkflowPublicDataUse use)
                UseActivated?.Invoke(this, use);
        };
        _add.Click += (_, _) => AddNew();
        _edit.Click += (_, _) => EditSelected();
        _remove.Click += (_, _) => RemoveSelected();
        _declareMissing.Click += (_, _) => Guarded(() => _model?.DeclareMissing());

        // 一行操作（左概况，右按钮），下面左清单、右“使用它的节点”：底部页签矮，所以左右排而不是上下排。
        var bar = new WorkflowBlock { Dock = DockStyle.Top, LogicalExtent = 40, LogicalPadding = new Padding(4, 0, 0, 6) };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, WrapContents = false, FlowDirection = FlowDirection.RightToLeft, BackColor = Color.Transparent };
        foreach (var button in new[] { _declareMissing, _remove, _edit, _add })
            buttons.Controls.Add(WorkflowSizing.Fit(button));
        bar.Controls.Add(_summary);
        bar.Controls.Add(buttons);
        var uses = new WorkflowBlock { Dock = DockStyle.Right, LogicalExtent = 380 };
        uses.Controls.Add(_uses);
        uses.Controls.Add(_usesHeader);
        Controls.Add(_list);
        Controls.Add(WorkflowPage.Gap(DockStyle.Right));
        Controls.Add(uses);
        Controls.Add(bar);
        WorkflowWinFormsTheme.Follow(this, Rebuild);
        Rebuild();
    }

    /// <summary>获取或设置模型；页面不负责释放模型。</summary>
    public WorkflowPublicDataManifestModel? Model
    {
        get => _model;
        set
        {
            if (ReferenceEquals(_model, value)) return;
            if (_model is not null) _model.Changed -= OnModelChanged;
            _model = value;
            if (_model is not null) _model.Changed += OnModelChanged;
            Rebuild();
        }
    }

    /// <summary>双击“使用”列表中的一行时发生，由宿主定位到节点。</summary>
    public event EventHandler<WorkflowPublicDataUse>? UseActivated;

    /// <summary>修改出错时发生（例如键重复），由宿主显示。</summary>
    public event EventHandler<string>? EditError;

    private void OnModelChanged(object? sender, EventArgs e)
    {
        if (IsDisposed) return;
        if (InvokeRequired) BeginInvoke(new Action(Rebuild));
        else Rebuild();
    }

    private void Rebuild()
    {
        var theme = WorkflowWinFormsTheme.Modern;
        var selectedKey = SelectedRow?.Key;
        var rows = _model?.Rows ?? Array.Empty<WorkflowPublicDataRow>();
        _list.Rows.Clear();
        foreach (var row in rows)
        {
            var d = row.Declaration;
            var index = _list.Rows.Add(d.Key, d.DisplayName ?? string.Empty, KindText(d.Kind),
                d.Kind == WorkflowPublicDataKind.Signal ? string.Empty : d.ValueType.ToString(),
                d.DefaultValueText ?? string.Empty, d.Persistent ? "是" : string.Empty, d.ResetOnRecovery ? "是" : string.Empty,
                row.Uses.Count.ToString(CultureInfo.InvariantCulture),
                row.Diagnostics.Count > 0 ? row.StatusText + "：" + row.Diagnostics[0].Message : row.StatusText);
            var line = _list.Rows[index];
            line.Tag = row;
            line.Cells[8].ToolTipText = string.Join(Environment.NewLine, row.Diagnostics.Select(x => x.Message));
            line.DefaultCellStyle.ForeColor = row.HasError ? theme.Error
                : !row.Declared || row.Diagnostics.Count > 0 ? theme.Warning
                : row.Unused ? theme.TextSecondary
                : theme.Text;
        }
        WorkflowPage.RestoreSelection(_list, selectedKey, line => (line.Tag as WorkflowPublicDataRow)?.Key);
        var declared = rows.Count(row => row.Declared);
        var undeclared = rows.Count - declared;
        _summary.Text = _model is null ? "未打开流程"
            : $"已登记 {declared} 项" + (undeclared > 0 ? $"，{undeclared} 项节点在用但未登记" : string.Empty);
        _summary.ForeColor = undeclared > 0 ? theme.Warning : theme.Text;
        ShowUses();
        UpdateCommands();
    }

    private WorkflowPublicDataRow? SelectedRow => _list.SelectedRows.Count == 1 ? _list.SelectedRows[0].Tag as WorkflowPublicDataRow : null;

    private void ShowUses()
    {
        var row = SelectedRow;
        _usesHeader.Subtitle = row is null ? "选中左边一项" : $"{row.Key} · {row.Uses.Count} 处";
        _uses.Rows.Clear();
        foreach (var use in row?.Uses ?? Array.Empty<WorkflowPublicDataUse>())
        {
            var parts = WorkflowNoticeLocation.Split(use.PlanPath);
            var index = _uses.Rows.Add(use.NodeId, parts.Count == 0 ? "主流程" : string.Join(" / ", parts), use.Declaration.Describe());
            _uses.Rows[index].Tag = use;
        }
        _uses.ClearSelection();
    }

    private void UpdateCommands()
    {
        var row = SelectedRow;
        _add.Enabled = _model is not null;
        _edit.Enabled = row is not null;
        _remove.Enabled = row?.Declared == true;
        _declareMissing.Enabled = _model?.UndeclaredCount > 0;
    }

    private void AddNew()
    {
        if (_model is null) return;
        using var dialog = new WorkflowPublicDataDialog(_model, null, null);
        if (dialog.ShowDialog(FindForm()) == DialogResult.OK && dialog.Result is { } result)
            Guarded(() => _model.Save(result));
    }

    private void EditSelected()
    {
        if (_model is null || SelectedRow is not { } row) return;
        // 未登记的行：用推断出的形态预填，确定后登记。
        var originalKey = row.Declared ? row.Key : null;
        using var dialog = new WorkflowPublicDataDialog(_model, row.Declaration, originalKey);
        if (dialog.ShowDialog(FindForm()) == DialogResult.OK && dialog.Result is { } result)
            Guarded(() => _model.Save(result, originalKey));
    }

    private void RemoveSelected()
    {
        if (_model is null || SelectedRow is not { Declared: true } row) return;
        var hint = row.Uses.Count > 0 ? $"仍有 {row.Uses.Count} 个节点在用 {row.Key}，删除后会显示为未登记。" : $"删除登记 {row.Key}？";
        if (MessageBox.Show(FindForm(), hint, "删除公共数据", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
        Guarded(() => _model.Remove(row.Key));
    }

    private void Guarded(Action action)
    {
        try { action(); }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            EditError?.Invoke(this, exception.Message);
        }
    }

    internal static string KindText(WorkflowPublicDataKind kind) => kind switch
    {
        WorkflowPublicDataKind.Signal => "bool 信号",
        WorkflowPublicDataKind.Value => "数值",
        _ => "队列"
    };

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing && _model is not null) _model.Changed -= OnModelChanged;
        base.Dispose(disposing);
    }
}

/// <summary>新增或修改一项公共数据登记的对话框。</summary>
internal sealed class WorkflowPublicDataDialog : Form
{
    private readonly WorkflowPublicDataManifestModel _model;
    private readonly string? _originalKey;
    private readonly ModernInput _key = new() { Width = 300 };
    private readonly ModernInput _displayName = new() { Width = 300 };
    private readonly ModernSelect _kind = new() { Width = 300 };
    private readonly ModernSelect _valueType = new() { Width = 300 };
    private readonly ModernInput _default = new() { Width = 300 };
    private readonly ModernCheckbox _persistent = new() { Text = "保持（断电保存）", Width = 300 };
    private readonly ModernCheckbox _resetOnRecovery = new() { Text = "故障恢复放行时复位为默认值（交接握手信号用）", Width = 340 };
    private readonly Label _error = new() { AutoSize = true, MaximumSize = new Size(380, 0) };

    public WorkflowPublicDataDialog(WorkflowPublicDataManifestModel model, WorkflowPublicDataDeclaration? initial, string? originalKey)
    {
        _model = model;
        _originalKey = originalKey;
        Text = originalKey is null ? (initial is null ? "新增公共数据" : "登记公共数据") : "修改公共数据";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = MaximizeBox = false;
        ShowInTaskbar = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(16);

        foreach (var kind in new[] { WorkflowPublicDataKind.Signal, WorkflowPublicDataKind.Value, WorkflowPublicDataKind.Queue })
            _kind.Items.Add(new Choice<WorkflowPublicDataKind>(kind, WorkflowPublicDataPage.KindText(kind)));
        foreach (E_SignalValueType type in Enum.GetValues(typeof(E_SignalValueType)))
            _valueType.Items.Add(new Choice<E_SignalValueType>(type, type.ToString()));
        _kind.SelectedIndexChanged += (_, _) => SyncKind();

        var value = initial ?? WorkflowPublicDataDeclaration.Signal(string.Empty);
        _key.Text = value.Key;
        _displayName.Text = value.DisplayName ?? string.Empty;
        _kind.SelectedIndex = (int)value.Kind;
        _valueType.SelectedItem = _valueType.Items.Cast<Choice<E_SignalValueType>>().First(item => item.Value == value.ValueType);
        _default.Text = value.DefaultValueText ?? string.Empty;
        _persistent.Checked = value.Persistent;
        _resetOnRecovery.Checked = value.ResetOnRecovery;
        SyncKind();

        var grid = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Dock = DockStyle.Fill };
        void Row(string label, Control control)
        {
            grid.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(0, 10, 12, 0) });
            grid.Controls.Add(control);
        }
        Row("键", _key);
        Row("显示名", _displayName);
        Row("形态", _kind);
        Row("值类型", _valueType);
        Row("默认值", _default);
        grid.Controls.Add(new Label());
        grid.Controls.Add(_persistent);
        grid.Controls.Add(new Label());
        grid.Controls.Add(_resetOnRecovery);
        grid.Controls.Add(new Label());
        grid.Controls.Add(_error);

        var ok = WorkflowSizing.Fit(new ModernButton { Text = "确定", ButtonType = ModernButtonType.Primary });
        var cancel = WorkflowSizing.Fit(new ModernButton { Text = "取消", DialogResult = DialogResult.Cancel });
        ok.Click += (_, _) => Accept();
        var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Bottom, Padding = new Padding(0, 12, 0, 0) };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);
        Controls.Add(grid);
        Controls.Add(buttons);
        CancelButton = cancel;
        WorkflowWinFormsTheme.Follow(this);
        _error.ForeColor = WorkflowWinFormsTheme.Modern.Error;
    }

    public WorkflowPublicDataDeclaration? Result { get; private set; }

    private WorkflowPublicDataKind Kind => ((Choice<WorkflowPublicDataKind>)_kind.SelectedItem!).Value;

    private void SyncKind()
    {
        var signal = Kind == WorkflowPublicDataKind.Signal;
        if (signal) _valueType.SelectedItem = _valueType.Items.Cast<Choice<E_SignalValueType>>().First(item => item.Value == E_SignalValueType.Boolean);
        _valueType.Enabled = !signal;
        _default.Enabled = Kind != WorkflowPublicDataKind.Queue;
    }

    private void Accept()
    {
        var declaration = new WorkflowPublicDataDeclaration(
            _key.Text.Trim(),
            Kind,
            ((Choice<E_SignalValueType>)_valueType.SelectedItem!).Value,
            _default.Enabled && !string.IsNullOrWhiteSpace(_default.Text) ? _default.Text.Trim() : null,
            _persistent.Checked,
            string.IsNullOrWhiteSpace(_displayName.Text) ? null : _displayName.Text.Trim(),
            _resetOnRecovery.Checked);
        if (_model.Check(declaration, _originalKey) is { } problem)
        {
            _error.Text = problem;
            return;
        }
        Result = declaration;
        DialogResult = DialogResult.OK;
        Close();
    }

    private sealed record Choice<T>(T Value, string Text)
    {
        public override string ToString() => Text;
    }
}
