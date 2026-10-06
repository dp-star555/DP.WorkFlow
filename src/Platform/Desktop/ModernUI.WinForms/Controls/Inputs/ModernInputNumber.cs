using System.ComponentModel;
using System.Globalization;

namespace ModernUI.WinForms;

/// <summary>Specifies how programmatic values outside a numeric input range are handled.</summary>
public enum ModernRangeValueBehavior
{
    Clamp,
    Reject
}

/// <summary>Provides a validated decimal editor with native text input, stepping and explicit user commit semantics.</summary>
[DefaultProperty(nameof(Value))]
[DefaultEvent(nameof(ValueChanged))]
[DefaultBindingProperty(nameof(Value))]
[Description("ModernInputNumber 现代数字输入框")]
[DisplayName("现代数值输入框")]
[ToolboxBitmap(typeof(ModernInputNumber), "Toolbox.Icons.Inputs.bmp")]
[ToolboxItem(true)]
public sealed class ModernInputNumber : ModernControl, IModernValidationControl
{
    private readonly ModernInput _input = new();
    private readonly ModernButton _decrease = new() { Text = string.Empty, AccessibleName = "减少", Icon = ModernIconKind.Minus, ButtonType = ModernButtonType.Text, StrongHoverFeedback = true, TabStop = false };
    private readonly ModernButton _increase = new() { Text = string.Empty, AccessibleName = "增加", Icon = ModernIconKind.Plus, ButtonType = ModernButtonType.Text, StrongHoverFeedback = true, TabStop = false };
    private readonly ModernToolTip _validationToolTip = new() { Placement = ModernToolTipPlacement.Top };
    private readonly System.Windows.Forms.Timer _repeatTimer = new();
    private decimal _value;
    private bool _hasValue = true;
    private decimal _minimum = -1_000_000_000m;
    private decimal _maximum = 1_000_000_000m;
    private decimal _increment = 1m;
    private int _decimalPlaces = 2;
    private bool _showStepButtons = true;
    private bool _readOnly;
    private bool _allowNull;
    private bool _thousandsSeparator;
    private string _formatString = string.Empty;
    private string _prefixText = string.Empty;
    private string _suffixText = string.Empty;
    private ModernRangeValueBehavior _rangeValueBehavior;
    private bool _repeatIncrease;
    private bool _repeatStarted;
    private bool _suppressNextClick;
    private bool _hasCommittedValue;
    private decimal? _lastCommittedValue;
    private FinalLayoutTransaction? _layoutTransaction;

    private FinalLayoutTransaction LayoutTransaction =>
        _layoutTransaction ??= new FinalLayoutTransaction(this, LayoutChildren);

    public ModernInputNumber()
    {
        AccessibleRole = AccessibleRole.SpinButton;
        Height = 34;
        MinimumSize = new Size(110, 30);
        Controls.AddRange([_input, _decrease, _increase]);
        ConfigureStepButton(_decrease, increase: false);
        ConfigureStepButton(_increase, increase: true);
        _repeatTimer.Interval = 450;
        _repeatTimer.Tick += (_, _) => RepeatStep();
        _input.TextChanged += Input_TextChanged;
        _input.InnerTextBox.Validated += (_, _) => ParseInput(commit: true);
        _input.InnerTextBox.KeyDown += (_, e) =>
        {
            if (ReadOnly) return;
            if (e.KeyCode is Keys.Up or Keys.Down && e.Modifiers == Keys.None)
            {
                StepBy(e.KeyCode == Keys.Up);
                RaiseCommitted();
                e.SuppressKeyPress = true;
                e.Handled = true;
                return;
            }
            if (e.KeyCode != Keys.Enter || e.Modifiers != Keys.None) return;
            ParseInput(commit: true);
            e.SuppressKeyPress = true;
            e.Handled = true;
        };
        LayoutChildren();
        SynchronizeText();
    }

    [Category("Behavior"), DefaultValue(typeof(decimal), "-1000000000")]
    [Description("允许输入的最小数值。")]
    public decimal Minimum
    {
        get => _minimum;
        set
        {
            if (value > Maximum) throw new ArgumentOutOfRangeException(nameof(value), value, "Minimum cannot exceed Maximum.");
            if (_minimum == value) return;
            _minimum = value;
            NormalizeCurrentAfterRangeChange();
        }
    }

    [Category("Behavior"), DefaultValue(typeof(decimal), "1000000000")]
    [Description("允许输入的最大数值。")]
    public decimal Maximum
    {
        get => _maximum;
        set
        {
            if (value < Minimum) throw new ArgumentOutOfRangeException(nameof(value), value, "Maximum cannot be less than Minimum.");
            if (_maximum == value) return;
            _maximum = value;
            NormalizeCurrentAfterRangeChange();
        }
    }

