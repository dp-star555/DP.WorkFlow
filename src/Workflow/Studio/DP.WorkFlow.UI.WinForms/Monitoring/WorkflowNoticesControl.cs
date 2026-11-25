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
    private readonly ModernListView _list = new()
    {
        Dock = DockStyle.Fill,
        View = View.Details,
        FullRowSelect = true,
        HideSelection = false,
        ShowItemToolTips = true,
        BorderStyle = BorderStyle.None
    };
    private readonly Label _summary = new() { AutoSize = true, Margin = new Padding(0, 8, 12, 0) };
    private readonly ModernButton _acknowledgeSelected = WorkflowSizing.Fit(new ModernButton { Text = "确认选中位置" });
    private readonly ModernButton _acknowledgeAll = WorkflowSizing.Fit(new ModernButton { Text = "全部清除" });
    private WorkflowNoticeBoard? _board;
    private long _shownVersion = long.MinValue;

    /// <summary>初始化提示页。</summary>
    public WorkflowNoticesControl()
    {
        _list.Columns.Add("时间", 140);
        _list.Columns.Add("位置", 220);
        _list.Columns.Add("类别", 110);
        _list.Columns.Add("说明", 420);
        _list.Columns.Add("次数", 60, HorizontalAlignment.Right);
        _list.DoubleClick += (_, _) => ActivateSelected();
        _list.SelectedIndexChanged += (_, _) => UpdateCommands();
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
        WorkflowWinFormsTheme.Follow(this, () => RebuildList());
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
    public int NoticeCount => _list.Items.Count;

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
        var before = _list.Items.Count;
        var selected = _list.SelectedItems.Count == 1 && _list.SelectedItems[0].Tag is WorkflowNotice current
            ? (current.PlanPath, current.NodeId, current.Category, current.Message)
            : default((string, string?, string, string)?);
        var notices = _board?.GetAll() ?? Array.Empty<WorkflowNotice>();
        var theme = WorkflowWinFormsTheme.Modern;
        _list.BeginUpdate();
        try
        {
            _list.Items.Clear();
            foreach (var notice in notices.Reverse())
            {
                var item = new ListViewItem(notice.Timestamp.ToLocalTime().ToString("MM-dd HH:mm:ss", CultureInfo.InvariantCulture))
                {
                    Tag = notice,
                    ToolTipText = notice.Message,
                    ForeColor = theme.Text
                };
                item.SubItems.Add(WorkflowNoticeLocation.Describe(notice));
                item.SubItems.Add(notice.Category);
                item.SubItems.Add(notice.Message);
                item.SubItems.Add(notice.Count.ToString(CultureInfo.InvariantCulture));
                _list.Items.Add(item);
                if (selected is { } key && key == (notice.PlanPath, notice.NodeId, notice.Category, notice.Message))
                    item.Selected = true;
            }
        }
        finally
        {
            _list.EndUpdate();
        }
        var dropped = _board?.DroppedCount ?? 0;
        _summary.Text = notices.Count == 0
            ? "没有提示"
            : $"共 {notices.Count} 条提示" + (dropped > 0 ? $"，超出上限已丢弃 {dropped} 条" : string.Empty);
        _summary.ForeColor = notices.Count == 0 ? theme.TextSecondary : theme.Text;
        UpdateCommands();
        if (before != _list.Items.Count) CountChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateCommands()
    {
        _acknowledgeSelected.Enabled = _board is not null && _list.SelectedItems.Count > 0;
        _acknowledgeAll.Enabled = _board is not null && _list.Items.Count > 0;
    }

    private void AcknowledgeSelected()
    {
        if (_board is null) return;
        foreach (var location in _list.SelectedItems.Cast<ListViewItem>()
                     .Select(item => item.Tag).OfType<WorkflowNotice>()
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
        if (_list.SelectedItems.Count == 1 && _list.SelectedItems[0].Tag is WorkflowNotice notice)
            NoticeActivated?.Invoke(this, notice);
    }
}
