using System.Collections.ObjectModel;

namespace DP.WorkFlow;

/// <summary>Binds every node in an immutable execution plan to exactly one runtime handler before a run starts.</summary>
public sealed class WorkflowRuntimeBinder
{
    private readonly WorkflowNodeHandlerCatalog _handlers;

    /// <summary>Creates a runtime binder using the configured handler catalog.</summary>
    /// <param name="handlers">Catalog used to resolve one handler for every root and child-plan node.</param>
    public WorkflowRuntimeBinder(WorkflowNodeHandlerCatalog handlers) =>
        _handlers = handlers ?? throw new ArgumentNullException(nameof(handlers));

    /// <summary>Recursively binds a compiled plan and fails immediately on missing or ambiguous handlers.</summary>
    /// <param name="plan">Immutable plan produced by <see cref="WorkflowCompiler"/>.</param>
    /// <returns>A runtime-ready plan with recursively bound handlers.</returns>
    public WorkflowBoundExecutionPlan Bind(WorkflowExecutionPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var resolutions = plan.NodeIds.ToDictionary(
            nodeId => nodeId,
            nodeId => _handlers.ResolveWithRequirements(plan.GetNodeOrThrow(nodeId)),
            StringComparer.Ordinal);
        var handlers = resolutions.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.Handler,
            StringComparer.Ordinal);
        var requirements = resolutions.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.RequiredCapabilities,
            StringComparer.Ordinal);
        var children = plan.ChildPlans.ToDictionary(
            pair => pair.Key,
            pair => Bind(pair.Value),
            StringComparer.Ordinal);
        return new WorkflowBoundExecutionPlan(plan, handlers, requirements, children);
    }
}

/// <summary>Combines an immutable semantic execution plan with pre-resolved runtime handlers.</summary>
public sealed class WorkflowBoundExecutionPlan
{
    private readonly IReadOnlyDictionary<string, IWorkflowNodeHandler> _handlers;
    private readonly IReadOnlyDictionary<string, IReadOnlyList<WorkflowRuntimeCapabilityRequirement>> _requirements;
    private readonly IReadOnlyDictionary<string, WorkflowBoundExecutionPlan> _childPlans;

    internal WorkflowBoundExecutionPlan(
        WorkflowExecutionPlan plan,
        IDictionary<string, IWorkflowNodeHandler> handlers,
        IDictionary<string, IReadOnlyList<WorkflowRuntimeCapabilityRequirement>> requirements,
        IDictionary<string, WorkflowBoundExecutionPlan> childPlans)
    {
        Plan = plan ?? throw new ArgumentNullException(nameof(plan));
        _handlers = new ReadOnlyDictionary<string, IWorkflowNodeHandler>(
            new Dictionary<string, IWorkflowNodeHandler>(handlers, StringComparer.Ordinal));
        _requirements = new ReadOnlyDictionary<string, IReadOnlyList<WorkflowRuntimeCapabilityRequirement>>(
            requirements.ToDictionary(
                pair => pair.Key,
                pair => (IReadOnlyList<WorkflowRuntimeCapabilityRequirement>)pair.Value.ToArray(),
                StringComparer.Ordinal));
        _childPlans = new ReadOnlyDictionary<string, WorkflowBoundExecutionPlan>(
            new Dictionary<string, WorkflowBoundExecutionPlan>(childPlans, StringComparer.Ordinal));
    }

    /// <summary>Gets the immutable semantic plan.</summary>
    public WorkflowExecutionPlan Plan { get; }

    /// <summary>Gets the handler resolved for a node in this plan.</summary>
    /// <param name="nodeId">Stable node ID.</param>
    /// <returns>The unique pre-resolved handler.</returns>
    public IWorkflowNodeHandler GetHandler(string nodeId) =>
        _handlers.TryGetValue(nodeId, out var handler)
            ? handler
            : throw new KeyNotFoundException($"执行计划中没有节点 {nodeId} 的绑定处理器。");

    /// <summary>Gets the runtime capabilities frozen for a node configuration during binding.</summary>
    /// <param name="nodeId">Stable node ID.</param>
    /// <returns>The immutable capability requirements for this node.</returns>
    public IReadOnlyList<WorkflowRuntimeCapabilityRequirement> GetRequiredCapabilities(string nodeId) =>
        _requirements.TryGetValue(nodeId, out var requirements)
            ? requirements
            : throw new KeyNotFoundException($"执行计划中没有节点 {nodeId} 的运行能力声明。");

    /// <summary>Gets the recursively bound child plan owned by a composite node.</summary>
    /// <param name="nodeId">Composite parent node ID.</param>
    /// <returns>The runtime-bound child plan.</returns>
    public WorkflowBoundExecutionPlan GetChildPlan(string nodeId) =>
        _childPlans.TryGetValue(nodeId, out var child)
            ? child
            : throw new KeyNotFoundException($"执行计划中没有节点 {nodeId} 的绑定子计划。");
}
