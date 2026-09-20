using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;

namespace ModernUI.WinForms;

public enum ModernButtonType { Default, Primary, Text, Danger }

/// <summary>Ant Design 风格现代按钮。</summary>
[DefaultProperty(nameof(Text))]
[DefaultEvent(nameof(Click))]
[Description("ModernButton 现代按钮")]
[DisplayName("现代按钮")]
[ToolboxBitmap(typeof(ModernButton), "Toolbox.Icons.Actions.bmp")]
[ToolboxItem(true)]
public sealed class ModernButton : ModernControl, IButtonControl
{
    private float _hoverProgress;
    private IDisposable? _hoverAnimation;
    private DialogResult _dialogResult;
    private ModernButtonType _buttonType;
    private bool _loading;
    private bool _showFocusBorder = true;
    private bool _strongHoverFeedback;
    private ModernIconKind _icon;
    private ModernIconPlacement _iconPlacement;
    private Image? _image;
    private ContentAlignment _imageAlign = ContentAlignment.MiddleLeft;
    private TextImageRelation _textImageRelation = TextImageRelation.ImageBeforeText;
    private int _iconSize = 14;
    private ContentAlignment _textAlign = ContentAlignment.MiddleCenter;
    private ModernCommand? _command;
    private readonly System.Windows.Forms.Timer _loadingTimer;
    private float _loadingAngle;

    public ModernButton()
    {
        AccessibleRole = AccessibleRole.PushButton;
        Size = new Size(96, 34);
        Text = "Button";
        Cursor = Cursors.Hand;
        _loadingTimer = new System.Windows.Forms.Timer { Interval = 60 };
        _loadingTimer.Tick += (_, _) =>
        {
            var inViewport = ModernAnimationVisibility.IsInVisibleViewport(this);
            ModernAnimationVisibility.UseInterval(_loadingTimer,
                inViewport ? 60 : ModernAnimationVisibility.HiddenPollInterval);
            if (!inViewport) return;
            _loadingAngle = (_loadingAngle + 30f) % 360f;
            Invalidate();
        };
    }

    [Category("Appearance"), DefaultValue("Button")]
    [Description("按钮上显示的文本内容。")]
    [AllowNull]
    public override string Text { get => base.Text; set => base.Text = value; }

    [Category("Appearance"), DefaultValue(ModernButtonType.Default)]
    [Description("按钮的视觉类型：Default 为普通按钮，Primary 为主按钮，Text 为文本按钮，Danger 为危险操作按钮。")]
    public ModernButtonType ButtonType
    {
        get => _buttonType;
        set { if (_buttonType == value) return; _buttonType = value; Invalidate(); }
    }

    [Category("Behavior"), DefaultValue(false)]
    [Description("是否显示加载状态；启用后按钮显示加载指示并阻止重复点击。")]
    public bool Loading
    {
        get => _loading;
        set
        {
            if (_loading == value) return;
            _loading = value;
            if (!value) _loadingAngle = 0;
            UpdateLoadingAnimation();
            Invalidate();
        }
    }

    /// <summary>获取或设置是否绘制键盘焦点描边。</summary>
    [Category("Appearance"), DefaultValue(true)]
    [Description("控件获得键盘焦点时是否显示焦点描边。")]
    public bool ShowFocusBorder
    {
        get => _showFocusBorder;
        set { if (_showFocusBorder == value) return; _showFocusBorder = value; Invalidate(); }
    }

    [Category("Appearance"), DefaultValue(false)]
    [Description("是否使用更明显的悬停背景，适用于尺寸较小的图标按钮。")]
    public bool StrongHoverFeedback
    {
        get => _strongHoverFeedback;
        set { if (_strongHoverFeedback == value) return; _strongHoverFeedback = value; Invalidate(); }
    }

    [Category("Appearance"), DefaultValue(ContentAlignment.MiddleCenter)]
    [Description("按钮内容的对齐方式。")]
    public ContentAlignment TextAlign
    {
        get => _textAlign;
        set { if (_textAlign == value) return; _textAlign = value; Invalidate(); }
    }

    [Category("Appearance"), DefaultValue(ModernIconKind.None)]
    [Description("按钮显示的内置矢量图标。")]
    public ModernIconKind Icon
    {
        get => _icon;
        set { if (_icon == value) return; _icon = value; Invalidate(); }
    }

    [Category("Appearance"), DefaultValue(ModernIconPlacement.Left)]
    [Description("内置矢量图标显示在文本的左侧或右侧。")]
    public ModernIconPlacement IconPlacement
    {
        get => _iconPlacement;
        set { if (_iconPlacement == value) return; _iconPlacement = value; Invalidate(); }
    }

