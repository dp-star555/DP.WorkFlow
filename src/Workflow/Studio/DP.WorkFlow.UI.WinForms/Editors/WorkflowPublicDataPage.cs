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
    private readonly ListView _list = new()
    {
        Dock = DockStyle.Fill,
        View = View.Details,
        FullRowSelect = true,
        HideSelection = false,
        MultiSelect = false,
        BorderStyle = BorderStyle.None,
        ShowItemToolTips = true
    };
    private readonly ListView _uses = new()
    {
        Dock = DockStyle.Fill,
        View = View.Details,
        FullRowSelect = true,
        MultiSelect = false,
        BorderStyle = BorderStyle.None
    };
    private readonly Label _summary = new() { AutoSize = true, Margin = new Padding(0, 8, 12, 0) };
    private readonly ModernButton _add = new() { Text = "新增", AutoSize = true };
    private readonly ModernButton _edit = new() { Text = "修改", AutoSize = true };
    private readonly ModernButton _remove = new() { Text = "删除", AutoSize = true };
    private readonly ModernButton _declareMissing = new() { Text = "补齐未登记的", AutoSize = true, ButtonType = ModernButtonType.Primary };
    private readonly Label _usesTitle = new() { AutoSize = true, Dock = DockStyle.Top, Padding = new Padding(0, 6, 0, 4) };
    private WorkflowPublicDataManifestModel? _model;

    /// <summary>初始化页面。</summary>
    public WorkflowPublicDataPage()
    {
        _list.Columns.Add("键", 160);
        _list.Columns.Add("显示名", 120);
        _list.Columns.Add("形态", 90);
        _list.Columns.Add("值类型", 80);
        _list.Columns.Add("默认值", 90);
        _list.Columns.Add("保持", 50);
        _list.Columns.Add("恢复时复位", 80);
        _list.Columns.Add("使用", 50, HorizontalAlignment.Right);
        _list.Columns.Add("状态", 200);
        _uses.Columns.Add("节点", 160);
        _uses.Columns.Add("所在画布", 200);
        _uses.Columns.Add("用作", 160);

        _list.SelectedIndexChanged += (_, _) => { ShowUses(); UpdateCommands(); };
        _list.DoubleClick += (_, _) => EditSelected();
        _uses.DoubleClick += (_, _) =>
        {
            if (_uses.SelectedItems.Count == 1 && _uses.SelectedItems[0].Tag is WorkflowPublicDataUse use)
                UseActivated?.Invoke(this, use);
        };
        _add.Click += (_, _) => AddNew();
        _edit.Click += (_, _) => EditSelected();
        _remove.Click += (_, _) => RemoveSelected();
        _declareMissing.Click += (_, _) => Guarded(() => _model?.DeclareMissing());

        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = false, Padding = new Padding(0, 0, 0, 6) };
        bar.Controls.AddRange(new Control[] { _summary, _add, _edit, _remove, _declareMissing });
        var split = new ModernSplitter { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, InitialPanel2Size = 120 };
        split.Panel1.Controls.Add(_list);
        split.Panel2.Controls.Add(_uses);
        split.Panel2.Controls.Add(_usesTitle);
        Controls.Add(split);
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
        _list.BeginUpdate();
        try
        {
            _list.Items.Clear();
            foreach (var row in rows)
            {
                var d = row.Declaration;
                var item = new ListViewItem(d.Key) { Tag = row, ToolTipText = string.Join(Environment.NewLine, row.Diagnostics.Select(x => x.Message)) };
                item.SubItems.Add(d.DisplayName ?? string.Empty);
                item.SubItems.Add(KindText(d.Kind));
                item.SubItems.Add(d.Kind == WorkflowPublicDataKind.Signal ? string.Empty : d.ValueType.ToString());
                item.SubItems.Add(d.DefaultValueText ?? string.Empty);
                item.SubItems.Add(d.Persistent ? "是" : string.Empty);
                item.SubItems.Add(d.ResetOnRecovery ? "是" : string.Empty);
                item.SubItems.Add(row.Uses.Count.ToString(CultureInfo.InvariantCulture));
                item.SubItems.Add(row.Diagnostics.Count > 0 ? row.StatusText + "：" + row.Diagnostics[0].Message : row.StatusText);
                item.ForeColor = row.HasError ? theme.Error
                    : !row.Declared || row.Diagnostics.Count > 0 ? theme.Warning
                    : row.Unused ? theme.TextSecondary
                    : theme.Text;
                _list.Items.Add(item);
                if (string.Equals(row.Key, selectedKey, StringComparison.Ordinal)) item.Selected = true;
            }
        }
        finally
        {
            _list.EndUpdate();
        }
        var declared = rows.Count(row => row.Declared);
        var undeclared = rows.Count - declared;
        _summary.Text = _model is null ? "未打开流程"
            : $"已登记 {declared} 项" + (undeclared > 0 ? $"，{undeclared} 项节点在用但未登记" : string.Empty);
        _summary.ForeColor = undeclared > 0 ? theme.Warning : theme.Text;
        ShowUses();
        UpdateCommands();
    }

    private WorkflowPublicDataRow? SelectedRow => _list.SelectedItems.Count == 1 ? _list.SelectedItems[0].Tag as WorkflowPublicDataRow : null;

    private void ShowUses()
    {
        var row = SelectedRow;
        _usesTitle.Text = row is null ? "选中一项查看使用它的节点" : $"使用 {row.Key} 的节点（{row.Uses.Count}）";
        _uses.BeginUpdate();
        try
        {
            _uses.Items.Clear();
            foreach (var use in row?.Uses ?? Array.Empty<WorkflowPublicDataUse>())
            {
                var parts = WorkflowNoticeLocation.Split(use.PlanPath);
                var item = new ListViewItem(use.NodeId) { Tag = use };
                item.SubItems.Add(parts.Count == 0 ? "主流程" : string.Join(" / ", parts));
                item.SubItems.Add(use.Declaration.Describe());
                _uses.Items.Add(item);
            }
        }
        finally
        {
            _uses.EndUpdate();
        }
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
    private readonly TextBox _key = new() { Width = 260 };
    private readonly TextBox _displayName = new() { Width = 260 };
    private readonly ComboBox _kind = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260 };
    private readonly ComboBox _valueType = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260 };
    private readonly TextBox _default = new() { Width = 260 };
    private readonly CheckBox _persistent = new() { Text = "保持（断电保存）", AutoSize = true };
    private readonly CheckBox _resetOnRecovery = new() { Text = "故障恢复放行时复位为默认值（交接握手信号用）", AutoSize = true };
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
            grid.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(0, 7, 12, 0) });
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

        var ok = new ModernButton { Text = "确定", ButtonType = ModernButtonType.Primary, AutoSize = true };
        var cancel = new ModernButton { Text = "取消", AutoSize = true, DialogResult = DialogResult.Cancel };
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
