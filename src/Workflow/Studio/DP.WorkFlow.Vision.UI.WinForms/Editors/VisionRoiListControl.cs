using DP.Vision.UI;
using ModernUI.WinForms;

namespace DP.WorkFlow.Vision.UI.WinForms;

/// <summary>节点窗口左侧“ROI”页：列出右侧画布正在编辑的 ROI，并提供选择、删除和用途切换。</summary>
internal sealed class VisionRoiListControl : UserControl
{
    private readonly VisionRoiListModel _model;
    private readonly ModernDataGridView _grid = new()
    {
        Dock = DockStyle.Fill,
        Theme = ModernTheme.Dark,
        ReadOnly = true,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        AllowUserToResizeRows = false,
        MultiSelect = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        RowHeadersVisible = false,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
    };
    private readonly Label _empty = new()
    {
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleCenter,
        Text = "暂无 ROI\r\n在右侧图像上使用“绘制范围”等工具添加。",
        Visible = false
    };
    private readonly ModernButton _delete;
    private readonly ModernButton? _include, _exclude, _toggle;
    private bool _syncing;

    public VisionRoiListControl(VisionRoiListModel model)
    {
        _model = model;
        _grid.Columns.Add("Id", "标识");
        _grid.Columns.Add("Shape", "形状");
        _grid.Columns.Add("Purpose", "用途");
        _grid.Columns.Add("Enabled", "启用");
        _grid.Columns.Add("Summary", "位置");
        _grid.Columns["Id"]!.FillWeight = 80;
        _grid.Columns["Shape"]!.FillWeight = 60;
        _grid.Columns["Purpose"]!.FillWeight = 45;
        _grid.Columns["Enabled"]!.FillWeight = 40;
        _grid.Columns["Summary"]!.FillWeight = 160;

        var actions = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, Padding = new Padding(2, 2, 2, 6) };
        _delete = Action(actions, "删除", _model.DeleteSelected);
        if (_model.SupportsRegions)
        {
            _include = Action(actions, "设为包含", () => _model.SetSelectedPurpose(ERoiPurpose.Include));
            _exclude = Action(actions, "设为排除", () => _model.SetSelectedPurpose(ERoiPurpose.Exclude));
            _toggle = Action(actions, "停用", () =>
            {
                if (_model.Items.FirstOrDefault(item => item.Id == _model.SelectedId) is { } selected)
                    _model.SetSelectedEnabled(!selected.Enabled);
            });
        }

        var body = new Panel { Dock = DockStyle.Fill };
        body.Controls.Add(_grid);
        body.Controls.Add(_empty);
        Controls.Add(body);
        Controls.Add(actions);

        _grid.SelectionChanged += (_, _) =>
        {
            if (_syncing) return;
            var id = _grid.SelectedRows.Count == 1 ? _grid.SelectedRows[0].Tag as string : null;
            if (id is not null) _model.Select(id);
            UpdateActions();
        };
        _model.Changed += OnModelChanged;
        Reload();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _model.Changed -= OnModelChanged;
        base.Dispose(disposing);
    }

    private ModernButton Action(FlowLayoutPanel host, string text, Action action)
    {
        var button = new ModernButton
        {
            Text = text,
            Theme = ModernTheme.Dark,
            Size = new Size(TextRenderer.MeasureText(text, Font).Width + 28, 30),
            Margin = new Padding(0, 0, 6, 0)
        };
        button.Click += (_, _) => action();
        host.Controls.Add(button);
        return button;
    }

    private void OnModelChanged(object? sender, EventArgs e)
    {
        if (IsDisposed) return;
        if (InvokeRequired) BeginInvoke(Reload);
        else Reload();
    }

    /// <summary>按 ROI 文档重建行并恢复画布中的当前选择。</summary>
    private void Reload()
    {
        if (IsDisposed) return;
        _syncing = true;
        try
        {
            var items = _model.Items;
            _grid.SuspendLayout();
            _grid.Rows.Clear();
            foreach (var item in items)
            {
                var index = _grid.Rows.Add(item.Id, item.Shape, item.Purpose, item.Enabled ? "是" : "否", item.Summary);
                _grid.Rows[index].Tag = item.Id;
            }
            _grid.ClearSelection();
            foreach (DataGridViewRow row in _grid.Rows)
                if (Equals(row.Tag, _model.SelectedId)) row.Selected = true;
            _grid.ResumeLayout();
            _grid.Visible = items.Count > 0;
            _empty.Visible = items.Count == 0;
        }
        finally { _syncing = false; }
        UpdateActions();
    }

    private void UpdateActions()
    {
        var selected = _model.Items.FirstOrDefault(item => item.Id == _model.SelectedId);
        _delete.Enabled = selected is not null;
        if (_include is null || _exclude is null || _toggle is null) return;
        _include.Enabled = selected is { Purpose: not "包含" };
        _exclude.Enabled = selected is { Purpose: not "排除" };
        _toggle.Enabled = selected is not null;
        _toggle.Text = selected is { Enabled: false } ? "启用" : "停用";
    }
}
