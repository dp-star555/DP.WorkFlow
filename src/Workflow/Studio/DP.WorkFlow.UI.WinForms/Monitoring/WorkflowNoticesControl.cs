using System.Globalization;
using ModernUI.WinForms;

namespace DP.WorkFlow.UI.WinForms;

/// <summary>
/// 提示页（R1-01）：列出提示板中全部提示（所有画布、所有节点、画布级），可按位置确认清除或全部清除，
/// 双击一行定位到对应节点。提示跨运行保留，直到这里或画布角标上确认。
/// </summary>
/// <remarks>按统一刷新时钟检查提示板版本，变了才重建列表；页面不可见时不刷新。</remarks>
public sealed class WorkflowNoticesControl : UserControl
{
    private readonly ModernDataGridView _list = WorkflowPage.Table("时间", "位置", "类别", "说明", "次数");
    private readonly Label _summary = new() { AutoSize = true, Margin = new Padding(0, 8, 12, 0) };
    private readonly ModernButton _acknowledgeSelected = WorkflowSizing.Fit(new ModernButton { Text = "确认选中位置" });
    private readonly ModernButton _acknowledgeAll = WorkflowSizing.Fit(new ModernButton { Text = "全部清除" });
    private WorkflowNoticeBoard? _board;
    private long _shownVersion = long.MinValue;

    /// <summary>初始化提示页。</summary>
    public WorkflowNoticesControl()
    {
        // 时间、类别、次数按内容，位置和说明按比例；可多选后“确认选中位置”。
        WorkflowPage.ColumnWidths(_list, 0, 30, 0, 70, 0);
        _list.MultiSelect = true;
        _list.Columns[4].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
        _list.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) ActivateSelected(); };
        _list.SelectionChanged += (_, _) => UpdateCommands();
        _acknowledgeSelected.Click += (_, _) => AcknowledgeSelected();
        _acknowledgeAll.Click += (_, _) => AcknowledgeAll();

        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = false, Padding = new Padding(0, 0, 0, 6) };
        bar.Controls.Add(_summary);
        bar.Controls.Add(_acknowledgeSelected);
        bar.Controls.Add(_acknowledgeAll);
        Controls.Add(_list);
        Controls.Add(bar);

        WorkflowRefreshClock.Subscribe(this, RefreshIfChanged);
        VisibleChanged += (_, _) => RefreshIfChanged();
        WorkflowWinFormsTheme.Follow(this, () =>
        {
            _list.Theme = WorkflowWinFormsTheme.Table(WorkflowWinFormsTheme.Modern);
            RebuildList();
        });
        RebuildList();
    }

    /// <summary>获取或设置提示板（通常为运行宿主的 <see cref="IWorkflowRuntimeHost.Notices"/>）。</summary>
    public WorkflowNoticeBoard? Board
    {
        get => _board;
        set
        {
            if (ReferenceEquals(_board, value)) return;
            _board = value;
            _shownVersion = long.MinValue;
            RebuildList();
        }
    }

    /// <summary>当前提示条数（每个位置每种说明算一条）。</summary>
    public int NoticeCount => _list.Rows.Count;

    /// <summary>双击一行时发生，由宿主定位到对应节点。</summary>
    public event EventHandler<WorkflowNotice>? NoticeActivated;

    /// <summary>提示条数变化时发生（用于页签标题上的数字）。</summary>
    public event EventHandler? CountChanged;

    /// <summary>立即按提示板重建列表。</summary>
    public void RefreshNotices()
    {
        _shownVersion = long.MinValue;
        RefreshIfChanged();
    }

    private void RefreshIfChanged()
    {
        if (IsDisposed) return;
        var version = _board?.Version ?? -1;
        if (version == _shownVersion) return;
        RebuildList();
    }

    private void RebuildList()
    {
        _shownVersion = _board?.Version ?? -1;
        var before = _list.Rows.Count;
        var selected = _list.SelectedRows.Count == 1 && _list.SelectedRows[0].Tag is WorkflowNotice current
            ? (current.PlanPath, current.NodeId, current.Category, current.Message)
            : default((string, string?, string, string)?);
        var notices = _board?.GetAll() ?? Array.Empty<WorkflowNotice>();
        var theme = WorkflowWinFormsTheme.Modern;
        _list.Rows.Clear();
        foreach (var notice in notices.Reverse())
        {
            var index = _list.Rows.Add(notice.Timestamp.ToLocalTime().ToString("MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                WorkflowNoticeLocation.Describe(notice), notice.Category, notice.Message,
                notice.Count.ToString(CultureInfo.InvariantCulture));
            var row = _list.Rows[index];
            row.Tag = notice;
            row.Cells[3].ToolTipText = notice.Message;
        }
        WorkflowPage.RestoreSelection(_list, selected, row => row.Tag is WorkflowNotice notice
            ? (notice.PlanPath, notice.NodeId, notice.Category, notice.Message)
            : null);
        var dropped = _board?.DroppedCount ?? 0;
        _summary.Text = notices.Count == 0
            ? "没有提示"
            : $"共 {notices.Count} 条提示" + (dropped > 0 ? $"，超出上限已丢弃 {dropped} 条" : string.Empty);
        _summary.ForeColor = notices.Count == 0 ? theme.TextSecondary : theme.Text;
        UpdateCommands();
        if (before != _list.Rows.Count) CountChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateCommands()
    {
        _acknowledgeSelected.Enabled = _board is not null && _list.SelectedRows.Count > 0;
        _acknowledgeAll.Enabled = _board is not null && _list.Rows.Count > 0;
    }

    private void AcknowledgeSelected()
    {
        if (_board is null) return;
        foreach (var location in _list.SelectedRows.Cast<DataGridViewRow>()
                     .Select(row => row.Tag).OfType<WorkflowNotice>()
                     .Select(notice => (notice.PlanPath, notice.NodeId)).Distinct().ToArray())
            _board.Acknowledge(location.PlanPath, location.NodeId);
        RefreshIfChanged();
    }

    private void AcknowledgeAll()
    {
        if (_board is null) return;
        _board.AcknowledgeAll();
        RefreshIfChanged();
    }

    private void ActivateSelected()
    {
        if (_list.SelectedRows.Count == 1 && _list.SelectedRows[0].Tag is WorkflowNotice notice)
            NoticeActivated?.Invoke(this, notice);
    }
}
