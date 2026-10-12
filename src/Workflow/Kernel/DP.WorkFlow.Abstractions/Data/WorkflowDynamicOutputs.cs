namespace DP.WorkFlow;

/// <summary>
/// 节点输出中可按键寻址的对象（例如测量组的测量项集合、单个测量项的信封）。
/// 绑定成员路径的某一段在该对象上找不到同名公开属性时，按键查找。
/// </summary>
public interface IWorkflowKeyedOutput
{
    /// <summary>按键读取子值。</summary>
    /// <param name="key">成员路径中的一段，例如测量项 Id。</param>
    /// <param name="value">找到时返回子值，可以为空。</param>
    /// <returns>键存在时返回 <see langword="true"/>。</returns>
    bool TryGetKeyedValue(string key, out object? value);

    /// <summary>键不存在或不可读时的说明，用于绑定错误消息。</summary>
    /// <param name="key">未找到的键。</param>
    string DescribeMissingKey(string key) => $"不存在键 {key}。";
}

/// <summary>节点当前配置下一个可绑定的输出成员。</summary>
/// <param name="Path">点分隔成员路径；<c>$</c> 表示整个输出。</param>
/// <param name="Type">成员的静态类型（去除 Nullable 包装）。</param>
/// <param name="DisplayName">显示名，例如“左边 · 拟合直线”。</param>
public sealed record WorkflowOutputMember(string Path, Type Type, string DisplayName);

/// <summary>
/// 输出成员随配置变化的节点（例如测量组：每个测量项都有自己的成员）。编译期绑定校验、绑定候选和设计器数据端口
/// 以 <see cref="DescribeOutputs"/> 为准；未实现此接口的节点按描述器的静态输出类型反射。
/// </summary>
public interface IWorkflowDynamicOutputNode
{
    /// <summary>列出当前配置下全部可绑定成员，包括 <c>$</c> 根值和静态成员。</summary>
    IReadOnlyList<WorkflowOutputMember> DescribeOutputs();
}

/// <summary>节点输出成员的设计期描述：动态节点以其自身说明为准，其余节点按静态输出类型反射。</summary>
public static class WorkflowOutputMembers
{
    private const System.Reflection.BindingFlags Public = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public;

    /// <summary>类型本身以及递归最多 <paramref name="maxDepth"/> 层的公开可读属性，显示名为路径。</summary>
    /// <param name="type">根类型。</param><param name="path">根路径，默认 <c>$</c>。</param>
    /// <param name="displayPrefix">显示名前缀，例如“左边 · ”；为空时显示名就是路径。</param>
    /// <param name="maxDepth">递归层数。</param>
    public static IEnumerable<WorkflowOutputMember> OfType(Type type, string path = "$", string? displayPrefix = null, int maxDepth = 3)
    {
        ArgumentNullException.ThrowIfNull(type);
        return Walk(type, path, "", 0);

        IEnumerable<WorkflowOutputMember> Walk(Type current, string currentPath, string relative, int depth)
        {
            yield return new WorkflowOutputMember(currentPath, current, displayPrefix is null ? currentPath : displayPrefix + (relative.Length == 0 ? "" : relative));
            if (depth >= maxDepth || IsSimple(current))
                yield break;
            foreach (var property in current.GetProperties(Public)
                         .Where(p => p.CanRead && p.GetIndexParameters().Length == 0)
                         .OrderBy(p => p.Name, StringComparer.Ordinal))
            {
                var child = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
                foreach (var member in Walk(child, currentPath == "$" ? property.Name : $"{currentPath}.{property.Name}",
                             relative.Length == 0 ? property.Name : $"{relative}.{property.Name}", depth + 1))
                    yield return member;
            }
        }
    }

    /// <summary>节点当前可绑定的全部成员。</summary>
    /// <param name="node">节点配置。</param><param name="outputType">描述器声明的静态输出类型，可为空。</param>
    public static IReadOnlyList<WorkflowOutputMember> Describe(IWorkflowNodeModel node, Type? outputType)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (node is IWorkflowDynamicOutputNode dynamic) return dynamic.DescribeOutputs();
        return outputType is null ? Array.Empty<WorkflowOutputMember>() : OfType(outputType).ToArray();
    }

    /// <summary>
    /// 解析成员路径的静态类型。动态节点先找说明中最长的匹配前缀，剩余部分按该成员的类型反射；
    /// 普通节点直接按输出类型反射。路径无效时返回空。
    /// </summary>
    /// <param name="node">来源节点。</param><param name="outputType">描述器声明的静态输出类型。</param><param name="path">成员路径。</param>
    public static Type? Resolve(IWorkflowNodeModel node, Type outputType, string path)
    {
        ArgumentNullException.ThrowIfNull(node); ArgumentNullException.ThrowIfNull(outputType); ArgumentNullException.ThrowIfNull(path);
        if (path == "$") return outputType;
        var segments = path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (node is IWorkflowDynamicOutputNode dynamic)
        {
            var members = dynamic.DescribeOutputs().ToDictionary(m => m.Path, m => m.Type, StringComparer.OrdinalIgnoreCase);
            for (int length = segments.Length; length > 0; length--)
                if (members.TryGetValue(string.Join('.', segments.Take(length)), out var prefix))
                    return ResolveProperties(prefix, segments.Skip(length));
            return null;
        }
        return ResolveProperties(outputType, segments);
    }

    private static Type? ResolveProperties(Type root, IEnumerable<string> segments)
    {
        var current = root;
        foreach (var segment in segments)
        {
            var property = current.GetProperty(segment, Public | System.Reflection.BindingFlags.IgnoreCase);
            if (property is null || !property.CanRead || property.GetIndexParameters().Length != 0)
                return null;
            current = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
        }
        return current;
    }

    private static bool IsSimple(Type type) => type.IsPrimitive || type.IsEnum || type == typeof(string)
        || type == typeof(decimal) || type == typeof(DateTime) || type == typeof(DateTimeOffset)
        || type == typeof(TimeSpan) || type == typeof(Guid);
}
