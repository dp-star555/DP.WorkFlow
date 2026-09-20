using System.Reflection;

namespace DP.WorkFlow;

/// <summary>表示设计期从节点配置中发现的一个绑定消费位置。</summary>
/// <param name="ConsumerNodeId">使用该绑定的节点实例 ID。</param>
/// <param name="Binding">来源节点或公共数据及其成员路径。</param>
/// <param name="ExpectedType">消费属性要求的目标类型。</param>
/// <param name="ConfigurationPath">绑定在节点对象图中的属性路径，用于定位错误配置。</param>
public sealed record WorkflowBindingUsage(
    string ConsumerNodeId,
    WorkflowBindingKey Binding,
    Type ExpectedType,
    string ConfigurationPath);

/// <summary>指定设计器绑定候选的数据来源种类。</summary>
public enum WorkflowBindingCandidateSourceKind
{
    /// <summary>来源为当前画布中保证先于消费者完成的节点标准输出。</summary>
    NodeOutput,

    /// <summary>来源为宿主在公共数据目录中声明的值。</summary>
    PublicData
}

/// <summary>表示设计器可展示和选择的一个强类型绑定候选。</summary>
/// <param name="SourceNodeId">节点输出来源 ID；公共数据候选中保存数据键。</param>
/// <param name="MemberPath">来源值的成员路径，<c>$</c> 表示整个根值。</param>
/// <param name="ValueType">成员路径解析后的静态值类型。</param>
/// <param name="DisplayPath">绑定树或下拉列表中显示的完整路径。</param>
/// <param name="SourceKind">节点输出或公共数据。</param>
/// <param name="SourceDisplayName">来源节点或公共数据的可选显示名称。</param>
/// <param name="Category">设计器绑定树中的可选分类。</param>
public sealed record WorkflowBindingCandidate(
    string SourceNodeId,
    string MemberPath,
    Type ValueType,
    string DisplayPath,
    WorkflowBindingCandidateSourceKind SourceKind = WorkflowBindingCandidateSourceKind.NodeOutput,
    string? SourceDisplayName = null,
    string? Category = null)
{
    /// <summary>转换为节点配置可以持久化的绑定键。</summary>
    /// <returns>根据 <see cref="SourceKind"/> 编码为节点输出或公共数据来源的绑定键。</returns>
    public WorkflowBindingKey ToBindingKey() => SourceKind == WorkflowBindingCandidateSourceKind.PublicData
        ? WorkflowBindingKey.FromPublicData(SourceNodeId, MemberPath)
        : new WorkflowBindingKey(SourceNodeId, MemberPath);
}

/// <summary>包含设计期绑定使用点、候选和诊断。</summary>
public sealed class WorkflowBindingAnalysis
{
    /// <summary>初始化一次不可变的设计期绑定分析结果。</summary>
    /// <param name="usages">从节点配置对象图中发现的绑定使用点。</param>
    /// <param name="candidates">按消费节点 ID 索引的可见来源候选。</param>
    /// <param name="diagnostics">绑定来源和类型检查产生的诊断。</param>
    internal WorkflowBindingAnalysis(
        IReadOnlyList<WorkflowBindingUsage> usages,
        IReadOnlyDictionary<string, IReadOnlyList<WorkflowBindingCandidate>> candidates,
        IReadOnlyList<WorkflowValidationError> diagnostics)
    {
        Usages = usages;
        CandidatesByConsumer = candidates;
        Diagnostics = diagnostics;
    }

    /// <summary>获取从当前画布节点配置中发现的全部绑定使用点。</summary>
    public IReadOnlyList<WorkflowBindingUsage> Usages { get; }

    /// <summary>获取按消费节点 ID 索引、并保证在该节点前可见的输出候选。</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<WorkflowBindingCandidate>> CandidatesByConsumer { get; }

    /// <summary>获取绑定来源、执行顺序、成员路径和类型兼容性诊断。</summary>
    public IReadOnlyList<WorkflowValidationError> Diagnostics { get; }

