namespace ModernUI.WinForms;

/// <summary>
/// Centralizes TextRenderer measurement and drawing flags. A caller describes the layout once so
/// the measured text and the painted text cannot silently disagree about padding or wrapping.
/// </summary>
internal static class ModernTextLayout
{
    public static TextFormatFlags Flags(bool wrap, bool verticalCenter = false,
        bool endEllipsis = false, HorizontalAlignment alignment = HorizontalAlignment.Left)
    {
        var flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix |
                    (wrap ? TextFormatFlags.WordBreak : TextFormatFlags.SingleLine) |
                    (verticalCenter ? TextFormatFlags.VerticalCenter : TextFormatFlags.Top);
        flags |= alignment switch
        {
            HorizontalAlignment.Center => TextFormatFlags.HorizontalCenter,
            HorizontalAlignment.Right => TextFormatFlags.Right,
            _ => TextFormatFlags.Left
        };
        if (endEllipsis) flags |= TextFormatFlags.EndEllipsis;
        return flags;
    }

    public static Size Measure(Graphics graphics, string? text, Font font, Size maximumSize,
        bool wrap, bool verticalCenter = false, bool endEllipsis = false,
        HorizontalAlignment alignment = HorizontalAlignment.Left)
    {
        ModernCompatibility.ThrowIfNull(graphics, nameof(graphics));
        ModernCompatibility.ThrowIfNull(font, nameof(font));
        if (string.IsNullOrEmpty(text)) return Size.Empty;
        return TextRenderer.MeasureText(graphics, text, font, NormalizeMaximum(maximumSize),
            Flags(wrap, verticalCenter, endEllipsis, alignment));
    }

    public static Size MeasurePhysical(string? text, Font font, int dpi, Size maximumSize,
        bool wrap, bool verticalCenter = false, bool endEllipsis = false,
        HorizontalAlignment alignment = HorizontalAlignment.Left)
    {
        ModernCompatibility.ThrowIfNull(font, nameof(font));
        if (string.IsNullOrEmpty(text)) return Size.Empty;
        using var bitmap = new Bitmap(1, 1);
        var physicalDpi = Math.Max(96, dpi);
        bitmap.SetResolution(physicalDpi, physicalDpi);
        using var graphics = Graphics.FromImage(bitmap);
        return Measure(graphics, text, font, maximumSize, wrap, verticalCenter, endEllipsis, alignment);
    }

    public static void Draw(Graphics graphics, string? text, Font font, Rectangle bounds, Color color,
        bool wrap, bool verticalCenter = false, bool endEllipsis = false,
        HorizontalAlignment alignment = HorizontalAlignment.Left)
    {
        ModernCompatibility.ThrowIfNull(graphics, nameof(graphics));
        ModernCompatibility.ThrowIfNull(font, nameof(font));
        TextRenderer.DrawText(graphics, text ?? string.Empty, font, bounds, color,
            Flags(wrap, verticalCenter, endEllipsis, alignment));
    }

    /// <summary>Returns a small physical-pixel allowance for CJK terminal glyph overhang.</summary>
    public static int TerminalGlyphSafety(int dpi) => Math.Max(1, ModernDpi.ScaleToInt(2, Math.Max(96, dpi)));

    private static Size NormalizeMaximum(Size maximumSize) => new(
        maximumSize.Width <= 0 ? 1 : maximumSize.Width,
        maximumSize.Height <= 0 ? 1 : maximumSize.Height);
}
