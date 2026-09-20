namespace ModernUI.WinForms;

/// <summary>统一绘制纵向和横向胶囊形滚动滑块。</summary>
internal static class ModernScrollThumbRenderer
{
    public const int DefaultThickness = 6;
    public const int HoverThickness = 8;
    public const int MinimumLength = 28;

    public static void Draw(Graphics graphics, RectangleF bounds, Color color)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0) return;
        var radius = Math.Min(bounds.Width, bounds.Height) / 2f;
        var previous = graphics.SmoothingMode;
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        try
        {
            using var brush = new SolidBrush(color);
            using var path = Geometry.CreateRoundedRectangle(bounds, radius);
            graphics.FillPath(brush, path);
        }
        finally { graphics.SmoothingMode = previous; }
    }
}
