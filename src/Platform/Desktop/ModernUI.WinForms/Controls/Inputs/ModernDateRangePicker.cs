using System.ComponentModel;

namespace ModernUI.WinForms;

/// <summary>由两个原生日期选择器组合而成的现代日期范围输入框。</summary>
[DefaultEvent(nameof(RangeChanged))]
[Description("ModernDateRangePicker 现代日期范围选择器")]
[DisplayName("现代日期范围选择器")]
[ToolboxBitmap(typeof(ModernDateRangePicker), "Toolbox.Icons.Inputs.bmp")]
[ToolboxItem(true)]
public sealed class ModernDateRangePicker : ModernControl, IModernValidationControl
{
    private readonly ModernDatePicker _start = new();
    private readonly ModernDatePicker _end = new();
    private readonly Label _separator = new() { Text = "—", TextAlign = ContentAlignment.MiddleCenter, Width = 22 };
    private bool _updating;
    private bool _readOnly;
    private FinalLayoutTransaction? _layoutTransaction;

    private FinalLayoutTransaction LayoutTransaction =>
        _layoutTransaction ??= new FinalLayoutTransaction(this, LayoutChildren);

    public ModernDateRangePicker()
    {
        AccessibleRole = AccessibleRole.Grouping;
        Height = 34;
        MinimumSize = new Size(280, 30);
        Controls.AddRange([_start, _separator, _end]);
        _start.RangeProvider = CurrentRange;
        _end.RangeProvider = CurrentRange;
        _start.ValueChanged += (_, _) =>
        {
            Normalize(true);
            StartDateChanged?.Invoke(this, EventArgs.Empty);
        };
        _end.ValueChanged += (_, _) =>
        {
            Normalize(false);
            EndDateChanged?.Invoke(this, EventArgs.Empty);
        };
        _start.ValueCommitted += (_, _) => RangeCommitted?.Invoke(this, EventArgs.Empty);
        _end.ValueCommitted += (_, _) => RangeCommitted?.Invoke(this, EventArgs.Empty);
        UpdateAccessibleNames();
        LayoutChildren();
    }

    [Category("Data"), Bindable(true), Description("范围的起始日期。")]
    public DateTime? StartDate { get => _start.Value; set => _start.Value = value; }
    [Category("Data"), Bindable(true), Description("范围的结束日期。")]
    public DateTime? EndDate { get => _end.Value; set => _end.Value = value; }
    [Category("Behavior"), DefaultValue(false), Description("是否允许查看日历但禁止用户改变范围；程序仍可设置日期。")]
    public bool ReadOnly
    {
        get => _readOnly;
        set { if (_readOnly == value) return; _readOnly = value; _start.ReadOnly = value; _end.ReadOnly = value; }
    }
    [Category("Appearance"), DefaultValue(ModernValidationState.None), Description("日期范围的统一验证状态。")]
    public ModernValidationState ValidationState { get => _start.ValidationState; set { _start.ValidationState = value; _end.ValidationState = value; } }
    [Category("Data"), DefaultValue(""), Description("当前验证结果的用户可读说明。")]
    public string ValidationMessage { get => _start.ValidationMessage; set { _start.ValidationMessage = value; _end.ValidationMessage = value; } }
    public event EventHandler? StartDateChanged;
    public event EventHandler? EndDateChanged;
    public event EventHandler? RangeChanged;
    public event EventHandler? RangeCommitted;

    // 子日期控件的边界完全由 LayoutChildren 根据最终父尺寸计算；禁止 WinForms 再次递归缩放。
    protected override bool ScaleChildren => false;
    protected override void RenderContent(GdiCanvas canvas, Rectangle bounds) { }
    protected override AccessibleObject CreateAccessibilityInstance() => new DateRangeAccessibleObject(this);
    protected override void OnLocalizationChanged()
    {
        base.OnLocalizationChanged();
        UpdateAccessibleNames();
    }
    protected override void OnThemeChanged()
    {
        if (_start is null) return;
        _start.Theme = Theme;
        _end.Theme = Theme;
        _separator.BackColor = Theme.Container;
        _separator.ForeColor = Theme.TextSecondary;
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
        if (_start is null) return;
        var separatorWidth = 26;
        var pickerWidth = Math.Max(0, (Width - separatorWidth) / 2);
        _start.Bounds = new Rectangle(0, 0, pickerWidth, Height);
        _separator.Bounds = new Rectangle(pickerWidth, 0, separatorWidth, Height);
        _end.Bounds = new Rectangle(pickerWidth + separatorWidth, 0, Math.Max(0, Width - pickerWidth - separatorWidth), Height);
    }
    private void UpdateAccessibleNames()
    {
        if (_start is null) return;
        _start.AccessibleName = FrameworkText(ModernUiTextKeys.StartDate);
        _end.AccessibleName = FrameworkText(ModernUiTextKeys.EndDate);
        // DatePicker delegates UIA to the retained native provider. If that provider has already
        // been created, update it together with the outer semantic name during locale changes.
        _start.InnerPicker.AccessibleName = _start.AccessibleName;
        _end.InnerPicker.AccessibleName = _end.AccessibleName;
    }

    private sealed class DateRangeAccessibleObject(ModernDateRangePicker owner) : ControlAccessibleObject(owner)
    {
        public override AccessibleRole Role => AccessibleRole.Grouping;
        public override int GetChildCount() => 2;
        public override AccessibleObject? GetChild(int index) => index switch
        {
            0 => owner._start.AccessibilityObject,
            1 => owner._end.AccessibilityObject,
            _ => null
        };
    }

    private (DateTime? Start, DateTime? End) CurrentRange() => (_start.Value, _end.Value);

    private void Normalize(bool startChanged)
    {
        if (_updating) return;
        _updating = true;
        try
        {
            if (_start.Value is { } start && _end.Value is { } end && start > end)
            {
                if (startChanged) _end.Value = start;
                else _start.Value = end;
            }
            // Keep both retained managed Calendar surfaces on the normalized endpoints. This also
            // prevents the next opening frame from briefly painting the value that was valid before
            // the opposite endpoint forced range normalization.
            _start.SynchronizeCalendarSelection();
            _end.SynchronizeCalendarSelection();
        }
        finally { _updating = false; }
        RangeChanged?.Invoke(this, EventArgs.Empty);
    }
}
