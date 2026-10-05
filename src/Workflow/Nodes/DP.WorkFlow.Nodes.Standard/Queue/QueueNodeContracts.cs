using System.ComponentModel;
using System.Globalization;

namespace DP.WorkFlow;

/// <summary>队列移除模式。</summary>
public enum E_WorkflowQueueRemoveMode { DequeueFirst = 0, RemoveValue = 1 }
/// <summary>队列等待条件。</summary>
public enum E_WorkflowQueueWaitCondition { HasData = 0, FirstEquals = 1, FirstNotEquals = 2, CountGreaterThan = 3 }
/// <summary>队列节点结构化结果。</summary>
public sealed record WorkflowQueueNodeResult([property: DisplayName("队列键")] string QueueKey, [property: DisplayName("队列值类型")] E_SignalValueType QueueValueType, [property: DisplayName("是否成功")] bool Success, [property: DisplayName("是否变化")] bool Changed, [property: DisplayName("数量")] int Count, [property: DisplayName("版本")] long Version, [property: DisplayName("有队首元素")] bool HasFirstValue, [property: DisplayName("队首字符串值")] string FirstStringValue, [property: DisplayName("队首整数值")] int FirstInt32Value, [property: DisplayName("队首长整数值")] long FirstInt64Value, [property: DisplayName("队首浮点值")] double FirstDoubleValue, [property: DisplayName("队首布尔值")] bool FirstBoolValue, [property: DisplayName("有受影响元素")] bool HasAffectedValue, [property: DisplayName("受影响字符串值")] string AffectedStringValue, [property: DisplayName("受影响整数值")] int AffectedInt32Value, [property: DisplayName("受影响长整数值")] long AffectedInt64Value, [property: DisplayName("受影响浮点值")] double AffectedDoubleValue, [property: DisplayName("受影响布尔值")] bool AffectedBoolValue);
/// <summary>队列等待结果。</summary>
public sealed record QueueWaitNodeResult([property: DisplayName("队列键")] string QueueKey, [property: DisplayName("队列值类型")] E_SignalValueType QueueValueType, [property: DisplayName("条件")] E_WorkflowQueueWaitCondition Condition, [property: DisplayName("是否成功")] bool Success, [property: DisplayName("数量")] int Count, [property: DisplayName("版本")] long Version, [property: DisplayName("有队首元素")] bool HasFirstValue, [property: DisplayName("队首字符串值")] string FirstStringValue, [property: DisplayName("队首整数值")] int FirstInt32Value, [property: DisplayName("队首长整数值")] long FirstInt64Value, [property: DisplayName("队首浮点值")] double FirstDoubleValue, [property: DisplayName("队首布尔值")] bool FirstBoolValue);
/// <summary>队列初始化项。</summary>
public sealed class QueueInitializeItem { /// <summary>队列键。</summary>
    public string QueueKey { get; set; } = string.Empty; /// <summary>固定值类型。</summary>
    public E_SignalValueType ValueType { get; set; } }
/// <summary>批量队列初始化结果。</summary>
public sealed record QueueInitializeResult([property: DisplayName("总数")] int TotalCount, [property: DisplayName("新建数量")] int CreatedCount, [property: DisplayName("清空数量")] int ClearedCount, [property: DisplayName("键列表")] string KeysText);

internal static class WorkflowQueueNodeRuntime
{
    internal static IWorkflowQueueService GetService(IWorkflowNodeExecutionContext context, string key)
    {
        if (string.IsNullOrWhiteSpace(key)) throw new InvalidOperationException("QueueKey 为空。");
        return context.GetRequiredCapability<IWorkflowQueueService>();
    }
    internal static WorkflowQueueNodeResult BuildResult(string key, E_SignalValueType type, WorkflowQueueSnapshot? snapshot, bool changed, object? affected)
    {
        static T ConvertOr<T>(object? value, T fallback, Func<object, T> convert)
        {
            try { return value is null ? fallback : convert(value); }
            catch (Exception exception) when (exception is FormatException or InvalidCastException or OverflowException) { return fallback; }
        }
        var first = snapshot?.FirstValue;
        return new(key, snapshot?.ValueType ?? type, true, changed, snapshot?.Count ?? 0, snapshot?.Version ?? 0, snapshot?.HasFirstValue == true, Convert.ToString(first, CultureInfo.InvariantCulture) ?? string.Empty, ConvertOr(first, 0, Convert.ToInt32), ConvertOr(first, 0L, Convert.ToInt64), ConvertOr(first, 0d, Convert.ToDouble), ConvertOr(first, false, Convert.ToBoolean), affected is not null, Convert.ToString(affected, CultureInfo.InvariantCulture) ?? string.Empty, ConvertOr(affected, 0, Convert.ToInt32), ConvertOr(affected, 0L, Convert.ToInt64), ConvertOr(affected, 0d, Convert.ToDouble), ConvertOr(affected, false, Convert.ToBoolean));
    }
    internal static object Resolve(E_SignalValueSource source, string literal, WorkflowInput<object> binding, E_SignalValueType type, IWorkflowNodeExecutionContext context) => SignalValueSetNodeHandler.ConvertValue(source == E_SignalValueSource.Binding ? context.ResolveInput(binding) : literal, type);
}