    [Category("Appearance"), DefaultValue(null)]
    [Description("调用方拥有的自定义图像；控件不会释放该图像。设置后优先于 Icon。")]
    public Image? Image
    {
        get => _image;
        set { if (ReferenceEquals(_image, value)) return; _image = value; Invalidate(); }
    }

    [Category("Appearance"), DefaultValue(ContentAlignment.MiddleLeft)]
    [Description("自定义图像在按钮内容区域中的对齐方式。")]
    public ContentAlignment ImageAlign
    {
        get => _imageAlign;
        set { if (_imageAlign == value) return; _imageAlign = value; Invalidate(); }
    }

    [Category("Appearance"), DefaultValue(TextImageRelation.ImageBeforeText)]
    [Description("自定义图像与文本之间的排列关系。")]
    public TextImageRelation TextImageRelation
    {
        get => _textImageRelation;
        set { if (_textImageRelation == value) return; _textImageRelation = value; Invalidate(); }
    }

    [Category("Appearance"), DefaultValue(14)]
    [Description("矢量图标的逻辑像素尺寸。")]
    public int IconSize
    {
        get => _iconSize;
        set { var next = Math.Max(8, value); if (_iconSize == next) return; _iconSize = next; Invalidate(); }
    }

    /// <summary>获取或设置由按钮执行的共享命令。</summary>
    [Category("Behavior"), DefaultValue(null)]
    [Description("由按钮展示并执行的共享命令。")]
    public ModernCommand? Command
    {
        get => _command;
        set
        {
            if (ReferenceEquals(_command, value)) return;
            if (_command is not null)
            {
                _command.PropertyChanged -= CommandChanged;
                _command.CanExecuteChanged -= CommandCanExecuteChanged;
            }
            _command = value;
            if (_command is not null)
            {
                _command.PropertyChanged += CommandChanged;
                _command.CanExecuteChanged += CommandCanExecuteChanged;
                ApplyCommand();
            }
        }
    }

    [Category("Behavior"), DefaultValue(DialogResult.None)]
    [Description("按钮位于对话框中时，单击按钮后返回的对话框结果。")]
    public DialogResult DialogResult { get => _dialogResult; set => _dialogResult = value; }

    protected override void RenderContent(GdiCanvas canvas, Rectangle bounds)
    {
        var borderInset = ScaleLogical(1f);
        var rect = RectangleF.Inflate(bounds, -borderInset, -borderInset);
        var background = ResolveBackground();
        var foreground = ResolveForeground();
        canvas.Fill(background, rect, ScaleLogical(Theme.Radius));
        if (ButtonType == ModernButtonType.Default)
            canvas.Draw(Geometry.Blend(Theme.Border, Theme.PrimaryHover, _hoverProgress),
                IsPressed ? ScaleLogical(1.5f) : ScaleLogical(1f), rect, ScaleLogical(Theme.Radius));

        var icon = Loading ? ModernIconKind.Loading : Icon;
        if (!Loading && Image is not null) DrawImageContent(canvas, rect, foreground, Image);
        else DrawContent(canvas, rect, foreground, icon);
    }

