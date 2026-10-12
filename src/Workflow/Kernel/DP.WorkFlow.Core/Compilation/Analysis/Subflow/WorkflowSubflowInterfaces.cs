namespace DP.WorkFlow;

/// <summary>复合节点子流程接口（输入/输出）的类型解析、成员注入、绑定声明和校验。</summary>
public static class WorkflowSubflowInterfaces
{
    /// <summary>
    /// 解析文档及其全部嵌套子画布中复合节点接口的类型并注入：输入成员注入子画布的启动节点，
    /// 输出成员注入复合节点。来源无法解析时按 <see cref="object"/> 处理，具体错误由编译期绑定校验报告。
    /// </summary>
    /// <param name="document">要刷新的文档。</param>
    /// <param name="catalog">节点目录；为空时类型全部按 <see cref="object"/> 处理。</param>
    public static void Refresh(WorkflowDocument document, WorkflowNodeCatalog? catalog)
    {
        ArgumentNullException.ThrowIfNull(document);
        Refresh(document, catalog, new HashSet<WorkflowDocument>(ReferenceEqualityComparer.Instance));
    }

    /// <summary>复合节点当前声明的输入成员及其类型。</summary>
    /// <param name="composite">复合节点。</param>
    /// <param name="parent">复合节点所在的文档，绑定来源在其中解析。</param>
    /// <param name="catalog">节点目录。</param>
    public static IReadOnlyList<WorkflowBindableMember> DescribeInputs(
        IWorkflowSubflowNode composite,
        WorkflowDocument parent,
        WorkflowNodeCatalog? catalog)
    {
        ArgumentNullException.ThrowIfNull(composite);
        ArgumentNullException.ThrowIfNull(parent);
        return UniqueValid(composite.Inputs, input => input.Name)
            .Select(input =>
            {
                var name = input.Name.Trim();
                var type = input.Source is { } source
                    ? ResolveBindingType(source, parent, catalog)
                    : WorkflowSubflowInput.ClrType(input.ValueType);
                return new WorkflowBindableMember(name, type ?? typeof(object), name);
            })
            .ToArray();
    }

    /// <summary>复合节点当前声明的输出成员及其类型（跟随子画布中的来源）。</summary>
    /// <param name="composite">复合节点。</param>
    /// <param name="catalog">节点目录。</param>
    public static IReadOnlyList<WorkflowBindableMember> DescribeOutputs(
        IWorkflowSubflowNode composite,
        WorkflowNodeCatalog? catalog)
    {
        ArgumentNullException.ThrowIfNull(composite);
        return UniqueValid(composite.Outputs, output => output.Name)
            .Select(output =>
            {
                var name = output.Name.Trim();
                var type = output.Source is { } source
                    ? ResolveBindingType(source, composite.SubDocument, catalog)
                    : null;
                return new WorkflowBindableMember(name, type ?? typeof(object), name);
            })
            .ToArray();
    }

    /// <summary>
    /// 复合节点的全部可绑定输出成员：节点输出类型的固定成员（不含结果页使用的逐项行）加上接口中的各项输出。
    /// </summary>
    /// <param name="composite">复合节点。</param>
    /// <param name="outputType">复合节点登记的输出类型。</param>
    public static IReadOnlyList<WorkflowBindableMember> DescribeCompositeOutputs(IWorkflowSubflowNode composite, Type outputType)
    {
        ArgumentNullException.ThrowIfNull(composite);
        ArgumentNullException.ThrowIfNull(outputType);
        return WorkflowOutputMembers.OfType(outputType)
            .Where(member => member.Path != nameof(IWorkflowResultRows.ResultRows)
                && !member.Path.StartsWith(nameof(IWorkflowResultRows.ResultRows) + ".", StringComparison.Ordinal))
            .Concat(composite.SubflowOutputs)
            .ToArray();
    }

