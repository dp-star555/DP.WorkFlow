using System.Collections;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Runtime.InteropServices;

namespace DP.WorkFlow;

/// <summary>
/// 把节点提交的任意诊断数据编码成有界、可安全持久化的 Payload。
/// </summary>
/// <remarks>
/// 编码器不会递归序列化任意对象图：小值直接保存，集合和复杂对象只保存摘要，
/// 图像、二进制、流和原生句柄只保存身份、尺寸和类型。任何编码异常都会被就地吸收，
/// 避免诊断编码问题掩盖原始业务故障。
/// </remarks>
public sealed class WorkflowTracePayloadEncoder
{
    private const int MaxItemTextLength = 128;

    private static readonly string[] SensitiveKeyFragments =
    {
        "password", "passwd", "secret", "token", "credential", "connectionstring", "apikey", "privatekey"
    };

    private readonly WorkflowRunRecordingOptions _options;

    /// <summary>初始化使用指定容量上限的 Payload 编码器。</summary>
    /// <param name="options">提供单事件字节预算、字符串长度和集合项数上限。</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> 为空。</exception>
    public WorkflowTracePayloadEncoder(WorkflowRunRecordingOptions options) =>
        _options = options ?? throw new ArgumentNullException(nameof(options));

    /// <summary>编码一条事件的 Payload。</summary>
    /// <param name="data">节点提交的结构化数据；可以为空。</param>
    /// <param name="message">面向使用者的可选说明；非空时作为 <c>Message</c> 键进入 Payload。</param>
    /// <returns>按字节预算截断后的只读 Payload；没有可记录内容时返回 <see langword="null"/>。</returns>
    public IReadOnlyDictionary<string, WorkflowTraceValue>? Encode(
        IReadOnlyDictionary<string, object?>? data,
        string? message = null)
    {
        var budget = new EncodingBudget(_options.MaxEventBytes);
        var result = new Dictionary<string, WorkflowTraceValue>(StringComparer.Ordinal);
        if (!string.IsNullOrEmpty(message))
            result["Message"] = EncodeString(message, budget);
        if (data is not null)
        {
            foreach (var pair in data)
            {
                if (budget.Remaining <= 0)
                    break;
                result[pair.Key] = IsSensitive(pair.Key)
                    ? new WorkflowTraceValue(WorkflowTraceValueKind.Scalar, "Redacted", "[已脱敏]")
                    : EncodeSafely(pair.Value, budget);
            }
        }
        return result.Count == 0 ? null : new ReadOnlyDictionary<string, WorkflowTraceValue>(result);
    }

    private WorkflowTraceValue EncodeSafely(object? value, EncodingBudget budget)
    {
        try
        {
            return EncodeValue(value, budget);
        }
        catch
        {
            // 诊断编码失败不得掩盖原始业务故障；退化为类型名摘要。
            return new WorkflowTraceValue(
                WorkflowTraceValueKind.Summary,
                value?.GetType().FullName ?? "null",
                "[编码失败]");
        }
    }

    private WorkflowTraceValue EncodeValue(object? value, EncodingBudget budget)
    {
        switch (value)
        {
            case null:
                return new WorkflowTraceValue(WorkflowTraceValueKind.Scalar, "null", "null", null);
            case string text:
                return EncodeString(text, budget);
            case bool flag:
                return Scalar(typeof(bool), flag, flag ? "true" : "false", 5, budget);
            case char character:
                return Scalar(typeof(char), character, character.ToString(), 1, budget);
            case byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal:
                return Scalar(value.GetType(), value, Format(value), 8, budget);
            case Enum:
                return Scalar(value.GetType(), value, value.ToString() ?? string.Empty, 16, budget);
            case Guid or DateTime or DateTimeOffset or TimeSpan or DateOnly or TimeOnly:
                return Scalar(value.GetType(), value, Format(value), 32, budget);
            case byte[] bytes:
                return new WorkflowTraceValue(
                    WorkflowTraceValueKind.Reference, bytes.GetType().FullName!, "字节数组", null, bytes.LongLength);
            case Array array when IsBinaryLike(array):
                return new WorkflowTraceValue(
                    WorkflowTraceValueKind.Reference, array.GetType().FullName!, "大型数组", null, array.LongLength);
            case Stream stream:
                return new WorkflowTraceValue(
                    WorkflowTraceValueKind.Reference, stream.GetType().FullName!, "数据流", null, TryGetStreamLength(stream));
            case IntPtr or UIntPtr or SafeHandle:
                return new WorkflowTraceValue(
                    WorkflowTraceValueKind.Reference, value.GetType().FullName!, "原生句柄");
            case Exception exception:
                return Summary(exception.GetType().FullName!, $"{exception.GetType().Name}: {exception.Message}", null, budget);
            case IDictionary dictionary:
                return Summary(
                    dictionary.GetType().FullName!,
                    $"字典，{dictionary.Count} 项",
                    dictionary.Count,
                    budget);
            case IEnumerable sequence:
                return EncodeSequence(sequence, budget);
            default:
                return Summary(value.GetType().FullName!, Describe(value), null, budget);
        }
    }

