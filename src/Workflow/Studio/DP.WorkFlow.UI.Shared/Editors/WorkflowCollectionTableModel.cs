using System.Collections;
using System.Globalization;
using System.Reflection;

namespace DP.WorkFlow.UI;

/// <summary>将常用 List、数组和 Dictionary 投影为双 UI 可编辑的二维表。</summary>
public sealed class WorkflowCollectionTableModel
{
    private readonly WorkflowPropertyEntry _entry;
    private readonly Type _itemType;
    private readonly Type? _dictionaryValueType;
    private readonly PropertyInfo[] _properties;

    /// <summary>初始化指定属性和元素结构对应的集合表格模型。</summary>
    /// <param name="entry">目标属性条目。</param>
    /// <param name="itemType">集合元素类型。</param>
    /// <param name="dictionaryValueType">字典值类型；非字典集合时为空。</param>
    /// <param name="properties">集合元素公开的可编辑属性。</param>
    private WorkflowCollectionTableModel(WorkflowPropertyEntry entry, Type itemType, Type? dictionaryValueType, PropertyInfo[] properties)
    {
        _entry = entry;
        _itemType = itemType;
        _dictionaryValueType = dictionaryValueType;
        _properties = properties;
    }

    /// <summary>获取集合表格的只读列名。</summary>
    public IReadOnlyList<string> Columns { get; private init; } = Array.Empty<string>();
    /// <summary>获取由属性当前值生成的只读文本行。</summary>
    public IReadOnlyList<IReadOnlyList<string>> Rows { get; private init; } = Array.Empty<IReadOnlyList<string>>();
    public bool IsDictionary => _dictionaryValueType is not null;

    /// <summary>尝试为受支持集合创建表格投影。</summary>
    /// <param name="entry">目标属性条目。</param>
    /// <param name="table">集合表格模型。</param>
    /// <returns>返回操作结果；具体含义参见方法说明。</returns>
    public static bool TryCreate(WorkflowPropertyEntry entry, out WorkflowCollectionTableModel? table)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var type = entry.ValueType;
        var dictionary = FindGeneric(type, typeof(IDictionary<,>));
        if (dictionary is not null && dictionary.GetGenericArguments()[0] == typeof(string))
        {
            var valueType = dictionary.GetGenericArguments()[1];
            if (!IsScalar(valueType)) { table = null; return false; }
            var dictionaryRows = ((IEnumerable?)entry.Value)?.Cast<object>().Select(item =>
            {
                var itemType = item.GetType();
                return (IReadOnlyList<string>)new[]
                {
                    Convert.ToString(itemType.GetProperty("Key")?.GetValue(item), CultureInfo.InvariantCulture) ?? string.Empty,
                    Convert.ToString(itemType.GetProperty("Value")?.GetValue(item), CultureInfo.InvariantCulture) ?? string.Empty
                };
            }).ToArray() ?? Array.Empty<IReadOnlyList<string>>();
            table = new WorkflowCollectionTableModel(entry, typeof(string), valueType, Array.Empty<PropertyInfo>())
            {
                Columns = new[] { "Key", "Value" }, Rows = dictionaryRows
            };
            return true;
        }

