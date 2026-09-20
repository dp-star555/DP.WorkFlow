namespace ModernUI.WinForms;

/// <summary>表示现代输入控件的验证结果。</summary>
public enum ModernValidationState
{
    None,
    Pending,
    Success,
    Warning,
    Error
}

/// <summary>由支持统一验证外观的现代输入控件实现。</summary>
public interface IModernValidationControl
{
    ModernValidationState ValidationState { get; set; }
    string ValidationMessage { get; set; }
}

internal static class ModernValidation
{
    public static Color ResolveBorder(ModernTheme theme, ModernValidationState state, Color fallback) => state switch
    {
        ModernValidationState.Pending => theme.Primary,
        ModernValidationState.Success => theme.Success,
        ModernValidationState.Warning => theme.Warning,
        ModernValidationState.Error => theme.Error,
        _ => fallback
    };

    public static bool IsEmphasized(ModernValidationState state) => state != ModernValidationState.None;
}