    [Category("Behavior"), DefaultValue(typeof(decimal), "1")]
    [Description("增减按钮和方向键每次改变的数值。")]
    public decimal Increment
    {
        get => _increment;
        set
        {
            if (value <= 0) throw new ArgumentOutOfRangeException(nameof(value), value, "Increment must be greater than zero.");
            _increment = value;
        }
    }

    [Category("Appearance"), DefaultValue(2)]
    [Description("默认数值格式保留的小数位数。")]
    public int DecimalPlaces
    {
        get => _decimalPlaces;
        set
        {
            if (value is < 0 or > 28) throw new ArgumentOutOfRangeException(nameof(value), value, "DecimalPlaces must be between 0 and 28.");
            if (_decimalPlaces == value) return;
            _decimalPlaces = value;
            SynchronizeText();
        }
    }

    [Category("Appearance"), DefaultValue(true)]
    [Description("是否显示数值输入框右侧的增减按钮；隐藏后仍可直接输入并使用上下方向键。")]
    public bool ShowStepButtons
    {
        get => _showStepButtons;
        set
        {
            if (_showStepButtons == value) return;
            _showStepButtons = value;
            _decrease.Visible = value;
            _increase.Visible = value;
            LayoutTransaction.Request();
        }
    }

    [Category("Behavior"), DefaultValue(false)]
    [Description("是否允许查看和复制但禁止用户编辑或步进；程序仍可设置值。")]
    public bool ReadOnly
    {
        get => _readOnly;
        set
        {
            if (_readOnly == value) return;
            _readOnly = value;
            _input.ReadOnly = value;
            StopRepeat(commit: false);
            UpdateStepButtons();
        }
    }

    [Category("Behavior"), DefaultValue(false)]
    [Description("是否允许 NullableValue 为 null 并以空文本显示。")]
    public bool AllowNull
    {
        get => _allowNull;
        set
        {
            if (_allowNull == value) return;
            _allowNull = value;
            if (!value && !_hasValue) SetValueCore(ModernCompatibility.Clamp(0m, Minimum, Maximum), hasValue: true);
        }
    }

    [Category("Behavior"), DefaultValue(false)]
    [Description("输入过程中只要文本能解析为范围内的数值就立即更新 Value 并引发 ValueChanged；回车或离开输入框时再格式化文本。")]
    public bool CommitWhileTyping { get; set; }

    [Category("Appearance"), DefaultValue("")]
    [Description("可选的标准或自定义 Decimal 格式字符串；空值使用 DecimalPlaces。")]
    public string FormatString
    {
        get => _formatString;
        set
        {
            value ??= string.Empty;
            if (_formatString == value) return;
            if (value.Length > 0) _ = 0m.ToString(value, CultureInfo.InvariantCulture);
            _formatString = value;
            SynchronizeText();
        }
    }

    [Category("Appearance"), DefaultValue(false)]
    [Description("默认格式是否使用当前区域性的千分位分隔符。")]
    public bool ThousandsSeparator
    {
        get => _thousandsSeparator;
        set { if (_thousandsSeparator == value) return; _thousandsSeparator = value; SynchronizeText(); }
    }

    [Category("Appearance"), DefaultValue("")]
    [Description("显示在格式化数值之前的固定文本。")]
    public string PrefixText
    {
        get => _prefixText;
        set { value ??= string.Empty; if (_prefixText == value) return; _prefixText = value; SynchronizeText(); }
    }

    [Category("Appearance"), DefaultValue("")]
    [Description("显示在格式化数值之后的固定文本。")]
    public string SuffixText
    {
        get => _suffixText;
        set { value ??= string.Empty; if (_suffixText == value) return; _suffixText = value; SynchronizeText(); }
    }

    [Category("Behavior"), DefaultValue(ModernRangeValueBehavior.Clamp)]
    [Description("程序设置超出范围的值时进行钳制或抛出异常。")]
    public ModernRangeValueBehavior RangeValueBehavior
    {
        get => _rangeValueBehavior;
        set => _rangeValueBehavior = value;
    }

    [Category("Appearance"), DefaultValue(ModernValidationState.None)]
    [Description("数值输入的统一验证状态。")]
    public ModernValidationState ValidationState { get => _input.ValidationState; set => _input.ValidationState = value; }

    [Category("Data"), DefaultValue("")]
    [Description("当前验证结果的用户可读说明。")]
    public string ValidationMessage { get => _input.ValidationMessage; set => _input.ValidationMessage = value; }

    [Category("Behavior"), DefaultValue(true)]
    [Description("输入无法转换或超出范围时是否显示错误气泡。")]
    public bool ShowValidationToolTip { get; set; } = true;

