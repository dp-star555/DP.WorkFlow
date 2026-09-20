using System.ComponentModel;
using System.Globalization;

namespace ModernUI.WinForms;

/// <summary>保留原生日历、键盘和 UIA 行为的现代日期选择器。</summary>
[DefaultProperty(nameof(Value))]
[DefaultEvent(nameof(ValueChanged))]
[DefaultBindingProperty(nameof(Value))]
[Description("ModernDatePicker 现代日期选择器")]
[DisplayName("现代日期选择器")]
[ToolboxBitmap(typeof(ModernDatePicker), "Toolbox.Icons.Inputs.bmp")]
[ToolboxItem(true)]
public sealed class ModernDatePicker : ModernValidatedControl
{
    private readonly ModernNativeDateTimePicker _picker = new() { Format = DateTimePickerFormat.Custom, CustomFormat = "yyyy-MM-dd", CalendarFont = SystemFonts.MessageBoxFont };
    private readonly ModernCalendarSurface _calendar = new();
    private readonly ModernPopupController _dropDown;
    private DateTime _lastAcceptedDate = DateTime.Today;
    private bool _reverting;
    private bool _readOnly;
    private Func<DateTime, bool>? _dateEnabledPredicate;
    private Func<(DateTime? Start, DateTime? End)>? _rangeProvider;
    private FinalLayoutTransaction? _layoutTransaction;

    private FinalLayoutTransaction LayoutTransaction =>
        _layoutTransaction ??= new FinalLayoutTransaction(this, LayoutPicker);

    public ModernDatePicker()
    {
        AccessibleRole = AccessibleRole.DropList;
        Height = 34;
        MinimumSize = new Size(130, 30);
        Padding = new Padding(7, 4, 7, 4);
        _dropDown = new ModernPopupController(_calendar);
        Controls.Add(_picker);
        _lastAcceptedDate = _picker.Value.Date;
        _picker.ValueChanged += (_, _) => AcceptValue();
        _picker.KeyUp += (_, e) =>
        {
            if (!ReadOnly && e.KeyCode is Keys.Up or Keys.Down or Keys.PageUp or Keys.PageDown or Keys.Home or Keys.End)
                ValueCommitted?.Invoke(this, EventArgs.Empty);
        };
        _picker.DropDown += (_, _) => Invalidate();
        _picker.CloseUp += (_, _) => Invalidate();
        _picker.ManagedDropDownRequested = ShowCalendar;
        _picker.NativePointerDown += PickerNativePointerDown;
        _picker.ManagedKeyHandler = key =>
            _dropDown.Visible ? _calendar.HandleKey(key) : key == Keys.Escape;
        _calendar.DateSelected += date =>
        {
            if (!ReadOnly)
            {
                // Commit the value while the complete popup frame still covers the owner. Closing first
                // exposes an intermediate owner frame containing the old text.
                Value = date;
                ValueCommitted?.Invoke(this, EventArgs.Empty);
            }
            _picker.Invalidate();
            Invalidate();
            _dropDown.Close();
        };
        _calendar.CloseRequested += (_, _) => _dropDown.Close();
        _dropDown.Closed += (_, _) =>
        {
            RestoreFocusAfterPopupClose();
            Invalidate();
        };
        _picker.HandleCreated += (_, _) => NativeControlTheme.ApplyDateTimePicker(_picker, Theme.IsDark);
        LayoutPicker();
        OnThemeChanged();
    }

    [Category("Data"), Bindable(true)]
    [Description("当前选择的日期；null 表示未选择。")]
    public DateTime? Value
    {
        get => _picker.ShowCheckBox && !_picker.Checked ? null : _picker.Value.Date;
        set { _picker.ShowCheckBox = value is null; _picker.Checked = value is not null; if (value is { } date) _picker.Value = date < MinimumDate ? MinimumDate : date > MaximumDate ? MaximumDate : date; Invalidate(); }
    }
    [Category("Data")]
    [Description("允许选择的最早日期。")]
    public DateTime MinimumDate { get => _picker.MinDate; set { _picker.MinDate = value; _calendar.MinimumDate = value.Date; } }
    [Category("Data")]
    [Description("允许选择的最晚日期。")]
    public DateTime MaximumDate { get => _picker.MaxDate; set { _picker.MaxDate = value; _calendar.MaximumDate = value.Date; } }
    /// <summary>可选的日期启用规则；返回 false 的日期会被拒绝。</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Func<DateTime, bool>? DateEnabledPredicate
    {
        get => _dateEnabledPredicate;
        set { _dateEnabledPredicate = value; _calendar.DateEnabledPredicate = value; _calendar.Invalidate(); }
    }