    /// <summary>获取指定消费者可见且与目标类型兼容的绑定候选。</summary>
    /// <param name="consumerNodeId">将使用绑定的节点实例 ID。</param>
    /// <param name="expectedType">节点输入属性要求的类型。</param>
    /// <returns>来源保证先执行且可转换为目标类型的候选；消费者不存在时返回空集合。</returns>
    public IReadOnlyList<WorkflowBindingCandidate> GetCandidates(string consumerNodeId, Type expectedType) =>
        CandidatesByConsumer.TryGetValue(consumerNodeId, out var candidates)
            ? candidates.Where(candidate => WorkflowBindingAnalyzer.IsTypeCompatible(candidate.ValueType, expectedType)).ToArray()
            : Array.Empty<WorkflowBindingCandidate>();
}

/// <summary>在不执行流程的情况下分析绑定可达性、成员路径和类型。</summary>
public sealed class WorkflowBindingAnalyzer
{
    private readonly WorkflowNodeCatalog? _catalog;

    /// <summary>初始化设计期绑定分析器。</summary>
    /// <param name="catalog">节点类型目录；为空时仍分析执行顺序，但无法验证节点输出成员及类型。</param>
    public WorkflowBindingAnalyzer(WorkflowNodeCatalog? catalog = null)
    {
        _catalog = catalog;
    }

    /// <summary>分析正式文档中绑定的来源必然性、可见候选、成员路径和类型兼容性。</summary>
    public WorkflowBindingAnalysis Analyze(WorkflowDocument document) =>
        Analyze(document, document?.EntryNodeId ?? throw new ArgumentNullException(nameof(document)));

