using System.ComponentModel;

namespace ModernUI.WinForms;

/// <summary>显示数量、文本或状态圆点的现代徽标。</summary>
[Description("ModernBadge 现代徽标")]
[DisplayName("现代徽标")]
[ToolboxBitmap(typeof(ModernBadge), "Toolbox.Icons.Indicators.bmp")]
[ToolboxItem(true)]
public sealed class ModernBadge : ModernControl
{
    private int _count;
    private int _overflowCount = 99;
    private bool _showZero;
    private bool _dot;
    private ModernVisualStatus _status = ModernVisualStatus.Error;
    private string? _badgeText;

    public ModernBadge()
    {
        AccessibleRole = AccessibleRole.StaticText;
        TabStop = false;
        Size = new Size(40, 24);
    }

    [Category("Data"), DefaultValue(0)]
    public int Count { get => _count; set { if (_count == value) return; _count = Math.Max(0, value); UpdateAccessibleName(); Invalidate(); } }

    [Category("Data"), DefaultValue(99)]
    public int OverflowCount { get => _overflowCount; set { _overflowCount = Math.Max(1, value); UpdateAccessibleName(); Invalidate(); } }

    [Category("Behavior"), DefaultValue(false)]
    public bool ShowZero { get => _showZero; set { if (_showZero == value) return; _showZero = value; UpdateAccessibleName(); Invalidate(); } }

    [Category("Appearance"), DefaultValue(false)]
    public bool Dot { get => _dot; set { if (_dot == value) return; _dot = value; UpdateAccessibleName(); Invalidate(); } }

    [Category("Appearance"), DefaultValue(ModernVisualStatus.Error)]
    public ModernVisualStatus Status { get => _status; set { if (_status == value) return; _status = value; UpdateAccessibleName(); Invalidate(); } }

    [Category("Appearance"), DefaultValue(null)]
    public string? BadgeText { get => _badgeText; set { if (_badgeText == value) return; _badgeText = value; UpdateAccessibleName(); Invalidate(); } }

    [Browsable(false)]
    public string DisplayText => Dot ? string.Empty : !string.IsNullOrEmpty(BadgeText) ? BadgeText! : Count == 0 && !ShowZero ? string.Empty : Count > OverflowCount ? $"{OverflowCount}+" : Count.ToString();

    protected override void RenderContent(GdiCanvas canvas, Rectangle bounds)
    {
        var text = DisplayText;
        if (!Dot && text.Length == 0) return;
        var color = ModernStatusColors.Resolve(Theme, Status);
        if (Dot)
        {
            var diameter = Math.Min(ScaleLogical(8), Math.Min(bounds.Width, bounds.Height));
            canvas.FillEllipse(color, new RectangleF((bounds.Width - diameter) / 2f, (bounds.Height - diameter) / 2f, diameter, diameter));
            return;
        }
        var textSize = TextRenderer.MeasureText(text, Font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
        var height = Math.Min(bounds.Height, ScaleLogical(20));
        var width = Math.Min(bounds.Width, Math.Max(height, textSize.Width + ScaleLogical(12)));
        var rect = new RectangleF((bounds.Width - width) / 2f, (bounds.Height - height) / 2f, width, height);
        canvas.Fill(color, rect, height / 2f);
        canvas.DrawText(text, Font, Color.White, Rectangle.Round(rect), ContentAlignment.MiddleCenter);
    }

    private void UpdateAccessibleName() => AccessibleName = Dot ? Status.ToString() : DisplayText;
}
