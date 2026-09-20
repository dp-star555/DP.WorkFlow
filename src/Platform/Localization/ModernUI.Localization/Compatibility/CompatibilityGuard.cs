namespace ModernUI.Localization;

internal static class CompatibilityGuard
{
    public static void NotNull(object? value, string parameterName)
    {
        if (value is null) throw new ArgumentNullException(parameterName);
    }
}
