using System.ComponentModel;

namespace ModernPropertyGrid.WinForms;

/// <summary>把属性放入分类下可展开的子组；各段是显示名称，不参与属性身份或读写。</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class PropertyGroupAttribute(params string[] path) : Attribute
{
    /// <summary>获取从外到内的子组路径。</summary>
    public IReadOnlyList<string> Path { get; } = path.ToArray();
}

/// <summary>指定属性在分类中的显示顺序。</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class PropertyOrderAttribute(int order) : Attribute
{
    /// <summary>获取显示顺序；较小的值优先显示。</summary>
    public int Order { get; } = order;
}

/// <summary>指定显示在属性值右侧的单位。</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class PropertyUnitAttribute(string unit) : Attribute
{
    /// <summary>获取单位文本。</summary>
    public string Unit { get; } = unit ?? throw new ArgumentNullException(nameof(unit));
}

/// <summary>为扩展编辑器指定稳定的查找键。</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class PropertyEditorKeyAttribute(string editorKey) : Attribute
{
    /// <summary>获取编辑器键。</summary>
    public string EditorKey { get; } = string.IsNullOrWhiteSpace(editorKey)
        ? throw new ArgumentException("编辑器键不能为空。", nameof(editorKey))
        : editorKey;
}

/// <summary>描述数值属性的范围、步进和小数位。</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class PropertyRangeAttribute(double minimum, double maximum, double increment = 1, int decimalPlaces = 2) : Attribute
{
    /// <summary>获取最小值。</summary>
    public double Minimum { get; } = minimum;
    /// <summary>获取最大值。</summary>
    public double Maximum { get; } = maximum;
    /// <summary>获取步进值。</summary>
    public double Increment { get; } = increment;
    /// <summary>获取小数位数。</summary>
    public int DecimalPlaces { get; } = decimalPlaces;
}

/// <summary>指定字符串集合属性可以选择的固定选项，并使用多选下拉编辑器。</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class PropertyMultiSelectAttribute(params string[] options) : Attribute
{
    /// <summary>获取允许选择的选项。</summary>
    public IReadOnlyList<string> Options { get; } = options?.Where(option => !string.IsNullOrWhiteSpace(option)).Distinct(StringComparer.CurrentCulture).ToArray()
        ?? throw new ArgumentNullException(nameof(options));
}

/// <summary>提供创建自定义行内属性编辑器所需的上下文。</summary>
public sealed class PropertyEditorContext
{
    private readonly Action<object?> _commit;

    internal PropertyEditorContext(object owner, PropertyDescriptor property, object? value, Action<object?> commit)
    {
        Owner = owner;
        Property = property;
        Value = value;
        _commit = commit;
    }

    /// <summary>获取属性所属对象。</summary>
    public object Owner { get; }
    /// <summary>获取标准 .NET 属性描述符。</summary>
    public PropertyDescriptor Property { get; }
    /// <summary>获取创建编辑器时的属性值。</summary>
    public object? Value { get; }
    /// <summary>提交编辑后的值，并进入控件统一验证流程。</summary>
    public void CommitValue(object? value) => _commit(value);
}

/// <summary>定义自定义属性编辑器 Provider。</summary>
public interface IPropertyEditorProvider
{
    /// <summary>获取 Provider 优先级；较大的值优先匹配。</summary>
    int Priority { get; }
    /// <summary>判断是否支持指定属性。</summary>
    bool CanEdit(PropertyDescriptor property);
    /// <summary>创建行内编辑器。</summary>
    Control CreateEditor(PropertyEditorContext context);
}

/// <summary>属性提交失败事件参数。</summary>
public sealed class PropertyValidationFailedEventArgs(PropertyDescriptor property, Exception exception) : EventArgs
{
    /// <summary>获取提交失败的属性。</summary>
    public PropertyDescriptor Property { get; } = property;
    /// <summary>获取失败原因。</summary>
    public Exception Exception { get; } = exception;
}

/// <summary>属性值修改完成事件参数。</summary>
public sealed class PropertyValueChangedEventArgs(PropertyDescriptor property, object? oldValue, object? newValue) : EventArgs
{
    /// <summary>获取已修改的属性。</summary>
    public PropertyDescriptor Property { get; } = property;
    /// <summary>获取原值。</summary>
    public object? OldValue { get; } = oldValue;
    /// <summary>获取新值。</summary>
    public object? NewValue { get; } = newValue;
}
