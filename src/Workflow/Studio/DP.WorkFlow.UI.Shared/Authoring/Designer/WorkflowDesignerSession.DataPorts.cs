using System.Reflection;

namespace DP.WorkFlow.UI;

/// <summary>节点标准输出中的一个成员。</summary>
/// <param name="Name">成员路径；标准输出为简单值时为 <c>$</c>。</param>
/// <param name="DisplayName">中文显示名称。</param>
/// <param name="ValueType">成员值类型。</param>
public sealed record WorkflowOutputMember(string Name, string DisplayName, Type ValueType);

/// <summary>画布上一条“数据端口 → 下游参数”的绑定关系。</summary>
/// <param name="SourceNodeId">数据来源节点。</param>
/// <param name="Member">来源节点的输出成员。</param>
/// <param name="ConsumerNodeId">引用该成员的下游节点。</param>
/// <param name="ConfigurationPath">下游节点中引用该成员的配置路径。</param>
public sealed record WorkflowDataLink(string SourceNodeId, string Member, string ConsumerNodeId, string ConfigurationPath);

/// <summary>从数据端口拖线到某个节点时，可接收该数据的一个参数。</summary>
/// <param name="NodeId">目标节点。</param>
/// <param name="PropertyName">目标参数属性名。</param>
/// <param name="DisplayName">目标参数中文名称。</param>
public sealed record WorkflowDataPortTarget(string NodeId, string PropertyName, string DisplayName);

/// <summary>
/// 数据端口：把节点标准输出的成员显示在节点上，并允许从端口拖线直接建立下游参数绑定。
/// 数据端口只是现有绑定机制的可视化入口，引擎执行语义不变。
/// </summary>
public sealed partial class WorkflowDesignerSession
{
    private IReadOnlyList<WorkflowDataLink>? _dataLinks;

    /// <summary>返回节点标准输出的成员；输出为简单值时返回代表整个值的 <c>$</c>，无输出时返回空集合。</summary>
    /// <param name="nodeId">节点标识。</param>
    public IReadOnlyList<WorkflowOutputMember> GetOutputMembers(string nodeId) =>
        GetOutputMembers(GetCanvasNodeOrThrow(nodeId));

    /// <summary>按输出类型声明顺序返回节点上已显示为数据端口的成员。</summary>
    /// <param name="node">画布节点。</param>
    public IReadOnlyList<WorkflowOutputMember> GetExposedOutputMembers(WorkflowCanvasNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (node.ExposedOutputMembers.Count == 0) return Array.Empty<WorkflowOutputMember>();
        return GetOutputMembers(node).Where(member => node.ExposedOutputMembers.Contains(member.Name)).ToArray();
    }

    /// <summary>显示或隐藏节点上的某个数据端口；可撤销，不改变已有绑定。</summary>
    /// <param name="nodeId">节点标识。</param>
    /// <param name="member">输出成员名。</param>
    /// <param name="exposed">是否在节点上显示。</param>
    /// <returns>状态发生变化时返回 <see langword="true"/>。</returns>
    public bool SetOutputMemberExposed(string nodeId, string member, bool exposed)
    {
        var node = GetCanvasNodeOrThrow(nodeId);
        if (GetOutputMembers(node).All(item => item.Name != member)) return false;
        var wasExposed = node.ExposedOutputMembers.Contains(member);
        if (wasExposed == exposed) return false;
        void Apply(bool value)
        {
            if (value) node.ExposedOutputMembers.Add(member);
            else node.ExposedOutputMembers.Remove(member);
            EnsureNodeDisplaySize(node);
        }
        Execute(new DesignerOperation(() => Apply(exposed), () => Apply(wasExposed)));
        return true;
    }

