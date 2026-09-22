using System.Collections.Concurrent;
using System.Reflection;

namespace DP.WorkFlow;

/// <summary>
/// 描述一个节点类型上可自动识别的普通输入槽；发现结果只取决于节点类型，可跨 Run 复用。
/// </summary>
/// <remarks>
/// <para>普通输入槽就是节点模型中公开的顶层 <see cref="WorkflowInput{T}"/> 属性。运行时绑定阶段
/// 用这里的元数据把"某个 <see cref="WorkflowInput{T}"/> 实例"映射回它的稳定属性名，
/// 使 Handler 不需要重复手写输入名也能记录可查询的数据血缘。</para>
/// <para>发现规则刻意保守：只认公开实例属性、getter 非空、非索引器、属性类型泛型定义是
/// <see cref="WorkflowInput{T}"/>。不扫描私有字段、嵌套配置对象、集合元素和 Handler 局部变量；
/// 动态输入使用显式的动态键逃生口，而不是让静态发现猜测。</para>
/// </remarks>
internal sealed class WorkflowNodeInputLayout
{
    private static readonly ConcurrentDictionary<Type, WorkflowNodeInputLayout> Cache = new();

    private readonly WorkflowNodeInputSlot[] _slots;

    private WorkflowNodeInputLayout(WorkflowNodeInputSlot[] slots) => _slots = slots;

    /// <summary>获取按属性名 Ordinal 排序的输入槽；顺序稳定以便诊断和测试对照。</summary>
    internal IReadOnlyList<WorkflowNodeInputSlot> Slots => _slots;

    /// <summary>按节点类型发现并缓存普通输入槽。</summary>
    /// <param name="nodeType">节点模型的运行时类型；继承的公开输入属性同样参与发现。</param>
    /// <returns>该类型的只读输入槽布局。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="nodeType"/> 为空。</exception>
    /// <exception cref="InvalidOperationException">继承层次中出现重复的稳定输入键。</exception>
    internal static WorkflowNodeInputLayout Discover(Type nodeType)
    {
        ArgumentNullException.ThrowIfNull(nodeType);
        return Cache.GetOrAdd(nodeType, static type => Create(type));
    }

    /// <summary>
    /// 对一次实际节点快照建立"输入实例到稳定键"的引用映射，并在此处拒绝歧义配置。
    /// </summary>
    /// <param name="node">本次执行使用的节点配置副本。</param>
    /// <returns>只属于本次执行的引用映射。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="node"/> 为空。</exception>
    /// <exception cref="InvalidOperationException">
    /// 两个输入属性引用同一个 <see cref="WorkflowInput{T}"/> 实例，或输入属性 getter 抛出异常。
    /// </exception>
    /// <remarks>
    /// 执行计划每次返回新的节点快照，因此映射必须按执行建立，不能缓存节点实例本身。
    /// 值为空的输入属性沿用现有配置验证语义，不在这里发明第二套空值规则。
    /// </remarks>
    internal WorkflowNodeInputMap Bind(IWorkflowNodeModel node)
    {
        ArgumentNullException.ThrowIfNull(node);
        var keys = new Dictionary<object, string>(ReferenceEqualityComparer.Instance);
        foreach (var slot in _slots)
        {
            object? input;
            try
            {
                input = slot.ReadInput(node);
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException(
                    $"读取节点 {node.Id}/{node.NodeType} 的输入槽 {slot.Key} 时属性 getter 抛出异常。",
                    exception);
            }

            if (input is null)
                continue;

            if (keys.TryGetValue(input, out var existing))
            {
                throw new InvalidOperationException(
                    $"节点 {node.Id}/{node.NodeType} 的输入槽 {existing} 与 {slot.Key} 引用了同一个 WorkflowInput 实例；"
                    + "同一个输入实例不能同时属于两个普通输入槽。");
            }

            keys[input] = slot.Key;
        }

        return new WorkflowNodeInputMap(keys);
    }

