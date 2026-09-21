namespace DP.WorkFlow;

/// <summary>表示一个 Payload 值在持久化前被采用的编码策略。</summary>
public enum WorkflowTraceValueKind
{
    /// <summary>直接保存的小值，例如数字、字符串、布尔、枚举、Guid 和时间。</summary>
    Scalar = 0,

    /// <summary>只保存类型和文本摘要，例如结果 DTO、集合、异常和复杂设备结果。</summary>
    Summary = 1,

    /// <summary>只保存身份和尺寸，例如图像、二进制、模型和设备资源。</summary>
    Reference = 2
}

/// <summary>
/// 表示经过统一编码的 Payload 值；不携带任意对象图，可安全交给外部保存。
/// </summary>
/// <param name="Kind">采用的编码策略。</param>
/// <param name="TypeName">原始值的运行时类型名。</param>
/// <param name="Text">面向分析人员的文本摘要；直接保存的小值也提供可读文本。</param>
/// <param name="Scalar">直接保存的小值本身；非 <see cref="WorkflowTraceValueKind.Scalar"/> 时为空。</param>
/// <param name="Length">集合项数、二进制长度或字符串原始长度；不适用时为空。</param>
/// <param name="Truncated">文本或集合是否因为容量上限被截断。</param>
public sealed record WorkflowTraceValue(
    WorkflowTraceValueKind Kind,
    string TypeName,
    string? Text = null,
    object? Scalar = null,
    long? Length = null,
    bool Truncated = false)
{
    /// <summary>还原为可放入旧 Trace 记录的值。</summary>
    /// <returns>直接保存的小值，或编码后的文本摘要。</returns>
    public object? ToObject() => Kind == WorkflowTraceValueKind.Scalar ? Scalar : Text;
}