    /// <summary>返回当前画布上来源成员已显示为数据端口的绑定关系，供画布绘制数据连线。</summary>
    public IReadOnlyList<WorkflowDataLink> GetDataLinks()
    {
        if (_dataLinks is not null) return _dataLinks;
        if (Canvas.Nodes.All(node => node.ExposedOutputMembers.Count == 0) || Canvas.Nodes.Count == 0)
            return _dataLinks = Array.Empty<WorkflowDataLink>();
        var exposed = Canvas.Nodes.ToDictionary(node => node.Node.Id, node => node.ExposedOutputMembers, StringComparer.Ordinal);
        try
        {
            var analysis = new WorkflowBindingAnalyzer(Catalog).Analyze(Document, ResolveAnalysisEntry());
            return _dataLinks = analysis.Usages
                .Where(usage => !usage.Binding.IsPublicData
                    && exposed.TryGetValue(usage.Binding.NodeId, out var members)
                    && members.Contains(RootMember(usage.Binding.MemberPath)))
                .Select(usage => new WorkflowDataLink(usage.Binding.NodeId, RootMember(usage.Binding.MemberPath),
                    usage.ConsumerNodeId, usage.ConfigurationPath))
                .Distinct()
                .ToArray();
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
        {
            return _dataLinks = Array.Empty<WorkflowDataLink>();
        }
    }

    /// <summary>
    /// 返回目标节点中可以绑定到来源成员的参数：类型兼容，且来源节点保证在目标节点执行前产生输出。
    /// </summary>
    /// <param name="sourceNodeId">数据来源节点。</param>
    /// <param name="member">来源输出成员。</param>
    /// <param name="targetNodeId">拖线落点所在节点。</param>
    public IReadOnlyList<WorkflowDataPortTarget> GetDataPortTargets(string sourceNodeId, string member, string targetNodeId)
    {
        if (string.Equals(sourceNodeId, targetNodeId, StringComparison.Ordinal)) return Array.Empty<WorkflowDataPortTarget>();
        var target = GetCanvasNodeOrThrow(targetNodeId).Node;
        var inputs = GetWorkflowInputProperties(target).ToArray();
        if (inputs.Length == 0) return Array.Empty<WorkflowDataPortTarget>();
        WorkflowBindingAnalysis analysis;
        try { analysis = new WorkflowBindingAnalyzer(Catalog).Analyze(Document, ResolveAnalysisEntry()); }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
        {
            return Array.Empty<WorkflowDataPortTarget>();
        }
        return inputs
            .Where(input => analysis.GetCandidates(targetNodeId, input.ValueType)
                .Any(candidate => candidate.SourceKind == WorkflowBindingCandidateSourceKind.NodeOutput
                    && candidate.SourceNodeId == sourceNodeId && candidate.MemberPath == member))
            .Select(input => new WorkflowDataPortTarget(targetNodeId, input.Property.Name, input.DisplayName))
            .ToArray();
    }

    /// <summary>把目标参数改为绑定到来源成员（保留原固定值以便切回），作为一次可撤销的节点配置修改。</summary>
    /// <param name="sourceNodeId">数据来源节点。</param>
    /// <param name="member">来源输出成员。</param>
    /// <param name="target">目标参数。</param>
    public void BindDataPort(string sourceNodeId, string member, WorkflowDataPortTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        var binding = new WorkflowBindingKey(sourceNodeId, member);
        ExecuteNodeConfigurationChange(target.NodeId, node =>
        {
            var property = node.GetType().GetProperty(target.PropertyName, BindingFlags.Instance | BindingFlags.Public)
                ?? throw new InvalidOperationException($"节点 {node.Id} 没有参数 {target.PropertyName}。");
            var current = property.GetValue(node);
            var input = Activator.CreateInstance(property.PropertyType)
                ?? throw new InvalidOperationException($"无法创建 {property.PropertyType.Name}。");
            var literal = property.PropertyType.GetProperty(nameof(WorkflowInput<object>.LiteralValue))!;
            literal.SetValue(input, current is null ? null : literal.GetValue(current));
            property.PropertyType.GetProperty(nameof(WorkflowInput<object>.Source))!.SetValue(input, WorkflowValueSource.Binding);
            property.PropertyType.GetProperty(nameof(WorkflowInput<object>.Binding))!.SetValue(input, binding);
            property.SetValue(node, input);
        });
    }

    private IReadOnlyList<WorkflowOutputMember> GetOutputMembers(WorkflowCanvasNode node)
    {
        if (!Catalog.TryGet(node.Node.NodeType, out var descriptor) || descriptor!.OutputType is not { } type)
            return Array.Empty<WorkflowOutputMember>();
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (IsSimpleOutput(type))
            return new[] { new WorkflowOutputMember("$", "值", type) };
        return type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property => property.CanRead && property.GetIndexParameters().Length == 0)
            .OrderBy(property => property.MetadataToken)
            .Select(property => new WorkflowOutputMember(property.Name, WorkflowOutputDisplayNames.Resolve(property), property.PropertyType))
            .ToArray();
    }

    private static IEnumerable<(PropertyInfo Property, Type ValueType, string DisplayName)> GetWorkflowInputProperties(IWorkflowNodeModel node) =>
        node.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property => property.CanRead && property.CanWrite && property.GetIndexParameters().Length == 0
                && property.PropertyType.IsGenericType
                && property.PropertyType.GetGenericTypeDefinition() == typeof(WorkflowInput<>))
            .Select(property => (property, property.PropertyType.GetGenericArguments()[0],
                property.GetCustomAttribute<WorkflowPropertyAttribute>()?.DisplayName
                ?? property.GetCustomAttribute<System.ComponentModel.DisplayNameAttribute>()?.DisplayName
                ?? property.Name));

    private string ResolveAnalysisEntry() =>
        !string.IsNullOrWhiteSpace(Document.EntryNodeId) ? Document.EntryNodeId : Canvas.Nodes[0].Node.Id;

    private static string RootMember(string memberPath)
    {
        var dot = memberPath.IndexOf('.');
        return dot < 0 ? memberPath : memberPath[..dot];
    }

    private static bool IsSimpleOutput(Type type) => type.IsPrimitive || type.IsEnum || type == typeof(string)
        || type == typeof(decimal) || type == typeof(DateTime) || type == typeof(DateTimeOffset)
        || type == typeof(TimeSpan) || type == typeof(Guid);
}
