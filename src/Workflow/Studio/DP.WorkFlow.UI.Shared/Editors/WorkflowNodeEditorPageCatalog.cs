namespace DP.WorkFlow.UI;

/// <summary>
/// 聚合内置和插件节点详情页，负责扩展标识唯一性、页面槽位替换和确定性排序。
/// 注册在首次创建页面后冻结，避免已经打开的编辑器与后续编辑器使用不同扩展集合。
/// </summary>
public sealed class WorkflowNodeEditorPageCatalog
{
    private readonly List<IWorkflowNodeEditorPageProvider> _providers = new();
    private readonly HashSet<string> _extensionIds = new(StringComparer.Ordinal);
    private bool _frozen;

    /// <summary>创建包含参数、子流程和脚本能力提供器的默认目录。</summary>
    /// <returns>可继续注册插件页面提供器的目录。</returns>
    public static WorkflowNodeEditorPageCatalog CreateDefault() => new WorkflowNodeEditorPageCatalog()
        .Register(new WorkflowPropertyEditorPageProvider())
        .Register(new WorkflowSubWorkflowEditorPageProvider())
        .Register(new WorkflowScriptEditorPageProvider())
        .Register(new WorkflowNodeResultPageProvider());

    /// <summary>注册一个节点详情页提供器。</summary>
    /// <param name="provider">UI 无关页面提供器。</param>
    /// <returns>当前目录。</returns>
    /// <exception cref="InvalidOperationException">目录已冻结或扩展标识重复。</exception>
    public WorkflowNodeEditorPageCatalog Register(IWorkflowNodeEditorPageProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        if (_frozen)
            throw new InvalidOperationException("节点详情页目录已经冻结，不能继续注册扩展。");
        if (string.IsNullOrWhiteSpace(provider.ExtensionId))
            throw new ArgumentException("节点详情页扩展标识不能为空。", nameof(provider));
        var extensionId = provider.ExtensionId.Trim();
        if (!_extensionIds.Add(extensionId))
            throw new InvalidOperationException($"节点详情页扩展“{extensionId}”已经注册。");
        _providers.Add(provider);
        return this;
    }

    /// <summary>为节点创建并解析最终页面集合。</summary>
    /// <param name="context">隔离编辑会话和节点上下文。</param>
    /// <returns>按显示顺序排列且每个页面槽位唯一的页面集合。</returns>
    public IReadOnlyList<WorkflowNodeEditorPageDescriptor> CreatePages(WorkflowNodeEditorContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _frozen = true;
        var candidates = new List<PageCandidate>();
        foreach (var provider in _providers)
        {
            if (!provider.CanProvide(context))
                continue;
            var pages = provider.CreatePages(context)
                ?? throw new InvalidOperationException($"节点详情页扩展“{provider.ExtensionId}”返回了空页面集合。");
            foreach (var page in pages)
            {
                ValidatePage(provider, page);
                candidates.Add(new PageCandidate(provider.ExtensionId, page));
            }
        }

        var selected = new List<WorkflowNodeEditorPageDescriptor>();
        foreach (var group in candidates.GroupBy(item => item.Page.PageId, StringComparer.Ordinal))
        {
            var priority = group.Max(item => item.Page.Priority);
            var winners = group.Where(item => item.Page.Priority == priority).ToArray();
            if (winners.Length != 1)
                throw new InvalidOperationException(
                    $"节点详情页槽位“{group.Key}”在优先级 {priority} 上存在多个扩展：{string.Join(", ", winners.Select(item => item.ExtensionId))}。");
            selected.Add(winners[0].Page);
        }

        return selected
            .OrderBy(page => page.Order)
            .ThenBy(page => page.Title, StringComparer.Ordinal)
            .ThenBy(page => page.PageId, StringComparer.Ordinal)
            .ToArray();
    }

