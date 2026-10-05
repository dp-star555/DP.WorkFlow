using System.ComponentModel;

namespace DP.WorkFlow;

/// <summary>布尔信号写入模式。</summary>
public enum E_SignalStateWriteMode
{
    /// <summary>直接写入目标状态。</summary>
    Set = 0,
    /// <summary>对当前状态取反。</summary>
    Toggle = 1
}

/// <summary>表示信号设置结果。</summary>
public sealed record SignalSetNodeResult([property: DisplayName("信号键")] string SignalKey, [property: DisplayName("信号状态")] bool State, [property: DisplayName("写入方式")] E_SignalStateWriteMode WriteMode);

/// <summary>表示信号等待结果。</summary>
public sealed record SignalWaitNodeResult([property: DisplayName("信号键")] string SignalKey, [property: DisplayName("期望状态")] bool ExpectedState, [property: DisplayName("是否成功")] bool Success, [property: DisplayName("自动复位")] bool AutoReset);

/// <summary>数据信号值类型。</summary>
public enum E_SignalValueType { String = 0, Int32 = 1, Int64 = 2, Double = 3, Boolean = 4 }
/// <summary>数据信号值来源。</summary>
public enum E_SignalValueSource { Literal = 0, Binding = 1 }
/// <summary>数据信号等待模式。</summary>
public enum E_SignalValueWaitMode { NextWrite = 0, ExistingOrNextWrite = 1 }
/// <summary>数据信号写入模式。</summary>
public enum E_SignalValueWriteMode { Set = 0, Increment = 1, Decrement = 2 }
/// <summary>数据信号比较操作。</summary>
public enum E_SignalWaitOperator { Equal = 0, NotEqual = 1, GreaterThan = 2, GreaterOrEqual = 3, LessThan = 4, LessOrEqual = 5 }

/// <summary>数据信号快照。</summary>
public sealed record WorkflowValueSignalSnapshot(string SignalKey, object? Value, long Version, bool HasValue);
/// <summary>数据信号节点结果，保留旧版全部类型字段。</summary>
public sealed record SignalValueNodeResult([property: DisplayName("信号键")] string SignalKey, [property: DisplayName("值类型")] E_SignalValueType ValueType, [property: DisplayName("是否成功")] bool Success, [property: DisplayName("版本")] long Version, [property: DisplayName("是否有值")] bool HasValue, [property: DisplayName("字符串值")] string StringValue, [property: DisplayName("整数值")] int Int32Value, [property: DisplayName("长整数值")] long Int64Value, [property: DisplayName("浮点值")] double DoubleValue, [property: DisplayName("布尔值")] bool BoolValue, [property: DisplayName("写入方式")] E_SignalValueWriteMode WriteMode);
/// <summary>布尔信号初始化项。</summary>
public sealed class SignalStateInitItem { /// <summary>信号键。</summary>
    public string SignalKey { get; set; } = string.Empty; /// <summary>默认状态。</summary>
    public bool DefaultState { get; set; } }
/// <summary>布尔信号批量初始化结果。</summary>
public sealed record SignalStateBatchInitializeResult([property: DisplayName("总数")] int TotalCount, [property: DisplayName("键列表")] string KeysText);
/// <summary>数据信号初始化项。</summary>
public sealed class SignalValueInitItem { /// <summary>信号键。</summary>
    public string SignalKey { get; set; } = string.Empty; /// <summary>值类型。</summary>
    public E_SignalValueType ValueType { get; set; } /// <summary>默认值文本。</summary>
    public string DefaultValueText { get; set; } = string.Empty; }
/// <summary>数据信号批量初始化结果。</summary>
public sealed record SignalValueBatchInitializeResult([property: DisplayName("总数")] int TotalCount, [property: DisplayName("键列表")] string KeysText);
