using ModernUI.Localization;

namespace ModernUI.WinForms;

/// <summary>配置现代确认对话框的内容和按钮。</summary>
public sealed class ModernDialogOptions
{
    public string Title { get; set; } = "提示";
    public string Message { get; set; } = string.Empty;
    public ModernVisualStatus Status { get; set; } = ModernVisualStatus.Primary;
    public string? ConfirmText { get; set; }
    public string? CancelText { get; set; }
    public ILocalizationContext? LocalizationContext { get; set; }
    public bool ShowCancelButton { get; set; } = true;
    public ModernTheme? Theme { get; set; }
}

/// <summary>提供保留键盘默认按钮和无障碍语义的现代对话框。</summary>
public static class ModernDialog
{
    public static DialogResult Show(IWin32Window? owner, ModernDialogOptions options)
    {
        ModernCompatibility.ThrowIfNull(options, nameof(options));
        var restoreFocus = owner is Control ownerControl ? FindFocusedControl(ownerControl) : null;
        using var dialog = Create(options);
        try { return owner is null ? dialog.ShowDialog() : dialog.ShowDialog(owner); }
        finally
        {
            if (restoreFocus is { IsDisposed: false, CanFocus: true }) restoreFocus.Focus();
        }
    }

    public static Task<DialogResult> ShowAsync(Control owner, ModernDialogOptions options)
    {
        ModernCompatibility.ThrowIfNull(owner, nameof(owner));
        ModernCompatibility.ThrowIfNull(options, nameof(options));
        var completion = new TaskCompletionSource<DialogResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        owner.BeginInvoke(() =>
        {
            try { completion.TrySetResult(Show(owner, options)); }
            catch (Exception exception) { completion.TrySetException(exception); }
        });
        return completion.Task;
    }

    private static Control? FindFocusedControl(Control owner)
    {
        Control? current = owner.FindForm() ?? owner;
        while (current is ContainerControl container && container.ActiveControl is { } active)
            current = active;
        return current?.ContainsFocus == true ? current : null;
    }

    private static Form Create(ModernDialogOptions options)
    {
        var theme = options.Theme ?? ModernUiSettings.DefaultTheme;
        var localization = options.LocalizationContext ?? ModernUiLocalization.DefaultContext;
        var form = new Form
        {
            Text = options.Title,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            ShowInTaskbar = false,
            MinimizeBox = false,
            MaximizeBox = false,
            ClientSize = new Size(430, 176),
            BackColor = theme.Container,
            ForeColor = theme.Text,
            AutoScaleMode = AutoScaleMode.Dpi,
            AccessibleRole = AccessibleRole.Dialog,
            AccessibleName = options.Title,
            AccessibleDescription = options.Message,
            RightToLeft = localization.Current.TextDirection == TextDirection.RightToLeft
                ? RightToLeft.Yes : RightToLeft.No,
            RightToLeftLayout = localization.Current.TextDirection == TextDirection.RightToLeft
        };
        var icon = new ModernBadge { Left = 22, Top = 24, Width = 22, Height = 22, Status = options.Status, Dot = true, Theme = theme, AccessibleName = options.Status.ToString() };
        var title = new Label { Left = 56, Top = 20, Width = 340, Height = 28, Text = options.Title, Font = new Font(SystemFonts.MessageBoxFont ?? SystemFonts.DefaultFont, FontStyle.Bold), ForeColor = theme.Text, BackColor = theme.Container };
        var message = new Label { Left = 56, Top = 52, Width = 344, Height = 62, Text = options.Message, ForeColor = theme.TextSecondary, BackColor = theme.Container };
        var confirm = new ModernButton { Text = options.ConfirmText ?? localization.Text(ModernUiTextKeys.Confirm), ButtonType = options.Status == ModernVisualStatus.Error ? ModernButtonType.Danger : ModernButtonType.Primary, DialogResult = DialogResult.OK, Width = 88, Left = 320, Top = 128, Theme = theme };
        var cancel = new ModernButton { Text = options.CancelText ?? localization.Text(ModernUiTextKeys.Cancel), DialogResult = DialogResult.Cancel, Width = 88, Left = 224, Top = 128, Theme = theme, Visible = options.ShowCancelButton };
        form.Controls.AddRange([icon, title, message, cancel, confirm]);
        form.AcceptButton = confirm;
        form.CancelButton = options.ShowCancelButton ? cancel : null;
        return form;
    }
}
