using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;

namespace ModernUI.WinForms;

/// <summary>用于表示局部后台工作的现代加载指示器。</summary>
[Description("ModernSpinner 现代加载指示器")]
[DisplayName("现代加载指示器")]
[ToolboxBitmap(typeof(ModernSpinner), "Toolbox.Icons.Indicators.bmp")]
[ToolboxItem(true)]
public sealed class ModernSpinner : ModernControl
{
    private const int FrameInterval = 70;
    private readonly System.Windows.Forms.Timer _timer;
    private bool _spinning;
    private int _phase;
    private int _indicatorSize = 18;

    public ModernSpinner()
    {
        AccessibleRole = AccessibleRole.Animation;
        TabStop = false;
        Size = new Size(120, 40);
        _timer = new System.Windows.Forms.Timer { Interval = FrameInterval };
        _timer.Tick += (_, _) =>
        {
            var inViewport = ModernAnimationVisibility.IsInVisibleViewport(this);
            ModernAnimationVisibility.UseInterval(_timer,
                inViewport ? FrameInterval : ModernAnimationVisibility.HiddenPollInterval);
            if (!inViewport) return;
            _phase = (_phase + 1) % 8;
            Invalidate();
        };
    }

    [Category("Behavior"), DefaultValue(false)]
    public bool Spinning
    {
        get => _spinning;
        set { if (_spinning == value) return; _spinning = value; UpdateAnimation(); Invalidate(); AccessibilityNotifyClients(AccessibleEvents.StateChange, -1); }
    }

    [Category("Appearance"), DefaultValue(18)]
    public int IndicatorSize { get => _indicatorSize; set { var next = Math.Max(8, value); if (_indicatorSize == next) return; _indicatorSize = next; Invalidate(); } }

    [Category("Appearance"), DefaultValue(null)]
    [AllowNull]
    public override string Text { get => base.Text; set { base.Text = value; AccessibleName = value; Invalidate(); } }

    protected override void RenderContent(GdiCanvas canvas, Rectangle bounds)
    {
        if (!Spinning) return;
        var size = Math.Min(ScaleLogical(IndicatorSize), bounds.Height - ScaleLogical(4));
        var textWidth = string.IsNullOrEmpty(Text) ? 0 : TextRenderer.MeasureText(Text, Font, Size.Empty, TextFormatFlags.NoPadding).Width;
        var gap = textWidth == 0 ? 0 : ScaleLogical(8);
        var left = (bounds.Width - size - gap - textWidth) / 2f;
        var center = new PointF(left + size / 2f, bounds.Height / 2f);
        var inner = size * .24f;
        var outer = size * .47f;
        for (var index = 0; index < 8; index++)
        {
            var angle = Math.PI * 2 * index / 8 - Math.PI / 2;
            var alpha = 45 + 210 * ((index - _phase + 8) % 8) / 7;
            canvas.DrawLines(Color.FromArgb(alpha, Theme.Primary), ScaleLogical(1.8f),
            [
                new PointF(center.X + (float)Math.Cos(angle) * inner, center.Y + (float)Math.Sin(angle) * inner),
                new PointF(center.X + (float)Math.Cos(angle) * outer, center.Y + (float)Math.Sin(angle) * outer)
            ]);
        }
        if (textWidth > 0)
            canvas.DrawText(Text, Font, Theme.TextSecondary,
                new Rectangle((int)(left + size + gap), 0, textWidth, bounds.Height), ContentAlignment.MiddleLeft);
    }

    protected override void OnVisibleChanged(EventArgs e) { base.OnVisibleChanged(e); UpdateAnimation(); }
    protected override void Dispose(bool disposing) { if (disposing) _timer.Dispose(); base.Dispose(disposing); }
    private void UpdateAnimation() => _timer.Enabled = Spinning && Visible && ModernUiSettings.EffectiveAnimationsEnabled;
}
