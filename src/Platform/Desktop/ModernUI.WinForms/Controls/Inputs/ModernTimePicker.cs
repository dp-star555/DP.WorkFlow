using System.ComponentModel;

namespace ModernUI.WinForms;

/// <summary>保留原生时间编辑、键盘和 UIA 行为的现代时间选择器。</summary>
[DefaultProperty(nameof(Value))]
[DefaultEvent(nameof(ValueChanged))]
[DefaultBindingProperty(nameof(Value))]
[Description("ModernTimePicker 现代时间选择器")]
[DisplayName("现代时间选择器")]
[ToolboxBitmap(typeof(ModernTimePicker), "Toolbox.Icons.Inputs.bmp")]
[ToolboxItem(true)]
public sealed class ModernTimePicker : ModernValidatedControl
{
    private readonly ModernNativeDateTimePicker _picker = new()
    {
        Format = DateTimePickerFormat.Custom,
        CustomFormat = "HH:mm:ss",
        ShowUpDown = true
    };
    private readonly ModernTimeSpinnerSurface _spinner = new();
    private bool _readOnly;
    private FinalLayoutTransaction? _layoutTransaction;

    private FinalLayoutTransaction LayoutTransaction =>
        _layoutTransaction ??= new FinalLayoutTransaction(this, LayoutPicker);

    public ModernTimePicker()
    {
        AccessibleRole = AccessibleRole.SpinButton;
        Height = 34;
        MinimumSize = new Size(110, 30);
        Padding = new Padding(7, 4, 7, 4);
        Controls.AddRange([_picker, _spinner]);
        _spinner.StepRequested += increase =>
        {
            if (ReadOnly) return;
            _picker.StepSelectedPart(increase);
        };
        _picker.ValueChanged += (_, _) => ValueChanged?.Invoke(this, EventArgs.Empty);
        _picker.KeyUp += (_, e) =>
        {
            if (!ReadOnly && e.KeyCode is Keys.Up or Keys.Down or Keys.PageUp or Keys.PageDown)
                ValueCommitted?.Invoke(this, EventArgs.Empty);
        };
        _picker.HandleCreated += (_, _) => NativeControlTheme.ApplyDateTimePicker(_picker, Theme.IsDark);
        LayoutPicker();
        OnThemeChanged();
    }

    [Category("Data"), Bindable(true)]
    [Description("一天内当前选择的时间。")]
    public TimeSpan Value
    {
        get => _picker.Value.TimeOfDay;
        set
        {
            if (value < TimeSpan.Zero || value >= TimeSpan.FromDays(1)) throw new ArgumentOutOfRangeException(nameof(value), "Time must be within one day.");
            var date = _picker.Value.Date;
            var candidate = date + value;
            _picker.Value = candidate < _picker.MinDate ? _picker.MinDate : candidate > _picker.MaxDate ? _picker.MaxDate : candidate;
        }
    }
    [Category("Behavior"), DefaultValue(false)]
    [Description("是否允许查看和聚焦但禁止用户调整时间；程序仍可设置 Value。")]
    public bool ReadOnly
    {
        get => _readOnly;
        set { if (_readOnly == value) return; _readOnly = value; _picker.ReadOnly = value; _spinner.Enabled = !value; Cursor = value ? Cursors.Default : Cursors.Hand; Invalidate(); }
    }

    [Category("Appearance"), DefaultValue("HH:mm:ss")]
    [Description("时间的原生自定义显示格式。")]
    public string CustomFormat { get => _picker.CustomFormat ?? "HH:mm:ss"; set => _picker.CustomFormat = string.IsNullOrWhiteSpace(value) ? "HH:mm:ss" : value; }
    public event EventHandler? ValueChanged;
    public event EventHandler? ValueCommitted;
    [Browsable(false)] public DateTimePicker InnerPicker => _picker;

