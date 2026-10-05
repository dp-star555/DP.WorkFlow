using System.Collections.Concurrent;
using System.ComponentModel;
using System.Reflection;

namespace DP.WorkFlow;

/// <summary>
/// 节点输出成员的中文显示名称。本仓库的输出类型直接在属性上标注 <see cref="DisplayNameAttribute"/>；
/// 来自外部程序集、无法标注特性的输出类型（如 DP.Vision 结果）由节点包在注册时提供名称表。
/// </summary>
public static class WorkflowOutputDisplayNames
{
    private static readonly ConcurrentDictionary<Type, IReadOnlyDictionary<string, string>> Registered = new();

    /// <summary>为外部输出类型登记成员显示名称；重复登记时合并，后登记的同名成员覆盖先前值。</summary>
    /// <param name="type">输出类型。</param>
    /// <param name="names">成员名到中文显示名称的映射。</param>
    public static void Register(Type type, IReadOnlyDictionary<string, string> names)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(names);
        Registered.AddOrUpdate(type, _ => new Dictionary<string, string>(names, StringComparer.Ordinal), (_, existing) =>
        {
            var merged = new Dictionary<string, string>(existing, StringComparer.Ordinal);
            foreach (var pair in names) merged[pair.Key] = pair.Value;
            return merged;
        });
    }

    /// <summary>
    /// 解析属性的显示名称：依次使用 <see cref="WorkflowPropertyAttribute"/>、<see cref="DisplayNameAttribute"/>
    /// 和登记的名称表（含基类与接口）；都没有时返回属性名。
    /// </summary>
    /// <param name="property">输出成员。</param>
    /// <returns>显示名称。</returns>
    public static string Resolve(PropertyInfo property)
    {
        ArgumentNullException.ThrowIfNull(property);
        var attributed = property.GetCustomAttribute<WorkflowPropertyAttribute>()?.DisplayName
            ?? property.GetCustomAttribute<DisplayNameAttribute>()?.DisplayName;
        if (!string.IsNullOrWhiteSpace(attributed)) return attributed;
        for (var type = property.ReflectedType ?? property.DeclaringType; type is not null; type = type.BaseType)
            if (Registered.TryGetValue(type, out var names) && names.TryGetValue(property.Name, out var name)) return name;
        foreach (var contract in (property.ReflectedType ?? property.DeclaringType)?.GetInterfaces() ?? Type.EmptyTypes)
            if (Registered.TryGetValue(contract, out var names) && names.TryGetValue(property.Name, out var name)) return name;
        return property.Name;
    }
}
