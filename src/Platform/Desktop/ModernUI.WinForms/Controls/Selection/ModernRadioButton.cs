using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;

namespace ModernUI.WinForms;

/// <summary>支持分组互斥、键盘导航、无障碍状态和选中动画的现代单选按钮。</summary>
[DefaultProperty(nameof(Text))]
[DefaultEvent(nameof(CheckedChanged))]
[Description("ModernRadioButton 现代单选按钮")]
[DisplayName("现代单选框")]
[ToolboxBitmap(typeof(ModernRadioButton), "Toolbox.Icons.Selection.bmp")]
[ToolboxItem(true)]
public sealed class ModernRadioButton : ModernControl
{
    private bool _checked;
    private float _checkProgress;
    private IDisposable? _animation;
    private string _groupName = string.Empty;

    /// <summary>初始化单选按钮的默认尺寸和交互语义。</summary>
    public ModernRadioButton()
    {
        AccessibleRole = AccessibleRole.RadioButton;
        Height = 30;
        MinimumSize = new Size(24, 24);
        Text = "Radio";
        Cursor = Cursors.Hand;
    }

    /// <summary>获取或设置单选按钮右侧显示的文本。</summary>
    [Category("Appearance"), DefaultValue("Radio")]
    [Description("单选按钮右侧显示的文本内容。")]
    [AllowNull]
    public override string Text { get => base.Text; set => base.Text = value; }

    /// <summary>获取或设置当前单选按钮是否选中。</summary>
    [Category("Behavior"), DefaultValue(false)]
    [Description("当前单选按钮是否处于选中状态。")]
    public bool Checked
    {
        get => _checked;
        set
        {
            if (_checked == value) return;
            if (value) UncheckGroupPeers();
            SetCheckedCore(value);
        }
    }

    /// <summary>获取或设置父容器内的逻辑分组名称；空名称的按钮属于默认组。</summary>
    [Category("Behavior"), DefaultValue("")]
    [Description("同一父容器内具有相同 GroupName 的按钮互斥。")]
    public string GroupName
    {
        get => _groupName;
        set
        {
            value ??= string.Empty;
            if (_groupName == value) return;
            _groupName = value;
            if (Checked) UncheckGroupPeers();
            UpdateGroupTabStops();
        }
    }

    /// <summary>获取或设置用户点击或按键时是否自动选中按钮。</summary>
    [Category("Behavior"), DefaultValue(true)]
    [Description("用户激活按钮时是否自动将 Checked 设置为 true。")]
    public bool AutoCheck { get; set; } = true;

    /// <summary>获取或设置选中状态动画时长；设置为 0 可关闭动画。</summary>
    [Category("Behavior"), DefaultValue(160)]
    [Description("选中状态切换动画的持续时间（毫秒）。")]
    public int AnimationDuration { get; set; } = 160;

    /// <summary>Checked 属性发生变化时发生。</summary>
    public event EventHandler? CheckedChanged;

    protected override AccessibleObject CreateAccessibilityInstance() => new RadioAccessibleObject(this);

    protected override void OnParentChanged(EventArgs e)
    {
        base.OnParentChanged(e);
        if (Checked) UncheckGroupPeers();
        UpdateGroupTabStops();
    }

