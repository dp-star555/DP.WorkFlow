namespace DP.WorkFlow;

/// <summary>节点对一项公共数据的使用。</summary>
/// <param name="Declaration">节点给出的键、形态和值类型（初始化类节点带默认值）。</param>
/// <param name="NodeId">节点 ID。</param>
/// <param name="PlanPath">节点所在画布的路径：<c>$</c> 为根画布，<c>$/循环块1</c> 为其子画布。</param>
public sealed record WorkflowPublicDataUse(WorkflowPublicDataDeclaration Declaration, string NodeId, string PlanPath);

/// <summary>
/// 流程公共数据清单的核对与推断（R2-d）：清单登记在根文档上，覆盖根画布和全部子画布中节点的使用。
/// </summary>
public static class WorkflowPublicDataManifest
{
    /// <summary>清单本身有误：键为空或重复、默认值无法按类型解析、bool 信号类型不是 Boolean。</summary>
    public const string InvalidDeclarationCode = "WFPD01";

    /// <summary>节点用到的公共数据没有登记。</summary>
    public const string UndeclaredCode = "WFPD02";

    /// <summary>节点使用的形态或类型与登记不一致，或不同节点对同一个键的用法不一致，或 <c>$global:</c> 绑定的成员或类型不匹配。</summary>
    public const string MismatchCode = "WFPD03";

    /// <summary>按画布顺序收集文档及其全部子画布中节点对公共数据的使用。</summary>
    /// <param name="document">根文档。</param>
    /// <returns>全部使用。</returns>
    public static IReadOnlyList<WorkflowPublicDataUse> CollectUses(WorkflowDocument document)
    {
        Guard.NotNull(document);
        var uses = new List<WorkflowPublicDataUse>();
        Collect(document, "$", uses, null, new HashSet<WorkflowDocument>(ReferenceEqualityComparer.Instance));
        return uses;
    }

