namespace ModernUI.WinForms;

/// <summary>Resolves the nearest explicit modern theme, then falls back to a descendant or the framework default.</summary>
internal static class ModernThemeResolver
{
    public static ModernTheme Resolve(Control origin)
    {
        ModernCompatibility.ThrowIfNull(origin, nameof(origin));
        for (Control? current = origin; current is not null; current = current.Parent)
            if (ModernThemeControlAdapter.TryGetTheme(current, out var theme)) return theme;

        return FindDescendant(origin) ?? ModernUiSettings.DefaultTheme;
    }

    private static ModernTheme? FindDescendant(Control root)
    {
        foreach (Control child in root.Controls)
        {
            if (ModernThemeControlAdapter.TryGetTheme(child, out var theme)) return theme;
            var nested = FindDescendant(child);
            if (nested is not null) return nested;
        }
        return null;
    }

}
