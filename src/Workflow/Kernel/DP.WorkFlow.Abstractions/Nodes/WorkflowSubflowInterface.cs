using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DP.WorkFlow;

/// <summary>子流程手动输入值的类型；绑定来源的输入类型跟随来源，不使用此值。</summary>
public enum E_WorkflowSubflowValueType
{
    /// <summary>整数（int）。</summary>
    Integer = 0,

    /// <summary>小数（double）。</summary>
    Number = 1,

    /// <summary>布尔值（bool）。</summary>
    Boolean = 2,

    /// <summary>文本（string）。</summary>
    Text = 3
}

/// <summary>
/// 复合节点子流程接口中的一个输入。来源绑定父画布节点输出时，类型自动跟随来源；
/// 没有来源时使用手动值，类型由 <see cref="ValueType"/> 指定。子画布内通过“启动节点.名称”读取。
/// </summary>
public sealed class WorkflowSubflowInput
{
    /// <summary>输入名称；子画布中按此名称从启动节点读取。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>父画布中的来源节点输出；为空时使用手动值。</summary>
    public WorkflowBindingKey? Source { get; set; }

    /// <summary>手动值的类型。</summary>
    public E_WorkflowSubflowValueType ValueType { get; set; }

    /// <summary>手动值；保存后重新加载时可能是 <see cref="JsonElement"/>，读取时按 <see cref="ValueType"/> 转换。</summary>
    public object? Value { get; set; }

    /// <summary>手动值类型对应的运行时类型。</summary>
    public static Type ClrType(E_WorkflowSubflowValueType valueType) => valueType switch
    {
        E_WorkflowSubflowValueType.Integer => typeof(int),
        E_WorkflowSubflowValueType.Number => typeof(double),
        E_WorkflowSubflowValueType.Boolean => typeof(bool),
        E_WorkflowSubflowValueType.Text => typeof(string),
        _ => throw new ArgumentOutOfRangeException(nameof(valueType), valueType, null)
    };

    /// <summary>把手动值转换为 <see cref="ValueType"/> 对应的类型。</summary>
    /// <param name="value">转换成功时返回类型化的值。</param>
    /// <param name="error">转换失败时的原因。</param>
    /// <returns>手动值有效时返回 <see langword="true"/>。</returns>
    public bool TryGetManualValue(out object? value, out string? error)
    {
        value = null;
        error = null;
        var raw = Value is JsonElement element ? FromJson(element) : Value;
        if (raw is null)
        {
            if (ValueType == E_WorkflowSubflowValueType.Text)
            {
                value = string.Empty;
                return true;
            }
            error = "未填写值。";
            return false;
        }
        var text = Convert.ToString(raw, CultureInfo.InvariantCulture) ?? string.Empty;
        switch (ValueType)
        {
            case E_WorkflowSubflowValueType.Text:
                value = text;
                return true;
            case E_WorkflowSubflowValueType.Integer when raw is int number:
                value = number;
                return true;
            case E_WorkflowSubflowValueType.Integer when int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed):
                value = parsed;
                return true;
            case E_WorkflowSubflowValueType.Number when raw is double number:
                value = number;
                return true;
            case E_WorkflowSubflowValueType.Number when double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed):
                value = parsed;
                return true;
            case E_WorkflowSubflowValueType.Boolean when raw is bool flag:
                value = flag;
                return true;
            case E_WorkflowSubflowValueType.Boolean when bool.TryParse(text, out var parsed):
                value = parsed;
                return true;
            default:
                error = $"值“{text}”不是有效的{DisplayName(ValueType)}。";
                return false;
        }
    }

    /// <summary>手动值类型的中文名称。</summary>
    public static string DisplayName(E_WorkflowSubflowValueType valueType) => valueType switch
    {
        E_WorkflowSubflowValueType.Integer => "整数",
        E_WorkflowSubflowValueType.Number => "小数",
        E_WorkflowSubflowValueType.Boolean => "布尔",
        E_WorkflowSubflowValueType.Text => "文本",
        _ => valueType.ToString()
    };

    private static object? FromJson(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.String => element.GetString(),
        _ => element.GetRawText()
    };
}

/// <summary>
/// 复合节点子流程接口中的一个输出：来源绑定子画布节点的输出，类型自动跟随来源。
/// 父画布中通过“复合节点.名称”读取。
/// </summary>
public sealed class WorkflowSubflowOutput
{
    /// <summary>输出名称；父画布中按此名称从复合节点读取。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>子画布中的来源节点输出。</summary>
    public WorkflowBindingKey? Source { get; set; }
}

/// <summary>
/// 子画布的入口节点（启动节点）：所在复合节点声明的输入作为它的输出成员，子画布中的节点像绑定普通节点一样读取。
/// 成员由设计器和编译器按所在复合节点的接口注入，不保存。
/// </summary>
public interface IWorkflowSubflowEntryNode : IWorkflowNodeModel
{
    /// <summary>所在复合节点声明的输入成员及其类型；不在子画布中时为空。</summary>
    IReadOnlyList<WorkflowBindableMember> SubflowInputs { get; set; }
}

/// <summary>子流程的输入或输出值集合，按名称读取（绑定路径中的一段即名称）。</summary>
public sealed class WorkflowSubflowValues : IWorkflowKeyedOutput, IWorkflowResultRows
{
    private readonly IReadOnlyDictionary<string, object?> _values;

    /// <summary>不含任何值的集合。</summary>
    public static WorkflowSubflowValues Empty { get; } = new(new Dictionary<string, object?>());

    /// <summary>不含任何输入的只读字典。</summary>
    public static IReadOnlyDictionary<string, object?> EmptyInputs { get; } =
        new System.Collections.ObjectModel.ReadOnlyDictionary<string, object?>(new Dictionary<string, object?>());

    /// <summary>用按名称索引的值创建集合。</summary>
    /// <param name="values">名称到值的映射；名称区分大小写。</param>
    public WorkflowSubflowValues(IReadOnlyDictionary<string, object?> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        _values = new Dictionary<string, object?>(values, StringComparer.Ordinal);
    }

    /// <inheritdoc />
    [DisplayName("各项值")]
    [JsonIgnore]
    public IReadOnlyList<KeyValuePair<string, object?>> ResultRows => _values.ToArray();

    /// <summary>按名称读取值。</summary>
    /// <param name="key">输入或输出名称。</param>
    /// <param name="value">找到时返回值，可以为空。</param>
    /// <returns>名称存在时返回 <see langword="true"/>。</returns>
    public bool TryGetKeyedValue(string key, out object? value) => _values.TryGetValue(key, out value);

    /// <inheritdoc />
    public string DescribeMissingKey(string key) => $"子流程接口中不存在名为 {key} 的项。";
}
