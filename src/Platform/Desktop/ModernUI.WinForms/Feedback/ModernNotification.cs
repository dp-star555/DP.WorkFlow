namespace ModernUI.WinForms;

/// <summary>配置右下角通知的内容、生命周期和可选操作。</summary>
public sealed class ModernNotificationOptions
{
    public string Title { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public ModernVisualStatus Status { get; init; } = ModernVisualStatus.Primary;
    public int Duration { get; init; } = 5000;
    public ModernCommand? ActionCommand { get; init; }
    public bool CloseOnAction { get; init; }
}

/// <summary>在宿主窗口右下角显示带标题和说明的通知卡片。</summary>
public static class ModernNotification
{
    private static readonly Dictionary<Form, List<NotificationWindow>> Active = [];
    private static readonly Dictionary<Form, ModernFeedbackOwnerSubscription> OwnerSubscriptions = [];

    internal static void Refresh(Form owner)
    {
        if (Active.TryGetValue(owner, out var windows)) Arrange(owner, windows);
    }

    public static IDisposable Show(Control owner, string title, string description, ModernVisualStatus status = ModernVisualStatus.Primary, int duration = 5000) =>
        Show(owner, new ModernNotificationOptions
        {
            Title = title ?? string.Empty,
            Description = description ?? string.Empty,
            Status = status,
            Duration = duration
        });

    public static IDisposable Show(Control owner, ModernNotificationOptions options)
    {
        ModernCompatibility.ThrowIfNull(owner, nameof(owner));
        ModernCompatibility.ThrowIfNull(options, nameof(options));
        var form = owner.FindForm() ?? owner as Form ?? throw new InvalidOperationException("ModernNotification requires a control hosted by a Form.");
        if (!Active.TryGetValue(form, out var windows))
        {
            Active[form] = windows = [];
            OwnerSubscriptions[form] = new ModernFeedbackOwnerSubscription(form, OwnerGeometryChanged, OwnerClosed);
        }
        var title = options.Title ?? string.Empty;
        var description = options.Description ?? string.Empty;
        var duration = Math.Max(0, options.Duration);
        if (ModernUiSettings.MergeDuplicateFeedback && windows.LastOrDefault(window => window.Matches(title, description, options.Status, options.ActionCommand)) is { } duplicate)
        {
            duplicate.RestartLifetime(duration);
            Arrange(form, windows);
            return duplicate;
        }
        var maximumVisible = Math.Max(1, ModernUiSettings.MaximumVisibleNotifications);
        while (windows.Count >= maximumVisible) windows[0].Dispose();
        var theme = ModernThemeResolver.Resolve(form);
        var window = new NotificationWindow(title, description, options.Status, theme, duration,
            options.ActionCommand, options.CloseOnAction, closed => Remove(form, closed));
        windows.Add(window);
        Arrange(form, windows);
        window.ShowFinalFrame(form,
            window.PrepareCompleteHandleTree,
            () =>
            {
                window.ApplyOwnerDpi(form.DeviceDpi);
                Arrange(form, windows);
            });
        return window;
    }