    private void DrawContent(GdiCanvas canvas, RectangleF bounds, Color foreground, ModernIconKind icon)
    {
        var text = Text ?? string.Empty;
        if (icon == ModernIconKind.None)
        {
            canvas.DrawText(text, Font, foreground, Rectangle.Round(bounds), TextAlign);
            return;
        }

        var iconSize = Math.Min(ScaleLogical(IconSize), Math.Max(0, (int)bounds.Height - ScaleLogical(8)));
        var gap = string.IsNullOrEmpty(text) ? 0 : ScaleLogical(6);
        var measuredTextSize = string.IsNullOrEmpty(text) ? Size.Empty : TextRenderer.MeasureText(text, Font, Size.Empty,
            TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
        // GDI 字形墨迹可能比测量 advance 多 1~3px，给绘制矩形保留尾部空间，避免中文末字被省略。
        var textSize = new Size(measuredTextSize.Width + (string.IsNullOrEmpty(text) ? 0 : ScaleLogical(18)), measuredTextSize.Height);
        var contentWidth = iconSize + gap + textSize.Width;
        var horizontalPadding = ScaleLogical(10);
        var contentLeft = TextAlign switch
        {
            ContentAlignment.MiddleLeft => bounds.Left + horizontalPadding,
            ContentAlignment.MiddleRight => bounds.Right - horizontalPadding - contentWidth,
            _ => bounds.Left + Math.Max(0, (bounds.Width - contentWidth) / 2f)
        };
        var iconLeft = IconPlacement == ModernIconPlacement.Left || string.IsNullOrEmpty(text)
            ? contentLeft : contentLeft + textSize.Width + gap;
        var iconBounds = new RectangleF(iconLeft, bounds.Top + (bounds.Height - iconSize) / 2f, iconSize, iconSize);
        if (icon == ModernIconKind.Loading)
            canvas.DrawIcon(icon, foreground, iconBounds, ScaleLogical(1.6f), _loadingAngle);
        else
            canvas.DrawIcon(icon, foreground, iconBounds, ScaleLogical(1.6f));

        if (string.IsNullOrEmpty(text)) return;
        var textLeft = IconPlacement == ModernIconPlacement.Left ? iconBounds.Right + gap : contentLeft;
        canvas.DrawText(text, Font, foreground,
            Rectangle.Round(new RectangleF(textLeft, bounds.Top, textSize.Width, bounds.Height)), ContentAlignment.MiddleLeft);
    }

    private void DrawImageContent(GdiCanvas canvas, RectangleF bounds, Color foreground, Image image)
    {
        var text = Text ?? string.Empty;
        var maximumImageSize = Math.Max(1, (int)bounds.Height - ScaleLogical(8));
        var scale = Math.Min(1f, Math.Min(maximumImageSize / (float)Math.Max(1, image.Width),
            maximumImageSize / (float)Math.Max(1, image.Height)));
        var imageSize = new SizeF(Math.Max(1, image.Width * scale), Math.Max(1, image.Height * scale));
        var gap = string.IsNullOrEmpty(text) || TextImageRelation == TextImageRelation.Overlay ? 0 : ScaleLogical(6);
        var textSize = string.IsNullOrEmpty(text) ? Size.Empty : TextRenderer.MeasureText(text, Font, Size.Empty,
            TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
        var horizontal = TextImageRelation is TextImageRelation.ImageBeforeText or TextImageRelation.TextBeforeImage;
        var contentWidth = horizontal ? imageSize.Width + gap + textSize.Width : Math.Max(imageSize.Width, textSize.Width);
        var contentHeight = horizontal ? Math.Max(imageSize.Height, textSize.Height) : imageSize.Height + gap + textSize.Height;
        if (TextImageRelation == TextImageRelation.Overlay)
        {
            contentWidth = Math.Max(imageSize.Width, textSize.Width);
            contentHeight = Math.Max(imageSize.Height, textSize.Height);
        }
        var contentLeft = ImageAlign is ContentAlignment.TopRight or ContentAlignment.MiddleRight or ContentAlignment.BottomRight
            ? bounds.Right - ScaleLogical(10) - contentWidth
            : ImageAlign is ContentAlignment.TopCenter or ContentAlignment.MiddleCenter or ContentAlignment.BottomCenter
                ? bounds.Left + (bounds.Width - contentWidth) / 2f
                : bounds.Left + ScaleLogical(10);
        var contentTop = bounds.Top + (bounds.Height - contentHeight) / 2f;
        var imageBefore = TextImageRelation is TextImageRelation.ImageBeforeText or TextImageRelation.ImageAboveText or TextImageRelation.Overlay;
        var imageBounds = horizontal
            ? new RectangleF(imageBefore ? contentLeft : contentLeft + textSize.Width + gap,
                contentTop + (contentHeight - imageSize.Height) / 2f, imageSize.Width, imageSize.Height)
            : new RectangleF(contentLeft + (contentWidth - imageSize.Width) / 2f,
                imageBefore ? contentTop : contentTop + textSize.Height + gap, imageSize.Width, imageSize.Height);
        canvas.DrawImage(image, imageBounds);
        if (string.IsNullOrEmpty(text)) return;
        var textBounds = horizontal
            ? new RectangleF(imageBefore ? imageBounds.Right + gap : contentLeft,
                contentTop, textSize.Width + ScaleLogical(4), contentHeight)
            : new RectangleF(contentLeft, imageBefore ? imageBounds.Bottom + gap : contentTop,
                contentWidth, textSize.Height);
        if (TextImageRelation == TextImageRelation.Overlay) textBounds = new RectangleF(contentLeft, contentTop, contentWidth, contentHeight);
        canvas.DrawText(text, Font, foreground, Rectangle.Round(textBounds), TextAlign);
    }

    private Color ResolveBackground()
    {
        if (!Enabled) return Theme.BorderSecondary;
        var normal = ButtonType switch
        {
            ModernButtonType.Primary => Theme.Primary,
            ModernButtonType.Danger => Theme.Error,
            ModernButtonType.Text => Color.Transparent,
            _ => Theme.Control
        };
        var hover = ButtonType switch
        {
            ModernButtonType.Primary => Theme.PrimaryHover,
            ModernButtonType.Danger => Geometry.Blend(Theme.Error, Color.White, .18f),
            ModernButtonType.Text when StrongHoverFeedback => Theme.PrimaryBackground,
            _ => Theme.ControlHover
        };
        if (IsPressed) return ButtonType == ModernButtonType.Primary ? Theme.PrimaryActive : Geometry.Blend(normal, Theme.Primary, .12f);
        return Geometry.Blend(normal, hover, _hoverProgress);
    }

    protected override void RenderOverlay(GdiCanvas canvas, Rectangle bounds)
    {
        if (!ShowFocusBorder || !ShouldShowFocusCue || !Enabled) return;
        var inset = ScaleLogical(2);
        var focusBounds = new RectangleF(inset, inset, Math.Max(0, bounds.Width - inset * 2 - 1), Math.Max(0, bounds.Height - inset * 2 - 1));
        var accent = ButtonType == ModernButtonType.Danger ? Theme.Error : Theme.Primary;
        ModernFocusVisual.Draw(canvas, Theme, focusBounds,
            Math.Max(2, ScaleLogical(Theme.Radius) - 1), DpiScale, accent);
    }

    private Color ResolveForeground() => !Enabled ? Theme.TextDisabled :
        ButtonType is ModernButtonType.Primary or ModernButtonType.Danger ? Color.White : Theme.Text;

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        UpdateLoadingAnimation();
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        _loadingTimer.Stop();
        base.OnHandleDestroyed(e);
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        UpdateLoadingAnimation();
    }

    protected override void OnParentChanged(EventArgs e)
    {
        base.OnParentChanged(e);
        UpdateLoadingAnimation();
    }

    private void UpdateLoadingAnimation() =>
        _loadingTimer.Enabled = Loading && Visible && IsHandleCreated &&
                                ModernUiSettings.EffectiveAnimationsEnabled;

    protected override void OnHoverChanged()
    {
        base.OnHoverChanged();
        AnimateHover(IsHovered ? 1 : 0);
    }
    protected override AccessibleObject CreateAccessibilityInstance() => new ButtonAccessibleObject(this);

    protected override void OnClick(EventArgs e)
    {
        if (Loading || Command is { CanExecute: false }) return;
        base.OnClick(e);
        Command?.TryExecute();
    }

    private void CommandChanged(object? sender, PropertyChangedEventArgs e) => ApplyCommand();
    private void CommandCanExecuteChanged(object? sender, EventArgs e) => Enabled = Command?.CanExecute ?? Enabled;
    private void ApplyCommand()
    {
        if (Command is null) return;
        Text = Command.Text;
        Icon = Command.Icon;
        Image = Command.Image;
        AccessibleDescription = Command.Description;
        Enabled = Command.CanExecute;
        Visible = Command.Visible;
    }
    protected override bool ProcessMnemonic(char charCode) { if (!CanSelect || !IsMnemonic(charCode, Text)) return false; PerformClick(); return true; }

    public void NotifyDefault(bool value) { }
    public void PerformClick()
    {
        if (!Enabled || Loading) return;
        OnClick(EventArgs.Empty);
        if (FindForm() is { } form && DialogResult != DialogResult.None) form.DialogResult = DialogResult;
    }

    private sealed class ButtonAccessibleObject(ModernButton owner) : ControlAccessibleObject(owner)
    {
        public override AccessibleRole Role => AccessibleRole.PushButton;
        public override string? DefaultAction => "Press";

        public override void DoDefaultAction() => owner.PerformClick();
    }

    private void AnimateHover(float target)
    {
        _hoverAnimation?.Dispose();
        var start = _hoverProgress;
        _hoverAnimation = ModernAnimation.Start(this, start, target, Theme.AnimationDuration, value => { _hoverProgress = value; Invalidate(); });
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _hoverAnimation?.Dispose();
            _loadingTimer.Dispose();
            if (_command is not null)
            {
                _command.PropertyChanged -= CommandChanged;
                _command.CanExecuteChanged -= CommandCanExecuteChanged;
            }
        }
        base.Dispose(disposing);
    }
}
