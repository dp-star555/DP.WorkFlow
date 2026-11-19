using System.Globalization;

namespace DP.WorkFlow;

// 公共数据的三种形态（R2-d 公共数据结论）：bool 信号、数值、队列。值类型只用 5 种基本类型。
// 契约放在 Abstractions，节点包按类型取用；实现和公共数据中心在 Runtime。

/// <summary>公共数据的值类型：只用 5 种基本类型。</summary>
public enum E_SignalValueType { String = 0, Int32 = 1, Int64 = 2, Double = 3, Boolean = 4 }

/// <summary>提供线程安全、UI 无关的命名布尔信号服务。</summary>
public interface IWorkflowSignalService
{
    /// <summary>读取信号；不存在时返回 <see langword="false"/>。</summary>
    bool Read(string signalKey);

    /// <summary>写入信号并唤醒等待者。</summary>
    void Write(string signalKey, bool value);

    /// <summary>等待信号达到期望状态，匹配返回 true，超时返回 false。</summary>
    ValueTask<bool> WaitAsync(
        string signalKey,
        bool expectedValue,
        TimeSpan timeout,
        CancellationToken cancellationToken);
}

/// <summary>提供带版本的强类型命名数据信号。</summary>
public interface IWorkflowValueSignalService
{
    /// <summary>读取当前快照。</summary>
    WorkflowValueSignalSnapshot? ReadSnapshot(string signalKey);
    /// <summary>写入值并递增版本。</summary>
    WorkflowValueSignalSnapshot WriteValue(string signalKey, object value);
    /// <summary>等待版本大于指定版本且满足谓词的值。</summary>
    ValueTask<WorkflowValueSignalSnapshot?> WaitValueAsync(string signalKey, long minVersion, Func<object?, bool> predicate, TimeSpan? timeout, CancellationToken cancellationToken);
}

/// <summary>数据信号快照。</summary>
public sealed record WorkflowValueSignalSnapshot(string SignalKey, object? Value, long Version, bool HasValue);

/// <summary>强类型队列快照。</summary>
public sealed record WorkflowQueueSnapshot(string QueueKey, E_SignalValueType ValueType, IReadOnlyList<object> Items, long Version)
{
    /// <summary>元素数。</summary>
    public int Count => Items.Count;
    /// <summary>是否存在首项。</summary>
    public bool HasFirstValue => Items.Count > 0;
    /// <summary>首项。</summary>
    public object? FirstValue => Items.Count > 0 ? Items[0] : null;
}

/// <summary>提供线程安全、固定值类型的命名队列。</summary>
public interface IWorkflowQueueService
{
    /// <summary>确保队列存在，返回是否新建。</summary>
    bool EnsureExists(string queueKey, E_SignalValueType valueType);
    /// <summary>清空队列并返回是否移除了数据。</summary>
    bool Clear(string queueKey, E_SignalValueType valueType, bool autoCreate);
    /// <summary>入队。</summary>
    bool EnqueueValue(string queueKey, object value, E_SignalValueType valueType, bool preventDuplicate, bool autoCreate);
    /// <summary>移除首项。</summary>
    bool TryDequeueValue(string queueKey, E_SignalValueType valueType, bool autoCreate, out object? value);
    /// <summary>移除第一条匹配值。</summary>
    bool RemoveValue(string queueKey, object value, E_SignalValueType valueType, bool autoCreate);
    /// <summary>读取快照。</summary>
    WorkflowQueueSnapshot? ReadSnapshot(string queueKey);
    /// <summary>等待队列条件成立。</summary>
    ValueTask<WorkflowQueueSnapshot?> WaitAsync(string queueKey, E_SignalValueType valueType, Func<WorkflowQueueSnapshot, bool> predicate, TimeSpan? timeout, bool autoCreate, CancellationToken cancellationToken);
}

/// <summary>公共数据值按 5 种基本类型转换。</summary>
public static class WorkflowPublicDataValues
{
    /// <summary>值类型对应的 CLR 类型。</summary>
    /// <param name="type">值类型。</param>
    public static Type GetClrType(E_SignalValueType type) => type switch
    {
        E_SignalValueType.String => typeof(string),
        E_SignalValueType.Int32 => typeof(int),
        E_SignalValueType.Int64 => typeof(long),
        E_SignalValueType.Double => typeof(double),
        E_SignalValueType.Boolean => typeof(bool),
        _ => throw new ArgumentOutOfRangeException(nameof(type))
    };

    /// <summary>
    /// 把值转换为指定基本类型（使用不变区域性）。文本按数值语义转换：整数类型接受小数文本并按
    /// <see cref="System.Convert.ToInt32(double)"/> 的规则取整，bool 接受 true/false 和数值文本（非 0 为 true），
    /// 与直接转换数值本身的结果一致（绑定来源值会先转为文本）。
    /// </summary>
    /// <param name="value">原始值，可以是文本。</param>
    /// <param name="type">目标类型。</param>
    /// <returns>转换后的值。</returns>
    public static object Convert(object? value, E_SignalValueType type)
    {
        if (value is string text && type is E_SignalValueType.Int32 or E_SignalValueType.Int64 or E_SignalValueType.Boolean)
        {
            var trimmed = text.Trim();
            if (type == E_SignalValueType.Boolean && bool.TryParse(trimmed, out var flag))
                return flag;
            if (type != E_SignalValueType.Boolean && long.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer))
                return type == E_SignalValueType.Int32 ? (object)System.Convert.ToInt32(integer) : integer;
            if (double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
                value = number;
        }
        return ConvertCore(value, type);
    }

    private static object ConvertCore(object? value, E_SignalValueType type) => type switch
    {
        E_SignalValueType.String => System.Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
        E_SignalValueType.Int32 => System.Convert.ToInt32(value, CultureInfo.InvariantCulture),
        E_SignalValueType.Int64 => System.Convert.ToInt64(value, CultureInfo.InvariantCulture),
        E_SignalValueType.Double => System.Convert.ToDouble(value, CultureInfo.InvariantCulture),
        E_SignalValueType.Boolean => System.Convert.ToBoolean(value, CultureInfo.InvariantCulture),
        _ => throw new ArgumentOutOfRangeException(nameof(type))
    };
}
