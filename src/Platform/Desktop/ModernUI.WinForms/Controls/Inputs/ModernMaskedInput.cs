using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;

namespace ModernUI.WinForms;

/// <summary>Preserves the native MaskedTextBox editing, clipboard and accessibility semantics behind modern chrome.</summary>
[DefaultProperty(nameof(Mask))]
[DefaultEvent(nameof(TextChanged))]
[DefaultBindingProperty(nameof(Text))]
[Description("ModernMaskedInput 现代掩码输入框")]
[DisplayName("现代掩码输入框")]
[ToolboxBitmap(typeof(ModernMaskedInput), "Toolbox.Icons.Inputs.bmp")]
[ToolboxItem(true)]
public sealed class ModernMaskedInput : ModernValidatedControl
{
    private readonly MaskedTextBox _textBox = new()
    {
        BorderStyle = BorderStyle.None,
        AccessibleRole = AccessibleRole.Text
    };
    private bool _settingText;
    private bool _userEditPending;
    private bool _hasCommittedText;
    private string _lastCommittedText = string.Empty;

    public ModernMaskedInput()
    {
        AccessibleRole = AccessibleRole.Text;
        Height = 34;
        MinimumSize = new Size(80, 30);
        Padding = new Padding(9, 7, 9, 5);
        Controls.Add(_textBox);
        _textBox.TextChanged += (_, _) =>
        {
            if (!_settingText && _textBox.Focused) _userEditPending = true;
            base.Text = _textBox.Text;
            OnTextChanged(EventArgs.Empty);
        };
        _textBox.Validated += (_, _) => CommitText();
        _textBox.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter || e.Modifiers != Keys.None) return;
            CommitText();
            e.SuppressKeyPress = true;
            e.Handled = true;
        };
        _textBox.MaskInputRejected += (_, e) => MaskInputRejected?.Invoke(this, e);
        _textBox.TypeValidationCompleted += (_, e) => TypeValidationCompleted?.Invoke(this, e);
        _textBox.GotFocus += (_, _) => Invalidate();
        _textBox.LostFocus += (_, _) => Invalidate();
        _textBox.HandleCreated += (_, _) => NativeControlTheme.ApplyExplorer(_textBox, Theme.IsDark);
        OnThemeChanged();
        LayoutEditor();
    }

    [Browsable(true), Bindable(true), AllowNull]
    public override string Text
    {
        get => _textBox.Text;
        set
        {
            var next = value ?? string.Empty;
            if (_textBox.Text == next) return;
            _userEditPending = false;
            _settingText = true;
            try { _textBox.Text = next; }
            finally { _settingText = false; }
        }
    }

    [Category("Behavior"), DefaultValue("")]
    public string Mask { get => _textBox.Mask; set => _textBox.Mask = value ?? string.Empty; }

    [Category("Behavior"), DefaultValue('_')]
    public char PromptChar { get => _textBox.PromptChar; set => _textBox.PromptChar = value; }

    [Category("Behavior"), DefaultValue('\0')]
    public char PasswordChar { get => _textBox.PasswordChar; set => _textBox.PasswordChar = value; }

    [Category("Behavior"), DefaultValue(false)]
    public bool UseSystemPasswordChar { get => _textBox.UseSystemPasswordChar; set => _textBox.UseSystemPasswordChar = value; }

    [Category("Behavior"), DefaultValue(false)]
    public bool ReadOnly
    {
        get => _textBox.ReadOnly;
        set { _textBox.ReadOnly = value; if (value) _userEditPending = false; }
    }

    [Category("Behavior"), DefaultValue(MaskFormat.IncludeLiterals)]
    public MaskFormat TextMaskFormat { get => _textBox.TextMaskFormat; set => _textBox.TextMaskFormat = value; }

    [Category("Behavior"), DefaultValue(MaskFormat.IncludeLiterals)]
    public MaskFormat CutCopyMaskFormat { get => _textBox.CutCopyMaskFormat; set => _textBox.CutCopyMaskFormat = value; }

    [Category("Behavior"), DefaultValue(false)]
    public bool BeepOnError { get => _textBox.BeepOnError; set => _textBox.BeepOnError = value; }

    [Category("Behavior"), DefaultValue(false)]
    public bool RejectInputOnFirstFailure { get => _textBox.RejectInputOnFirstFailure; set => _textBox.RejectInputOnFirstFailure = value; }

    [Category("Behavior"), DefaultValue(true)]
    public bool ResetOnPrompt { get => _textBox.ResetOnPrompt; set => _textBox.ResetOnPrompt = value; }

    [Category("Behavior"), DefaultValue(true)]
    public bool ResetOnSpace { get => _textBox.ResetOnSpace; set => _textBox.ResetOnSpace = value; }

    [Category("Behavior"), DefaultValue(true)]
    public bool SkipLiterals { get => _textBox.SkipLiterals; set => _textBox.SkipLiterals = value; }

    [Category("Appearance"), DefaultValue(HorizontalAlignment.Left)]
    public HorizontalAlignment TextAlign { get => _textBox.TextAlign; set => _textBox.TextAlign = value; }

    [Browsable(false)] public bool MaskCompleted => _textBox.MaskCompleted;
    [Browsable(false)] public bool MaskFull => _textBox.MaskFull;
    [Browsable(false)] public MaskedTextBox InnerTextBox => _textBox;

    public event MaskInputRejectedEventHandler? MaskInputRejected;
    public event TypeValidationEventHandler? TypeValidationCompleted;
    public event EventHandler? TextCommitted;

    private void CommitText()
    {
        if (ReadOnly || !_userEditPending || !MaskCompleted) return;
        _userEditPending = false;
        if (_hasCommittedText && string.Equals(_lastCommittedText, Text, StringComparison.Ordinal)) return;
        _hasCommittedText = true;
        _lastCommittedText = Text;
        TextCommitted?.Invoke(this, EventArgs.Empty);
    }

    protected override AccessibleObject CreateAccessibilityInstance()
    {
        _textBox.AccessibleName = AccessibleName;
        _textBox.AccessibleDescription = AccessibleDescription;
        return _textBox.AccessibilityObject;
    }

    protected override void SynchronizeValidationAccessibility()
    {
        base.SynchronizeValidationAccessibility();
        _textBox.AccessibleDescription = AccessibleDescription;
    }

    protected override void OnThemeChanged()
    {
        if (_textBox is null) return;
        _textBox.BackColor = Theme.Control;
        _textBox.ForeColor = Theme.Text;
        NativeControlTheme.ApplyExplorer(_textBox, Theme.IsDark);
        Invalidate();
    }

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        if (_textBox is null) return;
        _textBox.Font = Font;
        LayoutEditor();
    }

    protected override void OnLayout(LayoutEventArgs e) { base.OnLayout(e); LayoutEditor(); }
    protected override void OnResize(EventArgs e) { base.OnResize(e); LayoutEditor(); }
    protected override void OnPaddingChanged(EventArgs e) { base.OnPaddingChanged(e); LayoutEditor(); }

    protected override void RenderContent(GdiCanvas canvas, Rectangle bounds)
    {
        var rect = RectangleF.Inflate(bounds, -ScaleLogical(1), -ScaleLogical(1));
        canvas.Fill(Theme.Control, rect, ScaleLogical(Theme.Radius));
        var fallback = ContainsFocus ? Theme.Primary : IsHovered ? Theme.PrimaryHover : Theme.Border;
        canvas.Draw(ResolveValidationBorder(fallback), ContainsFocus || ModernValidation.IsEmphasized(ValidationState)
            ? ScaleLogical(1.5f) : ScaleLogical(1f), rect, ScaleLogical(Theme.Radius), centerStroke: true);
    }

    private void LayoutEditor()
    {
        if (_textBox is null) return;
        _textBox.Bounds = new Rectangle(Padding.Left,
            Math.Max(Padding.Top, (Height - _textBox.PreferredHeight) / 2),
            Math.Max(0, Width - Padding.Horizontal), _textBox.PreferredHeight);
    }
}
