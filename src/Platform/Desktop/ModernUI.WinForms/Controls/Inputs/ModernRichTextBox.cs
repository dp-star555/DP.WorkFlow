using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;

namespace ModernUI.WinForms;

/// <summary>Preserves native RichTextBox selection, RTF, clipboard, undo and accessibility semantics behind modern chrome.</summary>
[DefaultProperty(nameof(Text))]
[DefaultEvent(nameof(TextChanged))]
[DefaultBindingProperty(nameof(Text))]
[Description("ModernRichTextBox 现代富文本框")]
[DisplayName("现代富文本框")]
[ToolboxBitmap(typeof(ModernRichTextBox), "Toolbox.Icons.Inputs.bmp")]
[ToolboxItem(true)]
public sealed class ModernRichTextBox : ModernValidatedControl
{
    private readonly RichTextBox _textBox = new()
    {
        BorderStyle = BorderStyle.None,
        DetectUrls = true,
        AccessibleRole = AccessibleRole.Text
    };
    private bool _settingText;
    private bool _userEditPending;
    private bool _hasCommittedText;
    private string _lastCommittedText = string.Empty;

    public ModernRichTextBox()
    {
        AccessibleRole = AccessibleRole.Text;
        Size = new Size(280, 120);
        MinimumSize = new Size(100, 60);
        Padding = new Padding(9, 8, 9, 8);
        Controls.Add(_textBox);
        _textBox.TextChanged += (_, _) =>
        {
            if (!_settingText && _textBox.Focused) _userEditPending = true;
            base.Text = _textBox.Text;
            OnTextChanged(EventArgs.Empty);
        };
        _textBox.Validated += (_, _) => CommitText();
        _textBox.LinkClicked += (_, e) => LinkClicked?.Invoke(this, e);
        _textBox.SelectionChanged += (_, _) => SelectionChanged?.Invoke(this, EventArgs.Empty);
        _textBox.GotFocus += (_, _) => Invalidate();
        _textBox.LostFocus += (_, _) => Invalidate();
        _textBox.HandleCreated += (_, _) =>
        {
            _textBox.Font = Font;
            ResetNativeZoom();
            NativeControlTheme.ApplyExplorer(_textBox, Theme.IsDark);
        };
        OnThemeChanged();
        LayoutEditor();
    }

    [Browsable(true), Bindable(true), AllowNull]
    public override string Text
    {
        get => _textBox.Text;
        set => SetTextProgrammatically(() => _textBox.Text = value ?? string.Empty);
    }

    [Category("Data")]
    [Description("富文本内容的 RTF 表示。")]
    public string Rtf { get => _textBox.Rtf ?? string.Empty; set => SetTextProgrammatically(() => _textBox.Rtf = value ?? string.Empty); }

    [Category("Behavior"), DefaultValue(false)]
    public bool ReadOnly
    {
        get => _textBox.ReadOnly;
        set { _textBox.ReadOnly = value; if (value) _userEditPending = false; }
    }

    [Category("Behavior"), DefaultValue(true)]
    public bool DetectUrls { get => _textBox.DetectUrls; set => _textBox.DetectUrls = value; }

    [Category("Behavior"), DefaultValue(true)]
    public bool WordWrap { get => _textBox.WordWrap; set => _textBox.WordWrap = value; }

    [Category("Behavior"), DefaultValue(false)]
    public bool AcceptsTab { get => _textBox.AcceptsTab; set => _textBox.AcceptsTab = value; }

    [Category("Behavior"), DefaultValue(RichTextBoxScrollBars.Both)]
    public RichTextBoxScrollBars ScrollBars { get => _textBox.ScrollBars; set => _textBox.ScrollBars = value; }

    [Category("Behavior"), DefaultValue(2147483647)]
    public int MaxLength { get => _textBox.MaxLength; set => _textBox.MaxLength = value; }

    [Browsable(false)] public int SelectionStart { get => _textBox.SelectionStart; set => _textBox.SelectionStart = value; }
    [Browsable(false)] public int SelectionLength { get => _textBox.SelectionLength; set => _textBox.SelectionLength = value; }
    [Browsable(false), AllowNull] public string SelectedText { get => _textBox.SelectedText; set => _textBox.SelectedText = value ?? string.Empty; }
    [Browsable(false)] public string SelectedRtf { get => _textBox.SelectedRtf; set => _textBox.SelectedRtf = value ?? string.Empty; }
    [Browsable(false)] public Color SelectionColor { get => _textBox.SelectionColor; set => _textBox.SelectionColor = value; }
    [Browsable(false)] public Font? SelectionFont { get => _textBox.SelectionFont; set => _textBox.SelectionFont = value!; }
    [Browsable(false)] public bool CanUndo => _textBox.CanUndo;
    [Browsable(false)] public bool CanRedo => _textBox.CanRedo;
    [Browsable(false)] public RichTextBox InnerTextBox => _textBox;

    public event LinkClickedEventHandler? LinkClicked;
    public event EventHandler? SelectionChanged;
    public event EventHandler? TextCommitted;

    public void AppendText(string? text) => _textBox.AppendText(text ?? string.Empty);
    public void Clear() => _textBox.Clear();
    public void Copy() => _textBox.Copy();
    public void Cut() => _textBox.Cut();
    public void Paste() => _textBox.Paste();
    public void Undo() => _textBox.Undo();
    public void Redo() => _textBox.Redo();
    public void Select(int start, int length) => _textBox.Select(start, length);
    public void SelectAll() => _textBox.SelectAll();
    public void ScrollToCaret() => _textBox.ScrollToCaret();

    private void SetTextProgrammatically(Action update)
    {
        _userEditPending = false;
        _settingText = true;
        try { update(); }
        finally { _settingText = false; }
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
        ResetNativeZoom();
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
        _textBox.Bounds = new Rectangle(Padding.Left, Padding.Top,
            Math.Max(0, Width - Padding.Horizontal), Math.Max(0, Height - Padding.Vertical));
    }

    private void ResetNativeZoom()
    {
        if (!_textBox.IsHandleCreated) return;
        const int editSetZoom = 0x04E1;
        _ = SendMessage(_textBox.Handle, editSetZoom, (IntPtr)1, (IntPtr)1);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr parameter, IntPtr data);
}