    private static void ValidatePage(
        IWorkflowNodeEditorPageProvider provider,
        WorkflowNodeEditorPageDescriptor? page)
    {
        if (page is null)
            throw new InvalidOperationException($"节点详情页扩展“{provider.ExtensionId}”返回了 null 页面。");
        if (string.IsNullOrWhiteSpace(page.PageId))
            throw new InvalidOperationException($"节点详情页扩展“{provider.ExtensionId}”返回了空页面标识。");
        if (string.IsNullOrWhiteSpace(page.Title))
            throw new InvalidOperationException($"节点详情页“{page.PageId}”没有标题。");
        if (page.Model is null)
            throw new InvalidOperationException($"节点详情页“{page.PageId}”没有页面模型。");
        if (page.Kind == WorkflowNodeEditorPageKind.Custom
            && string.IsNullOrWhiteSpace(page.RendererKey))
            throw new InvalidOperationException($"自定义节点详情页“{page.PageId}”没有 RendererKey。");
    }

    private sealed record PageCandidate(string ExtensionId, WorkflowNodeEditorPageDescriptor Page);
}

/// <summary>为所有节点提供通用反射属性页。</summary>
public sealed class WorkflowPropertyEditorPageProvider : IWorkflowNodeEditorPageProvider
{
    /// <inheritdoc />
    public string ExtensionId => "Workflow.BuiltIn.Properties";

    /// <inheritdoc />
    public bool CanProvide(WorkflowNodeEditorContext context) => true;

    /// <inheritdoc />
    public IEnumerable<WorkflowNodeEditorPageDescriptor> CreatePages(WorkflowNodeEditorContext context)
    {
        yield return new WorkflowNodeEditorPageDescriptor(
            "Properties", "参数", WorkflowNodeEditorPageKind.Properties, 100,
            new WorkflowPropertyEditorPageModel(context.Session, context.EntryNodeId)
            { ChoiceProvider = context.ChoiceProvider, AdditionalProperties = context.AdditionalProperties }, "Properties");
    }
}

/// <summary>根据子文档能力提供嵌入式子工作流页。</summary>
public sealed class WorkflowSubWorkflowEditorPageProvider : IWorkflowNodeEditorPageProvider
{
    /// <inheritdoc />
    public string ExtensionId => "Workflow.BuiltIn.SubWorkflow";

    /// <inheritdoc />
    public bool CanProvide(WorkflowNodeEditorContext context) => context.Node is IWorkflowSubDocumentNode;

    /// <inheritdoc />
    public IEnumerable<WorkflowNodeEditorPageDescriptor> CreatePages(WorkflowNodeEditorContext context)
    {
        if (context.Node is not IWorkflowSubDocumentNode node)
            yield break;
        yield return new WorkflowNodeEditorPageDescriptor(
            "SubWorkflow", "流程", WorkflowNodeEditorPageKind.SubWorkflow, 200,
            new WorkflowSubWorkflowEditorPageModel(context.Session, node), "Workflow");
    }
}

/// <summary>根据脚本能力提供脚本编辑和诊断页。</summary>
public sealed class WorkflowScriptEditorPageProvider : IWorkflowNodeEditorPageProvider
{
    /// <inheritdoc />
    public string ExtensionId => "Workflow.BuiltIn.Script";

    /// <inheritdoc />
    public bool CanProvide(WorkflowNodeEditorContext context) => context.Node is IWorkflowScriptNode;

    /// <inheritdoc />
    public IEnumerable<WorkflowNodeEditorPageDescriptor> CreatePages(WorkflowNodeEditorContext context)
    {
        if (context.Node is not IWorkflowScriptNode node)
            yield break;
        var model = new WorkflowScriptEditorPageModel(context.Session, node);
        yield return new WorkflowNodeEditorPageDescriptor(
            "Script", "脚本", WorkflowNodeEditorPageKind.Script, 300, model, "Code");
        yield return new WorkflowNodeEditorPageDescriptor(
            "ScriptDiagnostics", "诊断", WorkflowNodeEditorPageKind.Diagnostics, 600, model, "Diagnostics");
    }
}