    private WorkflowTraceValue EncodeString(string text, EncodingBudget budget)
    {
        var truncated = text.Length > _options.MaxStringLength;
        var trimmed = truncated ? text[.._options.MaxStringLength] : text;
        budget.Remaining -= trimmed.Length;
        return new WorkflowTraceValue(
            WorkflowTraceValueKind.Scalar,
            typeof(string).FullName!,
            trimmed,
            trimmed,
            text.Length,
            truncated);
    }

    private WorkflowTraceValue EncodeSequence(IEnumerable sequence, EncodingBudget budget)
    {
        var typeName = sequence.GetType().FullName ?? "IEnumerable";
        var texts = new List<string>();
        var truncated = false;
        foreach (var item in sequence)
        {
            if (texts.Count >= _options.MaxCollectionItems)
            {
                truncated = true;
                break;
            }
            texts.Add(TrimItem(item));
        }
        var total = sequence is ICollection collection ? collection.Count : (int?)null;
        var text = string.Join(", ", texts);
        budget.Remaining -= text.Length;
        return new WorkflowTraceValue(
            WorkflowTraceValueKind.Summary,
            typeName,
            text,
            null,
            total ?? texts.Count,
            truncated);
    }

    private static WorkflowTraceValue Summary(string typeName, string? text, long? length, EncodingBudget budget)
    {
        var trimmed = text is null ? null : text.Length > MaxItemTextLength ? text[..MaxItemTextLength] : text;
        budget.Remaining -= trimmed?.Length ?? 0;
        return new WorkflowTraceValue(WorkflowTraceValueKind.Summary, typeName, trimmed, null, length);
    }

    private static WorkflowTraceValue Scalar(Type type, object value, string text, int estimate, EncodingBudget budget)
    {
        budget.Remaining -= estimate;
        return new WorkflowTraceValue(WorkflowTraceValueKind.Scalar, type.FullName!, text, value);
    }

    private static string TrimItem(object? item)
    {
        if (item is null)
            return "null";
        string text;
        try
        {
            text = Convert.ToString(item, CultureInfo.InvariantCulture) ?? item.GetType().Name;
        }
        catch
        {
            return item.GetType().Name;
        }
        return text.Length > MaxItemTextLength ? text[..MaxItemTextLength] : text;
    }

    private static string Describe(object value)
    {
        try
        {
            return value.ToString() ?? value.GetType().Name;
        }
        catch
        {
            return value.GetType().Name;
        }
    }

    private static string Format(object value) =>
        Convert.ToString(value, CultureInfo.InvariantCulture) ?? value.GetType().Name;

    private static bool IsBinaryLike(Array array)
    {
        if (array.Length <= 1)
            return false;
        var elementType = array.GetType().GetElementType();
        return elementType is not null
            && (elementType.IsPrimitive || elementType == typeof(decimal) || elementType == typeof(Guid));
    }

    private static long? TryGetStreamLength(Stream stream)
    {
        try
        {
            return stream.CanSeek ? stream.Length : null;
        }
        catch
        {
            return null;
        }
    }

    private static bool IsSensitive(string key)
    {
        foreach (var fragment in SensitiveKeyFragments)
        {
            if (key.Contains(fragment, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private sealed class EncodingBudget
    {
        internal EncodingBudget(int remaining) => Remaining = remaining;

        internal int Remaining { get; set; }
    }
}