    [Category("Behavior"), DefaultValue(3000)]
    [Description("输入错误气泡自动关闭前显示的毫秒数；零表示保持到值有效。")]
    public int ValidationToolTipDuration { get; set; } = 3000;

    [Category("Data"), DefaultValue(typeof(decimal), "0"), Bindable(true)]
    [Description("当前非空数值；AllowNull 状态请使用 NullableValue 判断空值。")]
    public decimal Value
    {
        get => _value;
        set => SetValueCore(NormalizeIncoming(value), hasValue: true);
    }

    [Category("Data"), DefaultValue(typeof(decimal), "0"), Bindable(true)]
    [Description("当前可空数值；仅当 AllowNull 为 true 时允许设置 null。")]
    public decimal? NullableValue
    {
        get => _hasValue ? _value : null;
        set
        {
            if (value is null)
            {
                if (!AllowNull) throw new ArgumentNullException(nameof(value), "Null requires AllowNull=true.");
                SetValueCore(_value, hasValue: false);
                return;
            }
            SetValueCore(NormalizeIncoming(value.Value), hasValue: true);
        }
    }

    public event EventHandler? ValueChanged;
    public event EventHandler? ValueCommitted;

    public override ModernTheme Theme
    {
        get => base.Theme;
        set { base.Theme = value; _input.Theme = value; _decrease.Theme = value; _increase.Theme = value; }
    }

    protected override void OnLocalizationChanged()
    {
        base.OnLocalizationChanged();
        if (_input is null) return;
        _decrease.AccessibleName = FrameworkText(ModernUiTextKeys.NumberDecrease);
        _increase.AccessibleName = FrameworkText(ModernUiTextKeys.NumberIncrease);
        SynchronizeText();
    }

