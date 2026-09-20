using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace DP.WorkFlow.OperatorUI.Wpf;

/// <summary>真实WPF人工选择/确认适配器；窗口非模态，不禁用主界面的停止操作。</summary>
public sealed class WorkflowWpfOperatorService : IWorkflowOperatorService, IDisposable
{
    private readonly Window _owner;
    private readonly WorkflowOperatorInteraction _interaction = new();
    private Window? _dialog;
    private Guid? _shownPrompt;
    private int _refreshQueued;
    private int _disposed;

    /// <summary>在所属Dispatcher线程创建；主窗口尚未显示时暂不创建提示窗口。</summary>
    /// <param name="owner">提示所属主窗口。</param>
    public WorkflowWpfOperatorService(Window owner)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        owner.Dispatcher.VerifyAccess();
        if (owner.Dispatcher.HasShutdownStarted) throw new InvalidOperationException("窗口Dispatcher正在关闭。");
        _interaction.PromptChanged += OnPromptChanged;
        owner.IsVisibleChanged += OnOwnerVisible;
        owner.Closed += OnOwnerClosed;
        owner.Dispatcher.ShutdownStarted += OnOwnerClosed;
    }

    /// <summary>当前尚未完成的人工任务。</summary>
    public WorkflowOperatorPrompt? CurrentPrompt => _interaction.CurrentPrompt;

    /// <inheritdoc />
    public ValueTask<string> AskChoiceAsync(string title, string message, IReadOnlyList<OperatorChoiceOption> options, CancellationToken cancellationToken) =>
        _interaction.AskChoiceAsync(title, message, options, cancellationToken);

    /// <inheritdoc />
    public ValueTask ConfirmStepAsync(string title, string message, CancellationToken cancellationToken) =>
        _interaction.ConfirmStepAsync(title, message, cancellationToken);

    private void OnPromptChanged(object? sender, EventArgs e) => QueueRefresh();
    private void OnOwnerVisible(object sender, DependencyPropertyChangedEventArgs e) => QueueRefresh();
    private void OnOwnerClosed(object? sender, EventArgs e) { Dispose(); CleanupUi(); }

    private void QueueRefresh()
    {
        if (_owner.Dispatcher.HasShutdownStarted) { Dispose(); return; }
        if (Interlocked.Exchange(ref _refreshQueued, 1) != 0) return;
        try
        {
            _owner.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                Volatile.Write(ref _refreshQueued, 0);
                if (Volatile.Read(ref _disposed) != 0) CleanupUi(); else RefreshUi();
            }));
        }
        catch (InvalidOperationException)
        {
            Volatile.Write(ref _refreshQueued, 0);
            Dispose();
        }
    }

    private void RefreshUi()
    {
        if (!_owner.IsVisible) { CloseDialog(); return; }
        var prompt = _interaction.CurrentPrompt;
        if (_dialog is not null && _shownPrompt == prompt?.Id) return;
        CloseDialog();
        prompt = _interaction.CurrentPrompt;
        if (prompt is null || Volatile.Read(ref _disposed) != 0) return;
        var window = CreateWindow(prompt);
        _dialog = window;
        _shownPrompt = prompt.Id;
        window.Closed += (_, _) =>
        {
            if (!ReferenceEquals(_dialog, window)) return;
            _dialog = null; _shownPrompt = null;
            _interaction.TryDismiss(prompt.Id);
        };
        try { window.Show(); }
        catch (Exception failure)
        {
            _interaction.TryDismiss(prompt.Id);
            CloseDialog();
            System.Diagnostics.Debug.WriteLine(failure);
        }
    }

    private Window CreateWindow(WorkflowOperatorPrompt prompt)
    {
        var window = new Window
        {
            Name = "WorkflowOperatorPrompt", Owner = _owner,
            Title = string.IsNullOrWhiteSpace(prompt.Title) ? "人工处理" : prompt.Title,
            Width = 580, Height = 340, MinWidth = 440, MinHeight = 260, FontSize = 14,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false, ResizeMode = ResizeMode.CanResizeWithGrip
        };
        var panel = new DockPanel { Margin = new Thickness(16) };
        var buttons = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        var buttonScroll = new ScrollViewer
        {
            Content = buttons, MaxHeight = 150, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        DockPanel.SetDock(buttonScroll, Dock.Bottom);
        panel.Children.Add(buttonScroll);
        var footer = new TextBlock
        {
            Text = "仅提交人工选择或确认，不替代设备状态检查。关闭窗口不会默认同意。",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0)
        };
        DockPanel.SetDock(footer, Dock.Bottom);
        panel.Children.Add(footer);
        var message = new TextBox
        {
            Name = "OperatorMessage", Text = prompt.Message, IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap, AcceptsReturn = true,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, BorderThickness = new Thickness(0)
        };
        panel.Children.Add(message);
        void AddButton(string text, string key, Func<bool> respond)
        {
            var button = new Button
            {
                Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap }, Tag = key, MinWidth = 120, MinHeight = 36,
                Margin = new Thickness(4), Padding = new Thickness(12, 4, 12, 4), IsDefault = false
            };
            button.Click += (_, _) =>
            {
                if (respond()) foreach (UIElement item in buttons.Children) item.IsEnabled = false;
            };
            buttons.Children.Add(button);
        }
        if (prompt.Kind == WorkflowOperatorPromptKind.Confirmation)
            AddButton("处理完成", "Confirm", () => _interaction.TryConfirm(prompt.Id));
        else
            foreach (var option in prompt.Options)
                AddButton(option.Text, option.Key, () => _interaction.TryChoose(prompt.Id, option.Key));
        window.Content = panel;
        window.ContentRendered += (_, _) => message.Focus();
        return window;
    }

    private void CloseDialog()
    {
        var window = _dialog;
        _dialog = null;
        _shownPrompt = null;
        window?.Close();
    }

    private void CleanupUi()
    {
        _owner.IsVisibleChanged -= OnOwnerVisible;
        _owner.Closed -= OnOwnerClosed;
        _owner.Dispatcher.ShutdownStarted -= OnOwnerClosed;
        CloseDialog();
    }

    /// <summary>取消当前及排队任务，在Dispatcher线程关闭窗口；幂等。</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _interaction.PromptChanged -= OnPromptChanged;
        _interaction.Dispose();
        if (_owner.Dispatcher.CheckAccess()) CleanupUi(); else QueueRefresh();
    }
}
