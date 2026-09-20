using System.ComponentModel;

namespace ModernUI.WinForms;

/// <summary>带平滑状态过渡的现代开关。</summary>
[DefaultProperty(nameof(Checked))]
[DefaultEvent(nameof(CheckedChanged))]
[DefaultBindingProperty(nameof(Checked))]
[Description("ModernSwitch 现代开关")]
[DisplayName("现代开关")]
[ToolboxBitmap(typeof(ModernSwitch), "Toolbox.Icons.Selection.bmp")]
[ToolboxItem(true)]
public sealed class ModernSwitch : ModernControl
{
    private bool _checked;
    private bool _readOnly;
    private float _position;
    private IDisposable? _animation;

    public ModernSwitch()
    {
        AccessibleRole = AccessibleRole.CheckButton;
        Size = new Size(58, 28);
        Cursor = Cursors.Hand;
    }

    /// <summary>获取或设置是否绘制键盘焦点外圈。</summary>
    [Category("Appearance"), DefaultValue(true)]
    [Description("控件获得键盘焦点时是否显示焦点外圈。")]
    public bool ShowFocusBorder { get; set; } = true;

    [Category("Behavior"), DefaultValue(false)]
    [Description("是否允许查看和聚焦但禁止用户切换；程序仍可设置 Checked。")]
    public bool ReadOnly
    {
        get => _readOnly;
        set { if (_readOnly == value) return; _readOnly = value; Cursor = value ? Cursors.Default : Cursors.Hand; Invalidate(); }
    }

    [Category("Behavior"), DefaultValue(false), Bindable(true)]
    [Description("开关是否处于打开状态。")]
    public bool Checked
    {
        get => _checked;
        set
        {
            if (_checked == value) return;
            _checked = value;
            if (IsHandleCreated) AccessibilityNotifyClients(AccessibleEvents.StateChange, -1);
            _animation?.Dispose();
            _animation = ModernAnimation.Start(this, _position, value ? 1 : 0, Theme.AnimationDuration, progress => { _position = progress; Invalidate(); });
            CheckedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    [Description("开关的 Checked 状态发生更改时引发。")]
    public event EventHandler? CheckedChanged;

    [Description("用户通过鼠标或键盘提交 Checked 状态时引发；程序赋值不会引发。")]
    public event EventHandler? CheckedCommitted;

    protected override AccessibleObject CreateAccessibilityInstance() => new SwitchAccessibleObject(this);

    protected override void RenderContent(GdiCanvas canvas, Rectangle bounds)
    {
        var focusPadding = ScaleLogical(3);
        var height = Math.Min(Math.Max(0, bounds.Height - focusPadding * 2), ScaleLogical(24));
        var width = Math.Min(Math.Max(0, bounds.Width - focusPadding * 2), ScaleLogical(46));
        var rect = new RectangleF(focusPadding, (bounds.Height - height) / 2f, width, height);
        var off = Theme.TextDisabled;
        var on = IsHovered ? Theme.PrimaryHover : Theme.Primary;
        canvas.Fill(Geometry.Blend(off, on, _position), rect, height / 2);
        var gap = ScaleLogical(3);
        var diameter = height - gap * 2;
        var x = rect.X + gap + (rect.Width - height) * _position;
        canvas.FillEllipse(Color.White, new RectangleF(x, rect.Y + gap, diameter, diameter));
        if (!Enabled) canvas.Fill(Color.FromArgb(105, Theme.Background), rect, height / 2);
        if (ShowFocusBorder && ShouldShowFocusCue)
        {
            var focus = RectangleF.Inflate(rect, ScaleLogical(2), ScaleLogical(2));
            var edge = ScaleLogical(1);
            focus.Intersect(new RectangleF(edge, edge, Math.Max(0, bounds.Width - edge * 2), Math.Max(0, bounds.Height - edge * 2)));
            ModernFocusVisual.Draw(canvas, Theme, focus, focus.Height / 2, DpiScale);
        }
    }

    protected override void OnClick(EventArgs e)
    {
        if (Enabled && !ReadOnly)
        {
            Checked = !Checked;
            CheckedCommitted?.Invoke(this, EventArgs.Empty);
        }
        base.OnClick(e);
    }
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData != Keys.Space || !Enabled || ReadOnly) return base.ProcessCmdKey(ref msg, keyData);
        OnClick(EventArgs.Empty);
        return true;
    }
    protected override void Dispose(bool disposing) { if (disposing) _animation?.Dispose(); base.Dispose(disposing); }

    private sealed class SwitchAccessibleObject(ModernSwitch owner) : ControlAccessibleObject(owner)
    {
        public override AccessibleStates State => owner.Checked ? base.State | AccessibleStates.Checked : base.State;
    }
}
