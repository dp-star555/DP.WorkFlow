using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;

namespace ModernUI.WinForms;

/// <summary>使用原生 TextBox 保留输入法能力，并由外层提供现代边框和状态。</summary>
[DefaultProperty(nameof(Text))]
[DefaultEvent(nameof(TextChanged))]
[DefaultBindingProperty(nameof(Text))]
[Description("ModernInput 现代文本输入框")]
[DisplayName("现代文本框")]
[ToolboxBitmap(typeof(ModernInput), "Toolbox.Icons.Inputs.bmp")]
[ToolboxItem(true)]
public class ModernInput : ModernValidatedControl
{
    private readonly ModernNativeTextBox _textBox = new()
    {
        BorderStyle = BorderStyle.None,
        AccessibleRole = AccessibleRole.Text
    };
    private readonly ModernButton _passwordReveal = new()
    {
        Text = string.Empty, Icon = ModernIconKind.Eye, ButtonType = ModernButtonType.Text,
        StrongHoverFeedback = true, TabStop = false, Visible = false
    };
    private string? _placeholderText;
    private bool _showPasswordRevealButton;
    private bool _passwordRevealed;
    private bool _passwordToggleInProgress;
    private bool _fontSyncPending;
    private bool _settingText;
    private bool _userEditPending;
    private bool _hasCommittedText;
    private string _lastCommittedText = string.Empty;
    private FinalLayoutTransaction? _layoutTransaction;

    private FinalLayoutTransaction LayoutTransaction =>
        _layoutTransaction ??= new FinalLayoutTransaction(this, ApplyFinalLayout);

