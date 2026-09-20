using System.Collections.ObjectModel;

namespace DP.WorkFlow;

/// <summary>
/// 表示已经过校验、与可编辑文档隔离且可直接绑定运行时的不可变执行计划。
/// </summary>
public sealed class WorkflowExecutionPlan
{
    private readonly IReadOnlyDictionary<string, IWorkflowNodeModel> _nodes;
    private readonly IReadOnlyDictionary<WorkflowPortAddress, IReadOnlyList<string>> _outgoing;
    private readonly IReadOnlyDictionary<string, WorkflowParallelScopePlan> _parallelScopes;
    private readonly IReadOnlyDictionary<string, WorkflowExecutionPlan> _childPlans;

    /// <summary>初始化已经完成结构和绑定校验的运行定义。</summary>
    /// <param name="name">工作流显示名称。</param>
    /// <param name="startNodeId">根执行 Token 的入口节点 ID。</param>
    /// <param name="nodes">按节点 ID 索引的节点配置；构造函数会包装为只读字典。</param>
    /// <param name="outgoing">按来源节点和输出端口索引的后继节点 ID 集合。</param>
    /// <param name="parallelScopes">按 ParallelAll 节点 ID 索引的静态分支入口和汇聚边界。</param>
    /// <param name="childPlans">按复合节点 ID 索引、已经递归编译的子流程定义。</param>
    /// <param name="diagnostics">不会阻止运行的编译警告，例如不可达节点。</param>
    internal WorkflowExecutionPlan(
        string name,
        string startNodeId,
        IDictionary<string, IWorkflowNodeModel> nodes,
        IDictionary<WorkflowPortAddress, IReadOnlyList<string>> outgoing,
        IReadOnlyDictionary<string, WorkflowParallelScopePlan>? parallelScopes = null,
        IReadOnlyDictionary<string, WorkflowExecutionPlan>? childPlans = null,
        IReadOnlyList<WorkflowValidationError>? diagnostics = null)
    {
        Name = name;
        EntryNodeId = startNodeId;
        Diagnostics = Array.AsReadOnly((diagnostics ?? Array.Empty<WorkflowValidationError>()).ToArray());

        var nodeSnapshots = nodes.ToDictionary(
            pair => pair.Key,
            pair => WorkflowNodeConfigurationSnapshotter.Capture(pair.Value),
            StringComparer.Ordinal);
        _nodes = new ReadOnlyDictionary<string, IWorkflowNodeModel>(nodeSnapshots);

        var outgoingSnapshot = outgoing.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<string>)Array.AsReadOnly(pair.Value.ToArray()));
        _outgoing = new ReadOnlyDictionary<WorkflowPortAddress, IReadOnlyList<string>>(outgoingSnapshot);
        IsAcyclic = CheckAcyclic(nodeSnapshots.Keys, outgoingSnapshot);

        var scopeSnapshots = (parallelScopes ?? new Dictionary<string, WorkflowParallelScopePlan>(StringComparer.Ordinal))
            .ToDictionary(
                pair => pair.Key,
                pair => new WorkflowParallelScopePlan(
                    pair.Value.ScopeNodeId,
                    Array.AsReadOnly(pair.Value.BranchEntryNodeIds.ToArray()),
                    pair.Value.MergeNodeId),
                StringComparer.Ordinal);
        _parallelScopes = new ReadOnlyDictionary<string, WorkflowParallelScopePlan>(scopeSnapshots);

        _childPlans = new ReadOnlyDictionary<string, WorkflowExecutionPlan>(
            (childPlans ?? new Dictionary<string, WorkflowExecutionPlan>(StringComparer.Ordinal))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal));
    }

    private static bool CheckAcyclic(IEnumerable<string> nodeIds,
        IReadOnlyDictionary<WorkflowPortAddress, IReadOnlyList<string>> outgoing)
    {
        var edges = nodeIds.ToDictionary(id => id, _ => new HashSet<string>(StringComparer.Ordinal), StringComparer.Ordinal);
        var incoming = edges.Keys.ToDictionary(id => id, _ => 0, StringComparer.Ordinal);
        foreach (var pair in outgoing)
        foreach (var target in pair.Value)
            if (edges[pair.Key.NodeId].Add(target)) incoming[target]++;
        var ready = new Queue<string>(incoming.Where(pair => pair.Value == 0).Select(pair => pair.Key));
        var visited = 0;
        while (ready.TryDequeue(out var nodeId))
        {
            visited++;
            foreach (var target in edges[nodeId])
                if (--incoming[target] == 0) ready.Enqueue(target);
        }
        return visited == edges.Count;
    }

    /// <summary>当前文档的全部控制边是否无环；V1命名恢复入口只支持无环串行计划。</summary>
    public bool IsAcyclic { get; }

    /// <summary>获取流程名称。</summary>
    public string Name { get; }

    /// <summary>获取执行计划的入口节点 ID。</summary>
    public string EntryNodeId { get; }

    /// <summary>获取不会阻止运行的编译诊断，例如不可达节点。</summary>
    public IReadOnlyList<WorkflowValidationError> Diagnostics { get; }

    /// <summary>
    /// 获取节点配置的独立副本索引。修改返回的模型不会影响编译定义或后续执行。
    /// </summary>
    public IReadOnlyDictionary<string, IWorkflowNodeModel> Nodes =>
        new ReadOnlyDictionary<string, IWorkflowNodeModel>(_nodes.ToDictionary(
            pair => pair.Key,
            pair => WorkflowNodeConfigurationSnapshotter.Capture(pair.Value),
            StringComparer.Ordinal));

    /// <summary>获取当前定义中全部稳定节点 ID。</summary>
    public IReadOnlyList<string> NodeIds => Array.AsReadOnly(_nodes.Keys.ToArray());

    /// <summary>获取编译后的并行作用域。</summary>
    public IReadOnlyDictionary<string, WorkflowParallelScopePlan> ParallelScopes => _parallelScopes;

    /// <summary>获取以复合节点 ID 索引的编译后子流程。</summary>
    public IReadOnlyDictionary<string, WorkflowExecutionPlan> ChildPlans => _childPlans;

    /// <summary>获取复合节点的编译后子流程。</summary>
    /// <param name="nodeId">实现 <see cref="IWorkflowSubDocumentNode"/> 的父文档节点 ID。</param>
    /// <returns>编译器为该节点子画布生成的只读定义。</returns>
    /// <exception cref="KeyNotFoundException">节点没有子画布定义或不属于当前流程。</exception>
    public WorkflowExecutionPlan GetChildPlanOrThrow(string nodeId) =>
        _childPlans.TryGetValue(nodeId, out var definition)
            ? definition
            : throw new KeyNotFoundException($"节点 {nodeId} 没有编译后的子流程定义。");

    /// <summary>获取指定并行派发节点的静态作用域定义。</summary>
    /// <param name="nodeId">类型为 <c>ParallelAll</c> 的节点 ID。</param>
    /// <returns>分支入口集合及共同汇聚节点。</returns>
    /// <exception cref="KeyNotFoundException">节点没有成功编译出的并行作用域。</exception>
    public WorkflowParallelScopePlan GetParallelScopeOrThrow(string nodeId) =>
        _parallelScopes.TryGetValue(nodeId, out var scope)
            ? scope
            : throw new KeyNotFoundException($"并行节点 {nodeId} 没有编译后的汇聚作用域。");

    /// <summary>按画布内稳定 ID 创建本次执行使用的独立节点配置。</summary>
    /// <param name="nodeId">要查找的节点实例 ID。</param>
    /// <returns>与编译快照分离的节点配置；处理器对其修改不会污染执行计划。</returns>
    /// <exception cref="KeyNotFoundException">当前定义中不存在该节点 ID。</exception>
    public IWorkflowNodeModel GetNodeOrThrow(string nodeId) =>
        _nodes.TryGetValue(nodeId, out var node)
            ? WorkflowNodeConfigurationSnapshotter.Capture(node)
            : throw new KeyNotFoundException($"找不到节点：{nodeId}。");

    /// <summary>获取节点指定输出端口连接的全部目标节点。</summary>
    /// <param name="nodeId">来源节点 ID。</param>
    /// <param name="portKey">节点执行结果选择的稳定输出端口键。</param>
    /// <returns>按画布连接顺序去重的目标节点 ID；端口没有连线时返回空集合。</returns>
    public IReadOnlyList<string> GetNextNodeIds(string nodeId, string portKey) =>
        _outgoing.TryGetValue(new WorkflowPortAddress(nodeId, portKey), out var nodeIds)
            ? nodeIds
            : Array.Empty<string>();
}

/// <summary>唯一标识节点的一个输出端口，用作编译后出边索引的键。</summary>
/// <param name="NodeId">来源节点 ID。</param>
/// <param name="PortKey">来源输出端口稳定键。</param>
public readonly record struct WorkflowPortAddress(string NodeId, string PortKey);

/// <summary>描述一个编译后的并行派发及其共同汇聚边界。</summary>
/// <param name="ScopeNodeId">发起派发的 <c>ParallelAll</c> 节点 ID。</param>
/// <param name="BranchEntryNodeIds">从 Branch 端口连接的全部分支入口节点 ID。</param>
/// <param name="MergeNodeId">所有分支共同可达的 <c>WaitAllInputsCompleted</c> 节点 ID。</param>
public sealed record WorkflowParallelScopePlan(
    string ScopeNodeId,
    IReadOnlyList<string> BranchEntryNodeIds,
    string MergeNodeId);