    private static WorkflowNodeInputLayout Create(Type nodeType)
    {
        var slots = new List<WorkflowNodeInputSlot>();
        // 逐层读取"本层声明"的属性：反射对 new 隐藏的同名属性只返回最派生一个，
        // 因此必须自己走继承链才能发现真正的重复稳定键。override 链视为同一个槽。
        var slotOwners = new Dictionary<string, Type>(StringComparer.Ordinal);
        for (var type = nodeType; type is not null && type != typeof(object); type = type.BaseType)
        {
            foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
            {
                if (property.GetIndexParameters().Length != 0)
                    continue;
                var getter = property.GetGetMethod(nonPublic: false);
                if (getter is null)
                    continue;

                var propertyType = property.PropertyType;
                if (!propertyType.IsGenericType
                    || propertyType.GetGenericTypeDefinition() != typeof(WorkflowInput<>))
                {
                    continue;
                }

                var slotOwner = getter.GetBaseDefinition().DeclaringType ?? property.DeclaringType ?? type;
                if (slotOwners.TryGetValue(property.Name, out var existingOwner))
                {
                    if (existingOwner == slotOwner)
                        continue;
                    throw new InvalidOperationException(
                        $"节点类型 {nodeType.FullName} 的输入槽 {property.Name} 在继承层次中重复定义。");
                }

                slotOwners[property.Name] = slotOwner;
                slots.Add(new WorkflowNodeInputSlot(
                    property.Name,
                    propertyType.GetGenericArguments()[0],
                    node => property.GetValue(node)));
            }
        }

        slots.Sort(static (left, right) => string.CompareOrdinal(left.Key, right.Key));
        return new WorkflowNodeInputLayout(slots.ToArray());
    }
}

/// <summary>一个普通输入槽的稳定键、值类型和读取计划。</summary>
/// <param name="Key">节点模型中 <see cref="WorkflowInput{T}"/> 属性的公开名称。</param>
/// <param name="ValueType">该输入的强类型值类型（<c>T</c>）。</param>
/// <param name="ReadInput">从节点快照读取输入实例的访问计划。</param>
internal sealed record WorkflowNodeInputSlot(
    string Key,
    Type ValueType,
    Func<IWorkflowNodeModel, object?> ReadInput);

/// <summary>输入解析事件中"输入键从哪来"的稳定状态值。</summary>
internal static class WorkflowInputMetadataStatus
{
    /// <summary>由绑定阶段冻结的输入槽元数据自动识别；普通内置节点的正常路径。</summary>
    internal const string Automatic = "Automatic";

    /// <summary>由调用方显式声明的动态键识别；用于集合元素、动态端口和脚本输入。</summary>
    internal const string ExplicitDynamic = "ExplicitDynamic";

    /// <summary>无法从静态输入槽识别；记录降级诊断但不改变解析结果。</summary>
    internal const string Unresolved = "Unresolved";
}

/// <summary>
/// 一次节点执行内"输入实例到稳定键"的引用映射。
/// </summary>
/// <remarks>
/// 必须使用引用相等而不是 <see cref="WorkflowInput{T}"/> 的值相等语义：
/// 两个内容相同但属于不同属性的输入是不同输入槽，不能被错误合并。
/// </remarks>
internal sealed class WorkflowNodeInputMap
{
    private readonly Dictionary<object, string> _keys;

    internal WorkflowNodeInputMap(Dictionary<object, string> keys) => _keys = keys;

    /// <summary>按引用查找输入实例对应的稳定键。</summary>
    /// <param name="input">Handler 实际传入的输入实例。</param>
    /// <param name="key">找到时返回稳定键；否则返回空字符串。</param>
    /// <returns>该实例属于本节点的一个普通输入槽时返回 <see langword="true"/>。</returns>
    internal bool TryGetKey(object input, out string key)
    {
        if (input is not null && _keys.TryGetValue(input, out var found))
        {
            key = found;
            return true;
        }

        key = string.Empty;
        return false;
    }
}
