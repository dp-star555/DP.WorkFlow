using ModernUI.WinForms;

namespace DP.WorkFlow.Vision.UI.WinForms;

/// <summary>测量组节点左侧“测量项”页：按类型新增、删除、启用/停用测量项；选中项在右侧画布上编辑，参数在参数页的“测量项”分组里。</summary>
internal sealed class VisionMeasureItemListControl : UserControl
{
    private readonly VisionFrameEditorPageModel _model;
    private readonly ModernDataGridView _grid = new()
    {
        Dock = DockStyle.Fill, Theme = ModernTheme.Dark, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
        AllowUserToResizeRows = false, MultiSelect = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        RowHeadersVisible = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
    };
    private readonly Label _empty = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, Text = "暂无测量项\r\n用上方按钮添加找线、找圆或找矩形。", Visible = false };
    private readonly ModernButton _delete, _toggle;
    private bool _syncing;

    public VisionMeasureItemListControl(VisionFrameEditorPageModel model)
    {
        _model = model;
        foreach (var (name, header, weight) in new[] { ("Name", "名称", 80f), ("Kind", "类型", 50f), ("Enabled", "启用", 36f), ("Status", "结果", 110f), ("Id", "标识", 70f) })
        {
            _grid.Columns.Add(name, header);
            _grid.Columns[name]!.FillWeight = weight;
        }
        var actions = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, Padding = new Padding(2, 2, 2, 6) };
        foreach (var kind in Enum.GetValues<EVisionMeasureKind>())
            Action(actions, "+" + MeasureVisionGroupNodeModel.KindName(kind), () => _model.AddMeasureItem(kind));
        _delete = Action(actions, "删除", _model.RemoveSelectedMeasureItem);
        _toggle = Action(actions, "停用", () =>
        {
            if (Selected() is { } selected) _model.SetMeasureItemEnabled(selected.Id, !selected.Enabled);
        });
        var body = new Panel { Dock = DockStyle.Fill };
        body.Controls.Add(_grid); body.Controls.Add(_empty);
        Controls.Add(body); Controls.Add(actions);
        _grid.SelectionChanged += (_, _) =>
        {
            if (_syncing) return;
            if (_grid.SelectedRows.Count == 1 && _grid.SelectedRows[0].Tag is string id) _model.SelectMeasureItem(id);
            UpdateActions();
        };
        _model.MeasureItemsChanged += OnModelChanged;
        Reload();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _model.MeasureItemsChanged -= OnModelChanged;
        base.Dispose(disposing);
    }

    private ModernButton Action(FlowLayoutPanel host, string text, Action action)
    {
        var button = new ModernButton { Text = text, Theme = ModernTheme.Dark, Size = new Size(TextRenderer.MeasureText(text, Font).Width + 28, 30), Margin = new Padding(0, 0, 6, 6) };
        button.Click += (_, _) =>
        {
            try { action(); }
            catch (Exception error) when (error is InvalidOperationException or ArgumentException) { MessageBox.Show(this, error.Message, "测量项"); }
        };
        host.Controls.Add(button);
        return button;
    }

    private VisionMeasureItemRow? Selected() => _model.MeasureItems.FirstOrDefault(item => item.Id == _model.SelectedMeasureItemId);

    private void OnModelChanged(object? sender, EventArgs e)
    {
        if (IsDisposed) return;
        if (InvokeRequired) BeginInvoke(Reload); else Reload();
    }

    private void Reload()
    {
        if (IsDisposed) return;
        _syncing = true;
        try
        {
            var items = _model.MeasureItems;
            _grid.SuspendLayout();
            _grid.Rows.Clear();
            foreach (var item in items)
            {
                var index = _grid.Rows.Add(item.Name, item.Kind, item.Enabled ? "是" : "否", item.Status, item.Id);
                _grid.Rows[index].Tag = item.Id;
            }
            _grid.ClearSelection();
            foreach (DataGridViewRow row in _grid.Rows)
                if (Equals(row.Tag, _model.SelectedMeasureItemId)) row.Selected = true;
            _grid.ResumeLayout();
            _grid.Visible = items.Count > 0; _empty.Visible = items.Count == 0;
        }
        finally { _syncing = false; }
        UpdateActions();
    }

    private void UpdateActions()
    {
        var selected = Selected();
        _delete.Enabled = _toggle.Enabled = selected is not null;
        _toggle.Text = selected is { Enabled: false } ? "启用" : "停用";
    }
}
