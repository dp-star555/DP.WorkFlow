using ModernUI.WinForms;
using DP.WorkFlow.UI;

namespace DP.WorkFlow.UI.WinForms;

/// <summary>
/// 节点“输出”页（检查器和节点窗口共用）：多输出节点先列出输出端口开关（显示哪些输出，例如“失败”），
/// 下面一行是最近一次运行状态，表格列出标准输出的全部成员（尚未运行时值为占位符）。
/// 下游节点通过参数的“选来源”引用这些成员。
/// </summary>
internal sealed class WorkflowNodeResultsControl : UserControl
{
    private readonly WorkflowNodeResultPageModel _model;
    private readonly FlowLayoutPanel _ports = new()
    {
        Dock = DockStyle.Top,
        AutoSize = true,
        WrapContents = true,
        Padding = new Padding(4, 6, 4, 6),
        Visible = false
    };
    private readonly WorkflowLabel _portsTitle = new() { Text = "输出端口", Bold = true, AutoSize = true, Margin = new Padding(4, 6, 12, 4) };
    private readonly Label _state = new()
    {
        Dock = DockStyle.Top,
        AutoSize = false,
        Height = 48,
        Padding = new Padding(8, 6, 8, 6),
        AutoEllipsis = true
    };
    private readonly ModernDataGridView _grid = WorkflowPage.Table("名称", "值");
    private string _signature = string.Empty;
    private string _portsKey = string.Empty;
    private bool _syncing;

    public WorkflowNodeResultsControl(WorkflowNodeResultPageModel model)
    {
        _model = model;
        WorkflowPage.ColumnWidths(_grid, 0, 1);
        Controls.Add(_grid);
        Controls.Add(_state);
        Controls.Add(_ports);
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

    /// <summary>端口或行结构不变时只刷新勾选和值，保留滚动与选择；结构变化时整体重建。</summary>
    private void RefreshItems()
    {
        if (IsDisposed) return;
        _syncing = true;
        try
        {
            RefreshPorts();
            var items = _model.GetItems();
            var states = items.Where(item => item.Category == WorkflowNodeResultPageModel.StateCategory).ToArray();
            _state.Text = string.Join("    ", states.Select(item => item.Name == "状态" ? item.Value : $"{item.Name}：{item.Value}"));
            var rows = _model.GetMembers().Select(member => (member.DisplayName, member.Value))
                .Concat(items.Where(item => item.Category != WorkflowNodeResultPageModel.StateCategory).Select(item => (DisplayName: item.Name, item.Value)))
                .ToArray();
            var signature = string.Join("|", rows.Select(row => row.DisplayName));
            if (signature != _signature)
            {
                _signature = signature;
                _grid.Rows.Clear();
                foreach (var row in rows) _grid.Rows.Add(row.DisplayName, row.Value);
                _grid.ClearSelection();
            }
            else
                for (var index = 0; index < rows.Length; index++)
                {
                    var cell = _grid.Rows[index].Cells[1];
                    if (!Equals(cell.Value, rows[index].Value)) cell.Value = rows[index].Value;
                }
        }
        finally { _syncing = false; }
    }

    private void RefreshPorts()
    {
        var ports = _model.GetOutputPorts();
        var key = string.Join(",", ports.Select(item => item.Port.Key));
        if (key != _portsKey)
        {
            _portsKey = key;
            _ports.SuspendLayout();
            foreach (Control control in _ports.Controls.Cast<Control>().ToArray())
                if (!ReferenceEquals(control, _portsTitle)) control.Dispose();
            _ports.Controls.Clear();
            _ports.Visible = ports.Count > 0;
            if (ports.Count > 0) _ports.Controls.Add(_portsTitle);
            foreach (var (port, _) in ports)
            {
                var check = WorkflowSizing.Fit(new ModernCheckbox
                {
                    Text = WorkflowPorts.GetDisplayName(port.Key),
                    Theme = WorkflowWinFormsTheme.Modern,
                    Margin = new Padding(4, 2, 8, 2),
                    Tag = port.Key
                });
                var portKey = port.Key;
                check.CheckedChanged += (_, _) =>
                {
                    if (_syncing) return;
                    // 改不了（例如只剩一个显示的输出）时把勾恢复成实际状态。
                    if (!_model.SetOutputPortVisible(portKey, check.Checked) && IsHandleCreated) BeginInvoke(RefreshItems);
                };
                _ports.Controls.Add(check);
            }
            _ports.ResumeLayout(true);
        }
        foreach (var check in _ports.Controls.OfType<ModernCheckbox>())
            check.Checked = ports.FirstOrDefault(item => item.Port.Key == (string?)check.Tag).Visible;
    }
}
