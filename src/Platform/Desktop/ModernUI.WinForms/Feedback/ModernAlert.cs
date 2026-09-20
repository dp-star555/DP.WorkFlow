using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;

namespace ModernUI.WinForms;

/// <summary>在页面内持续显示带语义状态的反馈信息。</summary>
[DefaultProperty(nameof(Text))]
[Description("ModernAlert 页面内反馈提示")]
[DisplayName("现代页面提示")]
[ToolboxBitmap(typeof(ModernAlert), "Toolbox.Icons.Feedback.bmp")]
[ToolboxItem(true)]
public sealed class ModernAlert : ModernControl
{
    private ModernVisualStatus _status = ModernVisualStatus.Primary;
    private string _description = string.Empty;
    private bool _closable;
    private bool _wrapText;
    private ModernCommand? _actionCommand;

    public ModernAlert()
    {
        AccessibleRole = AccessibleRole.Alert;
        Height = 66;
        MinimumSize = new Size(160, 42);
        Padding = new Padding(14, 10, 14, 10);
        Text = "提示";
    }

    [AllowNull]
    [Description("提示的标题文本。")]
    public override string Text { get => base.Text; set => base.Text = value ?? string.Empty; }
    [Category("Appearance"), DefaultValue(ModernVisualStatus.Primary)]
    [Description("提示使用的信息、成功、警告或错误语义状态。")]
    public ModernVisualStatus Status { get => _status; set { if (_status == value) return; _status = value; Invalidate(); } }
    [Category("Appearance"), DefaultValue("")]
    [Description("显示在标题下方的详细说明。")]
    public string Description
    {
        get => _description;
        set
        {
            value ??= string.Empty;
            if (_description == value) return;
            _description = value;
            Invalidate();
            NotifyLiveRegionChanged();
        }
    }
    [Category("Behavior"), DefaultValue(false)]
    [Description("是否允许用户关闭提示。")]
    public bool Closable { get => _closable; set { if (_closable == value) return; _closable = value; Invalidate(); } }
    [Category("Appearance"), DefaultValue(false)]
    [Description("标题和说明是否根据可用宽度自动换行。")]
    public bool WrapText { get => _wrapText; set { if (_wrapText == value) return; _wrapText = value; Invalidate(); } }
    [Category("Behavior"), DefaultValue(null)]
    [Description("单击提示正文时执行的可选命令。")]
    public ModernCommand? ActionCommand { get => _actionCommand; set { _actionCommand = value; Invalidate(); } }
    public event EventHandler? Closed;

    protected override void RenderContent(GdiCanvas canvas, Rectangle bounds)
    {
        var accent = ModernStatusColors.Resolve(Theme, Status);
        var surface = ModernStatusColors.ResolveSurface(Theme, Status);
        var rect = RectangleF.Inflate(bounds, -1, -1);
        canvas.Fill(surface, rect, ScaleLogical(Theme.Radius));
        canvas.Draw(Geometry.Blend(Theme.Border, accent, .5f), ScaleLogical(1), rect, ScaleLogical(Theme.Radius));
        var iconSize = ScaleLogical(18);
        var iconBounds = new RectangleF(Padding.Left, Padding.Top + 1, iconSize, iconSize);
        canvas.DrawIcon(ModernIconKind.Info, accent, iconBounds, ScaleLogical(1.7f));
        var left = (int)iconBounds.Right + ScaleLogical(9);
        var right = bounds.Right - Padding.Right - (Closable ? ScaleLogical(22) : 0);
        using var titleFont = new Font(Font, FontStyle.Bold);
        var textWidth = Math.Max(0, right - left);
        var contentBottom = Height - Padding.Bottom;
        var titleHeight = ScaleLogical(23);
        if (WrapText && textWidth > 0)
        {
            var measuredTitle = ModernTextLayout.Measure(canvas.Graphics, Text, titleFont,
                new Size(textWidth, Math.Max(1, contentBottom - Padding.Top)), wrap: true);
            var maximumTitleHeight = string.IsNullOrEmpty(Description)
                ? Math.Max(0, contentBottom - Padding.Top)
                : ScaleLogical(46);
            titleHeight = Math.Min(Math.Max(titleFont.Height, measuredTitle.Height), maximumTitleHeight);
            canvas.DrawTextWrapped(Text, titleFont, Theme.Text,
                new Rectangle(left, Padding.Top, textWidth, titleHeight));
        }
        else
        {
            canvas.DrawText(Text, titleFont, Theme.Text,
                new Rectangle(left, Padding.Top - 2, textWidth, titleHeight), ContentAlignment.MiddleLeft);
        }
        if (!string.IsNullOrEmpty(Description))
        {
            var descriptionTop = Padding.Top + titleHeight + (WrapText ? ScaleLogical(2) : 0);
            var descriptionBounds = new Rectangle(left, descriptionTop, textWidth, Math.Max(0, contentBottom - descriptionTop));
            if (WrapText) canvas.DrawTextWrapped(Description, Font, Theme.TextSecondary, descriptionBounds);
            else canvas.DrawText(Description, Font, Theme.TextSecondary, descriptionBounds, ContentAlignment.TopLeft);
        }
        if (Closable)
            canvas.DrawIcon(ModernIconKind.Close, Theme.TextSecondary,
                new RectangleF(bounds.Right - Padding.Right - ScaleLogical(14), Padding.Top + ScaleLogical(2), ScaleLogical(12), ScaleLogical(12)), ScaleLogical(1.4f));
    }

    protected override AccessibleObject CreateAccessibilityInstance() => new AlertAccessibleObject(this);

    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        NotifyLiveRegionChanged();
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (Visible) NotifyLiveRegionChanged();
    }

    private void NotifyLiveRegionChanged()
    {
        if (!IsHandleCreated || !Visible) return;
        AccessibilityNotifyClients(AccessibleEvents.NameChange, -1);
        AccessibilityNotifyClients(ModernAccessibilityEvents.LiveRegionChanged, -1);
    }

    private sealed class AlertAccessibleObject(ModernAlert owner) : ControlAccessibleObject(owner)
    {
        public override string? Name
        {
            get => string.IsNullOrWhiteSpace(owner.AccessibleName)
                ? string.Join(". ", new[] { owner.Text, owner.Description }.Where(text => !string.IsNullOrWhiteSpace(text)))
                : owner.AccessibleName;
            set => owner.AccessibleName = value;
        }
        public override string? Description => owner.Description;
        public override AccessibleRole Role => AccessibleRole.Alert;
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != MouseButtons.Left) return;
        if (Closable && e.X >= Width - Padding.Right - ScaleLogical(24))
        {
            Visible = false;
            Closed?.Invoke(this, EventArgs.Empty);
        }
        else ActionCommand?.TryExecute();
    }
}
