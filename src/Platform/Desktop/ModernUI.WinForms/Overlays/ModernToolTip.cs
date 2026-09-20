using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace ModernUI.WinForms;

public enum ModernToolTipPlacement { Top, Bottom, Left, Right }
public enum ModernToolTipKind { Default, Error }

/// <summary>可在设计器中为控件设置提示文本，也可用于显示即时校验气泡。</summary>
[ProvideProperty("ToolTipText", typeof(Control))]
[Description("ModernToolTip 现代气泡提示组件")]
[DisplayName("现代工具提示")]
[ToolboxBitmap(typeof(ModernToolTip), "Toolbox.Icons.Feedback.bmp")]
[ToolboxItem(true)]
public sealed partial class ModernToolTip : Component, IExtenderProvider
{
    [ThreadStatic]
    private static ModernToolTip? _active;
    private readonly Dictionary<Control, string> _texts = [];
    private readonly System.Windows.Forms.Timer _showTimer = new();
    private readonly System.Windows.Forms.Timer _hideTimer = new();
    private BubbleForm? _popup;
    private string _popupText = string.Empty;
    private ModernToolTipKind _popupKind;
    private Control? _pendingControl;
    private Control? _anchor;
    private readonly ModernOverlayAnchorTracker _anchorTracker;

    public ModernToolTip()
    {
        _anchorTracker = new ModernOverlayAnchorTracker(AnchorGeometryChanged);
        _showTimer.Tick += (_, _) => ShowPending();
        _hideTimer.Tick += (_, _) => Hide();
    }

    public ModernToolTip(IContainer container) : this() => container.Add(this);

    [Category("Behavior"), DefaultValue(500)]
    [Description("鼠标悬停后显示气泡前等待的毫秒数。")]
    public int InitialDelay { get; set; } = 500;

    [Category("Behavior"), DefaultValue(5000)]
    [Description("普通提示气泡自动关闭前保持的毫秒数；设置为 0 表示不自动关闭。")]
    public int AutoPopDelay { get; set; } = 5000;

    [Category("Appearance"), DefaultValue(ModernToolTipPlacement.Top)]
    [Description("气泡相对于目标控件的首选显示位置，空间不足时会自动翻转。")]
    public ModernToolTipPlacement Placement { get; set; } = ModernToolTipPlacement.Top;

    [Category("Appearance"), DefaultValue(280)]
    [Description("气泡文本区域允许使用的最大宽度。")]
    public int MaximumWidth { get; set; } = 280;

    [Category("Appearance"), DefaultValue(6)]
    [Description("气泡主体的圆角半径。")]
    public int Radius { get; set; } = 6;

    [Category("Appearance"), DefaultValue(8)]
    [Description("气泡指向目标控件的箭头尺寸。")]
    public int ArrowSize { get; set; } = 8;

    [Category("Appearance")]
    [Description("普通气泡的背景颜色。")]
    public Color BubbleBackColor { get; set; } = ModernTheme.Light.OverlaySurface;

    [Category("Appearance")]
    [Description("普通气泡的文字颜色。")]
    public Color BubbleForeColor { get; set; } = ModernTheme.Light.ToolTipText;

    public bool CanExtend(object extendee) => extendee is Control and not Form;

    [DefaultValue("")]
    [Description("鼠标悬停在目标控件上时显示的气泡提示文本。")]
    public string GetToolTipText(Control control) => _texts.GetValueOrDefault(control, string.Empty);

    public void SetToolTipText(Control control, string? text)
    {
        ModernCompatibility.ThrowIfNull(control, nameof(control));
        Unhook(control);
        if (string.IsNullOrWhiteSpace(text))
        {
            _texts.Remove(control);
            return;
        }
        _texts[control] = text!;
        control.MouseEnter += Control_MouseEnter;
        control.MouseLeave += Control_MouseLeave;
        control.Disposed += Control_Disposed;
    }

