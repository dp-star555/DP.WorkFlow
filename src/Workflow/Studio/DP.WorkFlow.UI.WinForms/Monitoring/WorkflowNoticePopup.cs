using System.Globalization;
using ModernUI.WinForms;

namespace DP.WorkFlow.UI.WinForms;

/// <summary>
/// 节点（或画布级）提示的小弹层：点画布上的提示角标打开，列出这个位置的提示，可以确认清除或转到提示页。
/// </summary>
internal sealed class WorkflowNoticePopup : ToolStripDropDown
{
    private readonly Action _acknowledge;
    private readonly Action? _showAll;

    /// <summary>初始化弹层。</summary>
    /// <param name="title">标题，例如节点名称。</param>
    /// <param name="notices">要显示的提示。</param>
    /// <param name="acknowledge">确认清除。</param>
    /// <param name="showAll">转到提示页；为空时不显示该按钮。</param>
    public WorkflowNoticePopup(string title, IReadOnlyList<WorkflowNotice> notices, Action acknowledge, Action? showAll)
    {
        _acknowledge = acknowledge;
        _showAll = showAll;
        AutoClose = true;
        Padding = Padding.Empty;
        DropShadowEnabled = true;

        var theme = WorkflowWinFormsTheme.Modern;
        var panel = new TableLayoutPanel
        {
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(12, 10, 12, 10),
            BackColor = theme.Elevated,
            Size = new Size(440, 260)
        };
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var total = notices.Sum(notice => notice.Count);
        var header = new Label
        {
            AutoSize = true,
            Text = $"{title} · {notices.Count} 条提示" + (total > notices.Count ? $"（共出现 {total} 次）" : string.Empty),
            ForeColor = theme.Text,
            Font = new Font(Font, FontStyle.Bold),
            Margin = new Padding(0, 0, 0, 6)
        };
        panel.Controls.Add(header, 0, 0);

        var list = new ModernListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            HeaderStyle = ColumnHeaderStyle.Nonclickable,
            BorderStyle = BorderStyle.None,
            ShowItemToolTips = true
        };
        list.Columns.Add("时间", 76);
        list.Columns.Add("类别", 80);
        list.Columns.Add("说明", 210);
        list.Columns.Add("次数", 44, HorizontalAlignment.Right);
        foreach (var notice in notices.Reverse())
        {
            var item = new ListViewItem(notice.Timestamp.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture))
            {
                ToolTipText = notice.Message,
                ForeColor = theme.Text
            };
            item.SubItems.Add(notice.Category);
            item.SubItems.Add(notice.Message);
            item.SubItems.Add(notice.Count.ToString(CultureInfo.InvariantCulture));
            list.Items.Add(item);
        }
        panel.Controls.Add(list, 0, 1);

        var buttons = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            Margin = new Padding(0, 8, 0, 0),
            BackColor = theme.Elevated
        };
        var acknowledgeButton = WorkflowSizing.Fit(new ModernButton { Text = "确认并清除", ButtonType = ModernButtonType.Primary });
        acknowledgeButton.Click += (_, _) =>
        {
            Close(ToolStripDropDownCloseReason.ItemClicked);
            _acknowledge();
        };
        buttons.Controls.Add(acknowledgeButton);
        if (_showAll is not null)
        {
            var showAllButton = WorkflowSizing.Fit(new ModernButton { Text = "查看全部提示" });
            showAllButton.Click += (_, _) =>
            {
                Close(ToolStripDropDownCloseReason.ItemClicked);
                _showAll();
            };
            buttons.Controls.Add(showAllButton);
        }
        panel.Controls.Add(buttons, 0, 2);

        WorkflowWinFormsTheme.Apply(panel);
        var host = new ToolStripControlHost(panel) { Padding = Padding.Empty, Margin = Padding.Empty, AutoSize = false, Size = panel.Size };
        Items.Add(host);
        BackColor = theme.Elevated;
    }
}
