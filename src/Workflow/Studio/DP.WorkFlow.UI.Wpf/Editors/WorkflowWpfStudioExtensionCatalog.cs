using DP.WorkFlow.UI;

namespace DP.WorkFlow.UI.Wpf;

/// <summary>WPF Studio 插件 Module；一次注册同时贡献 UI 无关页面和平台 Renderer。</summary>
public interface IWorkflowWpfStudioExtension
{
    /// <summary>获取稳定插件标识。</summary>
    string ExtensionId { get; }

    /// <summary>向 WPF Studio 扩展目录注册能力。</summary>
    /// <param name="extensions">扩展目录。</param>
    void Register(WorkflowWpfStudioExtensionCatalog extensions);
}

/// <summary>
/// WPF 节点详情扩展组合目录。首次打开节点编辑器后目录冻结，重复页面提供器或 RendererKey 立即失败。
/// </summary>
public sealed class WorkflowWpfStudioExtensionCatalog
{
    private readonly WorkflowStudioPluginCatalog _shared = new();
    private readonly List<IWorkflowWpfNodeEditorPageRenderer> _renderers = new();
    private readonly HashSet<string> _extensionIds = new(StringComparer.Ordinal);
    private readonly HashSet<string> _rendererKeys = new(StringComparer.Ordinal);
    private bool _frozen;

    /// <summary>注册一个完整 Studio 插件 Module。</summary>
    /// <param name="extension">插件 Module。</param>
    /// <returns>当前目录。</returns>
    public WorkflowWpfStudioExtensionCatalog Register(IWorkflowWpfStudioExtension extension)
    {
        ArgumentNullException.ThrowIfNull(extension);
        EnsureMutable();
        var extensionId = RequireKey(extension.ExtensionId, nameof(extension));
        if (!_extensionIds.Add(extensionId))
            throw new InvalidOperationException($"WPF Studio 扩展“{extensionId}”已经注册。");
        extension.Register(this);
        return this;
    }

    /// <summary>注册 UI 无关节点页面提供器。</summary>
    /// <param name="provider">页面提供器。</param>
    /// <returns>当前目录。</returns>
    public WorkflowWpfStudioExtensionCatalog RegisterPageProvider(IWorkflowNodeEditorPageProvider provider)
    {
        EnsureMutable();
        _shared.RegisterPageProvider(provider);
        return this;
    }

    /// <summary>注册一个 UI 无关 Studio 插件 Module。</summary>
    /// <param name="extension">共享 Studio 插件 Module。</param>
    /// <returns>当前目录。</returns>
    public WorkflowWpfStudioExtensionCatalog Register(IWorkflowStudioPluginModule extension)
    {
        EnsureMutable();
        _shared.Register(extension);
        return this;
    }

    /// <summary>从插件目录装载 UI 无关 Studio Module。</summary>
    /// <param name="pluginRoot">插件包根目录。</param>
    /// <param name="loader">共享插件加载器。</param>
    /// <returns>实际注册数量。</returns>
    public int LoadSharedPlugins(string pluginRoot, WorkflowPluginLoader loader)
    {
        EnsureMutable();
        return _shared.LoadPlugins(pluginRoot, loader);
    }

    /// <summary>注册 WPF 页面 Renderer。</summary>
    /// <param name="renderer">平台 Renderer。</param>
    /// <returns>当前目录。</returns>
    public WorkflowWpfStudioExtensionCatalog RegisterRenderer(IWorkflowWpfNodeEditorPageRenderer renderer)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        EnsureMutable();
        var rendererKey = RequireKey(renderer.RendererKey, nameof(renderer));
        if (!_rendererKeys.Add(rendererKey))
            throw new InvalidOperationException($"WPF 节点详情页渲染器“{rendererKey}”已经注册。");
        _renderers.Add(renderer);
        return this;
    }

    internal IReadOnlyList<IWorkflowNodeEditorPageProvider> GetPageProviders()
    {
        _frozen = true;
        return _shared.GetPageProviders();
    }

    internal IReadOnlyList<IWorkflowWpfNodeEditorPageRenderer> GetRenderers()
    {
        _frozen = true;
        return _renderers.ToArray();
    }

    private void EnsureMutable()
    {
        if (_frozen)
            throw new InvalidOperationException("WPF Studio 扩展目录已经冻结。");
    }

    private static string RequireKey(string? key, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("扩展标识不能为空。", parameterName);
        return key.Trim();
    }
}