    // 原生时间子 HWND 的边界由 LayoutPicker 使用最终父尺寸和 PreferredHeight 计算。
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
        var fallback = ContainsFocus ? Theme.Primary : IsHovered ? Theme.PrimaryHover : Theme.Border;
        canvas.Draw(ResolveValidationBorder(fallback), ContainsFocus || ModernValidation.IsEmphasized(ValidationState) ? ScaleLogical(1.5f) : ScaleLogical(1),
            rect, ScaleLogical(Theme.Radius), centerStroke: true);
    }
    protected override void OnThemeChanged() { if (_picker is null) return; _picker.ModernTheme = Theme; _spinner.Theme = Theme; NativeControlTheme.ApplyDateTimePicker(_picker, Theme.IsDark); _picker.Invalidate(); _spinner.Invalidate(); Invalidate(); }
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
    private void ScheduleLayoutPicker() => LayoutTransaction.Request();
    private void LayoutPicker()
    {
        if (_picker is null || _spinner is null) return;
        _picker.Bounds = new Rectangle(Padding.Left, Math.Max(Padding.Top, (Height - _picker.PreferredHeight) / 2),
            Math.Max(0, Width - Padding.Horizontal), _picker.PreferredHeight);
        var spinnerWidth = Math.Min(ScaleLogical(26), Math.Max(ScaleLogical(22), Width / 5));
        _spinner.Bounds = new Rectangle(Math.Max(Padding.Left, Width - Padding.Right - spinnerWidth),
            ScaleLogical(2), spinnerWidth, Math.Max(0, Height - ScaleLogical(4)));
        _spinner.BringToFront();
    }
}

internal sealed class ModernTimeSpinnerSurface : Control
{
    private int _hoveredHalf = -1;
    private int _pressedHalf = -1;

    public ModernTimeSpinnerSurface()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        TabStop = false;
        AccessibleRole = AccessibleRole.SpinButton;
        AccessibleName = "调整时间";
    }

    public ModernTheme Theme { get; set; } = ModernTheme.Light;
    public event Action<bool>? StepRequested;

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Theme.Control);
        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        var half = Height / 2;
        if (_hoveredHalf >= 0)
        {
            using var hover = new SolidBrush(_pressedHalf == _hoveredHalf ? Theme.PrimaryBackground : Theme.ControlHover);
            e.Graphics.FillRectangle(hover, 0, _hoveredHalf == 0 ? 0 : half,
                Width, _hoveredHalf == 0 ? half : Height - half);
        }
        using var separator = new Pen(Theme.BorderSecondary);
        e.Graphics.DrawLine(separator, 0, half, Width, half);
        using var canvas = new GdiCanvas(e.Graphics);
        var iconWidth = Math.Min(8, Math.Max(6, Width - 10));
        var left = (Width - iconWidth) / 2f;
        canvas.DrawIcon(ModernIconKind.ChevronUp, Enabled ? Theme.TextSecondary : Theme.TextDisabled,
            new RectangleF(left, half / 2f - 3, iconWidth, 6), 1);
        canvas.DrawIcon(ModernIconKind.ChevronDown, Enabled ? Theme.TextSecondary : Theme.TextDisabled,
            new RectangleF(left, half + (Height - half) / 2f - 3, iconWidth, 6), 1);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var half = e.Y < Height / 2 ? 0 : 1;
        if (_hoveredHalf == half) return;
        _hoveredHalf = half;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hoveredHalf = -1;
        _pressedHalf = -1;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left || !Enabled) return;
        _pressedHalf = e.Y < Height / 2 ? 0 : 1;
        Capture = true;
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != MouseButtons.Left) return;
        var releasedHalf = e.Y < Height / 2 ? 0 : 1;
        var invoke = Enabled && _pressedHalf == releasedHalf;
        _pressedHalf = -1;
        Capture = false;
        Invalidate();
        if (invoke) StepRequested?.Invoke(releasedHalf == 0);
    }
}
