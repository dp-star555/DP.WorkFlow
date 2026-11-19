namespace DP.WorkFlow;

/// <summary>公共数据的形态。</summary>
public enum WorkflowPublicDataKind
{
    /// <summary>bool 信号：读、写、取反、等待。</summary>
    Signal = 0,

    /// <summary>数值：设置、加减、等待比较，带版本号。</summary>
    Value = 1,

    /// <summary>队列：入队、出队、移除、等待，建队时定类型。</summary>
    Queue = 2
}

/// <summary>
/// 一项公共数据的登记（R2-d）：键、形态、值类型、默认值、是否保持。先登记后使用；
/// 流程文件携带自己用到的登记项，加载到流程管理时逐项核对。
/// </summary>
/// <param name="Key">键，区分大小写，所有形态共用一个命名空间。</param>
/// <param name="Kind">形态。</param>
/// <param name="ValueType">值类型；bool 信号固定为 <see cref="E_SignalValueType.Boolean"/>。</param>
/// <param name="DefaultValueText">默认值文本（按值类型解析）；为空表示没有默认值（信号为 false，数值没有值，队列为空）。</param>
/// <param name="Persistent">是否保持（断电保存）；默认不保持。</param>
/// <param name="DisplayName">显示名。</param>
public sealed record WorkflowPublicDataDeclaration(
    string Key,
    WorkflowPublicDataKind Kind,
    E_SignalValueType ValueType,
    string? DefaultValueText = null,
    bool Persistent = false,
    string? DisplayName = null)
{
    /// <summary>创建 bool 信号登记。</summary>
    public static WorkflowPublicDataDeclaration Signal(string key, bool defaultValue = false, bool persistent = false) =>
        new(key, WorkflowPublicDataKind.Signal, E_SignalValueType.Boolean, defaultValue ? "true" : null, persistent);

    /// <summary>创建数值登记。</summary>
    public static WorkflowPublicDataDeclaration Value(string key, E_SignalValueType valueType, string? defaultValueText = null, bool persistent = false) =>
        new(key, WorkflowPublicDataKind.Value, valueType, defaultValueText, persistent);

    /// <summary>创建队列登记。</summary>
    public static WorkflowPublicDataDeclaration Queue(string key, E_SignalValueType valueType, bool persistent = false) =>
        new(key, WorkflowPublicDataKind.Queue, valueType, null, persistent);

    /// <summary>两项登记的形态和值类型是否一致（默认值、保持、显示名不参与比较）。</summary>
    /// <param name="other">另一项登记。</param>
    public bool IsCompatibleWith(WorkflowPublicDataDeclaration other) =>
        other is not null
        && string.Equals(Key, other.Key, StringComparison.Ordinal)
        && Kind == other.Kind
        && ValueType == other.ValueType;

    /// <summary>形态和类型的说明，例如"队列(Int32)"。</summary>
    public string Describe() => Kind switch
    {
        WorkflowPublicDataKind.Signal => "bool 信号",
        WorkflowPublicDataKind.Value => $"数值({ValueType})",
        _ => $"队列({ValueType})"
    };
}
