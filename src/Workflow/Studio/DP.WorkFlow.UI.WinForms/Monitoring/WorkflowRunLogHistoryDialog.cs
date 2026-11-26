using System.Globalization;
using ModernUI.WinForms;

namespace DP.WorkFlow.UI.WinForms;

/// <summary>
/// 查历史运行：选日期和流程，查存下来的运行（<see cref="IWorkflowRunLogStore"/>），双击或“打开”在运行日志页里看。
/// </summary>
internal sealed class WorkflowRunLogHistoryDialog : Form
{
    private const string AllFlows = "全部流程";
    private readonly IWorkflowRunLogStore _store;
    private readonly ModernDatePicker _from = new() { Width = 150 };
    private readonly ModernDatePicker _to = new() { Width = 150 };
    private readonly ModernSelect _flow = new() { Width = 200 };
    private readonly ModernButton _find = new() { Text = "查询", ButtonType = ModernButtonType.Primary };
    private readonly ModernDataGridView _list = WorkflowPage.Table("开始时间", "流程", "结果", "耗时");
    private readonly Label _status = WorkflowPage.Label();
    private readonly ModernButton _open = new() { Text = "打开", ButtonType = ModernButtonType.Primary };
    private CancellationTokenSource? _finding;

    public WorkflowRunLogHistoryDialog(IWorkflowRunLogStore store, IEnumerable<string> flows, string? flow)
    {
        _store = store;
        Text = "历史运行";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(760, 520);
        MinimumSize = new Size(560, 360);
        ShowInTaskbar = false;
        ShowIcon = false;
        MinimizeBox = false;
        Padding = new Padding(10);

        _from.Value = DateTime.Today.AddDays(-6);
        _to.Value = DateTime.Today;
        _flow.Items.Add(AllFlows);
        foreach (var name in flows.Distinct(StringComparer.Ordinal).OrderBy(name => name, StringComparer.Ordinal)) _flow.Items.Add(name);
        _flow.SelectedItem = flow is not null && _flow.Items.Contains(flow) ? flow : AllFlows;
        WorkflowPage.ColumnWidths(_list, 0, 1, 0, 0);

        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = false, Padding = new Padding(0, 0, 0, 8) };
        bar.Controls.Add(new Label { Text = "从", AutoSize = true, Margin = new Padding(0, 9, 6, 0) });
        bar.Controls.Add(_from);
        bar.Controls.Add(new Label { Text = "到", AutoSize = true, Margin = new Padding(10, 9, 6, 0) });
        bar.Controls.Add(_to);
        bar.Controls.Add(new Label { Text = "流程", AutoSize = true, Margin = new Padding(10, 9, 6, 0) });
        bar.Controls.Add(_flow);
        bar.Controls.Add(WorkflowSizing.Fit(_find));

        var cancel = WorkflowSizing.Fit(new ModernButton { Text = "取消", DialogResult = DialogResult.Cancel });
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 8, 0, 0) };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(WorkflowSizing.Fit(_open));
        _status.Dock = DockStyle.Bottom;

        Controls.Add(_list);
        Controls.Add(bar);
        Controls.Add(_status);
        Controls.Add(buttons);
        CancelButton = cancel;

        _find.Click += async (_, _) => await FindAsync();
        _open.Click += (_, _) => Accept();
        _list.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) Accept(); };
        _list.SelectionChanged += (_, _) => _open.Enabled = Selected is not null;
        Shown += async (_, _) => await FindAsync();
        FormClosed += (_, _) => _finding?.Cancel();
        _open.Enabled = false;
        WorkflowWinFormsTheme.Follow(this);
    }

    /// <summary>选中要看的运行。</summary>
    public WorkflowRunLogHistoryEntry? Result { get; private set; }

    private WorkflowRunLogHistoryEntry? Selected => _list.SelectedRows.Count == 1 ? _list.SelectedRows[0].Tag as WorkflowRunLogHistoryEntry : null;

    private async Task FindAsync()
    {
        _finding?.Cancel();
        var finding = _finding = new CancellationTokenSource();
        var from = (_from.Value ?? DateTime.Today).Date;
        var to = (_to.Value ?? DateTime.Today).Date.AddDays(1);
        var flow = _flow.SelectedItem as string is { } name && name != AllFlows ? name : null;
        _find.Enabled = false;
        _status.Text = "正在查询……";
        try
        {
            var entries = await _store.FindAsync(new WorkflowRunLogHistoryQuery(new DateTimeOffset(from), new DateTimeOffset(to), flow), finding.Token).ConfigureAwait(true);
            if (IsDisposed || finding.IsCancellationRequested) return;
            _list.Rows.Clear();
            foreach (var entry in entries)
            {
                var index = _list.Rows.Add(entry.Started.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture), entry.FlowName,
                    entry.State is { } state ? WorkflowRunLogText.State(state) : string.Empty,
                    entry.Ended is { } ended ? WorkflowRunLogText.Duration(ended - entry.Started) : string.Empty);
                _list.Rows[index].Tag = entry;
            }
            _list.ClearSelection();
            _status.Text = entries.Count == 0 ? "这段时间没有保存的运行。" : $"共 {entries.Count} 次运行，双击打开。";
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            if (!IsDisposed) _status.Text = "查询失败：" + exception.Message;
        }
        finally
        {
            if (!IsDisposed) _find.Enabled = true;
        }
    }

    private void Accept()
    {
        if (Selected is not { } entry) return;
        Result = entry;
        DialogResult = DialogResult.OK;
        Close();
    }
}
