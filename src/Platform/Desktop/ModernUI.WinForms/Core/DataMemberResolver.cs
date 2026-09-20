using System.ComponentModel;

namespace ModernUI.WinForms;

/// <summary>统一解析选择控件的数据成员路径，避免不同 WinForms ListControl 的绑定行为分叉。</summary>
internal static class DataMemberResolver
{
    public static object? Resolve(object? item, string memberPath)
    {
        if (string.IsNullOrWhiteSpace(memberPath)) return item;
        object? value = item;
        var valueType = item?.GetType();
        foreach (var member in memberPath.Split('.'))
        {
            if (valueType is null) return null;
            var properties = value is null
                ? TypeDescriptor.GetProperties(valueType)
                : TypeDescriptor.GetProperties(value);
            var property = properties.Find(member, true)
                ?? throw new InvalidOperationException(
                    $"Data member '{memberPath}' cannot be resolved for {valueType.Name}.");
            value = value is null ? null : property.GetValue(value);
            valueType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
        }
        return value;
    }
}
