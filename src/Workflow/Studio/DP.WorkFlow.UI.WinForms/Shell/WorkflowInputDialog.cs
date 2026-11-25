using ModernUI.WinForms;

namespace DP.WorkFlow.UI.WinForms;

/// <summary>输入一行文字的小窗口（改名、连线备注）：说明、ModernInput、确定 / 取消。</summary>
internal static class WorkflowInputDialog
{
    /// <summary>问一行文字；取消或没填时返回空。</summary>
    /// <param name="owner">父窗口。</param>
    /// <param name="title">标题。</param>
    /// <param name="prompt">说明。</param>
    /// <param name="value">初始值。</param>
    /// <param name="maxLength">最多字数。</param>
    /// <param name="allowEmpty">是否允许确定空文字（例如清除备注）。</param>
    public static string? Ask(IWin32Window? owner, string title, string prompt, string value, int maxLength, bool allowEmpty = false)
    {
        using var dialog = new Form
        {
            Text = title,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false,
            ShowIcon = false,
            AutoScaleMode = AutoScaleMode.Dpi,
            AutoScaleDimensions = new SizeF(96, 96),
            ClientSize = new Size(400, 140),
            Padding = new Padding(16, 14, 16, 12)
        };
        var hint = new Label { Text = prompt, Dock = DockStyle.Top, Height = 26, UseMnemonic = false, AutoEllipsis = true };
        var input = new ModernInput { Text = value, MaxLength = maxLength, Dock = DockStyle.Top, Height = 34 };
        var ok = new ModernButton { Text = "确定", ButtonType = ModernButtonType.Primary, DialogResult = DialogResult.OK };
        var cancel = new ModernButton { Text = "取消", DialogResult = DialogResult.Cancel };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Height = 40 };
        buttons.Controls.Add(WorkflowSizing.Fit(cancel));
        buttons.Controls.Add(WorkflowSizing.Fit(ok));
        dialog.Controls.Add(input);
        dialog.Controls.Add(hint);
        dialog.Controls.Add(buttons);
        dialog.AcceptButton = ok;
        dialog.CancelButton = cancel;
        dialog.Shown += (_, _) => { input.Focus(); input.SelectAll(); };
        WorkflowWinFormsTheme.Follow(dialog);
        if (dialog.ShowDialog(owner) != DialogResult.OK) return null;
        var text = input.Text.Trim();
        return text.Length > 0 || allowEmpty ? text : null;
    }
}
