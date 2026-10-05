using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using ModernUI.Localization;
using ModernUI.WinForms;

namespace ModernPropertyGrid.WinForms;

public sealed partial class ModernPropertyGrid
{
    private bool RefreshEditorValue(string propertyName)
    {
        if (_selectedObject is null || !_editors.TryGetValue(propertyName, out var editor)) return false;
        var property = TypeDescriptor.GetProperties(_selectedObject)[propertyName];
        if (property is null) return false;
        var value = property.GetValue(_selectedObject);
        try
        {
            _refreshingEditor = true;
            switch (editor)
            {
                case ModernInput input:
                    input.Text = _presentationProvider.FormatValue(_selectedObject, property, value, _localizationContext.Current.Culture);
                    break;
                case ModernInputNumber number:
                    number.Value = Convert.ToDecimal(value ?? 0, CultureInfo.InvariantCulture);
                    break;
                case ModernSwitch toggle:
                    toggle.Checked = value is true;
                    break;
                case ModernSelect select:
                    select.SelectedItem = select.Items.Cast<object>().OfType<PropertyChoice>()
                        .FirstOrDefault(choice => Equals(choice.Value, value)) ?? value;
                    break;
                case ModernSelectMultiple multiSelect:
                    var values = EnumerateSelectedValues(value).ToArray();
                    multiSelect.SetSelectedItems(multiSelect.Items.Cast<object>().OfType<PropertyChoice>()
                        .Where(choice => values.Any(item => Equals(item, choice.Value))));
                    break;
                case Label label:
                    label.Text = _presentationProvider.FormatValue(_selectedObject, property, value, _localizationContext.Current.Culture);
                    break;
                default:
                    return false;
            }
            return true;
        }
        finally
        {
            _refreshingEditor = false;
        }
    }

    /// <summary>将尚未因焦点切换而验证的文本编辑值提交到当前对象。</summary>
    /// <returns>所有待提交文本均通过类型转换和属性验证时返回 <see langword="true"/>。</returns>
    public bool CommitPendingEdit()
    {
        if (_building || _selectedObject is null) return true;
        var succeeded = true;
        foreach (var entry in _editors.ToArray())
        {
            if (entry.Value is not ModernInput input) continue;
            if (input.Tag is string shown && shown == input.Text) continue; // 用户未编辑过的显示值不提交。
            var property = TypeDescriptor.GetProperties(_selectedObject)[entry.Key];
            if (property is null) continue;
            var current = _presentationProvider.FormatValue(
                _selectedObject, property, property.GetValue(_selectedObject), _localizationContext.Current.Culture);
            if (string.Equals(current, input.Text, StringComparison.CurrentCulture)) continue;
            succeeded &= Commit(property, input.Text);
        }
        return succeeded;
    }

    private bool Commit(PropertyDescriptor property, object? candidate)
    {
        if (_building || _selectedObject is null) return true;
        var oldValue = property.GetValue(_selectedObject);
        try
        {
            var converted = ConvertCandidate(property, candidate);
            property.SetValue(_selectedObject, converted);
            _details.ForeColor = _theme.TextSecondary;
            ShowDetails(property);
            PropertyValueChanged?.Invoke(this, new PropertyValueChangedEventArgs(property, oldValue, converted));
            return true;
        }
        catch (Exception exception)
        {
            var actual = exception is TargetInvocationException { InnerException: not null } ? exception.InnerException : exception;
            _details.ForeColor = _theme.Error;
            _details.Text = T(PropertyGridTextKeys.ValidationInvalid, new Dictionary<string, object?>
            {
                ["property"] = Presentation(property).DisplayName,
                ["message"] = actual.Message
            });
            ValidationFailed?.Invoke(this, new PropertyValidationFailedEventArgs(property, actual));
            return false;
        }
    }

    private static IEnumerable<object?> EnumerateSelectedValues(object? value) => value switch
    {
        null => [],
        string text => [text],
        System.Collections.IEnumerable values => values.Cast<object?>(),
        _ => [value]
    };

    private object ConvertMultiSelection(Type propertyType, IReadOnlyList<object> selectedItems)
    {
        var culture = _localizationContext.Current.Culture;
        var values = selectedItems.Select(item => Convert.ToString(item is PropertyChoice choice ? choice.Value : item, culture) ?? string.Empty).ToArray();
        if (propertyType == typeof(string[])) return values;
        if (propertyType == typeof(List<string>) || propertyType.IsAssignableFrom(typeof(List<string>))) return values.ToList();
        if (propertyType == typeof(HashSet<string>) || propertyType.IsAssignableFrom(typeof(HashSet<string>))) return values.ToHashSet(StringComparer.CurrentCulture);
        if (propertyType.IsAssignableFrom(typeof(string[]))) return values;
        throw new InvalidOperationException(T(PropertyGridTextKeys.MultiSelectUnsupported,
            new Dictionary<string, object?> { ["type"] = propertyType.Name }));
    }

    private object? ConvertCandidate(PropertyDescriptor property, object? candidate)
    {
        var nullable = Nullable.GetUnderlyingType(property.PropertyType);
        var target = nullable ?? property.PropertyType;
        if (candidate is string { Length: 0 } && nullable is not null) return null;
        if (candidate is null || target.IsInstanceOfType(candidate)) return candidate;
        var culture = _localizationContext.Current.Culture;
        if (candidate is string text && property.Converter.CanConvertFrom(typeof(string)))
            return property.Converter.ConvertFrom(null, culture, text);
        return Convert.ChangeType(candidate, target, culture);
    }
}