    /// <summary>在文档中解析绑定来源成员的静态类型；来源不存在或无法解析时返回空。</summary>
    /// <param name="binding">绑定键。</param>
    /// <param name="document">来源节点所在文档。</param>
    /// <param name="catalog">节点目录。</param>
    public static Type? ResolveBindingType(WorkflowBindingKey binding, WorkflowDocument document, WorkflowNodeCatalog? catalog)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (catalog is null || binding.IsPublicData)
            return null;
        var node = document.Graph.Nodes.FirstOrDefault(item => string.Equals(item.Id, binding.NodeId, StringComparison.Ordinal));
        if (node is null || !catalog.TryGet(node.NodeType, out var descriptor) || descriptor!.OutputType is null)
            return null;
        return WorkflowOutputMembers.Resolve(node, descriptor.OutputType, binding.MemberPath);
    }

    /// <summary>接口中引用的绑定：输入来源属于父画布，输出来源属于子画布。</summary>
    /// <param name="composite">复合节点。</param>
    public static IEnumerable<WorkflowDeclaredBinding> DeclareBindings(IWorkflowSubflowNode composite)
    {
        ArgumentNullException.ThrowIfNull(composite);
        for (var index = 0; index < composite.Inputs.Count; index++)
        {
            if (composite.Inputs[index].Source is { } source)
                yield return new WorkflowDeclaredBinding(source, typeof(object), $"Inputs[{index}].Source");
        }
        for (var index = 0; index < composite.Outputs.Count; index++)
        {
            if (composite.Outputs[index].Source is { } source)
            {
                yield return new WorkflowDeclaredBinding(
                    source,
                    typeof(object),
                    $"Outputs[{index}].Source",
                    WorkflowBindingGraphScope.SubDocument);
            }
        }
    }

    /// <summary>校验接口本身：名称、重名、手动值和输出来源。来源节点及成员路径由绑定分析校验。</summary>
    /// <param name="composite">复合节点。</param>
    /// <param name="outputType">复合节点登记的输出类型；输出名称不能与其中的成员重名。</param>
    public static IReadOnlyList<WorkflowValidationError> Validate(IWorkflowSubflowNode composite, Type? outputType)
    {
        ArgumentNullException.ThrowIfNull(composite);
        var errors = new List<WorkflowValidationError>();
        ValidateNames(composite, composite.Inputs.Select(input => input.Name), "输入", errors);
        ValidateNames(composite, composite.Outputs.Select(output => output.Name), "输出", errors);
        foreach (var input in composite.Inputs.Where(input => input.Source is null))
        {
            if (!input.TryGetManualValue(out _, out var error))
                errors.Add(Error(composite, "WFS003", $"输入 {input.Name} 的手动值无效：{error}"));
        }
        foreach (var output in composite.Outputs.Where(output => output.Source is null))
            errors.Add(Error(composite, "WFS004", $"输出 {output.Name} 未选择子画布中的来源。"));
        if (outputType is not null)
        {
            var builtIn = outputType.GetProperties()
                .Select(property => property.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var name in composite.Outputs.Select(output => output.Name.Trim()).Where(builtIn.Contains).Distinct())
                errors.Add(Error(composite, "WFS005", $"输出名称 {name} 与节点的内置输出成员重名。"));
        }
        return errors;
    }

    /// <summary>名称是否可用作绑定路径中的一段。</summary>
    /// <param name="name">输入或输出名称。</param>
    public static bool IsValidName(string? name) =>
        !string.IsNullOrWhiteSpace(name) && name.Trim() != "$" && name.IndexOfAny(new[] { '.', '|' }) < 0;

    private static void Refresh(WorkflowDocument document, WorkflowNodeCatalog? catalog, HashSet<WorkflowDocument> ancestors)
    {
        if (!ancestors.Add(document))
            return;
        try
        {
            var composites = document.Graph.Nodes.OfType<IWorkflowSubflowNode>().ToArray();
            var processed = new HashSet<IWorkflowSubflowNode>(ReferenceEqualityComparer.Instance);
            foreach (var composite in composites)
                Process(composite);

            void Process(IWorkflowSubflowNode composite)
            {
                if (!processed.Add(composite))
                    return;
                // 输入来源是同一画布上的另一个复合节点时，先解析它的输出。
                foreach (var input in composite.Inputs)
                {
                    if (input.Source is { } source
                        && composites.FirstOrDefault(item => string.Equals(item.Id, source.NodeId, StringComparison.Ordinal)) is { } upstream)
                        Process(upstream);
                }
                var inputs = DescribeInputs(composite, document, catalog);
                foreach (var entry in composite.SubDocument.Graph.Nodes.OfType<IWorkflowSubflowEntryNode>())
                    entry.SubflowInputs = inputs;
                // 子画布中的嵌套复合节点先解析，外层输出才能引用它们的输出。
                Refresh(composite.SubDocument, catalog, ancestors);
                composite.SubflowOutputs = DescribeOutputs(composite, catalog);
            }
        }
        finally
        {
            ancestors.Remove(document);
        }
    }

    private static IEnumerable<T> UniqueValid<T>(IEnumerable<T> items, Func<T, string> name) =>
        items.Where(item => IsValidName(name(item)))
            .GroupBy(item => name(item).Trim(), StringComparer.Ordinal)
            .Select(group => group.First());

    private static void ValidateNames(
        IWorkflowSubflowNode composite,
        IEnumerable<string> names,
        string category,
        ICollection<WorkflowValidationError> errors)
    {
        var all = names.ToArray();
        foreach (var name in all.Where(name => !IsValidName(name)))
            errors.Add(Error(composite, "WFS001", $"{category}名称“{name}”无效：不能为空，不能是 $，不能包含 . 或 |。"));
        foreach (var duplicate in all.Where(IsValidName)
                     .GroupBy(name => name.Trim(), StringComparer.Ordinal)
                     .Where(group => group.Count() > 1))
            errors.Add(Error(composite, "WFS002", $"{category}名称 {duplicate.Key} 重复。"));
    }

    private static WorkflowValidationError Error(IWorkflowSubflowNode composite, string code, string message) =>
        new(code, $"节点 {composite.Id} 的子流程接口：{message}", composite.Id);
}
