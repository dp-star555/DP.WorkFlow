using System.ComponentModel;

namespace ModernUI.WinForms;

public enum ModernDurationUnit { Milliseconds, Seconds, Minutes, Hours, Days }

/// <summary>使用 TimeSpan 语义编辑持续时间，避免把时间点与时长混用。</summary>
[DefaultProperty(nameof(Value))]
[DefaultEvent(nameof(ValueChanged))]
[DefaultBindingProperty(nameof(Value))]
[Description("ModernDurationInput 现代持续时间输入框")]
[DisplayName("现代持续时间输入框")]
[ToolboxBitmap(typeof(ModernDurationInput), "Toolbox.Icons.Inputs.bmp")]
[ToolboxItem(true)]
public sealed class ModernDurationInput : ModernControl, IModernValidationControl
{
    private readonly ModernInputNumber _number = new() { DecimalPlaces = 2, Minimum = 0, Maximum = 1_000_000_000 };
    private readonly ModernSelect _unit = new();
    private TimeSpan _value;
    private TimeSpan _minimum = TimeSpan.Zero;
    private TimeSpan _maximum = TimeSpan.FromDays(3650);
    private bool _updating;
    private bool _readOnly;
    private FinalLayoutTransaction? _layoutTransaction;

    private FinalLayoutTransaction LayoutTransaction =>
        _layoutTransaction ??= new FinalLayoutTransaction(this, LayoutChildren);

    public ModernDurationInput()
    {
        AccessibleRole = AccessibleRole.Grouping;
        Height = 34;
        // 数字输入最小 110、间距 6、单位选择最小 120；组合控件不能声明一个内部布局无法满足的最小宽度。
        MinimumSize = new Size(236, 30);
        UpdateUnitItems(ModernDurationUnit.Seconds);
        Controls.AddRange([_number, _unit]);
        _number.ValueChanged += (_, _) => ReadValue();
        _number.ValueCommitted += (_, _) => ValueCommitted?.Invoke(this, EventArgs.Empty);
        _unit.SelectedIndexChanged += (_, _) => WriteValue();
        _unit.SelectionCommitted += (_, _) => ValueCommitted?.Invoke(this, EventArgs.Empty);
        LayoutChildren();
    }

    [Category("Data"), Bindable(true)]
    [Description("当前编辑的持续时间。")]
    public TimeSpan Value { get => _value; set { var next = value < Minimum ? Minimum : value > Maximum ? Maximum : value; if (_value == next) return; _value = next; WriteValue(); ValueChanged?.Invoke(this, EventArgs.Empty); } }
    [Category("Data")]
    [Description("允许输入的最短持续时间。")]
    public TimeSpan Minimum { get => _minimum; set { if (value < TimeSpan.Zero || value > Maximum) throw new ArgumentOutOfRangeException(nameof(value)); _minimum = value; Value = _value; } }
    [Category("Data")]
    [Description("允许输入的最长持续时间。")]
    public TimeSpan Maximum { get => _maximum; set { if (value < Minimum) throw new ArgumentOutOfRangeException(nameof(value)); _maximum = value; Value = _value; } }
    [Category("Appearance"), DefaultValue(ModernDurationUnit.Seconds)]
    [Description("编辑框显示和输入数值使用的时间单位。")]
    public ModernDurationUnit Unit { get => (ModernDurationUnit)Math.Max(0, _unit.SelectedIndex); set => _unit.SelectedIndex = (int)value; }
    [Category("Behavior"), DefaultValue(false)]
    [Description("是否允许查看和展开单位列表但禁止用户编辑；程序仍可设置 Value 和 Unit。")]
    public bool ReadOnly
    {
        get => _readOnly;
        set { if (_readOnly == value) return; _readOnly = value; _number.ReadOnly = value; _unit.ReadOnly = value; }
    }
    [Category("Appearance"), DefaultValue(ModernValidationState.None)]
    [Description("持续时间输入的统一验证状态。")]
    public ModernValidationState ValidationState { get => _number.ValidationState; set { _number.ValidationState = value; _unit.ValidationState = value; } }
    [Category("Data"), DefaultValue("")]
    [Description("当前验证结果的用户可读说明。")]
    public string ValidationMessage { get => _number.ValidationMessage; set { _number.ValidationMessage = value; _unit.ValidationMessage = value; } }
    public event EventHandler? ValueChanged;
    public event EventHandler? ValueCommitted;

