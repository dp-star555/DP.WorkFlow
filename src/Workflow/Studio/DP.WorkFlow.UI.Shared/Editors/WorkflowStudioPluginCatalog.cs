namespace DP.WorkFlow.UI;

/// <summary>与 WinForms/WPF 无关的 Studio 插件 Module；贡献节点详情页提供器。</summary>
public interface IWorkflowStudioPluginModule
{
    /// <summary>获取稳定插件 Module 标识。</summary>
    string ExtensionId { get; }

    /// <summary>向共享 Studio 目录注册 UI 无关页面提供器。</summary>
    /// <param name="extensions">共享 Studio 扩展目录。</param>
    void Register(WorkflowStudioPluginCatalog extensions);
}

/// <summary>共享 Studio 插件组合 Module，负责页面提供器的成组注册、冲突诊断和冻结。</summary>
public sealed class WorkflowStudioPluginCatalog
{
    private readonly List<IWorkflowNodeEditorPageProvider> _pageProviders = new();
    private readonly HashSet<string> _extensionIds = new(StringComparer.Ordinal);
    private readonly HashSet<string> _pageProviderIds = new(StringComparer.Ordinal);
    private bool _frozen;

    /// <summary>注册一个共享 Studio 插件 Module。</summary>
    /// <param name="extension">插件 Module。</param>
    /// <returns>当前目录。</returns>
    public WorkflowStudioPluginCatalog Register(IWorkflowStudioPluginModule extension)
    {
        ArgumentNullException.ThrowIfNull(extension);
        EnsureMutable();
        var extensionId = RequireKey(extension.ExtensionId, nameof(extension));
        if (!_extensionIds.Add(extensionId))
            throw new InvalidOperationException($"共享 Studio 扩展“{extensionId}”已经注册。");
        extension.Register(this);
        return this;
    }

    /// <summary>注册 UI 无关节点详情页提供器。</summary>
    /// <param name="provider">页面提供器。</param>
    /// <returns>当前目录。</returns>
    public WorkflowStudioPluginCatalog RegisterPageProvider(IWorkflowNodeEditorPageProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        EnsureMutable();
        var extensionId = RequireKey(provider.ExtensionId, nameof(provider));
        if (!_pageProviderIds.Add(extensionId))
            throw new InvalidOperationException($"节点详情页扩展“{extensionId}”已经注册。");
        _pageProviders.Add(provider);
        return this;
    }

    /// <summary>从插件目录发现并注册 UI 无关 Studio Module。</summary>
    /// <param name="pluginRoot">插件包根目录。</param>
    /// <param name="loader">可复用的插件加载器；为空时创建新实例。</param>
    /// <returns>实际注册的 Module 数量。</returns>
    public int LoadPlugins(string pluginRoot, WorkflowPluginLoader? loader = null)
    {
        var modules = (loader ?? new WorkflowPluginLoader())
            .LoadModules<IWorkflowStudioPluginModule>(pluginRoot, WorkflowPluginModuleGroups.Studio);
        foreach (var module in modules)
            Register(module);
        return modules.Count;
    }

    /// <summary>冻结目录并获取页面提供器快照。</summary>
    /// <returns>按注册顺序排列的提供器。</returns>
    public IReadOnlyList<IWorkflowNodeEditorPageProvider> GetPageProviders()
    {
        _frozen = true;
        return _pageProviders.ToArray();
    }

    private void EnsureMutable()
    {
        if (_frozen)
            throw new InvalidOperationException("共享 Studio 扩展目录已经冻结。");
    }

    private static string RequireKey(string? key, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("扩展标识不能为空。", parameterName);
        return key.Trim();
    }
}