    protected override void RenderContent(GdiCanvas canvas, Rectangle bounds)
    {
        var size = Math.Min(ScaleLogical(18), Math.Max(ScaleLogical(14), bounds.Height - ScaleLogical(10)));
        var circle = new RectangleF(ScaleLogical(2), (bounds.Height - size) / 2f, size, size);
        var border = Enabled
            ? Geometry.Blend(IsHovered ? Theme.Primary : Theme.Border, Theme.Primary, _checkProgress)
            : Theme.BorderSecondary;
        canvas.FillEllipse(border, circle);
        var ringInset = Math.Max(ScaleLogical(1.5f), size * .09f);
        var inner = RectangleF.Inflate(circle, -ringInset, -ringInset);
        canvas.FillEllipse(Enabled ? Theme.Control : Theme.Background, inner);
        if (_checkProgress > .01f)
        {
            var dotSize = inner.Width * .52f * ModernCompatibility.Clamp(_checkProgress, 0, 1);
            var dot = new RectangleF(inner.Left + (inner.Width - dotSize) / 2f,
                inner.Top + (inner.Height - dotSize) / 2f, dotSize, dotSize);
            canvas.FillEllipse(Enabled ? Theme.Primary : Theme.TextDisabled, dot);
        }

        canvas.DrawText(Text, Font, Enabled ? Theme.Text : Theme.TextDisabled,
            new Rectangle((int)circle.Right + ScaleLogical(9), 0,
                Math.Max(0, bounds.Width - (int)circle.Right - ScaleLogical(11)), bounds.Height),
            ContentAlignment.MiddleLeft);
        if (ShouldShowFocusCue)
        {
            var focusBounds = RectangleF.Inflate(bounds, -ScaleLogical(1.5f), -ScaleLogical(1.5f));
            ModernFocusVisual.Draw(canvas, Theme, focusBounds, ScaleLogical(Theme.Radius), DpiScale);
        }
    }

    protected override void OnClick(EventArgs e)
    {
        if (Enabled && AutoCheck) Checked = !Checked;
        base.OnClick(e);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData is Keys.Space or Keys.Enter)
        {
            OnClick(EventArgs.Empty);
            return true;
        }
        if (keyData is Keys.Left or Keys.Up)
            return MoveSelection(-1);
        if (keyData is Keys.Right or Keys.Down)
            return MoveSelection(1);
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private bool MoveSelection(int delta)
    {
        var group = GetGroupMembers().Where(item => item.Enabled && item.Visible).ToArray();
        if (group.Length == 0) return false;
        var index = Array.IndexOf(group, this);
        if (index < 0) return false;
        var target = group[(index + delta + group.Length) % group.Length];
        target.Checked = true;
        target.Focus();
        return true;
    }

    private IEnumerable<ModernRadioButton> GetGroupMembers()
    {
        if (Parent is null) return [this];
        return Parent.Controls.OfType<ModernRadioButton>()
            .Where(item => string.Equals(item.GroupName, GroupName, StringComparison.Ordinal))
            .OrderBy(item => item.TabIndex)
            .ThenBy(item => Parent.Controls.GetChildIndex(item));
    }

    private void UncheckGroupPeers()
    {
        foreach (var peer in GetGroupMembers())
            if (!ReferenceEquals(peer, this) && peer.Checked) peer.SetCheckedCore(false);
    }

    private void SetCheckedCore(bool value)
    {
        if (_checked == value) return;
        _checked = value;
        TabStop = value;
        AnimateCheck(value ? 1 : 0);
        if (IsHandleCreated) AccessibilityNotifyClients(AccessibleEvents.StateChange, -1);
        CheckedChanged?.Invoke(this, EventArgs.Empty);
        UpdateGroupTabStops();
    }

    private void UpdateGroupTabStops()
    {
        var group = GetGroupMembers().ToArray();
        var selected = group.FirstOrDefault(item => item.Checked);
        if (selected is not null)
        {
            foreach (var item in group) item.TabStop = ReferenceEquals(item, selected);
            return;
        }
        if (group.Length > 0)
        {
            foreach (var item in group) item.TabStop = false;
            group[0].TabStop = true;
        }
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

    private sealed class RadioAccessibleObject(ModernRadioButton owner) : ControlAccessibleObject(owner)
    {
        public override AccessibleStates State => base.State |
            (owner.Checked ? AccessibleStates.Checked : AccessibleStates.None);
        public override string? DefaultAction => owner.Checked ? "取消选择" : "选择";

        public override void DoDefaultAction()
        {
            if (owner.Enabled && owner.AutoCheck) owner.Checked = !owner.Checked;
        }
    }
}
