using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.InteropServices;

namespace DP.WorkFlow;

/// <summary>节点已提交输出的稳定键与安全摘要集合。</summary>
/// <param name="OutputType">输出值的运行时类型名；空输出为空。</param>
/// <param name="Values">稳定输出键到原始值；复杂值的编码交给统一 Payload 编码器。</param>
/// <param name="Diagnostics">自动提取过程中的降级诊断；空表示提取完整。</param>
internal sealed record WorkflowOutputValueSet(
    string? OutputType,
    IReadOnlyDictionary<string, object?> Values,
    IReadOnlyList<string> Diagnostics);

/// <summary>
/// 从正式提交的节点输出中自动提取可被数据绑定引用的稳定键和值。
/// </summary>
/// <remarks>
/// <para>标量、二进制、集合、字典、流、原生句柄和异常等根值统一使用 <c>$</c>，不做属性展开；
/// 其他普通结果 DTO 只提取第一层公开可读属性，不递归展开未知对象。
/// 没有任何公开可读属性的对象同样回退到 <c>$</c>，保证每个已提交输出都至少有一个稳定键。</para>
/// <para>提取器不负责序列化：属性值继续交给 <see cref="WorkflowTracePayloadEncoder"/>，
/// 由统一策略决定标量、摘要还是引用，因此图像、像素和句柄不会因为自动键值提取而被完整持久化。</para>
/// </remarks>
internal sealed class WorkflowOutputValueExtractor
{
    /// <summary>标量根输出和不可展开根值使用的统一键。</summary>
    internal const string RootKey = "$";

    private readonly int _maxProperties;
    private readonly ConcurrentDictionary<Type, PropertyInfo[]> _plans = new();

    /// <summary>初始化提取器。</summary>
    /// <param name="maxProperties">单个输出 DTO 最多提取的公开属性个数。</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxProperties"/> 非正。</exception>
    internal WorkflowOutputValueExtractor(int maxProperties)
    {
        if (maxProperties <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxProperties));
        _maxProperties = maxProperties;
    }

    /// <summary>提取一次已提交输出的稳定键和值。</summary>
    /// <param name="output">节点正式提交的输出值；可以为空。</param>
    /// <returns>稳定键到原始值的映射，以及需要上报的降级诊断。</returns>
    internal WorkflowOutputValueSet Extract(object? output)
    {
        if (ShouldUseRootKey(output))
        {
            var rootValues = new Dictionary<string, object?>(StringComparer.Ordinal) { [RootKey] = output };
            return new WorkflowOutputValueSet(output?.GetType().FullName, rootValues, Array.Empty<string>());
        }

        var outputType = output!.GetType();
        var properties = _plans.GetOrAdd(outputType, static type => BuildPlan(type));
        if (properties.Length == 0)
        {
            // 没有任何公开可读属性的对象（不透明结果、只有字段、只有非公开成员）无法展开成属性键。
            // 回退到根键，保证"每个已提交输出都至少有一个稳定键"，由统一编码器给出类型摘要。
            var opaqueValues = new Dictionary<string, object?>(StringComparer.Ordinal) { [RootKey] = output };
            return new WorkflowOutputValueSet(outputType.FullName, opaqueValues, Array.Empty<string>());
        }

        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        var diagnostics = new List<string>();
        foreach (var property in properties)
        {
            if (values.Count >= _maxProperties)
            {
                diagnostics.Add(
                    $"输出类型 {outputType.Name} 的公开属性超过上限 {_maxProperties}，其余输出键已截断。");
                break;
            }

            try
            {
                values[property.Name] = property.GetValue(output);
            }
            catch (Exception exception)
            {
                // 单个属性读取失败不得阻止其他键被记录，也不得改变已经提交的节点输出和 Run 终态。
                values[property.Name] = "[读取失败]";
                diagnostics.Add($"输出属性 {outputType.Name}.{property.Name} 读取失败：{exception.Message}");
            }
        }

        return new WorkflowOutputValueSet(outputType.FullName, values, diagnostics);
    }

    /// <summary>判断根值是否必须使用统一 <c>$</c> 键而不是展开属性。</summary>
    /// <param name="output">已提交的输出值。</param>
    /// <returns>需要整体作为根值记录时返回 <see langword="true"/>。</returns>
    private static bool ShouldUseRootKey(object? output) => output switch
    {
        null => true,
        string or char or bool => true,
        byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal => true,
        Enum => true,
        Guid or DateTime or DateTimeOffset or TimeSpan or DateOnly or TimeOnly => true,
        byte[] => true,
        Array => true,
        IDictionary => true,
        IEnumerable => true,
        Stream => true,
        IntPtr or UIntPtr or SafeHandle => true,
        Exception => true,
        _ => false
    };

    /// <summary>为输出类型构建第一层公开可读属性计划，按属性名 Ordinal 排序。</summary>
    /// <param name="outputType">输出值的运行时类型。</param>
    /// <returns>稳定排序的公开属性数组。</returns>
    private static PropertyInfo[] BuildPlan(Type outputType)
    {
        var properties = new List<PropertyInfo>();
        foreach (var property in outputType.GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (property.GetIndexParameters().Length != 0)
                continue;
            if (property.GetGetMethod(nonPublic: false) is null)
                continue;
            properties.Add(property);
        }

        properties.Sort(static (left, right) => string.CompareOrdinal(left.Name, right.Name));
        return properties.ToArray();
    }
}