    // 数字和单位边界完全由 LayoutChildren 根据最终父尺寸计算；禁止 WinForms 再次递归缩放。
    protected override bool ScaleChildren => false;
    protected override void RenderContent(GdiCanvas canvas, Rectangle bounds) { }
    protected override void OnThemeChanged() { if (_number is null) return; _number.Theme = Theme; _unit.Theme = Theme; }
    protected override void OnLocalizationChanged()
    {
        base.OnLocalizationChanged();
        if (_unit is null) return;
        UpdateUnitItems(Unit);
    }
    protected override void OnResize(EventArgs e) { base.OnResize(e); LayoutTransaction.Request(); }
    protected override void OnCreateControl()
    {
        base.OnCreateControl();
        ScheduleLayoutChildren();
    }
    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        ScheduleLayoutChildren();
    }
    private void ScheduleLayoutChildren() => LayoutTransaction.Request();
    private void LayoutChildren()
    {
        if (_number is null) return;
        var gap = ScaleLogical(6);
        var minimumUnitWidth = Math.Max(_unit.MinimumSize.Width, ScaleLogical(82));
        var desiredUnitWidth = Math.Max(minimumUnitWidth, ScaleLogical(112));
        var maximumUnitWidth = Math.Max(minimumUnitWidth, Width - _number.MinimumSize.Width - gap);
        var unitWidth = Math.Min(desiredUnitWidth, maximumUnitWidth);
        var numberWidth = Math.Max(0, Width - unitWidth - gap);
        _number.Bounds = new Rectangle(0, 0, numberWidth, Height);
        _unit.Bounds = new Rectangle(numberWidth + gap, 0, Math.Max(0, Width - numberWidth - gap), Height);
    }
    private void UpdateUnitItems(ModernDurationUnit selectedUnit)
    {
        _updating = true;
        try
        {
            _unit.Items.Clear();
            _unit.Items.AddRange([
                FrameworkText(ModernUiTextKeys.DurationMilliseconds),
                FrameworkText(ModernUiTextKeys.DurationSeconds),
                FrameworkText(ModernUiTextKeys.DurationMinutes),
                FrameworkText(ModernUiTextKeys.DurationHours),
                FrameworkText(ModernUiTextKeys.DurationDays)
            ]);
            _unit.SelectedIndex = (int)selectedUnit;
        }
        finally { _updating = false; }
    }

    private void ReadValue()
    {
        if (_updating) return;
        var next = Unit switch
        {
            ModernDurationUnit.Milliseconds => TimeSpan.FromMilliseconds((double)_number.Value),
            ModernDurationUnit.Seconds => TimeSpan.FromSeconds((double)_number.Value),
            ModernDurationUnit.Minutes => TimeSpan.FromMinutes((double)_number.Value),
            ModernDurationUnit.Hours => TimeSpan.FromHours((double)_number.Value),
            _ => TimeSpan.FromDays((double)_number.Value)
        };
        Value = next;
    }
    private void WriteValue()
    {
        if (_updating) return;
        _updating = true;
        try
        {
            var amount = Unit switch
            {
                ModernDurationUnit.Milliseconds => _value.TotalMilliseconds,
                ModernDurationUnit.Seconds => _value.TotalSeconds,
                ModernDurationUnit.Minutes => _value.TotalMinutes,
                ModernDurationUnit.Hours => _value.TotalHours,
                _ => _value.TotalDays
            };
            _number.Value = (decimal)ModernCompatibility.Clamp(amount, 0, 1_000_000_000d);
        }
        finally { _updating = false; }
    }
}
