namespace DP.WorkFlow;

/// <summary>无桌面依赖的工作流插件 Module；一次注册同时贡献节点模型和执行处理器。</summary>
public interface IWorkflowRuntimePluginModule
{
    /// <summary>获取稳定插件 Module 标识。</summary>
    string ExtensionId { get; }

    /// <summary>向运行组合目录注册节点和执行处理器。</summary>
    /// <param name="extensions">运行组合目录。</param>
    void Register(WorkflowRuntimePluginCatalog extensions);
}

/// <summary>
/// 工作流运行插件组合 Module。它隐藏节点目录与处理器目录的成对注册，并拒绝重复插件标识。
/// </summary>
public sealed class WorkflowRuntimePluginCatalog
{
    private readonly HashSet<string> _extensionIds = new(StringComparer.Ordinal);
    private readonly object _syncRoot = new();
    private bool _frozen;

    /// <summary>初始化运行插件组合目录。</summary>
    /// <param name="nodes">节点类型目录。</param>
    /// <param name="handlers">节点执行处理器目录。</param>
    public WorkflowRuntimePluginCatalog(
        WorkflowNodeCatalog nodes,
        WorkflowNodeHandlerCatalog handlers)
    {
        Nodes = nodes ?? throw new ArgumentNullException(nameof(nodes));
        Handlers = handlers ?? throw new ArgumentNullException(nameof(handlers));
    }

    /// <summary>获取插件注册使用的节点类型目录。</summary>
    public WorkflowNodeCatalog Nodes { get; }

    /// <summary>获取插件注册使用的执行处理器目录。</summary>
    public WorkflowNodeHandlerCatalog Handlers { get; }

    /// <summary>注册一个运行插件 Module。</summary>
    /// <param name="extension">运行插件 Module。</param>
    /// <returns>当前组合目录。</returns>
    public WorkflowRuntimePluginCatalog Register(IWorkflowRuntimePluginModule extension)
    {
        ArgumentNullException.ThrowIfNull(extension);
        lock (_syncRoot)
        {
            EnsureMutable();
            if (string.IsNullOrWhiteSpace(extension.ExtensionId))
                throw new ArgumentException("工作流运行插件标识不能为空。", nameof(extension));
            var extensionId = extension.ExtensionId.Trim();
            if (!_extensionIds.Add(extensionId))
                throw new InvalidOperationException($"工作流运行插件“{extensionId}”已经注册。");
            extension.Register(this);
            return this;
        }
    }

    /// <summary>从插件目录发现并注册全部 Runtime Module。</summary>
    /// <param name="pluginRoot">插件包根目录。</param>
    /// <param name="loader">可复用的插件加载器；为空时创建新实例。</param>
    /// <returns>实际注册的 Module 数量。</returns>
    public int LoadPlugins(string pluginRoot, WorkflowPluginLoader? loader = null)
    {
        lock (_syncRoot)
            EnsureMutable();
        var modules = (loader ?? new WorkflowPluginLoader())
            .LoadModules<IWorkflowRuntimePluginModule>(pluginRoot, WorkflowPluginModuleGroups.Runtime);
        foreach (var module in modules)
            Register(module);
        return modules.Count;
    }

    /// <summary>获取运行组合目录是否已经冻结。</summary>
    public bool IsFrozen
    {
        get
        {
            lock (_syncRoot)
                return _frozen;
        }
    }

    /// <summary>
    /// 冻结节点和处理器目录，并验证每种已注册节点都能创建有效模型且恰好匹配一个处理器。
    /// </summary>
    /// <returns>当前已经冻结的运行组合目录。</returns>
    /// <exception cref="InvalidOperationException">节点工厂无效，或节点缺少处理器、匹配多个处理器。</exception>
    /// <remarks>
    /// 全部校验都在发布之前完成：任何一步失败都不会冻结节点目录、处理器目录或本目录，
    /// 因此失败后不会留下"已冻结"的外观，调用方可以补全配置后重试。
    /// </remarks>
    public WorkflowRuntimePluginCatalog Freeze()
    {
        lock (_syncRoot)
        {
            if (_frozen)
                return this;

            // 第一步：在候选配置上完整校验。此阶段不得产生任何状态变更。
            Nodes.Validate();
            foreach (var descriptor in Nodes.Snapshot().Values)
            {
                IWorkflowNodeModel sample;
                try
                {
                    sample = descriptor.Factory();
                }
                catch (Exception exception)
                {
                    throw new InvalidOperationException(
                        $"节点类型 {descriptor.NodeType} 的工厂无法创建模型。", exception);
                }

                try
                {
                    _ = Handlers.ResolveWithRequirements(sample);
                }
                catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
                {
                    throw new InvalidOperationException(
                        $"节点类型 {descriptor.NodeType} 的运行处理器配置无效：{exception.Message}", exception);
                }
            }

            // 第二步：校验全部通过，此时发布（冻结）才是安全的。
            Nodes.Freeze();
            Handlers.Freeze();
            _frozen = true;
            return this;
        }
    }

    private void EnsureMutable()
    {
        if (_frozen)
            throw new InvalidOperationException("工作流运行插件组合目录已经冻结。");
    }
}
