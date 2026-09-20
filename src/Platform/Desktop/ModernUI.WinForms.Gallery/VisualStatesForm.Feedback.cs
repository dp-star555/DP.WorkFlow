using System.Globalization;
using ModernUI.Localization;
using ModernUI.WinForms;

namespace ModernUI.WinForms.Gallery;

internal sealed partial class VisualStatesForm
{
    private static Control CreateBadges()
    {
        var panel = new FlowLayoutPanel { Width = 170, Height = 34, WrapContents = false };
        panel.Controls.Add(new ModernBadge { Count = 8 });
        panel.Controls.Add(new ModernBadge { Count = 120, OverflowCount = 99, Status = ModernVisualStatus.Warning });
        panel.Controls.Add(new ModernBadge { Dot = true, Status = ModernVisualStatus.Success, Width = 24 });
        return panel;
    }

    private Control CreateCollapsible()
    {
        var panel = new ModernCollapsiblePanel
        {
            Width = 230,
            Height = 84,
            HeaderIcon = ModernIconKind.Settings
        };
        BindGalleryText("AdvancedSettings", text => panel.Text = text);
        var content = new Label { AutoSize = false, TextAlign = ContentAlignment.MiddleLeft };
        BindGalleryText("AdvancedSettingsContent", text => content.Text = text);
        panel.Content = content;
        return panel;
    }

    private Control CreateCommandBar()
    {
        var bar = new ModernCommandBar { Width = 400 };
        var run = new ModernCommand { Icon = ModernIconKind.Play };
        var settings = new ModernCommand { Icon = ModernIconKind.Settings };
        var diagnostics = new ModernCommand { Icon = ModernIconKind.Info };
        BindGalleryText("CommandRun", text => run.Text = text);
        BindGalleryText("CommandSettings", text => settings.Text = text);
        BindGalleryText("CommandDiagnostics", text => diagnostics.Text = text);
        bar.Commands.Add(run);
        bar.Commands.Add(settings);
        bar.Commands.Add(new ModernCommand { Kind = ModernCommandKind.Separator });
        bar.Commands.Add(diagnostics);
        return bar;
    }

    private Control CreateFeedbackButtons()
    {
        var panel = new FlowLayoutPanel { Width = 230, Height = 42, WrapContents = false };
        var message = LocalizedText(new ModernButton { Width = 104 }, "MessageButton");
        var dialog = LocalizedText(new ModernButton { Width = 96 }, "DialogButton");
        message.Click += (_, _) => { if (message.FindForm() is { } form) ModernMessage.Success(form, G("OperationCompleted")); };
        dialog.Click += (_, _) => ModernDialog.Show(dialog, new ModernDialogOptions { Title = G("ConfirmRunTitle"), Message = G("ConfirmRunMessage") });
        panel.Controls.AddRange([message, dialog]);
        return panel;
    }

    private static Control CreateTagMultiSelect()
    {
        var select = new ModernSelectMultiple { Width = 260, DisplayMode = MultipleSelectionDisplayMode.Tags, MaxVisibleTags = 2 };
        select.Items.AddRange(["Camera", "PLC", "Robot"]);
        select.SetItemChecked(0, true);
        select.SetItemChecked(1, true);
        select.SetItemChecked(2, true);
        return select;
    }

    private ModernAlert CreateLocalizedAlert()
    {
        var alert = new ModernAlert { Width = 310, Status = ModernVisualStatus.Success, Closable = true };
        BindGalleryText("AlertSaved", text => alert.Text = text);
        BindGalleryText("AlertSavedDescription", text => alert.Description = text);
        return alert;
    }

    private ModernEmptyState CreateLocalizedEmptyState()
    {
        var empty = new ModernEmptyState { Width = 280, Height = 90 };
        BindGalleryText("EmptyRuns", text => empty.Description = text);
        return empty;
    }

    private Control CreateNotificationButton()
    {
        var button = LocalizedText(new ModernButton { Width = 150 }, "NotificationButton");
        var action = new ModernCommand { Icon = ModernIconKind.Info };
        BindGalleryText("CommandDiagnostics", text => action.Text = text);
        action.ExecuteAction = () => ModernMessage.Info(button, G("WorkflowCompletedDescription"));
        button.Click += (_, _) =>
        {
            if (button.FindForm() is not { } form) return;
            ModernNotification.Show(form, new ModernNotificationOptions
            {
                Title = G("WorkflowCompleted"),
                Description = G("WorkflowCompletedDescription"),
                Status = ModernVisualStatus.Success,
                ActionCommand = action,
                CloseOnAction = true
            });
        };
        return button;
    }

    private Control CreateValidationDemo()
    {
        var panel = new Panel { Width = 480, Height = 76 };
        var input = LocalizedText(new ModernInput { Left = 0, Top = 0, Width = 150 }, "InvalidValue");
        var summary = new ModernValidationSummary { Left = 162, Top = 0, Width = 310, Height = 66 };
        var provider = new ModernValidationProvider();
        summary.Provider = provider;
        BindGalleryText("DeviceAddressInvalid", text => provider.SetValidation(input, ModernValidationState.Error, text));
        panel.Controls.AddRange([input, summary]);
        panel.Disposed += (_, _) => provider.Dispose();
        return panel;
    }

    private Control CreateContextMenuButton()
    {
        var button = LocalizedText(new ModernButton { Width = 120 }, "OpenMenu");
        var menu = new ModernContextMenu();
        var run = menu.Items.Add(string.Empty);
        var pause = menu.Items.Add(string.Empty);
        menu.Items.Add(new ToolStripSeparator());
        var delete = menu.Items.Add(string.Empty);
        BindGalleryText("MenuRun", text => run.Text = text);
        BindGalleryText("MenuPause", text => pause.Text = text);
        BindGalleryText("MenuDelete", text => delete.Text = text);
        button.ContextMenuStrip = menu;
        button.Click += (_, _) => menu.Show(button, new Point(0, button.Height));
        return button;
    }

    private Control CreateStatusBar()
    {
        var bar = new ModernStatusBar { Width = 250, Dock = DockStyle.None };
        var ready = new ToolStripStatusLabel();
        BindGalleryText("StatusReady", text => ready.Text = text);
        bar.Items.Add(ready);
        bar.AddSpring();
        bar.Items.Add(new ToolStripStatusLabel("100%"));
        return bar;
    }
}
