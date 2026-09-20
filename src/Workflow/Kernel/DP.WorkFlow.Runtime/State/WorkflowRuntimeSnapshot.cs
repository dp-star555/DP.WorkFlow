namespace DP.WorkFlow;

/// <summary>表示节点在当前运行中的生命周期状态。</summary>
public enum E_NodeState
{
    /// <summary>尚未执行。</summary>
    Idle,

    /// <summary>正在执行。</summary>
    Running,

    /// <summary>已经成功完成。</summary>
    Completed,

    /// <summary>执行失败。</summary>
    Failed,

    /// <summary>执行被取消。</summary>
    Canceled
}

/// <summary>表示一个节点最近一次执行的不可变运行信息。</summary>
/// <param name="NodeId">画布内节点实例 ID。</param>
/// <param name="State">最近执行实例的生命周期状态。</param>
/// <param name="ExecutionCount">该节点在本轮运行中的执行次数。</param>
/// <param name="ExecutionSequence">最近一次开始执行时的全局节点序号。</param>
/// <param name="StartedAt">最近一次执行开始的 UTC 时间。</param>
/// <param name="CompletedAt">最近一次执行结束的 UTC 时间；运行中为空。</param>
/// <param name="Elapsed">最近一次执行截至快照时刻的耗时。</param>
/// <param name="Message">失败、取消或宿主附加的状态消息。</param>
public sealed record WorkflowNodeRuntimeInfo(
    string NodeId,
    E_NodeState State,
    int ExecutionCount,
    long ExecutionSequence,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    TimeSpan Elapsed,
    string? Message = null);

/// <summary>表示当前正在执行或等待调度的路径 Token。</summary>
/// <param name="TokenId">本次运行中唯一的 Token ID。</param>
/// <param name="AncestorTokenIds">从直接父 Token 开始的祖先 Token ID 集合。</param>
/// <param name="ScopeIds">从外到内的并行作用域实例路径。</param>
/// <param name="CurrentNodeId">Token 当前正在执行的节点 ID。</param>
public sealed record WorkflowActiveTokenInfo(
    long TokenId,
    IReadOnlyList<long> AncestorTokenIds,
    IReadOnlyList<long> ScopeIds,
    string? CurrentNodeId);

/// <summary>表示父节点当前或最近一次子流程运行的不可变状态。</summary>
/// <param name="ParentNodeId">拥有该子画布的复合节点 ID。</param>
/// <param name="ParentExecution">触发本次子流程的父节点执行身份。</param>
/// <param name="Snapshot">子引擎最近发布的完整运行快照。</param>
public sealed record WorkflowChildRuntimeInfo(
    string ParentNodeId,
    WorkflowExecutionIdentity ParentExecution,
    WorkflowRuntimeSnapshot Snapshot);

/// <summary>表示一次 ParallelAll 派发产生的运行时并行作用域状态。</summary>
/// <param name="RuntimeScopeId">区别循环中多次进入同一并行节点的作用域实例 ID。</param>
/// <param name="ScopeNodeId">静态 ParallelAll 节点 ID。</param>
/// <param name="MergeNodeId">所有分支等待到达的共同汇聚节点 ID。</param>
/// <param name="TotalBranches">本次派发创建的分支总数。</param>
/// <param name="CompletedBranches">已经到达汇聚点的分支数。</param>
/// <param name="IsCompleted">所有分支完成且输出已可向父 Token 暴露时为真。</param>
public sealed record WorkflowParallelScopeInfo(
    long RuntimeScopeId,
    string ScopeNodeId,
    string MergeNodeId,
    int TotalBranches,
    int CompletedBranches,
    bool IsCompleted);

/// <summary>
/// 表示某一时刻工作流运行状态的不可变快照。
/// </summary>
/// <param name="RunId">本次运行 ID；尚未启动时为 <see cref="Guid.Empty"/>。</param>
/// <param name="Sequence">同一引擎内单调递增的快照序号。</param>
/// <param name="Timestamp">创建快照的 UTC 时间。</param>
/// <param name="WorkflowName">编译定义中的流程名称。</param>
/// <param name="ExecutionState">快照时刻的引擎状态。</param>
/// <param name="CurrentNodeId">兼容单节点监视器的代表性活动节点 ID。</param>
/// <param name="ActiveNodeIds">并行运行时全部活动节点 ID。</param>
/// <param name="ActiveTokens">按 Token ID 索引的活动执行路径。</param>
/// <param name="TotalElapsed">本轮运行截至快照时刻的总耗时。</param>
/// <param name="Nodes">按节点 ID 索引的最近一次执行状态。</param>
/// <param name="ParallelScopes">按运行时 Scope ID 索引的并行进度。</param>
/// <param name="ChildWorkflows">按父节点执行实例键索引的子流程快照。</param>
/// <param name="ExternalHoldReasons">当前阻止继续调度的外部条件快照。</param>
/// <param name="Message">触发本快照的可选状态消息。</param>
/// <param name="Faults">本轮运行截至当前快照已经发生的节点故障事实。</param>
/// <param name="NodeOutputs">本轮运行截至当前快照已经成功提交的节点输出。</param>
public sealed record WorkflowRuntimeSnapshot(
    Guid RunId,
    long Sequence,
    DateTimeOffset Timestamp,
    string WorkflowName,
    E_WorkflowExecutionState ExecutionState,
    string? CurrentNodeId,
    IReadOnlyCollection<string> ActiveNodeIds,
    IReadOnlyDictionary<long, WorkflowActiveTokenInfo> ActiveTokens,
    TimeSpan TotalElapsed,
    IReadOnlyDictionary<string, WorkflowNodeRuntimeInfo> Nodes,
    IReadOnlyDictionary<long, WorkflowParallelScopeInfo> ParallelScopes,
    IReadOnlyDictionary<string, WorkflowChildRuntimeInfo> ChildWorkflows,
    IReadOnlyCollection<string> ExternalHoldReasons,
    string? Message = null,
    IReadOnlyList<WorkflowNodeFault>? Faults = null,
    IReadOnlyList<WorkflowNodeOutput>? NodeOutputs = null);

/// <summary>表示内存中当前保留的不可变节点 Trace 批次。</summary>
/// <param name="RunId">这些条目所属的运行 ID。</param>
/// <param name="LastSequence">批次最后一条 Trace 的序号；空批次为零。</param>
/// <param name="Entries">按生成顺序排列且受 MaxTraceEntries 限制的条目。</param>
public sealed record WorkflowTraceBatch(
    Guid RunId,
    long LastSequence,
    IReadOnlyList<WorkflowTraceEntry> Entries);
