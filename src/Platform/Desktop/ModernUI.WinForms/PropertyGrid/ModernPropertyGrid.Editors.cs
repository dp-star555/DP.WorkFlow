using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using ModernUI.Localization;
using ModernUI.WinForms;

namespace ModernPropertyGrid.WinForms;

public sealed partial class ModernPropertyGrid
{
    private Control CreateEditor(PropertyDescriptor property)
    {
        var value = property.GetValue(_selectedObject);
        var provider = _providers.FirstOrDefault(item => item.CanEdit(property));
        if (provider is not null)
            return provider.CreateEditor(new PropertyEditorContext(_selectedObject!, property, value, candidate => Commit(property, candidate)));
        if (property.IsReadOnly) return CreateReadOnly(property, value);
        if (property.Attributes[typeof(PropertyMultiSelectAttribute)] is PropertyMultiSelectAttribute multiSelect)
            return CreateMultiSelect(property, multiSelect, value);
        var coreType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
        if (coreType == typeof(bool)) return CreateBoolean(property, value);
        if (coreType.IsEnum) return CreateEnum(property, coreType, value);
        if (IsNumber(coreType)) return CreateNumber(property, coreType, value);
        var converter = property.Converter;
        if (converter.GetStandardValuesSupported() && converter.GetStandardValuesExclusive())
            return CreateStandardValues(property, converter, value);
        return CreateText(property, value);
    }

    private Control CreateReadOnly(PropertyDescriptor property, object? value) => new Label
    {
        Text = _presentationProvider.FormatValue(_selectedObject!, property, value, _localizationContext.Current.Culture),
        TextAlign = ContentAlignment.MiddleLeft,
        ForeColor = _theme.TextSecondary,
        AutoEllipsis = true
    };

    private Control CreateMultiSelect(PropertyDescriptor property, PropertyMultiSelectAttribute metadata, object? value)
    {
        var select = new ModernSelectMultiple
        {
            Theme = _theme,
            LocalizationContext = _localizationContext,
            AccessibleName = Presentation(property).DisplayName,
            PlaceholderText = null
        };
        var selectedValues = EnumerateSelectedValues(value).ToArray();
        var choices = metadata.Options.Select(option => new PropertyChoice(option,
            _presentationProvider.FormatValue(_selectedObject!, property, option, _localizationContext.Current.Culture))).ToArray();
        select.DisplayMember = nameof(PropertyChoice.Text);
        select.Items.AddRange(choices);
        select.SetSelectedItems(choices.Where(choice => selectedValues.Any(selected => Equals(selected, choice.Value))));
        select.SelectionChanged += (_, _) =>
        {
            if (!_building && !_refreshingEditor)
                Commit(property, ConvertMultiSelection(property.PropertyType, select.SelectedItems));
        };
        return select;
    }

    private Control CreateBoolean(PropertyDescriptor property, object? value)
    {
        var toggle = new ModernSwitch
        {
            Checked = value is true,
            Theme = _theme,
            LocalizationContext = _localizationContext,
            AccessibleName = Presentation(property).DisplayName
        };
        toggle.CheckedChanged += (_, _) =>
        {
            if (!_building && !_refreshingEditor) Commit(property, toggle.Checked);
        };
        return toggle;
    }

    private Control CreateEnum(PropertyDescriptor property, Type type, object? value) =>
        CreateCombo(property, Enum.GetValues(type).Cast<object>(), value);

    private Control CreateStandardValues(PropertyDescriptor property, TypeConverter converter, object? value) =>
        CreateCombo(property, converter.GetStandardValues()?.Cast<object>() ?? [], value);

    private Control CreateCombo(PropertyDescriptor property, IEnumerable<object> values, object? value)
    {
        var choices = values.Select(item => new PropertyChoice(item,
            _presentationProvider.FormatValue(_selectedObject!, property, item, _localizationContext.Current.Culture))).ToArray();
        var select = new ModernSelect
        {
            Theme = _theme,
            LocalizationContext = _localizationContext,
            AccessibleName = Presentation(property).DisplayName,
            DisplayMember = nameof(PropertyChoice.Text)
        };
        select.Items.AddRange(choices);
        select.SelectedItem = choices.FirstOrDefault(choice => Equals(choice.Value, value));
        select.SelectedIndexChanged += (_, _) =>
        {
            if (!_building && !_refreshingEditor && select.SelectedItem is PropertyChoice choice) Commit(property, choice.Value);
        };
        return select;
    }

    private Control CreateNumber(PropertyDescriptor property, Type type, object? value)
    {
        var range = property.Attributes[typeof(PropertyRangeAttribute)] as PropertyRangeAttribute;
        try
        {
            var numeric = new ModernInputNumber
            {
                Theme = _theme,
                LocalizationContext = _localizationContext,
                DecimalPlaces = range?.DecimalPlaces ?? (IsIntegral(type) ? 0 : 3),
                Minimum = Convert.ToDecimal(range?.Minimum ?? -1_000_000_000d, CultureInfo.InvariantCulture),
                Maximum = Convert.ToDecimal(range?.Maximum ?? 1_000_000_000d, CultureInfo.InvariantCulture),
                Increment = Convert.ToDecimal(range?.Increment ?? (IsIntegral(type) ? 1d : 0.1d), CultureInfo.InvariantCulture),
                ShowStepButtons = ShowNumericStepButtons,
                AccessibleName = Presentation(property).DisplayName
            };
            numeric.Value = ModernCompatibility.Clamp(Convert.ToDecimal(value ?? 0, CultureInfo.InvariantCulture), numeric.Minimum, numeric.Maximum);
            numeric.ValueChanged += (_, _) =>
            {
                if (!_building && !_refreshingEditor) Commit(property, ConvertNumber(numeric.Value, type));
            };
            return numeric;
        }
        catch (Exception exception) when (exception is OverflowException or FormatException)
        {
            return CreateText(property, value);
        }
    }

    private Control CreateText(PropertyDescriptor property, object? value)
    {
        var text = new ModernInput
        {
            Text = _presentationProvider.FormatValue(_selectedObject!, property, value, _localizationContext.Current.Culture),
            Theme = _theme,
            LocalizationContext = _localizationContext,
            AccessibleName = Presentation(property).DisplayName
        };
        // 只提交用户实际改过的文字：校验所有子控件时，未编辑的旧显示值不能覆盖别处已更新的属性。
        text.Tag = text.Text;
        text.InnerTextBox.Validated += (_, _) =>
        {
            if (Equals(text.Tag, text.Text)) return;
            if (Commit(property, text.Text)) text.Tag = text.Text;
        };
        return text;
    }
}
