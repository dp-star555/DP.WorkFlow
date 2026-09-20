namespace DP.WorkFlow;

/// <summary>
/// 保存当前运行宿主可使用的节点处理器。
/// </summary>
public sealed class WorkflowNodeHandlerCatalog
{
    private readonly List<HandlerRegistration> _registrations = new();
    private readonly object _syncRoot = new();
    private bool _frozen;

    /// <summary>注册一个节点处理器；目录允许链式配置。</summary>
    /// <param name="handler">参与运行时 CanHandle 匹配的处理器实例。</param>
    /// <returns>当前目录实例。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="handler"/> 为空。</exception>
    /// <remarks>同一处理器重复注册不会去重，并会导致解析时出现多匹配错误。</remarks>
    public WorkflowNodeHandlerCatalog Register(IWorkflowNodeHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        lock (_syncRoot)
        {
            EnsureMutable();
            _registrations.Add(new HandlerRegistration(
                handler,
                static _ => Array.Empty<WorkflowRuntimeCapabilityRequirement>()));
        }
        return this;
    }

    /// <summary>注册节点处理器及其固定运行能力要求。</summary>
    /// <param name="handler">参与运行时匹配的处理器。</param>
    /// <param name="requiredCapabilities">该处理器执行匹配节点时始终要求的宿主能力。</param>
    /// <returns>当前目录实例。</returns>
    public WorkflowNodeHandlerCatalog Register(
        IWorkflowNodeHandler handler,
        params WorkflowRuntimeCapabilityRequirement[] requiredCapabilities)
    {
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(requiredCapabilities);
        var requirements = ValidateRequirements(requiredCapabilities).ToArray();
        lock (_syncRoot)
        {
            EnsureMutable();
            _registrations.Add(new HandlerRegistration(handler, _ => requirements));
        }
        return this;
    }

    /// <summary>注册节点处理器及其根据冻结节点配置计算的运行能力要求。</summary>
    /// <param name="handler">参与运行时匹配的处理器。</param>
    /// <param name="requirementResolver">根据匹配节点配置返回运行能力要求的纯函数。</param>
    /// <returns>当前目录实例。</returns>
    public WorkflowNodeHandlerCatalog Register(
        IWorkflowNodeHandler handler,
        Func<IWorkflowNodeModel, IEnumerable<WorkflowRuntimeCapabilityRequirement>> requirementResolver)
    {
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(requirementResolver);
        lock (_syncRoot)
        {
            EnsureMutable();
            _registrations.Add(new HandlerRegistration(handler, node =>
                ValidateRequirements(requirementResolver(node)
                    ?? throw new InvalidOperationException($"处理器 {handler.GetType().FullName} 返回了 null 运行能力集合。"))));
        }
        return this;
    }

    /// <summary>解析能够处理指定节点的唯一处理器。</summary>
    /// <param name="node">即将执行的节点配置。</param>
    /// <returns>唯一一个 CanHandle 返回真的已注册处理器。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="node"/> 为空。</exception>
    /// <exception cref="InvalidOperationException">没有处理器或存在多个匹配处理器。</exception>
    public IWorkflowNodeHandler Resolve(IWorkflowNodeModel node)
    {
        ArgumentNullException.ThrowIfNull(node);
        HandlerRegistration[] matches;
        lock (_syncRoot)
            matches = _registrations.Where(item => item.Handler.CanHandle(node)).ToArray();

        return matches.Length switch
        {
            1 => matches[0].Handler,
            0 => throw new InvalidOperationException($"节点 {node.Id}/{node.NodeType} 没有已注册的处理器。"),
            _ => throw new InvalidOperationException($"节点 {node.Id}/{node.NodeType} 匹配到多个处理器。")
        };
    }

    /// <summary>解析唯一 Handler，并冻结当前节点配置对应的运行能力要求。</summary>
    /// <param name="node">即将绑定到执行计划的节点配置。</param>
    /// <returns>唯一 Handler 和该配置要求的去重能力集合。</returns>
    public WorkflowNodeHandlerResolution ResolveWithRequirements(IWorkflowNodeModel node)
    {
        ArgumentNullException.ThrowIfNull(node);
        HandlerRegistration[] matches;
        lock (_syncRoot)
            matches = _registrations.Where(item => item.Handler.CanHandle(node)).ToArray();
        if (matches.Length == 0)
            throw new InvalidOperationException($"节点 {node.Id}/{node.NodeType} 没有已注册的处理器。");
        if (matches.Length > 1)
            throw new InvalidOperationException($"节点 {node.Id}/{node.NodeType} 匹配到多个处理器。");

        var registration = matches[0];
        var requirements = ValidateRequirements(registration.RequirementResolver(node))
            .GroupBy(item => item.CapabilityType)
            .Select(group => group.First())
            .OrderBy(item => item.CapabilityType.FullName, StringComparer.Ordinal)
            .ToArray();
        return new WorkflowNodeHandlerResolution(registration.Handler, requirements);
    }

    /// <summary>获取目录是否已经冻结。</summary>
    public bool IsFrozen
    {
        get
        {
            lock (_syncRoot)
                return _frozen;
        }
    }

    /// <summary>冻结处理器目录；冻结后仍可解析处理器，但不能继续注册。</summary>
    /// <returns>冻结时刻按注册顺序排列的处理器快照。</returns>
    public IReadOnlyList<IWorkflowNodeHandler> Freeze()
    {
        lock (_syncRoot)
        {
            _frozen = true;
            return _registrations.Select(item => item.Handler).ToArray();
        }
    }

    /// <summary>创建当前处理器注册顺序的只读快照。</summary>
    /// <returns>不会随目录后续注册而变化的处理器集合。</returns>
    public IReadOnlyList<IWorkflowNodeHandler> Snapshot()
    {
        lock (_syncRoot)
            return _registrations.Select(item => item.Handler).ToArray();
    }

    private static IEnumerable<WorkflowRuntimeCapabilityRequirement> ValidateRequirements(
        IEnumerable<WorkflowRuntimeCapabilityRequirement> requirements)
    {
        ArgumentNullException.ThrowIfNull(requirements);
        foreach (var requirement in requirements)
            yield return requirement ?? throw new InvalidOperationException("运行能力要求集合不能包含 null。");
    }

    private void EnsureMutable()
    {
        if (_frozen)
            throw new InvalidOperationException("节点处理器目录已经冻结，不能继续注册处理器。");
    }

    private sealed record HandlerRegistration(
        IWorkflowNodeHandler Handler,
        Func<IWorkflowNodeModel, IEnumerable<WorkflowRuntimeCapabilityRequirement>> RequirementResolver);
}
