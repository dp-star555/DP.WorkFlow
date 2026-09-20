using System.Runtime.InteropServices;

namespace ModernUI.WinForms;

/// <summary>把 ModernTheme 映射到必须保留原生行为的 Windows 子控件。</summary>
internal static class NativeControlTheme
{
    public static void ApplyExplorer(Control control, bool dark) =>
        Apply(control, dark ? "DarkMode_Explorer" : "Explorer");

    public static void ApplyExplorer(nint handle, bool dark) =>
        Apply(handle, dark ? "DarkMode_Explorer" : "Explorer");

    public static void ApplyComboBox(Control control, bool dark) =>
        Apply(control, dark ? "DarkMode_CFD" : "Explorer");

    public static void ApplyDateTimePicker(Control control, bool dark)
    {
        var themeClass = dark ? "DarkMode_CFD" : "Explorer";
        Apply(control, themeClass);
        if (!ModernCompatibility.IsWindows() || !control.IsHandleCreated) return;
        _ = EnumChildWindows(control.Handle, (handle, _) => { Apply(handle, themeClass); return true; }, 0);
    }

    private static void Apply(Control control, string themeClass)
    {
        if (!ModernCompatibility.IsWindows() || !control.IsHandleCreated) return;
        Apply(control.Handle, themeClass);
        control.Invalidate();
    }

    private static void Apply(nint handle, string themeClass)
    {
        if (!ModernCompatibility.IsWindows() || handle == 0) return;
        _ = SetWindowTheme(handle, themeClass, null);
    }

    private delegate bool EnumWindowCallback(nint window, nint parameter);

    [DllImport("user32.dll")]
    private static extern bool EnumChildWindows(nint parent, EnumWindowCallback callback, nint parameter);

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(nint window, string? subAppName, string? subIdList);
}
