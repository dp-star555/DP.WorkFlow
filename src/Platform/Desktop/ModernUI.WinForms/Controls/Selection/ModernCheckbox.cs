using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;

namespace ModernUI.WinForms;

/// <summary>支持三态、主题和选中动画的现代复选框。</summary>
[DefaultProperty(nameof(Text))]
[DefaultEvent(nameof(CheckedChanged))]
[Description("ModernCheckbox 现代复选框")]
[DisplayName("现代复选框")]
[ToolboxBitmap(typeof(ModernCheckbox), "Toolbox.Icons.Selection.bmp")]
[ToolboxItem(true)]
public sealed class ModernCheckbox : ModernControl
{
    private CheckState _checkState;
    private float _checkProgress;
    private IDisposable? _animation;

    public ModernCheckbox()
    {
        AccessibleRole = AccessibleRole.CheckButton;
        Height = 30;
        MinimumSize = new Size(24, 24);
        Text = "Checkbox";
        Cursor = Cursors.Hand;
    }

    [Category("Appearance"), DefaultValue("Checkbox")]
    [Description("复选框右侧显示的文本内容。")]
    [AllowNull]
    public override string Text { get => base.Text; set => base.Text = value; }

    [Category("Behavior"), DefaultValue(false)]
    [Description("复选框是否处于选中状态；在三态模式下 Indeterminate 也视为已选中。")]
    public bool Checked
    {
        get => _checkState != CheckState.Unchecked;
        set => CheckState = value ? CheckState.Checked : CheckState.Unchecked;
    }

    [Category("Behavior"), DefaultValue(CheckState.Unchecked)]
    [Description("复选框当前状态：未选中、选中或不确定。")]
    public CheckState CheckState
    {
        get => _checkState;
        set
        {
            if (_checkState == value) return;
            var oldChecked = Checked;
            _checkState = value;
            AnimateCheck(value == CheckState.Unchecked ? 0 : 1);
            if (IsHandleCreated) AccessibilityNotifyClients(AccessibleEvents.StateChange, -1);
            CheckStateChanged?.Invoke(this, EventArgs.Empty);
            if (oldChecked != Checked) CheckedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    [Category("Behavior"), DefaultValue(false)]
    [Description("是否允许使用 Indeterminate 不确定状态。")]
    public bool ThreeState { get; set; }

    /// <summary>选中状态动画时长（毫秒），设为 0 可关闭动画。</summary>
    [Category("Behavior"), DefaultValue(160)]
    [Description("选中状态切换动画的持续时间（毫秒）；设置为 0 可关闭动画。")]
    public int AnimationDuration { get; set; } = 160;

    [Description("Checked 属性发生更改时引发。")]
    public event EventHandler? CheckedChanged;

    [Description("CheckState 属性发生更改时引发。")]
    public event EventHandler? CheckStateChanged;

    protected override AccessibleObject CreateAccessibilityInstance() => new CheckAccessibleObject(this);

    protected override void RenderContent(GdiCanvas canvas, Rectangle bounds)
    {
        var size = Math.Min(ScaleLogical(18), Math.Max(ScaleLogical(14), bounds.Height - ScaleLogical(10)));
        var box = new RectangleF(ScaleLogical(2), (bounds.Height - size) / 2f, size, size);
        ModernCheckboxRenderer.Draw(canvas, box, Theme, _checkProgress, _checkState == CheckState.Indeterminate,
            IsHovered, Enabled, DpiScale);
        canvas.DrawText(Text, Font, Enabled ? Theme.Text : Theme.TextDisabled,
            new Rectangle((int)box.Right + ScaleLogical(9), 0,
                Math.Max(0, bounds.Width - (int)box.Right - ScaleLogical(11)), bounds.Height),
            ContentAlignment.MiddleLeft);
        if (ShouldShowFocusCue)
        {
            var focusBounds = RectangleF.Inflate(bounds, -ScaleLogical(1.5f), -ScaleLogical(1.5f));
            ModernFocusVisual.Draw(canvas, Theme, focusBounds,
                ScaleLogical(Theme.Radius), DpiScale);
        }
    }

    protected override void OnClick(EventArgs e)
    {
        if (Enabled)
        {
            CheckState = ThreeState
                ? CheckState switch
                {
                    CheckState.Unchecked => CheckState.Checked,
                    CheckState.Checked => CheckState.Indeterminate,
                    _ => CheckState.Unchecked
                }
                : Checked ? CheckState.Unchecked : CheckState.Checked;
        }
        base.OnClick(e);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData is Keys.Space or Keys.Enter)
        {
            OnClick(EventArgs.Empty);
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void AnimateCheck(float target)
    {
        _animation?.Dispose();
        _animation = ModernAnimation.Start(this, _checkProgress, target, Math.Max(0, AnimationDuration),
            value =>
            {
                _checkProgress = value;
                Invalidate();
            }, () => _animation = null);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _animation?.Dispose();
        base.Dispose(disposing);
    }

    private sealed class CheckAccessibleObject(ModernCheckbox owner) : ControlAccessibleObject(owner)
    {
        public override AccessibleStates State => owner.CheckState switch
        {
            CheckState.Checked => base.State | AccessibleStates.Checked,
            CheckState.Indeterminate => base.State | AccessibleStates.Mixed,
            _ => base.State
        };
    }
}

internal static class ModernCheckboxRenderer
{
    public static void Draw(GdiCanvas canvas, RectangleF box, ModernTheme theme, float progress,
        bool indeterminate, bool hovered, bool enabled, float dpiScale)
    {
        progress = ModernCompatibility.Clamp(progress, 0, 1);
        var fill = enabled ? Geometry.Blend(theme.Elevated, theme.Primary, progress) : theme.Control;
        var border = enabled
            ? Geometry.Blend(hovered ? theme.Primary : theme.Border, theme.Primary, progress)
            : theme.BorderSecondary;
        canvas.Fill(fill, box, 4 * dpiScale);
        canvas.Draw(border, 1.2f * dpiScale, box, 4 * dpiScale);
        if (progress <= .01f) return;

        var markColor = Color.FromArgb((int)(255 * progress), Color.White);
        if (indeterminate)
        {
            var inset = box.Width * .27f;
            canvas.DrawLines(markColor, 2.2f * dpiScale,
            [
                new PointF(box.Left + inset, box.Top + box.Height / 2),
                new PointF(box.Right - inset, box.Top + box.Height / 2)
            ]);
            return;
        }

        var start = new PointF(box.Left + box.Width * .22f, box.Top + box.Height * .52f);
        var middle = new PointF(box.Left + box.Width * .43f, box.Top + box.Height * .72f);
        var end = new PointF(box.Left + box.Width * .79f, box.Top + box.Height * .31f);
        if (progress < .45f)
        {
            var local = progress / .45f;
            canvas.DrawLines(markColor, 2.2f * dpiScale,
                [start, new PointF(start.X + (middle.X - start.X) * local, start.Y + (middle.Y - start.Y) * local)]);
        }
        else
        {
            var local = (progress - .45f) / .55f;
            canvas.DrawLines(markColor, 2.2f * dpiScale,
            [
                start,
                middle,
                new PointF(middle.X + (end.X - middle.X) * local, middle.Y + (end.Y - middle.Y) * local)
            ]);
        }
    }
}