    private static void Remove(Form owner, NotificationWindow window)
    {
        if (!Active.TryGetValue(owner, out var windows)) return;
        windows.Remove(window);
        if (windows.Count == 0)
        {
            Active.Remove(owner);
            if (OwnerSubscriptions.Remove(owner, out var subscription)) subscription.Dispose();
        }
        else Arrange(owner, windows);
    }
    private static void OwnerGeometryChanged(object? sender, EventArgs e)
    {
        if (sender is Form owner && Active.TryGetValue(owner, out var windows)) Arrange(owner, windows);
    }
    private static void OwnerClosed(object? sender, FormClosedEventArgs e)
    {
        if (sender is not Form owner || !Active.TryGetValue(owner, out var windows)) return;
        foreach (var window in windows.ToArray()) window.Dispose();
        Active.Remove(owner);
        if (OwnerSubscriptions.Remove(owner, out var subscription)) subscription.Dispose();
    }
    private static void Arrange(Form owner, List<NotificationWindow> windows)
    {
        if (owner.IsDisposed) return;
        var dpi = Math.Max(96, owner.DeviceDpi);
        var theme = ModernThemeResolver.Resolve(owner);
        var ownerActive = owner.Visible && owner.WindowState != FormWindowState.Minimized;
        foreach (var window in windows)
        {
            window.SetOwnerActive(ownerActive);
            window.ApplyTheme(theme);
            window.ApplyOwnerDpi(dpi);
        }
        var scale = dpi / 96f;
        var edge = (int)Math.Round(18 * scale);
        var gap = (int)Math.Round(10 * scale);
        var bounds = owner.RectangleToScreen(owner.ClientRectangle);
        var requiredHeight = windows.Sum(window => window.Height) + Math.Max(0, windows.Count - 1) * gap;
        if (windows.Count > 1 && requiredHeight > Math.Max(1, bounds.Height - edge * 2))
        {
            windows[0].Dispose();
            return;
        }
        var bottom = bounds.Bottom - edge;
        foreach (var window in windows.AsEnumerable().Reverse())
        {
            window.SetArrangedLocation(new Point(bounds.Right - window.Width - edge, bottom - window.Height));
            bottom -= window.Height + gap;
        }
    }
    private sealed class NotificationWindow : ModernOwnedOverlayForm
    {
        private readonly FeedbackAutoCloseTimer _lifetime;
        private readonly Action<NotificationWindow> _closed;
        private readonly ModernAlert _alert;
        private readonly int _cornerRadius;
        private readonly string _title;
        private readonly string _description;
        private readonly ModernVisualStatus _status;
        private readonly ModernCommand? _actionCommand;
        private readonly ModernButton? _actionButton;
        private IDisposable? _animation;
        private Point _arrangedLocation;
        private float _ownerScale = 1f;
        private int _ownerDpi = 96;
        private bool _hasArrangedLocation;
        private bool _opening;
        private bool _closing;
        private bool _notified;
        public NotificationWindow(string title, string description, ModernVisualStatus status, ModernTheme theme, int duration,
            ModernCommand? actionCommand, bool closeOnAction, Action<NotificationWindow> closed)
            : base(compositedSurface: true)
        {
            _closed = closed;
            _cornerRadius = theme.Radius;
            _title = title;
            _description = description;
            _status = status;
            _actionCommand = actionCommand;
            _lifetime = new FeedbackAutoCloseTimer(Close);
            AccessibleRole = AccessibleRole.Alert;
            AccessibleName = title;
            AccessibleDescription = description;
            Opacity = ModernUiSettings.EffectiveAnimationsEnabled ? 0 : 1;
            Font = ModernFeedbackTypography.Create();
            _alert = new ModernAlert { Dock = DockStyle.Fill, Text = title, Description = description, Status = status, Closable = true, Theme = theme, WrapText = true };
            _alert.Closed += (_, _) => Close();
            _alert.MouseEnter += (_, _) => _lifetime.SetPaused(FeedbackPauseReason.Pointer, true);
            _alert.MouseLeave += (_, _) => { if (!_closing) _lifetime.SetPaused(FeedbackPauseReason.Pointer, false); };
            Controls.Add(_alert);
            if (actionCommand is not null)
            {
                _actionButton = new ModernButton
                {
                    Command = actionCommand,
                    ButtonType = ModernButtonType.Text,
                    Theme = theme,
                    TabStop = true
                };
                if (closeOnAction) _actionButton.Click += ActionButtonClicked;
                _actionButton.MouseEnter += (_, _) => _lifetime.SetPaused(FeedbackPauseReason.Pointer, true);
                _actionButton.MouseLeave += (_, _) => { if (!_closing) _lifetime.SetPaused(FeedbackPauseReason.Pointer, false); };
                Controls.Add(_actionButton);
                _actionButton.BringToFront();
            }
            RestartLifetime(duration);
        }

        public void ApplyTheme(ModernTheme theme)
        {
            BackColor = ModernStatusColors.ResolveSurface(theme, _status);
            _alert.Theme = theme;
            if (_actionButton is not null) _actionButton.Theme = theme;
        }

        public bool Matches(string title, string description, ModernVisualStatus status, ModernCommand? actionCommand) =>
            _title == title && _description == description && _status == status &&
            ReferenceEquals(_actionCommand, actionCommand) && !_closing && !IsDisposed;

        public void RestartLifetime(int duration) => _lifetime.Restart(duration);

        public void SetOwnerActive(bool active) =>
            _lifetime.SetPaused(FeedbackPauseReason.OwnerInactive, !active);

