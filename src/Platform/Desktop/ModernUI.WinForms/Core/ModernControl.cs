using System.ComponentModel;
using System.Globalization;
using ModernUI.Localization;

namespace ModernUI.WinForms;

/// <summary>为现代控件提供主题、DPI、交互状态和固定绘制管线。</summary>
[ToolboxItem(false)]
public abstract class ModernControl : UserControl
{
    private ModernTheme _theme = ModernUiSettings.DefaultTheme;
    private bool _hovered;
    private bool _pressed;
    private ILocalizationContext _localizationContext = ModernUiLocalization.DefaultContext;
    private int _lastDeviceDpi = 96;
    private SizeF _logicalSizeBeforeDpi;
    private Padding _logicalMarginBeforeDpi;
    private bool _dpiBoundsCaptured;

    protected ModernControl()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                 ControlStyles.SupportsTransparentBackColor, true);
        // 可复用 UserControl 继承宿主的缩放周期；内部固定几何由 DeviceDpi 布局，避免嵌套控件重复缩放。
        AutoScaleMode = AutoScaleMode.Inherit;
        BackColor = Color.Transparent;
        TabStop = true;
    }

    /// <summary>获取或设置控件主题。</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public virtual ModernTheme Theme
    {
        get => _theme;
        set
        {
            _theme = value ?? throw new ArgumentNullException(nameof(value));
            OnThemeChanged();
            Invalidate(true);
        }
    }

    /// <summary>获取或设置控件用于框架内部文本和格式化的本地化上下文。</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public virtual ILocalizationContext LocalizationContext
    {
        get => _localizationContext;
        set
        {
            ModernCompatibility.ThrowIfNull(value, nameof(value));
            if (ReferenceEquals(_localizationContext, value)) return;
            _localizationContext.Changed -= LocalizationContextChanged;
            _localizationContext = value;
            _localizationContext.Changed += LocalizationContextChanged;
            foreach (Control child in Controls)
                if (child is ModernControl modern) modern.LocalizationContext = value;
            OnLocalizationChanged();
        }
    }

    protected CultureInfo Culture => LocalizationContext.Current.Culture;
    protected string FrameworkText(TextKey key, IReadOnlyDictionary<string, object?>? arguments = null) =>
        LocalizationContext.Text(key, arguments);

    protected bool IsHovered => _hovered;
    protected bool IsPressed => _pressed;
    protected bool IsFocused => Focused || ContainsFocus;
    protected bool ShouldShowFocusCue => IsFocused && ShowFocusCues;
    protected float DpiScale => DeviceDpi / 96f;
    protected int ScaleLogical(int value) => ModernDpi.ScaleToInt(value, DeviceDpi);
    protected float ScaleLogical(float value) => value * DpiScale;

    protected virtual void OnThemeChanged() { }
    protected virtual void OnLocalizationChanged()
    {
        RightToLeft = LocalizationContext.Current.TextDirection == TextDirection.RightToLeft ? RightToLeft.Yes : RightToLeft.No;
        // 每个现代子控件都订阅同一快照；仅失效自身，避免父子控件递归失效形成 O(n²) 绘制风暴。
        Invalidate();
    }

    private void LocalizationContextChanged(object? sender, LocaleChangedEventArgs e)
    {
        if (IsDisposed) return;
        if (InvokeRequired) { BeginInvoke(OnLocalizationChanged); return; }
        OnLocalizationChanged();
    }

    /// <summary>鼠标进入或离开控件（包括其子控件）时发生。</summary>
    protected virtual void OnHoverChanged() => Invalidate();

    protected sealed override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var canvas = new GdiCanvas(e.Graphics);
        RenderBackground(canvas, ClientRectangle);
        RenderContent(canvas, ClientRectangle);
        RenderOverlay(canvas, ClientRectangle);
    }

    protected virtual void RenderBackground(GdiCanvas canvas, Rectangle bounds) { }
    protected abstract void RenderContent(GdiCanvas canvas, Rectangle bounds);
    protected virtual void RenderOverlay(GdiCanvas canvas, Rectangle bounds) { }

    protected override void OnControlAdded(ControlEventArgs e)
    {
        base.OnControlAdded(e);
        if (e.Control is { } child)
        {
            TrackChildHover(child);
            if (child is ModernControl modern) modern.LocalizationContext = LocalizationContext;
        }
    }

    protected override void OnMouseEnter(EventArgs e) { SetHovered(true); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { RefreshHoverAfterMouseTransition(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) _pressed = true; Focus(); Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _pressed = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        _lastDeviceDpi = Math.Max(1, DeviceDpi);
    }

    protected override void OnDpiChangedBeforeParent(EventArgs e)
    {
        var oldDpi = Math.Max(1, _lastDeviceDpi);
        _logicalSizeBeforeDpi = new SizeF(Width * 96f / oldDpi, Height * 96f / oldDpi);
        _logicalMarginBeforeDpi = new Padding(
            (int)Math.Round(Margin.Left * 96f / oldDpi),
            (int)Math.Round(Margin.Top * 96f / oldDpi),
            (int)Math.Round(Margin.Right * 96f / oldDpi),
            (int)Math.Round(Margin.Bottom * 96f / oldDpi));
        _dpiBoundsCaptured = true;
        base.OnDpiChangedBeforeParent(e);
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        var newDpi = Math.Max(1, DeviceDpi);
        if (!_dpiBoundsCaptured)
        {
            _lastDeviceDpi = newDpi;
            Invalidate();
            return;
        }
        if (!AutoSize && Dock == DockStyle.None && !_logicalSizeBeforeDpi.IsEmpty)
            Size = new Size(
                Math.Max(1, (int)Math.Round(_logicalSizeBeforeDpi.Width * newDpi / 96f)),
                Math.Max(1, (int)Math.Round(_logicalSizeBeforeDpi.Height * newDpi / 96f)));
        Margin = new Padding(
            (int)Math.Round(_logicalMarginBeforeDpi.Left * newDpi / 96f),
            (int)Math.Round(_logicalMarginBeforeDpi.Top * newDpi / 96f),
            (int)Math.Round(_logicalMarginBeforeDpi.Right * newDpi / 96f),
            (int)Math.Round(_logicalMarginBeforeDpi.Bottom * newDpi / 96f));
        _lastDeviceDpi = newDpi;
        _dpiBoundsCaptured = false;
        Parent?.PerformLayout(this, nameof(Bounds));
        Parent?.Parent?.PerformLayout(Parent, nameof(Bounds));
        Invalidate();
    }

    private void TrackChildHover(Control child)
    {
        child.MouseEnter += (_, _) => SetHovered(true);
        child.MouseLeave += (_, _) => RefreshHoverAfterMouseTransition();
        child.ControlAdded += (_, e) =>
        {
            if (e.Control is { } descendant) TrackChildHover(descendant);
        };
        foreach (Control descendant in child.Controls) TrackChildHover(descendant);
    }

    private void RefreshHoverAfterMouseTransition()
    {
        if (!IsHandleCreated)
        {
            SetHovered(false);
            return;
        }
        BeginInvoke(() =>
        {
            if (IsDisposed) return;
            var inside = ClientRectangle.Contains(PointToClient(Cursor.Position));
            SetHovered(inside);
            if (!inside) _pressed = false;
        });
    }

    private void SetHovered(bool value)
    {
        if (_hovered == value) return;
        _hovered = value;
        OnHoverChanged();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _localizationContext.Changed -= LocalizationContextChanged;
        base.Dispose(disposing);
    }
}
