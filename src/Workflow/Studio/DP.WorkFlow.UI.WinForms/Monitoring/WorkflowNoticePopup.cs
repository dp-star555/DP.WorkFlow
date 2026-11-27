using System.Globalization;
using ModernUI.WinForms;

namespace DP.WorkFlow.UI.WinForms;

/// <summary>
/// 节点（或画布级）提示的小弹层：点画布上的提示角标打开，列出这个位置的提示，可以确认清除。
/// 每条提示两行：上面一行灰字“时间 · 类别 · 次数”，下面是完整说明（自动换行，不截断）。
/// 字体用画布所在窗口的字体，尺寸按屏幕 DPI 缩放；条数多时列表内滚动。
/// </summary>
internal sealed class WorkflowNoticePopup : ToolStripDropDown
{
    private const int LogicalWidth = 380;
    private const int LogicalMaxListHeight = 300;

    /// <summary>初始化弹层。</summary>
    /// <param name="owner">打开弹层的控件，取它的字体和 DPI。</param>
    /// <param name="title">标题，例如节点名称。</param>
    /// <param name="notices">要显示的提示。</param>
    /// <param name="acknowledge">确认清除。</param>
    public WorkflowNoticePopup(Control owner, string title, IReadOnlyList<WorkflowNotice> notices, Action acknowledge)
    {
        AutoClose = true;
        Padding = Padding.Empty;
        DropShadowEnabled = true;
        var font = owner.FindForm()?.Font ?? owner.Font;
        Font = font;
        int Scale(int value) => (int)Math.Round(value * owner.DeviceDpi / 96d);

        var theme = WorkflowWinFormsTheme.Modern;
        var width = Scale(LogicalWidth);
        var panel = new TableLayoutPanel
        {
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(Scale(14), Scale(12), Scale(14), Scale(12)),
            BackColor = theme.Elevated,
            Font = font,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(width, 0),
            MaximumSize = new Size(width, 0)
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var total = notices.Sum(notice => notice.Count);
        var header = new Label
        {
            AutoSize = true,
            Text = $"{title} · {notices.Count} 条提示" + (total > notices.Count ? $"（共出现 {total} 次）" : string.Empty),
            ForeColor = theme.Text,
            Font = new Font(font, FontStyle.Bold),
            Margin = new Padding(0, 0, 0, Scale(8)),
            UseMnemonic = false
        };
        panel.Controls.Add(header, 0, 0);

        var inner = width - panel.Padding.Horizontal;
        var rows = new TableLayoutPanel
        {
            ColumnCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = Padding.Empty,
            BackColor = theme.Elevated,
            Width = inner - SystemInformation.VerticalScrollBarWidth
        };
        rows.MinimumSize = rows.MaximumSize = new Size(rows.Width, 0);
        rows.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var textWidth = rows.Width - Scale(4);
        foreach (var notice in notices.Reverse())
        {
            var meta = new Label
            {
                AutoSize = true,
                Text = $"{notice.Timestamp.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture)} · {notice.Category}"
                    + (notice.Count > 1 ? $" · {notice.Count} 次" : string.Empty),
                ForeColor = theme.TextSecondary,
                Margin = new Padding(0, Scale(6), 0, Scale(2)),
                UseMnemonic = false
            };
            var message = new Label
            {
                AutoSize = true,
                MaximumSize = new Size(textWidth, 0),
                Text = notice.Message,
                ForeColor = theme.Text,
                Margin = new Padding(0, 0, 0, Scale(6)),
                UseMnemonic = false
            };
            rows.Controls.Add(meta);
            rows.Controls.Add(message);
            rows.Controls.Add(new Panel { Height = 1, Anchor = AnchorStyles.Left | AnchorStyles.Right, Margin = Padding.Empty, BackColor = theme.BorderSecondary });
        }
        var height = Math.Min(Scale(LogicalMaxListHeight), rows.GetPreferredSize(new Size(rows.Width, 0)).Height);
        var scroller = new Panel
        {
            AutoScroll = true,
            Size = new Size(inner, height),
            Margin = Padding.Empty,
            BackColor = theme.Elevated
        };
        scroller.Controls.Add(rows);
        panel.Controls.Add(scroller, 0, 1);

        var buttons = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            Margin = new Padding(0, Scale(10), 0, 0),
            BackColor = theme.Elevated
        };
        var acknowledgeButton = WorkflowSizing.Fit(new ModernButton { Text = "确认并清除", ButtonType = ModernButtonType.Primary, Font = font });
        acknowledgeButton.Click += (_, _) =>
        {
            Close(ToolStripDropDownCloseReason.ItemClicked);
            acknowledge();
        };
        buttons.Controls.Add(acknowledgeButton);
        panel.Controls.Add(buttons, 0, 2);

        WorkflowWinFormsTheme.Apply(panel);
        panel.Size = panel.GetPreferredSize(new Size(width, 0));
        var host = new ToolStripControlHost(panel) { Padding = Padding.Empty, Margin = Padding.Empty, AutoSize = false, Size = panel.Size };
        Items.Add(host);
        BackColor = theme.Elevated;
    }
}
