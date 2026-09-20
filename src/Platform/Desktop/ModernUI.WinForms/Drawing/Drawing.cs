using System.Drawing.Drawing2D;

namespace ModernUI.WinForms;

/// <summary>现代控件的轻量 GDI+ 绘制封装。</summary>
public sealed class GdiCanvas : IDisposable
{
    private readonly Graphics _graphics;
    private readonly GraphicsState _state;

    internal Graphics Graphics => _graphics;

    public GdiCanvas(Graphics graphics)
    {
        _graphics = graphics ?? throw new ArgumentNullException(nameof(graphics));
        _state = graphics.Save();
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
    }

    public void Fill(Color color, RectangleF bounds, float radius)
    {
        using var brush = new SolidBrush(color);
        using var path = Geometry.CreateRoundedRectangle(bounds, radius);
        _graphics.FillPath(brush, path);
    }

    public void Draw(Color color, float width, RectangleF bounds, float radius, bool centerStroke = false)
    {
        using var pen = new Pen(color, width)
        {
            Alignment = centerStroke ? PenAlignment.Center : PenAlignment.Inset
        };
        using var path = Geometry.CreateRoundedRectangle(bounds, radius);
        _graphics.DrawPath(pen, path);
    }

    public void FillEllipse(Color color, RectangleF bounds)
    {
        using var brush = new SolidBrush(color);
        _graphics.FillEllipse(brush, bounds);
    }

    public void DrawLines(Color color, float width, IReadOnlyList<PointF> points)
    {
        if (points.Count < 2) return;
        using var pen = new Pen(color, width)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };
        _graphics.DrawLines(pen, points.ToArray());
    }

    public void DrawIcon(ModernIconKind icon, Color color, RectangleF bounds, float strokeWidth) =>
        ModernIconRenderer.Draw(_graphics, icon, color, bounds, strokeWidth);

    public void DrawIcon(ModernIconKind icon, Color color, RectangleF bounds, float strokeWidth, float rotationDegrees)
    {
        var state = _graphics.Save();
        try
        {
            _graphics.TranslateTransform(bounds.Left + bounds.Width / 2f, bounds.Top + bounds.Height / 2f);
            _graphics.RotateTransform(rotationDegrees);
            _graphics.TranslateTransform(-(bounds.Left + bounds.Width / 2f), -(bounds.Top + bounds.Height / 2f));
            ModernIconRenderer.Draw(_graphics, icon, color, bounds, strokeWidth);
        }
        finally { _graphics.Restore(state); }
    }

    public void DrawImage(Image image, RectangleF bounds) => _graphics.DrawImage(image, bounds);

    public void DrawText(string? text, Font font, Color color, Rectangle bounds, ContentAlignment alignment)
    {
        ModernTextLayout.Draw(_graphics, text, font, bounds, color, wrap: false,
            verticalCenter: alignment is ContentAlignment.MiddleLeft or ContentAlignment.MiddleCenter or ContentAlignment.MiddleRight,
            endEllipsis: true,
            alignment: alignment is ContentAlignment.MiddleRight or ContentAlignment.TopRight
                ? HorizontalAlignment.Right
                : alignment is ContentAlignment.MiddleCenter or ContentAlignment.TopCenter
                    ? HorizontalAlignment.Center
                    : HorizontalAlignment.Left);
    }

    public void DrawTextWrapped(string? text, Font font, Color color, Rectangle bounds)
    {
        ModernTextLayout.Draw(_graphics, text, font, bounds, color, wrap: true, endEllipsis: true);
    }

    public void Dispose() => _graphics.Restore(_state);

    private static TextFormatFlags ToFlags(ContentAlignment alignment) => alignment switch
    {
        ContentAlignment.MiddleLeft => TextFormatFlags.Left | TextFormatFlags.VerticalCenter,
        ContentAlignment.MiddleRight => TextFormatFlags.Right | TextFormatFlags.VerticalCenter,
        ContentAlignment.TopLeft => TextFormatFlags.Left | TextFormatFlags.Top,
        ContentAlignment.TopCenter => TextFormatFlags.HorizontalCenter | TextFormatFlags.Top,
        ContentAlignment.TopRight => TextFormatFlags.Right | TextFormatFlags.Top,
        _ => TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
    };
}

internal static class ModernFocusVisual
{
    public static Color ResolveColor(ModernTheme theme, Color? accent = null)
    {
        if (!theme.FocusRing.IsEmpty && accent is null) return theme.FocusRing;
        var blend = theme.IsDark ? .48f : .35f;
        return Geometry.Blend(theme.Background, accent ?? theme.Primary, blend);
    }

    public static float StrokeWidth(float dpiScale) => Math.Max(1f, 1.5f * dpiScale);

    public static void Draw(GdiCanvas canvas, ModernTheme theme, RectangleF bounds, float radius,
        float dpiScale, Color? accent = null) =>
        canvas.Draw(ResolveColor(theme, accent), StrokeWidth(dpiScale), bounds, radius);
}

internal static class RoundedNativeControlRegion
{
    public static void Apply(Control control, int logicalRadius) => Apply(control, logicalRadius, control.DeviceDpi);

    public static void Apply(Control control, int logicalRadius, int dpi)
    {
        if (control.Width <= 0 || control.Height <= 0) return;
        var radius = ModernDpi.ScaleToInt(Math.Max(0, logicalRadius), Math.Max(96, dpi));
        using var path = Geometry.CreateRoundedRectangle(
            new RectangleF(0, 0, control.Width, control.Height), radius);
        var previous = control.Region;
        control.Region = new Region(path);
        previous?.Dispose();
    }
}

internal static class Geometry
{
    public static GraphicsPath CreateRoundedRectangle(RectangleF bounds, float radius)
    {
        var path = new GraphicsPath();
        var diameter = Math.Min(Math.Min(bounds.Width, bounds.Height), Math.Max(0, radius * 2));
        if (diameter <= 0)
        {
            path.AddRectangle(bounds);
            return path;
        }
        var arc = new RectangleF(bounds.X, bounds.Y, diameter, diameter);
        path.AddArc(arc, 180, 90);
        arc.X = bounds.Right - diameter;
        path.AddArc(arc, 270, 90);
        arc.Y = bounds.Bottom - diameter;
        path.AddArc(arc, 0, 90);
        arc.X = bounds.Left;
        path.AddArc(arc, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static Color Blend(Color from, Color to, float progress)
    {
        progress = ModernCompatibility.Clamp(progress, 0, 1);
        return Color.FromArgb(
            (int)(from.A + (to.A - from.A) * progress),
            (int)(from.R + (to.R - from.R) * progress),
            (int)(from.G + (to.G - from.G) * progress),
            (int)(from.B + (to.B - from.B) * progress));
    }
}