        public void ApplyOwnerDpi(int dpi)
        {
            _ownerDpi = Math.Max(96, dpi);
            _ownerScale = _ownerDpi / 96f;
            var width = (int)Math.Round(340 * _ownerScale);
            var minimumHeight = (int)Math.Round(88 * _ownerScale);
            var maximumHeight = (int)Math.Round(200 * _ownerScale);
            var textWidth = Math.Max(1, width - (int)Math.Round(86 * _ownerScale));
            var messageFont = Font;
            using var titleFont = new Font(messageFont, FontStyle.Bold);
            using var textGraphics = CreateGraphics();
            var titleSize = ModernTextLayout.Measure(textGraphics, _title, titleFont,
                new Size(textWidth, (int)Math.Round(46 * _ownerScale)), wrap: true);
            var descriptionSize = ModernTextLayout.Measure(textGraphics, _description, messageFont,
                new Size(textWidth, maximumHeight), wrap: true);
            var verticalChrome = (int)Math.Round(28 * _ownerScale);
            var contentGap = string.IsNullOrEmpty(_description) ? 0 : (int)Math.Round(2 * _ownerScale);
            var actionRowHeight = _actionButton is null ? 0 : (int)Math.Round(36 * _ownerScale);
            var desiredHeight = verticalChrome + titleSize.Height + contentGap + descriptionSize.Height + actionRowHeight;
            ClientSize = new Size(width, ModernCompatibility.Clamp(desiredHeight, minimumHeight, maximumHeight));
            var horizontalPadding = (int)Math.Round(14 * _ownerScale);
            var verticalPadding = (int)Math.Round(10 * _ownerScale);
            _alert.Padding = new Padding(horizontalPadding, verticalPadding, horizontalPadding,
                verticalPadding + actionRowHeight);
            if (_actionButton is not null)
            {
                var actionHeight = (int)Math.Round(28 * _ownerScale);
                var measuredWidth = ModernTextLayout.Measure(textGraphics, _actionButton.Text,
                    _actionButton.Font, new Size(int.MaxValue, int.MaxValue), wrap: false).Width;
                var actionWidth = ModernCompatibility.Clamp(measuredWidth + (int)Math.Round(28 * _ownerScale),
                    (int)Math.Round(64 * _ownerScale), Math.Max(1, width - horizontalPadding * 2));
                _actionButton.Bounds = new Rectangle(width - horizontalPadding - actionWidth,
                    Height - verticalPadding - actionHeight, actionWidth, actionHeight);
                _actionButton.BringToFront();
            }
            ApplyRoundedRegion();
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            ApplyRoundedRegion();
        }

        private void ApplyRoundedRegion() => RoundedNativeControlRegion.Apply(this, _cornerRadius, _ownerDpi);

        public void SetArrangedLocation(Point location)
        {
            _arrangedLocation = location;
            _hasArrangedLocation = true;
            if (!_opening) Location = location;
            else Top = location.Y;
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (!_hasArrangedLocation) SetArrangedLocation(Location);
            _opening = true;
            _animation?.Dispose();
            _animation = ModernAnimation.Start(this, 0, 1, 200, progress =>
            {
                Opacity = progress;
                Left = _arrangedLocation.X + (int)Math.Round((1 - progress) * 18 * _ownerScale);
                Top = _arrangedLocation.Y;
            }, () =>
            {
                _opening = false;
                Location = _arrangedLocation;
                _animation = null;
            });
        }
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_closing || e.CloseReason is CloseReason.FormOwnerClosing or CloseReason.ApplicationExitCall) { base.OnFormClosing(e); return; }
            e.Cancel = true;
            BeginInvoke(StartCloseAnimation);
        }
        private void ActionButtonClicked(object? sender, EventArgs e)
        {
            if (!_closing) Close();
        }

        private void StartCloseAnimation()
        {
            if (_closing || IsDisposed) return;
            _lifetime.SetPaused(FeedbackPauseReason.OwnerInactive, true);
            var startOpacity = (float)Opacity;
            var startLeft = Left;
            _animation?.Dispose();
            _animation = ModernAnimation.Start(this, startOpacity, 0, 150, progress =>
            {
                Opacity = progress;
                Left = startLeft + (int)Math.Round((1 - progress) * 12 * _ownerScale);
            }, () => { _closing = true; Close(); });
        }
        protected override void OnFormClosed(FormClosedEventArgs e) { base.OnFormClosed(e); Notify(); }
        protected override void Dispose(bool disposing) { if (disposing) { _animation?.Dispose(); _lifetime.Dispose(); Notify(); } base.Dispose(disposing); }
        private void Notify() { if (_notified) return; _notified = true; _closed(this); }
    }
}
