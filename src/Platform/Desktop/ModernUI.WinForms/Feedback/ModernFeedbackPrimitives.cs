namespace ModernUI.WinForms;

internal static class ModernFeedbackTypography
{
    public const string FontFamily = "Microsoft YaHei UI";
    public const float LogicalFontSize = 9F;

    public static Font Create() => new(FontFamily, LogicalFontSize, FontStyle.Regular, GraphicsUnit.Point);

    public static SizeF MeasureLogical(string text, FontStyle style, SizeF maximumSize, bool wrap)
    {
        if (string.IsNullOrEmpty(text)) return SizeF.Empty;
        using var bitmap = new Bitmap(1, 1);
        bitmap.SetResolution(96, 96);
        using var graphics = Graphics.FromImage(bitmap);
        using var font = new Font(FontFamily, LogicalFontSize, style, GraphicsUnit.Point);
        using var format = (StringFormat)StringFormat.GenericTypographic.Clone();
        format.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces;
        if (!wrap) format.FormatFlags |= StringFormatFlags.NoWrap;
        return graphics.MeasureString(text, font, maximumSize, format);
    }

    /// <summary>按 TextRenderer 的实际绘制规则在指定物理 DPI 下测量文本。</summary>
    public static Size MeasurePhysical(string text, FontStyle style, int dpi, Size maximumSize, bool wrap)
    {
        using var font = new Font(FontFamily, LogicalFontSize, style, GraphicsUnit.Point);
        return MeasurePhysical(text, font, dpi, maximumSize, wrap);
    }

    public static Size MeasurePhysical(string text, Font font, int dpi, Size maximumSize, bool wrap)
    {
        if (string.IsNullOrEmpty(text)) return Size.Empty;
        return ModernTextLayout.MeasurePhysical(text, font, dpi, maximumSize, wrap);
    }
}

/// <summary>定义状态型控件使用的语义颜色。</summary>
public enum ModernVisualStatus
{
    Default,
    Primary,
    Success,
    Warning,
    Error
}

internal static class ModernStatusColors
{
    public static Color Resolve(ModernTheme theme, ModernVisualStatus status) => status switch
    {
        ModernVisualStatus.Success => theme.Success,
        ModernVisualStatus.Warning => theme.Warning,
        ModernVisualStatus.Error => theme.Error,
        _ => theme.Primary
    };

    public static Color ResolveSurface(ModernTheme theme, ModernVisualStatus status) => status switch
    {
        ModernVisualStatus.Success => theme.SuccessSurface,
        ModernVisualStatus.Warning => theme.WarningSurface,
        ModernVisualStatus.Error => theme.ErrorSurface,
        _ => theme.PrimarySurface
    };
}
