using System.ComponentModel;
using System.Globalization;

namespace ModernPropertyGrid.WinForms;

/// <summary>属性在指定语言下的完整呈现；Key 字段始终保持稳定，不使用翻译文本充当身份。</summary>
public sealed record PropertyPresentation(
    string PropertyKey,
    string DisplayName,
    string CategoryKey,
    string CategoryText,
    string Description,
    string? Unit);

/// <summary>隔离业务属性元数据、选项和值格式化的本地化 seam。</summary>
public interface IPropertyPresentationProvider
{
    PropertyPresentation GetPresentation(object owner, PropertyDescriptor property, CultureInfo culture);
    string FormatValue(object owner, PropertyDescriptor property, object? value, CultureInfo culture);
}

/// <summary>保留标准 TypeDescriptor 行为的默认 Adapter。</summary>
public sealed class DefaultPropertyPresentationProvider : IPropertyPresentationProvider
{
    public static DefaultPropertyPresentationProvider Instance { get; } = new();
    private DefaultPropertyPresentationProvider() { }

    public PropertyPresentation GetPresentation(object owner, PropertyDescriptor property, CultureInfo culture)
    {
        ModernCompatibility.ThrowIfNull(owner, nameof(owner));
        ModernCompatibility.ThrowIfNull(property, nameof(property));
        ModernCompatibility.ThrowIfNull(culture, nameof(culture));
        var unit = property.Attributes[typeof(PropertyUnitAttribute)] is PropertyUnitAttribute metadata
            ? metadata.Unit : null;
        return new PropertyPresentation(property.Name, property.DisplayName, property.Category,
            property.Category, property.Description, unit);
    }

    public string FormatValue(object owner, PropertyDescriptor property, object? value, CultureInfo culture)
    {
        ModernCompatibility.ThrowIfNull(owner, nameof(owner));
        ModernCompatibility.ThrowIfNull(property, nameof(property));
        ModernCompatibility.ThrowIfNull(culture, nameof(culture));
        return property.Converter.ConvertToString(null, culture, value)
            ?? Convert.ToString(value, culture)
            ?? string.Empty;
    }
}