    public ModernInput()
    {
        AccessibleRole = AccessibleRole.Text;
        Height = 34;
        MinimumSize = new Size(80, 30);
        Padding = new Padding(9, 7, 9, 5);
        UpdateEffectivePlaceholder();
        Controls.Add(_textBox);
        Controls.Add(_passwordReveal);
        _passwordReveal.Click += (_, _) => TogglePasswordVisibility();
        _textBox.TextChanged += (_, _) =>
        {
            if (!_settingText && _textBox.Focused) _userEditPending = true;
            base.Text = _textBox.Text;
            OnTextChanged(EventArgs.Empty);
        };
        _textBox.GotFocus += (_, _) => Invalidate();
        _textBox.LostFocus += (_, _) => Invalidate();
        _textBox.Validated += (_, _) => CommitText();
        _textBox.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter || e.Modifiers != Keys.None || _textBox.Multiline) return;
            CommitText();
            e.SuppressKeyPress = true;
            e.Handled = true;
        };
        _textBox.MouseEnter += (_, _) => Invalidate();
        _textBox.HandleCreated += (_, _) => NativeControlTheme.ApplyExplorer(_textBox, Theme.IsDark);
        OnThemeChanged();
        LayoutTextBox();
    }

    [Browsable(true)]
    [Bindable(true)]
    [AllowNull]
    public override string Text
    {
        get => _textBox.Text;
        set
        {
            var next = value ?? string.Empty;
            if (_textBox.Text == next) return;
            _settingText = true;
            try { _textBox.Text = next; }
            finally { _settingText = false; }
        }
    }

    [Category("Appearance"), DefaultValue(null)]
    [Description("输入内容为空时显示的显式提示文字；null 使用当前语言的框架默认文本。")]
    [AllowNull]
    public string? PlaceholderText { get => _placeholderText; set { _placeholderText = value; UpdateEffectivePlaceholder(); } }

    [Browsable(false)]
    public string EffectivePlaceholderText => _textBox.PlaceholderText;

    [Category("Behavior"), DefaultValue(false)]
    [Description("是否禁止用户修改输入内容。")]
    public bool ReadOnly
    {
        get => _textBox.ReadOnly;
        set
        {
            if (_textBox.ReadOnly == value) return;
            _textBox.ReadOnly = value;
        }
    }

    [Category("Behavior"), DefaultValue(false)]
    [Description("是否使用系统密码字符隐藏输入内容。")]
    public bool UseSystemPasswordChar
    {
        get => _textBox.UseSystemPasswordChar;
        set
        {
            _passwordRevealed = false;
            if (_textBox.UseSystemPasswordChar == value) { UpdatePasswordRevealButton(); return; }
            _textBox.UseSystemPasswordChar = value;
            UpdatePasswordRevealButton();
            LayoutTransaction.Request();
        }
    }

    /// <summary>获取或设置密码输入时是否显示原文显隐按钮。</summary>
    [Category("Behavior"), DefaultValue(false)]
    [Description("密码输入时是否在右侧显示原文显隐按钮；切换不会改变文本、选择和焦点。")]
    public bool ShowPasswordRevealButton
    {
        get => _showPasswordRevealButton;
        set
        {
            if (_showPasswordRevealButton == value) return;
            _showPasswordRevealButton = value;
            UpdatePasswordRevealButton();
            LayoutTransaction.Request();
        }
    }

    /// <summary>获取或设置用户可输入的最大字符数。</summary>
    [Category("Behavior"), DefaultValue(32767)]
    [Description("用户可在输入框中输入的最大字符数。")]
    public int MaxLength { get => _textBox.MaxLength; set => _textBox.MaxLength = value; }

    /// <summary>获取或设置输入字符的大小写转换方式。</summary>
    [Category("Behavior"), DefaultValue(CharacterCasing.Normal)]
    [Description("输入字符是否自动转换为大写或小写。")]
    public CharacterCasing CharacterCasing { get => _textBox.CharacterCasing; set => _textBox.CharacterCasing = value; }

    /// <summary>获取或设置文本的水平对齐方式。</summary>
    [Category("Appearance"), DefaultValue(HorizontalAlignment.Left)]
    [Description("输入文本在编辑区域中的水平对齐方式。")]
    public HorizontalAlignment TextAlign { get => _textBox.TextAlign; set => _textBox.TextAlign = value; }

    /// <summary>获取或设置是否启用系统剪贴板和撤销快捷键。</summary>
    [Category("Behavior"), DefaultValue(true)]
    [Description("是否启用复制、粘贴、剪切和撤销等系统快捷键。")]
    public bool ShortcutsEnabled { get => _textBox.ShortcutsEnabled; set => _textBox.ShortcutsEnabled = value; }

    /// <summary>获取或设置当前选择文本的起始位置。</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int SelectionStart { get => _textBox.SelectionStart; set => _textBox.SelectionStart = value; }

    /// <summary>获取或设置当前选择的字符数。</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int SelectionLength { get => _textBox.SelectionLength; set => _textBox.SelectionLength = value; }

    /// <summary>获取或替换当前选择的文本。</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    [AllowNull]
    public string SelectedText { get => _textBox.SelectedText; set => _textBox.SelectedText = value ?? string.Empty; }

    /// <summary>获取输入框当前是否可以撤销上一编辑操作。</summary>
    [Browsable(false)]
    public bool CanUndo => _textBox.CanUndo;

    /// <summary>选择指定范围的文本。</summary>
    /// <param name="start">从零开始的选择起始位置。</param>
    /// <param name="length">要选择的字符数。</param>
    public void Select(int start, int length) => _textBox.Select(start, length);

    /// <summary>选择输入框中的全部文本。</summary>
    public void SelectAll() => _textBox.SelectAll();

    /// <summary>清空输入内容。</summary>
    public void Clear() => _textBox.Clear();

    /// <summary>撤销上一文本编辑操作。</summary>
    public void Undo() => _textBox.Undo();

    [Category("Appearance"), DefaultValue(false)]
    [Description("是否以错误状态样式显示输入框。")]
    public bool HasError
    {
        get => ValidationState == ModernValidationState.Error;
        set { if (value) ValidationState = ModernValidationState.Error; else if (HasError) ValidationState = ModernValidationState.None; }
    }


    [Description("用户通过 Enter 或完成验证提交文本时引发；程序赋值不会引发。")]
    public event EventHandler? TextCommitted;

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public TextBox InnerTextBox => _textBox;

    protected Func<int, bool>? InnerMouseWheelHandler
    {
        set => _textBox.WheelHandler = value;
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
        if (_textBox is not null) _textBox.AccessibleDescription = AccessibleDescription;
    }

    protected override void OnLocalizationChanged()
    {
        base.OnLocalizationChanged();
        UpdateEffectivePlaceholder();
        UpdatePasswordRevealButton();
    }

    private void UpdateEffectivePlaceholder()
    {
        if (_textBox is null) return;
        _textBox.PlaceholderText = _placeholderText ?? FrameworkText(ModernUiTextKeys.InputPlaceholder);
    }

    private void CommitText()
    {
        if (ReadOnly || !_userEditPending) return;
        _userEditPending = false;
        if (_hasCommittedText && string.Equals(_lastCommittedText, Text, StringComparison.Ordinal)) return;
        _hasCommittedText = true;
        _lastCommittedText = Text;
        TextCommitted?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnThemeChanged()
    {
        if (_textBox is null) return;
        _textBox.BackColor = Theme.Control;
        _textBox.ForeColor = Theme.Text;
        _passwordReveal.Theme = Theme;
        NativeControlTheme.ApplyExplorer(_textBox, Theme.IsDark);
        Invalidate();
    }

    protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); if (_textBox is not null) _textBox.Font = Font; LayoutTransaction.Request(); }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        _fontSyncPending = true;
        LayoutTransaction.Request();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        LayoutTransaction.Request();
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        LayoutTransaction.Request();
    }

    protected override void OnPaddingChanged(EventArgs e)
    {
        base.OnPaddingChanged(e);
        LayoutTransaction.Request();
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        _fontSyncPending = true;
        LayoutTransaction.Request();
    }

    protected override void RenderContent(GdiCanvas canvas, Rectangle bounds)
    {
        var inset = ScaleLogical(1f);
        var rect = RectangleF.Inflate(bounds, -inset, -inset);
        canvas.Fill(Theme.Control, rect, ScaleLogical(Theme.Radius));
        var fallback = ContainsFocus ? Theme.Primary : IsHovered ? Theme.PrimaryHover : Theme.Border;
        var border = ResolveValidationBorder(fallback);
        canvas.Draw(border, ContainsFocus || ModernValidation.IsEmphasized(ValidationState) ? ScaleLogical(1.5f) : ScaleLogical(1f),
            rect, ScaleLogical(Theme.Radius), centerStroke: true);
    }

    private void ApplyFinalLayout()
    {
        if (_textBox is null) return;
        // Initial PMv2 creation on a monitor below the system DPI scales the parent font first and
        // can then scale the already synchronized native child a second time. Reapply the final
        // inherited font after the complete parent DPI transaction.
        if (_fontSyncPending)
        {
            _fontSyncPending = false;
            if (!_textBox.Font.Equals(Font)) _textBox.Font = Font;
        }
        LayoutTextBox();
    }

    private void TogglePasswordVisibility()
    {
        if (_passwordToggleInProgress) return;
        _passwordToggleInProgress = true;
        try
        {
            var selectionStart = _textBox.SelectionStart;
            var selectionLength = _textBox.SelectionLength;
            _textBox.UseSystemPasswordChar = !_textBox.UseSystemPasswordChar;
            _passwordRevealed = !_textBox.UseSystemPasswordChar;
            UpdatePasswordRevealButton();
            _textBox.Focus();
            _textBox.Select(Math.Min(selectionStart, _textBox.TextLength),
                Math.Min(selectionLength, Math.Max(0, _textBox.TextLength - selectionStart)));
            LayoutTransaction.Request();
        }
        finally { _passwordToggleInProgress = false; }
    }

    private void UpdatePasswordRevealButton()
    {
        if (_passwordReveal is null) return;
        _passwordReveal.Visible = ShowPasswordRevealButton &&
                                  (_textBox.UseSystemPasswordChar || _passwordRevealed);
        _passwordReveal.Icon = _textBox.UseSystemPasswordChar ? ModernIconKind.Eye : ModernIconKind.EyeOff;
        _passwordReveal.AccessibleName = FrameworkText(_textBox.UseSystemPasswordChar
            ? ModernUiTextKeys.ShowPassword : ModernUiTextKeys.HidePassword);
    }

    private void LayoutTextBox()
    {
        if (_textBox is null) return;
        var revealWidth = _passwordReveal.Visible ? Math.Min(ScaleLogical(28), Math.Max(0, Width - Padding.Horizontal)) : 0;
        var width = Math.Max(0, Width - Padding.Horizontal - revealWidth);
        if (_passwordReveal.Visible)
        {
            _passwordReveal.Bounds = new Rectangle(Math.Max(Padding.Left, Width - Padding.Right - revealWidth),
                Math.Max(ScaleLogical(2), (Height - ScaleLogical(28)) / 2), revealWidth, ScaleLogical(28));
            _passwordReveal.BringToFront();
        }
        if (_textBox.Multiline)
        {
            _textBox.Bounds = new Rectangle(Padding.Left, Padding.Top, width,
                Math.Max(0, Height - Padding.Vertical));
            return;
        }

        _textBox.Bounds = new Rectangle(Padding.Left, Math.Max(Padding.Top, (Height - _textBox.PreferredHeight) / 2),
            width, _textBox.PreferredHeight);
    }

}
