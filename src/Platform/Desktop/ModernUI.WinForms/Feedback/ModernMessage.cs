namespace ModernUI.WinForms;

/// <summary>在宿主窗口顶部显示自动关闭的轻量消息。</summary>
public static class ModernMessage
{
    private static readonly Dictionary<Form, MessageHost> Hosts = [];

    public static IDisposable Info(Control owner, string text, int duration = 3000) => Show(owner, text, ModernVisualStatus.Primary, duration);
    public static IDisposable Success(Control owner, string text, int duration = 3000) => Show(owner, text, ModernVisualStatus.Success, duration);
    public static IDisposable Warning(Control owner, string text, int duration = 3000) => Show(owner, text, ModernVisualStatus.Warning, duration);
    public static IDisposable Error(Control owner, string text, int duration = 3000) => Show(owner, text, ModernVisualStatus.Error, duration);

    internal static void Refresh(Form owner)
    {
        if (Hosts.TryGetValue(owner, out var host)) host.Refresh();
    }

    public static IDisposable Show(Control owner, string text, ModernVisualStatus status = ModernVisualStatus.Primary, int duration = 3000)
    {
        ModernCompatibility.ThrowIfNull(owner, nameof(owner));
        var form = owner.FindForm() ?? owner as Form ?? throw new InvalidOperationException("ModernMessage requires a control hosted by a Form.");
        if (!Hosts.TryGetValue(form, out var host))
        {
            host = new MessageHost(form, () => Hosts.Remove(form));
            Hosts.Add(form, host);
        }
        return host.Show(text ?? string.Empty, status, Math.Max(0, duration));
    }

    private sealed class MessageHost : IDisposable
    {
        private readonly Form _owner;
        private readonly Action _onDisposed;
        private readonly List<MessageWindow> _windows = [];
        private readonly ModernFeedbackOwnerSubscription _ownerSubscription;

        public MessageHost(Form owner, Action onDisposed)
        {
            _owner = owner;
            _onDisposed = onDisposed;
            _ownerSubscription = new ModernFeedbackOwnerSubscription(owner, OwnerGeometryChanged, OwnerClosed);
        }

        public IDisposable Show(string text, ModernVisualStatus status, int duration)
        {
            if (ModernUiSettings.MergeDuplicateFeedback && _windows.LastOrDefault(window => window.Matches(text, status)) is { } duplicate)
            {
                duplicate.RestartLifetime(duration);
                Arrange();
                return duplicate;
            }
            var maximumVisible = Math.Max(1, ModernUiSettings.MaximumVisibleMessages);
            while (_windows.Count >= maximumVisible) _windows[0].Dispose();
            var window = new MessageWindow(text, status, ModernThemeResolver.Resolve(_owner), duration, Remove);
            window.ApplyOwnerDpi(_owner.DeviceDpi, MeasureWindowSize(text));
            _windows.Add(window);
            Arrange();
            window.ShowFinalFrame(_owner,
                window.PrepareCompleteHandleTree,
                () =>
                {
                    // Show(owner) is the final PMv2 font/handle boundary on net48. Recommit
                    // typography and geometry before and after it through the shared overlay seam.
                    window.CommitFinalTypography(_owner.DeviceDpi, MeasureWindowSize(text));
                    Arrange();
                });
            return window;
        }

        private Size MeasureWindowSize(string text)
        {
            var dpi = Math.Max(96, _owner.DeviceDpi);
            var scale = dpi / 96f;
            var minimumWidth = (int)Math.Round(200 * scale);
            var ownerWidth = Math.Max(minimumWidth, _owner.ClientSize.Width - (int)Math.Round(36 * scale));
            var maximumWidth = Math.Max(minimumWidth, Math.Min((int)Math.Round(720 * scale), ownerWidth));
            // ModernAlert's physical content seam is Padding.Left + icon + gap on the left and
            // Padding.Right + close slot on the right: 14+18+9 + 14+22 = 77 logical px.
            // The extra 8 px absorbs GDI glyph overhang without changing the painted close seam.
            var chromeWidth = (int)Math.Round(92 * scale);
            var unwrapped = ModernFeedbackTypography.MeasurePhysical(text, FontStyle.Bold, dpi,
                new Size(10000, int.MaxValue), wrap: false);
            var desiredWidth = unwrapped.Width + chromeWidth;
            var width = ModernCompatibility.Clamp(desiredWidth, minimumWidth, maximumWidth);
            var minimumHeight = (int)Math.Round(46 * scale);
            if (desiredWidth <= maximumWidth) return new Size(width, minimumHeight);

            var textAreaWidth = Math.Max(1, width - chromeWidth);
            var measured = ModernFeedbackTypography.MeasurePhysical(text, FontStyle.Bold, dpi,
                new Size(textAreaWidth, int.MaxValue), wrap: true);
            var verticalChrome = (int)Math.Round(24 * scale);
            var ownerHeight = Math.Max(minimumHeight, _owner.ClientSize.Height - (int)Math.Round(36 * scale));
            var maximumHeight = Math.Max(minimumHeight, Math.Min((int)Math.Round(360 * scale), ownerHeight));
            return new Size(width,
                ModernCompatibility.Clamp(measured.Height + verticalChrome, minimumHeight, maximumHeight));
        }

        public void Refresh() => Arrange();