    protected override void RenderContent(GdiCanvas canvas, Rectangle bounds) { }
    protected override void OnResize(EventArgs e) { base.OnResize(e); LayoutTransaction.Request(); }
    protected override void OnLayout(LayoutEventArgs e) { base.OnLayout(e); LayoutTransaction.Request(); }
    protected override void OnDpiChangedAfterParent(EventArgs e) { base.OnDpiChangedAfterParent(e); LayoutTransaction.Request(); }
    protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); StopRepeat(commit: false); UpdateStepButtons(); }

    private void ConfigureStepButton(ModernButton button, bool increase)
    {
        button.Click += (_, _) =>
        {
            if (_suppressNextClick)
            {
                _suppressNextClick = false;
                return;
            }
            if (ReadOnly) return;
            StepBy(increase);
            RaiseCommitted();
        };
        button.MouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Left || ReadOnly || !button.Enabled) return;
            _repeatIncrease = increase;
            _repeatStarted = false;
            _repeatTimer.Interval = 450;
            _repeatTimer.Start();
        };
        button.MouseUp += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) StopRepeat(commit: _repeatStarted);
        };
        button.MouseCaptureChanged += (_, _) =>
        {
            if (!button.Capture) StopRepeat(commit: _repeatStarted);
        };
    }

    private void RepeatStep()
    {
        _repeatStarted = true;
        _repeatTimer.Interval = Math.Max(45, _repeatTimer.Interval - 65);
        StepBy(_repeatIncrease);
    }

    private void StopRepeat(bool commit)
    {
        if (!_repeatTimer.Enabled && !_repeatStarted) return;
        _repeatTimer.Stop();
        if (_repeatStarted)
        {
            _suppressNextClick = true;
            if (commit) RaiseCommitted();
        }
        _repeatStarted = false;
    }

    private void StepBy(bool increase)
    {
        if (ReadOnly) return;
        var current = _hasValue ? _value : ModernCompatibility.Clamp(0m, Minimum, Maximum);
        try { Value = increase ? current + Increment : current - Increment; }
        catch (OverflowException) { SetValueCore(increase ? Maximum : Minimum, hasValue: true); }
        catch (ArgumentOutOfRangeException) { SetValueCore(increase ? Maximum : Minimum, hasValue: true); }
    }

    private decimal NormalizeIncoming(decimal value)
    {
        if (value >= Minimum && value <= Maximum) return value;
        if (RangeValueBehavior == ModernRangeValueBehavior.Reject)
            throw new ArgumentOutOfRangeException(nameof(value), value, $"Value must be between {Minimum} and {Maximum}.");
        return ModernCompatibility.Clamp(value, Minimum, Maximum);
    }

    private void NormalizeCurrentAfterRangeChange()
    {
        if (_hasValue) SetValueCore(ModernCompatibility.Clamp(_value, Minimum, Maximum), hasValue: true);
        else UpdateStepButtons();
    }

    private void SetValueCore(decimal value, bool hasValue)
    {
        var changed = _hasValue != hasValue || hasValue && _value != value;
        _value = value;
        _hasValue = hasValue;
        SynchronizeText();
        UpdateStepButtons();
        ClearValidationError();
        if (changed) ValueChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ParseInput(bool commit)
    {
        if (ReadOnly) return;
        if (TryGetInputValue(out var value, out var hasValue))
        {
            SetValueCore(value, hasValue);
            if (commit) { SynchronizeText(force: true); RaiseCommitted(); }
            return;
        }

        var message = FrameworkText(ModernUiTextKeys.NumberValidationRange,
            new Dictionary<string, object?> { ["minimum"] = Minimum, ["maximum"] = Maximum });
        _input.ValidationMessage = message;
        _input.HasError = true;
        Invalidate();
        if (ShowValidationToolTip)
            _validationToolTip.Show(this, message, ModernToolTipKind.Error, Math.Max(0, ValidationToolTipDuration));
    }

    private void Input_TextChanged(object? sender, EventArgs e)
    {
        if (_input.HasError && TryGetInputValue(out _, out _)) ClearValidationError();
        if (!CommitWhileTyping || ReadOnly || _synchronizingText || !TryGetInputValue(out var value, out var hasValue)) return;
        // 输入中：只更新值，不改写正在编辑的文本。
        var changed = _hasValue != hasValue || hasValue && _value != value;
        if (!changed) return;
        _value = value;
        _hasValue = hasValue;
        UpdateStepButtons();
        ValueChanged?.Invoke(this, EventArgs.Empty);
    }

    private bool TryGetInputValue(out decimal value, out bool hasValue)
    {
        var text = _input.Text.Trim();
        if (PrefixText.Length > 0 && text.StartsWith(PrefixText, StringComparison.CurrentCulture))
            text = text.Substring(PrefixText.Length).TrimStart();
        if (SuffixText.Length > 0 && text.EndsWith(SuffixText, StringComparison.CurrentCulture))
            text = text.Substring(0, text.Length - SuffixText.Length).TrimEnd();
        if (text.Length == 0 && AllowNull)
        {
            value = _value;
            hasValue = false;
            return true;
        }
        hasValue = true;
        return decimal.TryParse(text, NumberStyles.Number | NumberStyles.AllowExponent, Culture, out value) &&
               value >= Minimum && value <= Maximum;
    }

    private bool _synchronizingText;

    private void SynchronizeText(bool force = false)
    {
        if (_input is null) return;
        // 边输入边提交时，外部回写同一个值（例如属性面板刷新）不能打断正在编辑的文本和光标。
        if (!force && CommitWhileTyping && _input.InnerTextBox.Focused
            && TryGetInputValue(out var typed, out var typedHasValue) && typedHasValue == _hasValue && typed == _value) return;
        var formatted = !_hasValue ? string.Empty : PrefixText + _value.ToString(EffectiveFormat, Culture) + SuffixText;
        if (_input.Text == formatted) return;
        _synchronizingText = true;
        try { _input.Text = formatted; }
        finally { _synchronizingText = false; }
    }

    private string EffectiveFormat => FormatString.Length > 0
        ? FormatString
        : $"{(ThousandsSeparator ? 'N' : 'F')}{DecimalPlaces}";

    private void RaiseCommitted()
    {
        var current = NullableValue;
        if (_hasCommittedValue && _lastCommittedValue == current) return;
        _hasCommittedValue = true;
        _lastCommittedValue = current;
        ValueCommitted?.Invoke(this, EventArgs.Empty);
    }

    private void ClearValidationError()
    {
        _input.HasError = false;
        _input.ValidationMessage = string.Empty;
        Invalidate();
        _validationToolTip.Hide();
    }

    private void UpdateStepButtons()
    {
        var editable = Enabled && !ReadOnly;
        _decrease.Enabled = editable && (!_hasValue || _value > Minimum);
        _increase.Enabled = editable && (!_hasValue || _value < Maximum);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _repeatTimer.Dispose();
            _validationToolTip.Dispose();
        }
        base.Dispose(disposing);
    }

    private void LayoutChildren()
    {
        if (_input is null || _decrease is null || _increase is null) return;
        var buttonWidth = ShowStepButtons ? ResolveButtonWidth() : 0;
        var topHeight = Height / 2;
        _input.Bounds = new Rectangle(0, 0, Math.Max(0, Width - buttonWidth), Height);
        _increase.Bounds = new Rectangle(Width - buttonWidth, 0, buttonWidth, topHeight);
        _decrease.Bounds = new Rectangle(Width - buttonWidth, topHeight, buttonWidth, Height - topHeight);
    }

    private int ResolveButtonWidth() =>
        Math.Min(ScaleLogical(28), Math.Max(ScaleLogical(20), Width / 5));
}