    /// <summary>立即在指定控件附近显示气泡。</summary>
    public void Show(Control control, string text, ModernToolTipKind kind = ModernToolTipKind.Default, int? duration = null)
    {
        ModernCompatibility.ThrowIfNull(control, nameof(control));
        if (control.IsDisposed || !control.IsHandleCreated || string.IsNullOrWhiteSpace(text)) return;
        if (_popup is { Visible: true } existing && ReferenceEquals(_anchor, control) &&
            string.Equals(_popupText, text, StringComparison.Ordinal) && _popupKind == kind)
        {
            _active = this;
            existing.CommitFinalFrame(control);
            RestartHideTimer(duration ?? AutoPopDelay);
            return;
        }
        var previous = _active;
        _active = this;
        if (previous is not null && !ReferenceEquals(previous, this)) previous.Hide();
        HideCore(clearActive: false);
        var isError = kind == ModernToolTipKind.Error;
        var theme = ModernThemeResolver.Resolve(control);
        var back = isError ? theme.ErrorSurface : BubbleBackColor;
        var fore = isError ? theme.Error : BubbleForeColor;
        var border = isError ? Geometry.Blend(theme.ErrorSurface, theme.Error, .45f) : theme.OverlayBorder;
        var dpiScale = control.DeviceDpi / 96f;
        _popup = new BubbleForm(text, control.Font, back, fore, border,
            Math.Max(ModernDpi.ScaleToInt(80, control.DeviceDpi), ModernDpi.ScaleToInt(MaximumWidth, control.DeviceDpi)),
            Math.Max(0, ModernDpi.ScaleToInt(Radius, control.DeviceDpi)),
            Math.Max(ModernDpi.ScaleToInt(4, control.DeviceDpi), ModernDpi.ScaleToInt(ArrowSize, control.DeviceDpi)),
            Placement, dpiScale);
        _anchor = control;
        _popupText = text;
        _popupKind = kind;
        _popup.Place(control);
        _popup.ShowFinalFrame(control.FindForm(),
            () => _popup.PrepareHiddenFrame(control),
            () => _popup.CommitFinalFrame(control));
        _anchorTracker.Track(control);
        RestartHideTimer(duration ?? AutoPopDelay);
    }

    public void Hide() => HideCore(clearActive: true);

    private void HideCore(bool clearActive)
    {
        if (clearActive && ReferenceEquals(_active, this)) _active = null;
        _showTimer.Stop();
        _hideTimer.Stop();
        _pendingControl = null;
        _anchorTracker.Untrack();
        _anchor = null;
        _popupText = string.Empty;
        if (_popup is null) return;
        _popup.Close();
        _popup.Dispose();
        _popup = null;
    }

    private void RestartHideTimer(int delay)
    {
        _hideTimer.Stop();
        if (delay <= 0) return;
        _hideTimer.Interval = Math.Max(1, delay);
        _hideTimer.Start();
    }

    private void Control_MouseEnter(object? sender, EventArgs e)
    {
        if (sender is not Control control || !_texts.ContainsKey(control)) return;
        _pendingControl = control;
        _showTimer.Interval = Math.Max(1, InitialDelay);
        _showTimer.Start();
    }

    private void Control_MouseLeave(object? sender, EventArgs e)
    {
        if (ReferenceEquals(_pendingControl, sender)) _showTimer.Stop();
        if (_popup is null || sender is not Control control) return;
        // Moving toward an overlapping tooltip briefly emits MouseLeave before the top-level
        // no-activate window receives pointer tracking. Defer one message turn and keep the same
        // frame while the pointer is inside either the anchor or bubble.
        control.BeginInvoke((Action)(() =>
        {
            if (_popup is null || control.IsDisposed) return;
            if (control.RectangleToScreen(control.ClientRectangle).Contains(Cursor.Position) ||
                _popup.Bounds.Contains(Cursor.Position)) return;
            Hide();
        }));
    }

    private void ShowPending()
    {
        _showTimer.Stop();
        if (_pendingControl is not { IsDisposed: false } control || !_texts.TryGetValue(control, out var text)) return;
        Show(control, text);
    }

    private void Control_Disposed(object? sender, EventArgs e)
    {
        if (sender is not Control control) return;
        if (ReferenceEquals(control, _anchor)) Hide();
        Unhook(control);
        _texts.Remove(control);
    }

    private void AnchorGeometryChanged(Control? anchor, Rectangle bounds)
    {
        if (anchor is not { IsDisposed: false, Visible: true } || bounds.IsEmpty || _popup is null)
        {
            Hide();
            return;
        }
        // Tooltips follow moving anchors; selection popups deliberately close instead. Both paths
        // share the same hierarchy tracking and cleanup contract.
        _popup.Place(anchor);
    }

    private void Unhook(Control control)
    {
        control.MouseEnter -= Control_MouseEnter;
        control.MouseLeave -= Control_MouseLeave;
        control.Disposed -= Control_Disposed;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Hide();
            foreach (var control in _texts.Keys.ToArray()) Unhook(control);
            _texts.Clear();
            _showTimer.Dispose();
            _hideTimer.Dispose();
            _anchorTracker.Dispose();
        }
        base.Dispose(disposing);
    }
}