    /// <summary>使用指定只读入口分析正式文档；不会为兼容旧画布而修改文档入口。</summary>
    /// <param name="document">待分析的正式工作流文档。</param>
    /// <param name="entryNodeId">本次分析使用的入口节点 ID。</param>
    /// <returns>绑定用法、候选和诊断。</returns>
    public WorkflowBindingAnalysis Analyze(WorkflowDocument document, string entryNodeId)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (string.IsNullOrWhiteSpace(entryNodeId))
            throw new ArgumentException("绑定分析入口不能为空。", nameof(entryNodeId));
        var graph = WorkflowGraphIndex.Create(document.Graph);
        var structuralDiagnostics = new List<WorkflowValidationError>();
        var parallelScopes = WorkflowParallelScopeAnalyzer.Analyze(graph, structuralDiagnostics);
        return Analyze(entryNodeId, graph, parallelScopes.Values.ToArray());
    }

    /// <summary>Runs binding analysis against the compiler's canonical graph and parallel-scope interpretation.</summary>
    /// <param name="startNodeId">The current graph entry.</param>
    /// <param name="graph">The canonical indexed control graph.</param>
    /// <param name="parallelScopes">The compiler-derived structured parallel scopes.</param>
    /// <returns>Binding usages, candidates, and diagnostics.</returns>
    internal WorkflowBindingAnalysis Analyze(
        string startNodeId,
        WorkflowGraphIndex graph,
        IReadOnlyList<WorkflowParallelScopePlan> parallelScopes)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(parallelScopes);

        var nodes = graph.Nodes;
        var outgoing = graph.Successors;
        var dominators = graph.ComputeDominators(startNodeId);
        var scopeHints = parallelScopes
            .Select(scope => new ParallelScopeHint(scope.BranchEntryNodeIds, scope.MergeNodeId))
            .ToArray();
        var usages = nodes.Values.SelectMany(ExtractCurrentCanvasUsages).ToArray();
        var diagnostics = new List<WorkflowValidationError>();
        foreach (var usage in usages)
            ValidateUsage(usage, nodes, outgoing, dominators, scopeHints, diagnostics);

        var candidates = nodes.Keys.ToDictionary(
            consumerId => consumerId,
            consumerId => (IReadOnlyList<WorkflowBindingCandidate>)BuildCandidates(
                consumerId, nodes, outgoing, dominators, scopeHints).ToArray(),
            StringComparer.Ordinal);
        return new WorkflowBindingAnalysis(usages, candidates, diagnostics);
    }

    /// <summary>分析复合节点显式声明的子画布输出绑定。</summary>
    /// <param name="composite">包含子画布且可能实现绑定声明接口的复合节点。</param>
    /// <param name="startNodeId">子画布入口节点 ID。</param>
    /// <returns>来源不存在、不能保证执行、成员路径错误或类型不兼容等诊断。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="composite"/> 为空。</exception>
    public IReadOnlyList<WorkflowValidationError> AnalyzeSubDocumentDeclarations(
        IWorkflowSubDocumentNode composite,
        string startNodeId)
    {
        ArgumentNullException.ThrowIfNull(composite);
        if (composite is not IWorkflowBindingDeclarationProvider provider)
            return Array.Empty<WorkflowValidationError>();
        var declarations = provider.GetDeclaredBindings()
            .Where(item => item.Scope == WorkflowBindingGraphScope.SubDocument)
            .ToArray();
        if (declarations.Length == 0)
            return Array.Empty<WorkflowValidationError>();

        var graph = WorkflowGraphIndex.Create(composite.SubDocument.Graph);
        var nodes = graph.Nodes;
        var outgoing = graph.Successors;
        var reachable = graph.CollectReachable(startNodeId);
        var dominators = graph.ComputeDominators(startNodeId);
        var structuralDiagnostics = new List<WorkflowValidationError>();
        var parallelScopes = WorkflowParallelScopeAnalyzer.Analyze(graph, structuralDiagnostics).Values
            .Select(scope => new ParallelScopeHint(scope.BranchEntryNodeIds, scope.MergeNodeId))
            .ToArray();
        var terminals = reachable.Where(nodeId => graph.GetSuccessors(nodeId).Count == 0).ToArray();
        var diagnostics = new List<WorkflowValidationError>();
        foreach (var declaration in declarations)
        {
            var usage = new WorkflowBindingUsage(
                composite.Id,
                declaration.Binding,
                declaration.ExpectedType,
                declaration.ConfigurationPath);
            if (declaration.Binding.IsPublicData)
                continue;
            if (!nodes.TryGetValue(declaration.Binding.NodeId, out var sourceNode))
            {
                diagnostics.Add(Error("WFB001", usage, $"子流程绑定来源节点 {declaration.Binding.NodeId} 不存在。"));
                continue;
            }
            if (terminals.Length == 0 || terminals.Any(terminal =>
                    !IsSourceGuaranteed(sourceNode.Id, terminal, outgoing, dominators, parallelScopes)
                    && sourceNode.Id != terminal))
            {
                diagnostics.Add(Error("WFB002", usage,
                    $"子流程来源节点 {sourceNode.Id} 在流程完成前并非必然执行。"));
                continue;
            }
            if (_catalog is null || !_catalog.TryGet(sourceNode.NodeType, out var descriptor) || descriptor!.OutputType is null)
            {
                diagnostics.Add(new WorkflowValidationError(
                    "WFB003",
                    $"节点 {composite.Id} 的子流程绑定 {declaration.Binding} 无法在设计期确定输出类型。",
                    composite.Id,
                    WorkflowValidationSeverity.Warning));
                continue;
            }
            var memberType = ResolveMemberType(descriptor.OutputType, declaration.Binding.MemberPath);
            if (memberType is null)
            {
                diagnostics.Add(Error("WFB004", usage,
                    $"子流程输出类型 {descriptor.OutputType.Name} 不包含成员路径 {declaration.Binding.MemberPath}。"));
            }
            else if (!IsTypeCompatible(memberType, declaration.ExpectedType))
            {
                diagnostics.Add(Error("WFB005", usage,
                    $"子流程成员 {declaration.Binding.MemberPath} 的类型 {memberType.Name} 与目标类型 {declaration.ExpectedType.Name} 不兼容。"));
            }
        }
        return diagnostics;
    }

    /// <summary>枚举正式文档中所有具有静态输出类型的节点及其可绑定成员。</summary>
    /// <param name="document">提供节点配置和显示标题的正式文档。</param>
    /// <returns>所有节点的输出根值和最多三层公开可读成员；未提供节点目录时返回空集合。</returns>
    public IReadOnlyList<WorkflowBindingCandidate> GetOutputCandidates(WorkflowDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (_catalog is null)
            return Array.Empty<WorkflowBindingCandidate>();
        return document.Graph.Nodes
            .OrderBy(node => node.Id, StringComparer.Ordinal)
            .SelectMany(node =>
            {
                if (!_catalog.TryGet(node.NodeType, out var descriptor) || descriptor!.OutputType is null)
                    return Array.Empty<WorkflowBindingCandidate>();
                return EnumerateMembers(descriptor.OutputType, "$", 0)
                    .Select(member => new WorkflowBindingCandidate(
                        node.Id,
                        member.Path,
                        member.Type,
                        $"{node.Title}/{member.Path}"));
            })
            .ToArray();
    }

    /// <summary>判断绑定来源类型能否由运行时转换规则赋给目标输入类型。</summary>
    /// <param name="sourceType">节点输出成员或公共数据声明的静态类型。</param>
    /// <param name="expectedType">消费节点输入属性要求的类型。</param>
    /// <returns>可直接赋值、可转为字符串、枚举兼容或均为数值类型时返回 <see langword="true"/>。</returns>
    public static bool IsTypeCompatible(Type sourceType, Type expectedType)
    {
        var source = Nullable.GetUnderlyingType(sourceType) ?? sourceType;
        var expected = Nullable.GetUnderlyingType(expectedType) ?? expectedType;
        if (expected == typeof(object) || expected.IsAssignableFrom(source))
            return true;
        if (expected == typeof(string))
            return true;
        if (expected.IsEnum)
            return source == typeof(string) || IsNumeric(source);
        return IsNumeric(source) && IsNumeric(expected);
    }

    /// <summary>验证单个绑定来源是否存在、保证先执行，并具有兼容的静态输出成员。</summary>
    /// <param name="usage">待验证的消费位置和绑定键。</param>
    /// <param name="nodes">当前画布节点索引。</param>
    /// <param name="outgoing">按节点 ID 汇总的有向后继关系。</param>
    /// <param name="dominators">每个可达节点的支配节点集合。</param>
    /// <param name="parallelScopes">用于识别汇聚后可见分支输出的并行结构提示。</param>
    /// <param name="diagnostics">接收绑定错误和类型未知警告的集合。</param>
    private void ValidateUsage(
        WorkflowBindingUsage usage,
        IReadOnlyDictionary<string, IWorkflowNodeModel> nodes,
        IReadOnlyDictionary<string, IReadOnlyList<string>> outgoing,
        IReadOnlyDictionary<string, HashSet<string>> dominators,
        IReadOnlyList<ParallelScopeHint> parallelScopes,
        ICollection<WorkflowValidationError> diagnostics)
    {
        if (usage.Binding.IsPublicData)
            return;
        if (!nodes.TryGetValue(usage.Binding.NodeId, out var sourceNode))
        {
            diagnostics.Add(Error("WFB001", usage, $"绑定来源节点 {usage.Binding.NodeId} 不存在。"));
            return;
        }
        if (!IsSourceGuaranteed(usage.Binding.NodeId, usage.ConsumerNodeId, outgoing, dominators, parallelScopes))
        {
            diagnostics.Add(Error("WFB002", usage, $"来源节点 {usage.Binding.NodeId} 在消费者之前并非必然完成。"));
            return;
        }
        if (_catalog is null || !_catalog.TryGet(sourceNode.NodeType, out var descriptor) || descriptor!.OutputType is null)
        {
            diagnostics.Add(new WorkflowValidationError(
                "WFB003",
                $"节点 {usage.ConsumerNodeId} 的绑定 {usage.Binding} 无法在设计期确定输出类型。",
                usage.ConsumerNodeId,
                WorkflowValidationSeverity.Warning));
            return;
        }
        var memberType = ResolveMemberType(descriptor.OutputType, usage.Binding.MemberPath);
        if (memberType is null)
        {
            diagnostics.Add(Error("WFB004", usage,
                $"输出类型 {descriptor.OutputType.Name} 不包含成员路径 {usage.Binding.MemberPath}。"));
            return;
        }
        if (!IsTypeCompatible(memberType, usage.ExpectedType))
        {
            diagnostics.Add(Error("WFB005", usage,
                $"成员 {usage.Binding.MemberPath} 的类型 {memberType.Name} 与输入类型 {usage.ExpectedType.Name} 不兼容。"));
        }
    }

    /// <summary>为一个消费节点生成保证在其执行前可见的强类型节点输出候选。</summary>
    /// <param name="consumerId">消费节点 ID。</param>
    /// <param name="nodes">当前画布节点索引。</param>
    /// <param name="outgoing">按节点 ID 汇总的有向后继关系。</param>
    /// <param name="dominators">每个节点的支配节点集合。</param>
    /// <param name="parallelScopes">允许汇聚后使用分支输出的并行结构提示。</param>
    /// <returns>来源节点的输出根值及公开可读成员。</returns>
    private IEnumerable<WorkflowBindingCandidate> BuildCandidates(
        string consumerId,
        IReadOnlyDictionary<string, IWorkflowNodeModel> nodes,
        IReadOnlyDictionary<string, IReadOnlyList<string>> outgoing,
        IReadOnlyDictionary<string, HashSet<string>> dominators,
        IReadOnlyList<ParallelScopeHint> parallelScopes)
    {
        if (_catalog is null)
            yield break;
        foreach (var source in nodes.Values.OrderBy(node => node.Id, StringComparer.Ordinal))
        {
            if (!IsSourceGuaranteed(source.Id, consumerId, outgoing, dominators, parallelScopes)
                || !_catalog.TryGet(source.NodeType, out var descriptor)
                || descriptor!.OutputType is null)
            {
                continue;
            }
            foreach (var member in EnumerateMembers(descriptor.OutputType, "$", 0))
                yield return new WorkflowBindingCandidate(source.Id, member.Path, member.Type, $"{source.Title}/{member.Path}");
        }
    }

    /// <summary>从一个节点的 WorkflowInput 属性和显式声明接口中提取当前画布绑定。</summary>
    /// <param name="node">待检查的节点配置对象。</param>
    /// <returns>以该节点 ID 为消费者的绑定使用点。</returns>
    private static IEnumerable<WorkflowBindingUsage> ExtractCurrentCanvasUsages(IWorkflowNodeModel node)
    {
        foreach (var usage in ExtractWorkflowInputs(node, node.GetType().Name, new HashSet<object>(ReferenceEqualityComparer.Instance), 0))
            yield return new WorkflowBindingUsage(node.Id, usage.Binding, usage.ExpectedType, usage.Path);
        if (node is IWorkflowBindingDeclarationProvider provider)
        {
            foreach (var declaration in provider.GetDeclaredBindings()
                         .Where(item => item.Scope == WorkflowBindingGraphScope.CurrentCanvas))
            {
                yield return new WorkflowBindingUsage(
                    node.Id, declaration.Binding, declaration.ExpectedType, declaration.ConfigurationPath);
            }
        }
    }

    /// <summary>递归扫描节点配置对象图中的 WorkflowInput 泛型属性。</summary>
    /// <param name="instance">当前扫描对象；为空、值类型或字符串时停止递归。</param>
    /// <param name="path">当前对象在节点配置中的诊断路径。</param>
    /// <param name="visited">按引用身份记录的已访问对象，防止配置对象循环引用。</param>
    /// <param name="depth">当前递归深度，超过五层时停止。</param>
    /// <returns>绑定键、目标泛型类型和配置路径。</returns>
    private static IEnumerable<(WorkflowBindingKey Binding, Type ExpectedType, string Path)> ExtractWorkflowInputs(
        object? instance,
        string path,
        ISet<object> visited,
        int depth)
    {
        if (instance is null || depth > 5 || instance is string || instance.GetType().IsValueType)
            yield break;
        if (!visited.Add(instance))
            yield break;
        foreach (var property in instance.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (!property.CanRead || property.GetIndexParameters().Length != 0)
                continue;
            var propertyType = property.PropertyType;
            object? value;
            try { value = property.GetValue(instance); }
            catch (TargetInvocationException) { continue; }
            var propertyPath = $"{path}.{property.Name}";
            if (propertyType.IsGenericType && propertyType.GetGenericTypeDefinition() == typeof(WorkflowInput<>))
            {
                if (value is null)
                    continue;
                var source = (WorkflowValueSource)propertyType.GetProperty(nameof(WorkflowInput<object>.Source))!.GetValue(value)!;
                var binding = (WorkflowBindingKey?)propertyType.GetProperty(nameof(WorkflowInput<object>.Binding))!.GetValue(value);
                if (source == WorkflowValueSource.Binding && binding.HasValue)
                    yield return (binding.Value, propertyType.GetGenericArguments()[0], propertyPath);
                continue;
            }
            if (value is System.Collections.IEnumerable sequence)
            {
                var index = 0;
                foreach (var item in sequence)
                {
                    foreach (var nested in ExtractWorkflowInputs(item, $"{propertyPath}[{index++}]", visited, depth + 1))
                        yield return nested;
                }
            }
            else if (propertyType.Namespace?.StartsWith("DP.WorkFlow", StringComparison.Ordinal) == true)
            {
                foreach (var nested in ExtractWorkflowInputs(value, propertyPath, visited, depth + 1))
                    yield return nested;
            }
        }
    }

    /// <summary>按不区分大小写的公开属性路径解析输出成员类型。</summary>
    /// <param name="rootType">节点描述器声明的标准输出根类型。</param>
    /// <param name="path">点分隔属性路径；<c>$</c> 代表根类型。</param>
    /// <returns>解析后的非 Nullable 成员类型；路径无效时返回 <see langword="null"/>。</returns>
    private static Type? ResolveMemberType(Type rootType, string path)
    {
        if (path == "$")
            return rootType;
        var current = rootType;
        foreach (var segment in path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var property = current.GetProperty(segment, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
            if (property is null || !property.CanRead || property.GetIndexParameters().Length != 0)
                return null;
            current = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
        }
        return current;
    }

    /// <summary>枚举类型本身以及递归最多三层的公开可读属性。</summary>
    /// <param name="type">要展开的根输出类型。</param>
    /// <param name="path">根值的显示路径，默认使用 <c>$</c>。</param>
    /// <param name="depth">当前递归深度；外部调用通常保持默认值零。</param>
    /// <returns>成员路径及其去除 Nullable 包装后的类型序列。</returns>
    public static IEnumerable<(string Path, Type Type)> EnumerateBindableMembers(Type type, string path = "$", int depth = 0) =>
        EnumerateMembers(type, path, depth);

    /// <summary>递归展开绑定根值和公开可读属性，简单类型或三层深度处停止。</summary>
    /// <param name="type">当前成员类型。</param>
    /// <param name="path">当前成员路径。</param>
    /// <param name="depth">当前递归深度。</param>
    /// <returns>包含当前值本身及其子成员的路径和类型。</returns>
    private static IEnumerable<(string Path, Type Type)> EnumerateMembers(Type type, string path, int depth)
    {
        yield return (path, type);
        if (depth >= 3 || IsSimple(type))
            yield break;
        foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                     .Where(property => property.CanRead && property.GetIndexParameters().Length == 0)
                     .OrderBy(property => property.Name, StringComparer.Ordinal))
        {
            var childType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
            var childPath = path == "$" ? property.Name : $"{path}.{property.Name}";
            foreach (var member in EnumerateMembers(childType, childPath, depth + 1))
                yield return member;
        }
    }

    /// <summary>判断来源节点输出是否保证在消费者执行前产生并对其可见。</summary>
    /// <param name="sourceId">绑定来源节点 ID。</param>
    /// <param name="consumerId">绑定消费节点 ID。</param>
    /// <param name="outgoing">有向后继关系。</param>
    /// <param name="dominators">消费者必经节点集合。</param>
    /// <param name="parallelScopes">结构化并行的分支入口和汇聚点。</param>
    /// <returns>来源支配消费者，或来源是消费者必经汇聚点之前的分支节点时返回 <see langword="true"/>。</returns>
    private static bool IsSourceGuaranteed(
        string sourceId,
        string consumerId,
        IReadOnlyDictionary<string, IReadOnlyList<string>> outgoing,
        IReadOnlyDictionary<string, HashSet<string>> dominators,
        IReadOnlyList<ParallelScopeHint> parallelScopes)
    {
        if (sourceId == consumerId)
            return false;
        if (dominators.TryGetValue(consumerId, out var consumerDominators) && consumerDominators.Contains(sourceId))
            return true;
        foreach (var scope in parallelScopes)
        {
            if (!consumerDominators?.Contains(scope.MergeNodeId) ?? true)
                continue;
            if (scope.BranchEntryNodeIds.Any(branch => IsRequiredBeforeMerge(branch, scope.MergeNodeId, sourceId, outgoing)))
                return true;
        }
        return false;
    }

    /// <summary>判断来源节点是否是指定分支到汇聚点之前无法绕过的节点。</summary>
    /// <param name="branchId">并行分支入口节点 ID。</param>
    /// <param name="mergeId">共同汇聚节点 ID。</param>
    /// <param name="sourceId">候选绑定来源节点 ID。</param>
    /// <param name="outgoing">有向后继关系。</param>
    /// <returns>来源位于该分支且移除来源后无法到达汇聚点时返回 <see langword="true"/>。</returns>
    private static bool IsRequiredBeforeMerge(
        string branchId,
        string mergeId,
        string sourceId,
        IReadOnlyDictionary<string, IReadOnlyList<string>> outgoing)
    {
        if (!CollectReachable(branchId, outgoing).Contains(sourceId))
            return false;
        if (sourceId == branchId)
            return true;
        return !CanReach(branchId, mergeId, outgoing, sourceId);
    }

    /// <summary>判断在排除一个节点后是否仍可从起点到达目标。</summary>
    /// <param name="start">遍历起点节点 ID。</param>
    /// <param name="target">目标节点 ID。</param>
    /// <param name="outgoing">有向后继关系。</param>
    /// <param name="excluded">遍历开始前视为已访问、因而不会经过的节点 ID。</param>
    /// <returns>存在不经过排除节点的路径时返回 <see langword="true"/>。</returns>
    private static bool CanReach(
        string start,
        string target,
        IReadOnlyDictionary<string, IReadOnlyList<string>> outgoing,
        string excluded)
    {
        var pending = new Stack<string>();
        var visited = new HashSet<string>(StringComparer.Ordinal) { excluded };
        pending.Push(start);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            if (!visited.Add(current))
                continue;
            if (current == target)
                return true;
            if (outgoing.TryGetValue(current, out var next))
                foreach (var item in next) pending.Push(item);
        }
        return false;
    }

    /// <summary>收集从起点经任意后继边可到达的节点。</summary>
    /// <param name="start">遍历起点，结果包含该节点。</param>
    /// <param name="outgoing">按节点 ID 索引的后继关系。</param>
    /// <returns>使用序号字符串比较的可达节点集合。</returns>
    private static HashSet<string> CollectReachable(string start, IReadOnlyDictionary<string, IReadOnlyList<string>> outgoing)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<string>();
        pending.Push(start);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            if (!result.Add(current)) continue;
            if (outgoing.TryGetValue(current, out var next)) foreach (var item in next) pending.Push(item);
        }
        return result;
    }

    /// <summary>创建带有消费节点及配置路径上下文的绑定错误。</summary>
    /// <param name="code">稳定的 WFB 诊断代码。</param>
    /// <param name="usage">发生问题的绑定使用点。</param>
    /// <param name="message">具体错误原因。</param>
    /// <returns>定位到消费节点的错误级诊断。</returns>
    private static WorkflowValidationError Error(string code, WorkflowBindingUsage usage, string message) =>
        new(code, $"节点 {usage.ConsumerNodeId} 配置 {usage.ConfigurationPath}：{message}", usage.ConsumerNodeId);

    /// <summary>判断类型是否应作为绑定叶节点而不再展开公开属性。</summary>
    /// <param name="type">待检查类型。</param>
    /// <returns>基础类型、枚举、字符串及常用标量类型返回 <see langword="true"/>。</returns>
    private static bool IsSimple(Type type) => type.IsPrimitive || type.IsEnum || type == typeof(string)
        || type == typeof(decimal) || type == typeof(DateTime) || type == typeof(DateTimeOffset)
        || type == typeof(TimeSpan) || type == typeof(Guid);

    /// <summary>判断类型是否属于运行时支持互相转换的数值类型集合。</summary>
    /// <param name="type">已去除 Nullable 包装的类型。</param>
    /// <returns>内置整数或浮点数类型时返回 <see langword="true"/>。</returns>
    private static bool IsNumeric(Type type) => Type.GetTypeCode(type) is
        TypeCode.Byte or TypeCode.SByte or TypeCode.Int16 or TypeCode.UInt16 or TypeCode.Int32
        or TypeCode.UInt32 or TypeCode.Int64 or TypeCode.UInt64 or TypeCode.Single
        or TypeCode.Double or TypeCode.Decimal;

    /// <summary>绑定分析使用的轻量结构化并行提示。</summary>
    /// <param name="BranchEntryNodeIds">并行分支入口节点 ID。</param>
    /// <param name="MergeNodeId">共同汇聚节点 ID。</param>
    private sealed record ParallelScopeHint(IReadOnlyList<string> BranchEntryNodeIds, string MergeNodeId);
}
