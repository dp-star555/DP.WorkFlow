using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace DP.WorkFlow;

/// <summary>
/// 从结构化节点输出中解析强类型输入。
/// </summary>
public sealed class WorkflowBindingResolver
{
    private static readonly ConcurrentDictionary<BindingPlanKey, BindingPathPlan> PathPlans = new();
    private readonly WorkflowContext _context;
    private readonly WorkflowExecutionIdentity _consumer;

    /// <summary>初始化限定到单个节点执行实例的绑定解析器。</summary>
    /// <param name="context">提供公共数据、节点输出历史和并行可见性状态的运行上下文。</param>
    /// <param name="consumer">当前消费节点的 Run、Token、Scope 和执行次数身份。</param>
    public WorkflowBindingResolver(WorkflowContext context, WorkflowExecutionIdentity consumer)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _consumer = consumer ?? throw new ArgumentNullException(nameof(consumer));
    }

    /// <summary>解析固定值、公共数据或当前执行实例可见的节点输出绑定。</summary>
    /// <typeparam name="T">节点输入要求的目标类型。</typeparam>
    /// <param name="input">包含来源模式、固定值或绑定键的输入配置。</param>
    /// <returns>固定值，或读取成员路径并按不变区域性转换后的绑定值。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="input"/> 为空。</exception>
    /// <exception cref="InvalidOperationException">输入配置缺少所选来源模式要求的值。</exception>
    /// <exception cref="WorkflowBindingException">来源不可见、成员路径无效或值不能转换为 <typeparamref name="T"/>。</exception>
    public T? Resolve<T>(WorkflowInput<T> input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var validationError = input.Validate();
        if (validationError is not null)
            throw new InvalidOperationException(validationError);

        if (input.Source == WorkflowValueSource.Literal)
        {
            if (typeof(T) == typeof(object) && input.LiteralValue is JsonElement element)
                return (T?)(object?)ConvertJsonElement(element);
            return input.LiteralValue;
        }

        var binding = input.Binding!.Value;
        object? root;
        if (binding.IsPublicData)
        {
            var publicDataKey = binding.PublicDataKey!;
            if (!_context.PublicData.TryGet<object>(publicDataKey, out root))
                throw new WorkflowBindingException(binding, $"公共数据 {publicDataKey} 不存在或尚未发布。");
        }
        else
        {
            if (!_context.TryGetVisibleNodeOutput(binding.NodeId, _consumer, out var output))
                throw new WorkflowBindingException(binding, $"来源节点 {binding.NodeId} 在当前 Token/Scope 中没有可见输出。");
            root = output!.Value;
        }

        var raw = ReadMemberPath(binding, root);
        return ConvertValue<T>(binding, raw);
    }

    /// <summary>从上下文的最终 Latest 输出解析绑定；仅用于已完成子流程的显式输出映射。</summary>
    /// <typeparam name="T">输出映射要求的目标类型。</typeparam>
    /// <param name="context">已经完成子流程运行的隔离上下文。</param>
    /// <param name="binding">子文档节点输出或公共数据绑定。</param>
    /// <returns>成员路径读取并转换后的值。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> 为空。</exception>
    /// <exception cref="WorkflowBindingException">来源尚未输出、成员路径无效或类型转换失败。</exception>
    /// <remarks>该方法绕过 Token/Scope 可见性检查，不应作为普通节点输入解析入口。</remarks>
    public static T? ResolveLatest<T>(WorkflowContext context, WorkflowBindingKey binding)
    {
        ArgumentNullException.ThrowIfNull(context);
        object? root;
        if (binding.IsPublicData)
        {
            var publicDataKey = binding.PublicDataKey!;
            if (!context.PublicData.TryGet<object>(publicDataKey, out root))
                throw new WorkflowBindingException(binding, $"公共数据 {publicDataKey} 不存在或尚未发布。");
        }
        else
        {
            if (!context.TryGetNodeOutput(binding.NodeId, out var output))
                throw new WorkflowBindingException(binding, $"来源节点 {binding.NodeId} 尚未产生输出。");
            root = output!.Value;
        }
        var raw = ReadMemberPath(binding, root);
        return ConvertValue<T>(binding, raw);
    }

    private static object? ConvertJsonElement(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number when element.TryGetInt64(out var integer) => integer,
        JsonValueKind.Number when element.TryGetDecimal(out var number) => number,
        JsonValueKind.Number => element.GetDouble(),
        JsonValueKind.Array => element.EnumerateArray().Select(ConvertJsonElement).ToArray(),
        JsonValueKind.Object => element.EnumerateObject().ToDictionary(
            property => property.Name,
            property => ConvertJsonElement(property.Value),
            StringComparer.Ordinal),
        _ => throw new NotSupportedException($"不支持的 JSON 值类型：{element.ValueKind}。")
    };

    /// <summary>使用按根类型和路径缓存的反射计划逐级读取公开属性。</summary>
    /// <param name="binding">提供成员路径及异常上下文的绑定键。</param>
    /// <param name="root">节点标准输出或公共数据的根值。</param>
    /// <returns>路径末端值；<c>$</c> 直接返回根值。</returns>
    private static object? ReadMemberPath(WorkflowBindingKey binding, object? root)
    {
        if (binding.MemberPath == "$")
            return root;
        if (root is null)
            throw new WorkflowBindingException(binding, "根输出值为空，无法读取成员路径。");

        var plan = PathPlans.GetOrAdd(
            new BindingPlanKey(root.GetType(), binding.MemberPath),
            static key => BuildPlan(key.RootType, key.MemberPath));
        if (plan.Error is not null)
            throw new WorkflowBindingException(binding, plan.Error);

        object? current = root;
        foreach (var property in plan.Properties)
        {
            if (current is null)
                throw new WorkflowBindingException(binding, $"读取成员 {property.Name} 前对象已经为空。");

            try
            {
                current = property.GetValue(current);
            }
            catch (TargetInvocationException exception)
            {
                throw new WorkflowBindingException(binding, $"读取成员 {property.Name} 时发生异常。", exception.InnerException ?? exception);
            }
        }
        return current;
    }

    /// <summary>为根运行时类型构建不区分大小写的公开可读属性访问计划。</summary>
    /// <param name="rootType">实际输出值的运行时类型。</param>
    /// <param name="memberPath">点分隔成员路径。</param>
    /// <returns>属性链；路径无效时返回携带错误文本的失败计划。</returns>
    private static BindingPathPlan BuildPlan(Type rootType, string memberPath)
    {
        var names = memberPath.Split('.', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (names.Length == 0)
            return BindingPathPlan.Failed("成员路径为空。");

        var properties = new List<PropertyInfo>(names.Length);
        var currentType = rootType;
        foreach (var name in names)
        {
            var property = currentType.GetProperty(
                name,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
            if (property is null || property.GetIndexParameters().Length != 0 || !property.CanRead)
                return BindingPathPlan.Failed($"类型 {currentType.Name} 中不存在可读成员 {name}。");

            properties.Add(property);
            currentType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
        }
        return BindingPathPlan.Succeeded(properties.ToArray());
    }

    /// <summary>按照运行时绑定规则转换成员原始值。</summary>
    /// <typeparam name="T">目标输入类型。</typeparam>
    /// <param name="binding">用于转换失败诊断的绑定键。</param>
    /// <param name="raw">成员路径产生的原始值。</param>
    /// <returns>直接赋值，或经枚举、Guid、TimeSpan 和通用标量转换后的值。</returns>
    private static T? ConvertValue<T>(WorkflowBindingKey binding, object? raw)
    {
        var targetType = typeof(T);
        var nullableType = Nullable.GetUnderlyingType(targetType);
        var coreType = nullableType ?? targetType;
        if (raw is null)
        {
            if (nullableType is not null || !coreType.IsValueType)
                return default;
            throw new WorkflowBindingException(binding, $"空值不能转换为 {coreType.Name}。");
        }

        if (raw is T direct)
            return direct;

        try
        {
            object converted;
            if (coreType.IsEnum)
            {
                converted = raw is string enumText
                    ? Enum.Parse(coreType, enumText, true)
                    : Enum.ToObject(coreType, raw);
            }
            else if (coreType == typeof(Guid))
            {
                converted = Guid.Parse(Convert.ToString(raw, CultureInfo.InvariantCulture)!);
            }
            else if (coreType == typeof(TimeSpan))
            {
                converted = TimeSpan.Parse(Convert.ToString(raw, CultureInfo.InvariantCulture)!, CultureInfo.InvariantCulture);
            }
            else
            {
                converted = Convert.ChangeType(raw, coreType, CultureInfo.InvariantCulture);
            }
            return (T)converted;
        }
        catch (Exception exception) when (exception is FormatException or InvalidCastException or OverflowException or ArgumentException)
        {
            throw new WorkflowBindingException(
                binding,
                $"值 {raw}（{raw.GetType().Name}）不能转换为 {coreType.Name}。",
                exception);
        }
    }

    private readonly record struct BindingPlanKey(Type RootType, string MemberPath);

    private sealed record BindingPathPlan(PropertyInfo[] Properties, string? Error)
    {
        public static BindingPathPlan Succeeded(PropertyInfo[] properties) => new(properties, null);

        public static BindingPathPlan Failed(string error) => new(Array.Empty<PropertyInfo>(), error);
    }
}
