using System.Globalization;

namespace DP.WorkFlow;

/// <summary>队列移除模式。</summary>
public enum E_WorkflowQueueRemoveMode { DequeueFirst = 0, RemoveValue = 1 }
/// <summary>队列等待条件。</summary>
public enum E_WorkflowQueueWaitCondition { HasData = 0, FirstEquals = 1, FirstNotEquals = 2, CountGreaterThan = 3 }
/// <summary>队列节点结构化结果。</summary>
public sealed record WorkflowQueueNodeResult(string QueueKey, E_SignalValueType QueueValueType, bool Success, bool Changed, int Count, long Version, bool HasFirstValue, string FirstStringValue, int FirstInt32Value, long FirstInt64Value, double FirstDoubleValue, bool FirstBoolValue, bool HasAffectedValue, string AffectedStringValue, int AffectedInt32Value, long AffectedInt64Value, double AffectedDoubleValue, bool AffectedBoolValue);
/// <summary>队列等待结果。</summary>
public sealed record QueueWaitNodeResult(string QueueKey, E_SignalValueType QueueValueType, E_WorkflowQueueWaitCondition Condition, bool Success, int Count, long Version, bool HasFirstValue, string FirstStringValue, int FirstInt32Value, long FirstInt64Value, double FirstDoubleValue, bool FirstBoolValue);
/// <summary>队列初始化项。</summary>
public sealed class QueueInitializeItem { /// <summary>队列键。</summary>
    public string QueueKey { get; set; } = string.Empty; /// <summary>固定值类型。</summary>
    public E_SignalValueType ValueType { get; set; } }
/// <summary>批量队列初始化结果。</summary>
public sealed record QueueInitializeResult(int TotalCount, int CreatedCount, int ClearedCount, string KeysText);

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
