using ModernUI.WinForms;

namespace ModernUI.WinForms.Gallery;

/// <summary>Paints static catalog text without a nested layout/control tree.</summary>
internal sealed class GalleryCategoryCard : ModernControl
{
    private readonly GalleryDemoDescriptor _descriptor;
    private readonly ModernButton _openButton;
    private readonly Font _titleFont;

    public GalleryCategoryCard(GalleryDemoDescriptor descriptor, Font baseFont, Action open)
    {
        _descriptor = descriptor;
        _titleFont = new Font(baseFont.FontFamily, 10.5F, FontStyle.Bold);
        AccessibleName = descriptor.Title;
        TabStop = false;
        Width = 350;
        Height = 214;
        Margin = new Padding(8);
        _openButton = new ModernButton
        {
            Text = "打开案例",
            ButtonType = ModernButtonType.Primary,
            Icon = descriptor.Icon,
            Width = 116
        };
        _openButton.Click += (_, _) => open();
        Controls.Add(_openButton);
    }

    protected override void RenderContent(GdiCanvas canvas, Rectangle bounds)
    {
        var inset = ScaleLogical(1f);
        var card = RectangleF.Inflate(bounds, -inset, -inset);
        canvas.Fill(Theme.Container, card, ScaleLogical(8));
        canvas.Draw(Theme.BorderSecondary, ScaleLogical(1f), card, ScaleLogical(8));

        var left = ScaleLogical(18);
        var width = Math.Max(0, Width - ScaleLogical(36));
        canvas.DrawText(_descriptor.Title, _titleFont, Theme.Text,
            new Rectangle(left, ScaleLogical(16), width, ScaleLogical(30)), ContentAlignment.MiddleLeft);
        canvas.DrawTextWrapped(_descriptor.Description, Font, Theme.TextSecondary,
            new Rectangle(left, ScaleLogical(50), width, ScaleLogical(42)));
        canvas.DrawTextWrapped(string.Join("  ·  ", _descriptor.Features), Font, Theme.TextSecondary,
            new Rectangle(left, ScaleLogical(96), width, ScaleLogical(50)));
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        if (_openButton is null) return;
        var width = ScaleLogical(116);
        var height = ScaleLogical(34);
        _openButton.Bounds = new Rectangle(
            Math.Max(ScaleLogical(18), ClientSize.Width - width - ScaleLogical(18)),
            Math.Max(ScaleLogical(16), ClientSize.Height - height - ScaleLogical(18)),
            width,
            height);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _titleFont.Dispose();
        base.Dispose(disposing);
    }
}