    [Category("Behavior"), DefaultValue(false)]
    [Description("是否允许查看日历但禁止用户改变日期；程序仍可设置 Value。")]
    public bool ReadOnly
    {
        get => _readOnly;
        set { if (_readOnly == value) return; _readOnly = value; _picker.ReadOnly = value; Cursor = value ? Cursors.Default : Cursors.Hand; Invalidate(); }
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool DroppedDown { get => _dropDown.Visible; set { if (value) ShowCalendar(); else _dropDown.Close(); } }

    [Category("Appearance"), DefaultValue("yyyy-MM-dd")]
    [Description("日期的原生自定义显示格式。")]
    public string CustomFormat { get => _picker.CustomFormat ?? "yyyy-MM-dd"; set => _picker.CustomFormat = string.IsNullOrWhiteSpace(value) ? "yyyy-MM-dd" : value; }
    public event EventHandler? ValueChanged;
    public event EventHandler? ValueCommitted;
    [Browsable(false)] public DateTimePicker InnerPicker => _picker;

    internal Func<(DateTime? Start, DateTime? End)>? RangeProvider
    {
        get => _rangeProvider;
        set => _rangeProvider = value;
    }

    internal void SynchronizeCalendarSelection()
    {
        (DateTime? Start, DateTime? End) range = _rangeProvider?.Invoke() ?? (null, null);
        _calendar.SynchronizeSelection(Value ?? _lastAcceptedDate, range.Start, range.End);
    }

    private void AcceptValue()
    {
        if (_reverting) return;
        var candidate = _picker.Value.Date;
        if (DateEnabledPredicate is not null && !DateEnabledPredicate(candidate))
        {
            _reverting = true;
            try { _picker.Value = _lastAcceptedDate; }
            finally { _reverting = false; }
            ValidationState = ModernValidationState.Error;
            ValidationMessage = FrameworkText(ModernUiTextKeys.DateUnavailable);
            return;
        }
        _lastAcceptedDate = candidate;
        _calendar.SelectedDate = candidate;
        ValueChanged?.Invoke(this, EventArgs.Empty);
        Invalidate();
    }

    protected override void OnLocalizationChanged()
    {
        base.OnLocalizationChanged();
        if (_calendar is null) return;
        _calendar.Culture = Culture;
        _calendar.RightToLeft = RightToLeft;
        _calendar.SelectDateAction = FrameworkText(ModernUiTextKeys.SelectDate);
        _calendar.Invalidate();
    }

    // 原生日期子 HWND 的边界由 LayoutPicker 使用最终父尺寸和 PreferredHeight 计算。
    protected override bool ScaleChildren => false;
    protected override AccessibleObject CreateAccessibilityInstance()
    {
        _picker.AccessibleName = AccessibleName;
        _picker.AccessibleDescription = AccessibleDescription;
        return _picker.AccessibilityObject!;
    }
    protected override void SynchronizeValidationAccessibility()
    {
        base.SynchronizeValidationAccessibility();
        if (_picker is not null) _picker.AccessibleDescription = AccessibleDescription;
    }
    protected override void RenderContent(GdiCanvas canvas, Rectangle bounds)
    {
        var rect = RectangleF.Inflate(bounds, -ScaleLogical(1), -ScaleLogical(1));
        canvas.Fill(Theme.Control, rect, ScaleLogical(Theme.Radius));
        var fallback = _dropDown.Visible || ContainsFocus ? Theme.Primary : IsHovered ? Theme.PrimaryHover : Theme.Border;
        canvas.Draw(ResolveValidationBorder(fallback), ContainsFocus || ModernValidation.IsEmphasized(ValidationState) ? ScaleLogical(1.5f) : ScaleLogical(1),
            rect, ScaleLogical(Theme.Radius), centerStroke: true);
    }
    protected override void OnThemeChanged() { if (_picker is null) return; _picker.ModernTheme = Theme; _calendar.Theme = Theme; if (_dropDown is not null) _dropDown.ApplyTheme(Theme); _picker.CalendarForeColor = Theme.Text; _picker.CalendarMonthBackground = Theme.Control; NativeControlTheme.ApplyDateTimePicker(_picker, Theme.IsDark); _picker.Invalidate(); _calendar.Invalidate(); Invalidate(); }
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Left) ShowCalendar();
    }
    protected override void OnResize(EventArgs e) { base.OnResize(e); LayoutTransaction.Request(); }
    protected override void OnPaddingChanged(EventArgs e) { base.OnPaddingChanged(e); LayoutTransaction.Request(); }
    protected override void OnCreateControl()
    {
        base.OnCreateControl();
        ScheduleLayoutPicker();
    }
    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        ScheduleLayoutPicker();
    }
    private void RestoreFocusAfterPopupClose()
    {
        if (!IsHandleCreated || IsDisposed) return;
        BeginInvoke((Action)(() =>
        {
            if (IsDisposed || _dropDown.Visible) return;
            var focused = FindForm()?.ActiveControl;
            // Closing one endpoint while another endpoint is being clicked must not steal focus
            // back to the old native editor. Restore only when focus stayed inside this picker or
            // fell back to the owner Form after a keyboard/date selection close.
            if (focused is null || ReferenceEquals(focused, this) || Contains(focused)) _picker.Focus();
        }));
    }

    private void PickerNativePointerDown(object? sender, EventArgs e) => ShowCalendar();
    private void ScheduleLayoutPicker() => LayoutTransaction.Request();
    private void LayoutPicker() { if (_picker is not null) _picker.Bounds = new Rectangle(Padding.Left, Math.Max(Padding.Top, (Height - _picker.PreferredHeight) / 2), Math.Max(0, Width - Padding.Horizontal), _picker.PreferredHeight); }
    private void ShowCalendar()
    {
        if (!Enabled || _dropDown.Visible) return;
        _calendar.Culture = CultureInfo.GetCultureInfo(Culture.Name);
        _calendar.RightToLeft = RightToLeft;
        _calendar.SelectDateAction = FrameworkText(ModernUiTextKeys.SelectDate);
        _calendar.MinimumDate = MinimumDate.Date;
        _calendar.MaximumDate = MaximumDate.Date;
        _calendar.DateEnabledPredicate = DateEnabledPredicate;
        SynchronizeCalendarSelection();
        _dropDown.Show(this, new Size(Math.Max(280, ScaleLogical(300)), ScaleLogical(286)), ScaleLogical(2), 140);
        Invalidate();
    }
    protected override void Dispose(bool disposing) { if (disposing) { _picker.NativePointerDown -= PickerNativePointerDown; _picker.ManagedDropDownRequested = null; _picker.ManagedKeyHandler = null; _dropDown.Dispose(); } base.Dispose(disposing); }
}