    /// <summary>
    /// 根据节点的使用推断清单：同一个键以第一次出现的用法为准，带默认值的使用（初始化类节点）补上默认值；
    /// 与先出现用法不一致的使用不进入清单，由编译核对报告。
    /// </summary>
    /// <param name="document">根文档。</param>
    /// <returns>推断出的登记项，按首次出现的顺序。</returns>
    public static IReadOnlyList<WorkflowPublicDataDeclaration> Infer(WorkflowDocument document)
    {
        var result = new List<WorkflowPublicDataDeclaration>();
        var index = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var use in CollectUses(document))
        {
            var item = use.Declaration;
            if (!index.TryGetValue(item.Key, out var position))
            {
                index[item.Key] = result.Count;
                result.Add(item);
            }
            else if (result[position].IsCompatibleWith(item) && result[position].DefaultValueText is null && item.DefaultValueText is not null)
            {
                result[position] = result[position] with { DefaultValueText = item.DefaultValueText };
            }
        }
        return result;
    }

    /// <summary>把推断出的、清单里还没有的登记项补进文档清单。</summary>
    /// <param name="document">根文档。</param>
    /// <returns>补进的登记项。</returns>
    public static IReadOnlyList<WorkflowPublicDataDeclaration> AddMissing(WorkflowDocument document)
    {
        Guard.NotNull(document);
        var known = new HashSet<string>(document.PublicData.Select(item => item.Key), StringComparer.Ordinal);
        var added = Infer(document).Where(item => known.Add(item.Key)).ToArray();
        document.PublicData.AddRange(added);
        return added;
    }

    /// <summary>核对清单本身以及全部节点的使用。</summary>
    /// <param name="document">根文档。</param>
    /// <returns>诊断；错误阻止编译。</returns>
    public static IReadOnlyList<WorkflowValidationError> Validate(WorkflowDocument document)
    {
        Guard.NotNull(document);
        var diagnostics = new List<WorkflowValidationError>();
        var declared = new Dictionary<string, WorkflowPublicDataDeclaration>(StringComparer.Ordinal);
        foreach (var item in document.PublicData)
        {
            if (item is null || string.IsNullOrWhiteSpace(item.Key))
            {
                diagnostics.Add(new WorkflowValidationError(InvalidDeclarationCode, "公共数据清单中有键为空的登记项。"));
                continue;
            }
            if (!declared.TryAdd(item.Key, item))
            {
                diagnostics.Add(new WorkflowValidationError(InvalidDeclarationCode, $"公共数据 {item.Key} 在清单中登记了多次。"));
                continue;
            }
            if (item.Kind == WorkflowPublicDataKind.Signal && item.ValueType != E_SignalValueType.Boolean)
                diagnostics.Add(new WorkflowValidationError(InvalidDeclarationCode, $"bool 信号 {item.Key} 的值类型必须是 Boolean。"));
            if (item.DefaultValueText is { } text && !CanConvert(text, item.ValueType))
                diagnostics.Add(new WorkflowValidationError(InvalidDeclarationCode, $"公共数据 {item.Key} 的默认值 \"{text}\" 不是 {item.ValueType}。"));
        }

        var firstUse = new Dictionary<string, WorkflowPublicDataUse>(StringComparer.Ordinal);
        var reportedUndeclared = new HashSet<string>(StringComparer.Ordinal);
        foreach (var use in CollectUses(document))
        {
            var key = use.Declaration.Key;
            if (declared.TryGetValue(key, out var declaration))
            {
                if (!declaration.IsCompatibleWith(use.Declaration))
                    diagnostics.Add(Diagnostic(MismatchCode,
                        $"节点 {use.NodeId} 把公共数据 {key} 作为 {use.Declaration.Describe()} 使用，清单登记的是 {declaration.Describe()}。", use));
                continue;
            }
            if (firstUse.TryGetValue(key, out var first))
            {
                if (!first.Declaration.IsCompatibleWith(use.Declaration))
                    diagnostics.Add(Diagnostic(MismatchCode,
                        $"节点 {use.NodeId} 把公共数据 {key} 作为 {use.Declaration.Describe()} 使用，节点 {first.NodeId} 用作 {first.Declaration.Describe()}。", use));
                continue;
            }
            firstUse[key] = use;
            if (reportedUndeclared.Add(key))
                diagnostics.Add(Diagnostic(UndeclaredCode,
                    $"公共数据 {key}（{use.Declaration.Describe()}）没有登记到流程的公共数据清单；运行时首次使用时自动登记。", use,
                    WorkflowValidationSeverity.Warning));
        }
        ValidateBindings(document, declared, diagnostics);
        return diagnostics;
    }

    /// <summary>核对 <c>$global:</c> 绑定：键应已登记，成员路径应存在且类型可转换为输入类型。</summary>
    private static void ValidateBindings(WorkflowDocument document, IReadOnlyDictionary<string, WorkflowPublicDataDeclaration> declared,
        List<WorkflowValidationError> diagnostics)
    {
        var bindings = new List<(WorkflowBindingUsage Usage, string PlanPath)>();
        Collect(document, "$", null, bindings, new HashSet<WorkflowDocument>(ReferenceEqualityComparer.Instance));
        foreach (var (usage, planPath) in bindings)
        {
            var key = usage.Binding.PublicDataKey!.Trim();
            if (!declared.TryGetValue(key, out var declaration))
            {
                diagnostics.Add(new WorkflowValidationError(UndeclaredCode,
                    $"节点 {usage.ConsumerNodeId} 的 {usage.ConfigurationPath} 绑定了未登记的公共数据 {key}。",
                    usage.ConsumerNodeId, WorkflowValidationSeverity.Warning) { PlanPath = planPath });
                continue;
            }
            var member = WorkflowBindingAnalyzer.EnumerateBindableMembers(declaration.GetClrType())
                .FirstOrDefault(item => string.Equals(item.Path, usage.Binding.MemberPath, StringComparison.Ordinal));
            if (member.Type is null)
                diagnostics.Add(new WorkflowValidationError(MismatchCode,
                    $"节点 {usage.ConsumerNodeId} 绑定的公共数据 {key}（{declaration.Describe()}）没有成员 {usage.Binding.MemberPath}。",
                    usage.ConsumerNodeId) { PlanPath = planPath });
            else if (!WorkflowBindingAnalyzer.IsTypeCompatible(member.Type, usage.ExpectedType))
                diagnostics.Add(new WorkflowValidationError(MismatchCode,
                    $"节点 {usage.ConsumerNodeId} 的 {usage.ConfigurationPath} 需要 {usage.ExpectedType.Name}，公共数据 {key} 的 {usage.Binding.MemberPath} 是 {member.Type.Name}。",
                    usage.ConsumerNodeId) { PlanPath = planPath });
        }
    }

    private static WorkflowValidationError Diagnostic(string code, string message, WorkflowPublicDataUse use,
        WorkflowValidationSeverity severity = WorkflowValidationSeverity.Error) =>
        new(code, message, use.NodeId, severity) { PlanPath = use.PlanPath };

    private static void Collect(WorkflowDocument document, string path, List<WorkflowPublicDataUse>? uses,
        List<(WorkflowBindingUsage Usage, string PlanPath)>? bindings, HashSet<WorkflowDocument> visiting)
    {
        if (!visiting.Add(document))
            return;
        foreach (var node in document.Graph.Nodes)
        {
            if (bindings is not null)
                foreach (var usage in WorkflowBindingAnalyzer.ExtractCurrentCanvasUsages(node).Where(item => item.Binding.IsPublicData))
                    bindings.Add((usage, path));
            if (uses is not null && node is IWorkflowPublicDataUser user)
            {
                foreach (var declaration in user.GetPublicDataUses() ?? Array.Empty<WorkflowPublicDataDeclaration>())
                    if (declaration is not null && !string.IsNullOrWhiteSpace(declaration.Key))
                        uses.Add(new WorkflowPublicDataUse(declaration with { Key = declaration.Key.Trim() }, node.Id, path));
            }
            if (node is IWorkflowSubDocumentNode composite && composite.SubDocument is not null)
                Collect(composite.SubDocument, path + "/" + Uri.EscapeDataString(node.Id), uses, bindings, visiting);
        }
        visiting.Remove(document);
    }

    private static bool CanConvert(string text, E_SignalValueType type)
    {
        try
        {
            _ = WorkflowPublicDataValues.Convert(text, type);
            return true;
        }
        catch (Exception exception) when (exception is FormatException or InvalidCastException or OverflowException or ArgumentException)
        {
            return false;
        }
    }
}