        private void Remove(MessageWindow window)
        {
            _windows.Remove(window);
            Arrange();
            if (_windows.Count == 0 && _owner.IsDisposed) Dispose();
        }

        private void Arrange()
        {
            if (_owner.IsDisposed) return;
            var theme = ModernThemeResolver.Resolve(_owner);
            var ownerBounds = _owner.RectangleToScreen(_owner.ClientRectangle);
            var dpi = Math.Max(96, _owner.DeviceDpi);
            var scale = dpi / 96f;
            var edge = (int)Math.Round(18 * scale);
            var gap = (int)Math.Round(8 * scale);
            var ownerActive = _owner.Visible && _owner.WindowState != FormWindowState.Minimized;
            foreach (var window in _windows)
            {
                window.SetOwnerActive(ownerActive);
                window.ApplyTheme(theme);
                window.ApplyOwnerDpi(dpi, MeasureWindowSize(window.MessageText));
            }
            var requiredHeight = _windows.Sum(window => window.Height) + Math.Max(0, _windows.Count - 1) * gap;
            if (_windows.Count > 1 && requiredHeight > Math.Max(1, ownerBounds.Height - edge * 2))
            {
                _windows[0].Dispose();
                return;
            }
            var top = ownerBounds.Top + edge;
            foreach (var window in _windows)
            {
                window.SetArrangedLocation(new Point(ownerBounds.Left + Math.Max(0, (ownerBounds.Width - window.Width) / 2), top));
                top += window.Height + gap;
            }
        }

        private void OwnerGeometryChanged(object? sender, EventArgs e) => Arrange();
        private void OwnerClosed(object? sender, FormClosedEventArgs e) => Dispose();
        public void Dispose()
        {
            _ownerSubscription.Dispose();
            foreach (var window in _windows.ToArray()) window.Dispose();
            _windows.Clear();
            _onDisposed();
        }
    }

    private sealed class MessageWindow : ModernOwnedOverlayForm
    {
        private readonly FeedbackAutoCloseTimer _lifetime;
        private readonly Action<MessageWindow> _closed;
        private readonly ModernAlert _alert;
        private readonly int _cornerRadius;
        private int _ownerDpi = 96;
        private readonly ModernVisualStatus _status;
        private readonly Font _ownedFont;
        public string MessageText { get; }
        private Point _arrangedLocation;
        private bool _hasArrangedLocation;
        private bool _opening;
        private bool _closing;
        private bool _notified;

        public MessageWindow(string text, ModernVisualStatus status, ModernTheme theme, int duration, Action<MessageWindow> closed)
            : base(compositedSurface: true)
        {
            _closed = closed;
            _cornerRadius = theme.Radius;
            MessageText = text;
            _status = status;
            _lifetime = new FeedbackAutoCloseTimer(Close);
            // Top-level opacity animation is composed independently from the owner and can expose
            // partially painted child text at 96 DPI. Messages appear as one complete card frame.
            Opacity = 1;
            _ownedFont = ModernFeedbackTypography.Create();
            Font = _ownedFont;
            _alert = new ModernAlert { Dock = DockStyle.Fill, Text = text, Status = status, Theme = theme, Closable = true, WrapText = true };
            _alert.Closed += (_, _) => Close();
            _alert.MouseEnter += (_, _) => _lifetime.SetPaused(FeedbackPauseReason.Pointer, true);
            _alert.MouseLeave += (_, _) => { if (!_closing) _lifetime.SetPaused(FeedbackPauseReason.Pointer, false); };
            Controls.Add(_alert);
            RestartLifetime(duration);
        }

        public void ApplyTheme(ModernTheme theme)
        {
            BackColor = ModernStatusColors.ResolveSurface(theme, _status);
            _alert.Theme = theme;
        }

        public bool Matches(string text, ModernVisualStatus status) =>
            MessageText == text && _status == status && !_closing && !IsDisposed;

        public void RestartLifetime(int duration) => _lifetime.Restart(duration);

        public void SetOwnerActive(bool active) =>
            _lifetime.SetPaused(FeedbackPauseReason.OwnerInactive, !active);

        public void ApplyOwnerDpi(int dpi, Size clientSize)
        {
            _ownerDpi = Math.Max(96, dpi);
            ClientSize = clientSize;
            ApplyRoundedRegion();
        }

        public void CommitFinalTypography(int dpi, Size clientSize)
        {
            Font = _ownedFont;
            _alert.Font = _ownedFont;
            ApplyOwnerDpi(dpi, clientSize);
            _alert.Bounds = ClientRectangle;
            PerformLayout();
            _alert.Invalidate();
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
            else Left = location.X;
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (!_hasArrangedLocation) SetArrangedLocation(Location);
            _opening = false;
            Location = _arrangedLocation;
        }
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_closing || e.CloseReason is CloseReason.FormOwnerClosing or CloseReason.ApplicationExitCall) { base.OnFormClosing(e); return; }
            _closing = true;
            base.OnFormClosing(e);
        }
        protected override void OnFormClosed(FormClosedEventArgs e) { base.OnFormClosed(e); NotifyClosed(); }
        protected override void Dispose(bool disposing)
        {
            if (disposing) { _lifetime.Dispose(); NotifyClosed(); }
            base.Dispose(disposing);
            if (disposing) _ownedFont.Dispose();
        }
        private void NotifyClosed() { if (_notified) return; _notified = true; _closed(this); }
    }
}
