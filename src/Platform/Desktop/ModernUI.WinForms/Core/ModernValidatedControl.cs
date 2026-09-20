using System.ComponentModel;

namespace ModernUI.WinForms;

/// <summary>为输入类控件集中提供验证状态、消息和语义颜色。</summary>
public abstract class ModernValidatedControl : ModernControl, IModernValidationControl
{
    private ModernValidationState _validationState;
    private string _validationMessage = string.Empty;
    private string? _accessibleDescriptionWithoutValidation;

    [Category("Appearance"), DefaultValue(ModernValidationState.None)]
    [Description("输入值的统一验证状态。")]
    public ModernValidationState ValidationState
    {
        get => _validationState;
        set
        {
            if (_validationState == value) return;
            _validationState = value;
            InvalidateValidationVisual();
            if (IsHandleCreated) AccessibilityNotifyClients(AccessibleEvents.StateChange, -1);
        }
    }

    [Category("Data"), DefaultValue("")]
    [Description("当前验证结果的用户可读说明。")]
    public string ValidationMessage
    {
        get => _validationMessage;
        set
        {
            value ??= string.Empty;
            if (_validationMessage == value) return;
            if (_validationMessage.Length == 0 && value.Length > 0)
                _accessibleDescriptionWithoutValidation = AccessibleDescription ?? string.Empty;
            _validationMessage = value;
            SynchronizeValidationAccessibility();
            if (IsHandleCreated) AccessibilityNotifyClients(AccessibleEvents.DescriptionChange, -1);
        }
    }

    protected Color ResolveValidationBorder(Color fallback) =>
        ModernValidation.ResolveBorder(Theme, ValidationState, fallback);

    protected virtual void SynchronizeValidationAccessibility()
    {
        var originalDescription = _accessibleDescriptionWithoutValidation ?? AccessibleDescription ?? string.Empty;
        AccessibleDescription = _validationMessage.Length == 0
            ? originalDescription
            : string.Join(" ", new[] { originalDescription, _validationMessage }
                .Where(part => !string.IsNullOrWhiteSpace(part)));
        if (_validationMessage.Length == 0) _accessibleDescriptionWithoutValidation = null;
    }

    protected virtual void InvalidateValidationVisual() => Invalidate();
}