        var itemType = type.IsArray ? type.GetElementType() : FindGeneric(type, typeof(IEnumerable<>))?.GetGenericArguments()[0];
        if (itemType is null) { table = null; return false; }
        var properties = IsScalar(itemType)
            ? Array.Empty<PropertyInfo>()
            : itemType.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Where(property => property.CanRead && property.CanWrite && IsScalar(property.PropertyType))
                .ToArray();
        if (!IsScalar(itemType) && properties.Length == 0) { table = null; return false; }
        var columns = properties.Length == 0 ? new[] { "Value" } : properties.Select(property => property.Name).ToArray();
        var rows = ((IEnumerable?)entry.Value)?.Cast<object?>().Select(item =>
            (IReadOnlyList<string>)(properties.Length == 0
                ? new[] { Convert.ToString(item, CultureInfo.InvariantCulture) ?? string.Empty }
                : properties.Select(property => Convert.ToString(property.GetValue(item), CultureInfo.InvariantCulture) ?? string.Empty).ToArray()))
            .ToArray() ?? Array.Empty<IReadOnlyList<string>>();
        table = new WorkflowCollectionTableModel(entry, itemType, null, properties) { Columns = columns, Rows = rows };
        return true;
    }

    /// <summary>校验表格文本并整体写回节点属性。</summary>
    /// <param name="rows">表格行数据。</param>
    public void Apply(IEnumerable<IReadOnlyList<string>> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (_dictionaryValueType is not null)
        {
            var dictionary = (IDictionary)(Activator.CreateInstance(_entry.ValueType)
                ?? throw new InvalidOperationException($"无法创建 {_entry.ValueType.Name}。"));
            foreach (var row in rows)
            {
                if (row.Count < 2 || string.IsNullOrWhiteSpace(row[0])) continue;
                dictionary.Add(row[0].Trim(), ConvertText(row[1], _dictionaryValueType));
            }
            _entry.SetValue(dictionary);
            return;
        }

        var values = new List<object?>();
        foreach (var row in rows)
        {
            if (_properties.Length == 0)
            {
                if (row.Count > 0) values.Add(ConvertText(row[0], _itemType));
                continue;
            }
            var item = Activator.CreateInstance(_itemType)
                ?? throw new InvalidOperationException($"无法创建 {_itemType.Name}，集合项必须有无参构造函数。");
            for (var index = 0; index < _properties.Length && index < row.Count; index++)
                _properties[index].SetValue(item, ConvertText(row[index], _properties[index].PropertyType));
            values.Add(item);
        }
        if (_entry.ValueType.IsArray)
        {
            var array = Array.CreateInstance(_itemType, values.Count);
            for (var index = 0; index < values.Count; index++) array.SetValue(values[index], index);
            _entry.SetValue(array);
            return;
        }
        var list = (IList)(Activator.CreateInstance(_entry.ValueType)
            ?? Activator.CreateInstance(typeof(List<>).MakeGenericType(_itemType))
            ?? throw new InvalidOperationException($"无法创建 {_entry.ValueType.Name}。"));
        foreach (var value in values) list.Add(value);
        _entry.SetValue(list);
    }

    /// <summary>在类型及其接口中查找指定泛型类型定义。</summary>
    /// <param name="type">目标 CLR 类型。</param>
    /// <param name="definition">要查找的泛型类型定义。</param>
    /// <returns>返回操作结果；具体含义参见方法说明。</returns>
    private static Type? FindGeneric(Type type, Type definition)
    {
        if (type.IsGenericType && type.GetGenericTypeDefinition() == definition) return type;
        return type.GetInterfaces().FirstOrDefault(item => item.IsGenericType && item.GetGenericTypeDefinition() == definition);
    }

    /// <summary>判断类型是否适合使用单个文本单元格编辑。</summary>
    /// <param name="type">目标 CLR 类型。</param>
    /// <returns>返回操作结果；具体含义参见方法说明。</returns>
    private static bool IsScalar(Type type)
    {
        var core = Nullable.GetUnderlyingType(type) ?? type;
        return core.IsEnum || core.IsPrimitive || core == typeof(string) || core == typeof(decimal)
            || core == typeof(Guid) || core == typeof(DateTime) || core == typeof(DateTimeOffset) || core == typeof(TimeSpan);
    }

    /// <summary>将单元格文本转换为目标 CLR 类型。</summary>
    /// <param name="text">输入文本。</param>
    /// <param name="type">目标 CLR 类型。</param>
    /// <returns>返回操作结果；具体含义参见方法说明。</returns>
    private static object? ConvertText(string text, Type type)
    {
        var nullable = Nullable.GetUnderlyingType(type);
        var core = nullable ?? type;
        if (nullable is not null && string.IsNullOrWhiteSpace(text)) return null;
        if (core == typeof(string)) return text;
        if (core.IsEnum) return Enum.Parse(core, text, true);
        if (core == typeof(Guid)) return Guid.Parse(text);
        if (core == typeof(DateTime)) return DateTime.Parse(text, CultureInfo.InvariantCulture);
        if (core == typeof(DateTimeOffset)) return DateTimeOffset.Parse(text, CultureInfo.InvariantCulture);
        if (core == typeof(TimeSpan)) return TimeSpan.Parse(text, CultureInfo.InvariantCulture);
        return Convert.ChangeType(text, core, CultureInfo.InvariantCulture);
    }
}
