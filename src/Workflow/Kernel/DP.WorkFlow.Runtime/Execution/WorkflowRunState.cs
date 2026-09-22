using System.Collections.Concurrent;

namespace DP.WorkFlow;

/// <summary>Records one node execution that did not complete its contract.</summary>
/// <param name="NodeId">Faulted node ID.</param>
/// <param name="Identity">Run, token, scope, and execution identity at the fault.</param>
/// <param name="Message">Diagnostic fault message.</param>
/// <param name="InterruptAlarmCode">Optional alarm code attached to the fault fact.</param>
/// <param name="ExceptionType">Underlying exception type when execution threw.</param>
/// <param name="Timestamp">UTC time at which the engine observed the fault.</param>
public sealed record WorkflowNodeFault(
    string NodeId,
    WorkflowExecutionIdentity Identity,
    string Message,
    int InterruptAlarmCode,
    string? ExceptionType,
    DateTimeOffset Timestamp);

/// <summary>
/// Holds the externally observable state of one workflow run independently from the editable document
/// and immutable execution plan.
/// </summary>
public sealed class WorkflowRunState
{
    private readonly object _syncRoot = new();
    private readonly List<WorkflowNodeFault> _faults = new();
    private readonly ConcurrentDictionary<string, WorkflowNodeOutput> _latestNodeOutputs = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<WorkflowNodeOutput> _nodeOutputHistory = new();
    private readonly ConcurrentDictionary<long, ParallelScopeVisibility> _parallelScopes = new();
    private readonly ConcurrentDictionary<long, byte> _invalidatedOutputs = new();
    private readonly List<WorkflowRecoveryEvent> _recoveryEvents = new();
    private long _nodeExecutionSequence;

    /// <summary>Gets the identity of this run.</summary>
    public Guid RunId { get; private set; }

    /// <summary>Gets all successfully committed node outputs in execution order.</summary>
    public IReadOnlyList<WorkflowNodeOutput> NodeOutputs => _nodeOutputHistory.ToArray();

    /// <summary>Gets all node faults observed during this run in occurrence order.</summary>
    public IReadOnlyList<WorkflowNodeFault> Faults
    {
        get
        {
            lock (_syncRoot)
                return _faults.ToArray();
        }
    }

    /// <summary>恢复处理记录；与原节点故障分开保存。</summary>
    public IReadOnlyList<WorkflowRecoveryEvent> RecoveryEvents
    {
        get { lock (_syncRoot) return _recoveryEvents.ToArray(); }
    }

    /// <summary>历史输出中已经失效的序号；失效不删除历史或回滚设备动作。</summary>
    public IReadOnlyList<long> InvalidatedOutputSequences => _invalidatedOutputs.Keys.OrderBy(value => value).ToArray();

    internal void RecordRecovery(WorkflowRecoveryEvent recovery)
    {
        lock (_syncRoot) _recoveryEvents.Add(recovery);
    }

    internal void InvalidateOutputsFrom(long sequence)
    {
        foreach (var output in _nodeOutputHistory.Where(output => output.ExecutionSequence >= sequence))
            _invalidatedOutputs.TryAdd(output.ExecutionSequence, 0);
        _latestNodeOutputs.Clear();
        foreach (var output in _nodeOutputHistory.Where(output => !_invalidatedOutputs.ContainsKey(output.ExecutionSequence)))
            _latestNodeOutputs[output.NodeId] = output;
    }

    internal IReadOnlyList<WorkflowNodeOutput> ValidNodeOutputs =>
        _nodeOutputHistory.Where(output => !_invalidatedOutputs.ContainsKey(output.ExecutionSequence)).ToArray();

    internal void Begin(Guid runId)
    {
        lock (_syncRoot)
        {
            RunId = runId;
            _faults.Clear();
            _invalidatedOutputs.Clear();
            _recoveryEvents.Clear();
            _latestNodeOutputs.Clear();
            _parallelScopes.Clear();
            while (_nodeOutputHistory.TryDequeue(out _))
            {
            }
            Interlocked.Exchange(ref _nodeExecutionSequence, 0);
        }
    }

    internal void RecordFault(WorkflowNodeFault fault)
    {
        ArgumentNullException.ThrowIfNull(fault);
        lock (_syncRoot)
            _faults.Add(fault);
    }

    internal bool TryGetLatestNodeOutput(string nodeId, out WorkflowNodeOutput? output)
    {
        if (_latestNodeOutputs.TryGetValue(nodeId, out var found))
        {
            output = found;
            return true;
        }
        output = null;
        return false;
    }

    internal WorkflowNodeOutput CommitNodeOutput(
        string nodeId,
        WorkflowExecutionIdentity identity,
        object? value)
    {
        var output = new WorkflowNodeOutput(
            Interlocked.Increment(ref _nodeExecutionSequence),
            identity.RunId,
            nodeId,
            identity.NodeExecutionCount,
            identity.TokenId,
            Array.AsReadOnly(identity.ScopeIds.ToArray()),
            DateTimeOffset.UtcNow,
            value);
        _nodeOutputHistory.Enqueue(output);
        _latestNodeOutputs[nodeId] = output;
        return output;
    }

    internal bool TryGetVisibleNodeOutput(
        string nodeId,
        WorkflowExecutionIdentity consumer,
        out WorkflowNodeOutput? output)
    {
        var visibleTokenIds = new HashSet<long>(consumer.AncestorTokenIds) { consumer.TokenId };
        foreach (var candidate in _nodeOutputHistory.ToArray().AsEnumerable().Reverse())
        {
            if (_invalidatedOutputs.ContainsKey(candidate.ExecutionSequence)
                || candidate.RunId != consumer.RunId
                || !string.Equals(candidate.NodeId, nodeId, StringComparison.Ordinal))
                continue;
            if (candidate.TokenId == consumer.TokenId
                || IsAncestorOutputVisible(candidate, consumer, visibleTokenIds)
                || IsCompletedBranchOutputVisible(candidate, consumer, visibleTokenIds))
            {
                output = candidate;
                return true;
            }
        }
        output = null;
        return false;
    }

    internal void RegisterParallelScope(long scopeId, long ownerTokenId)
    {
        if (!_parallelScopes.TryAdd(scopeId, new ParallelScopeVisibility(ownerTokenId)))
            throw new InvalidOperationException($"并行作用域 {scopeId} 已经注册。");
    }

    internal void CompleteParallelScope(long scopeId)
    {
        if (_parallelScopes.TryGetValue(scopeId, out var scope))
            scope.IsCompleted = true;
    }

    private static bool IsAncestorOutputVisible(
        WorkflowNodeOutput output,
        WorkflowExecutionIdentity consumer,
        ISet<long> visibleTokenIds)
    {
        if (!visibleTokenIds.Contains(output.TokenId)
            || output.ScopeIds.Count > consumer.ScopeIds.Count)
            return false;
        return output.ScopeIds.SequenceEqual(consumer.ScopeIds.Take(output.ScopeIds.Count));
    }

    private bool IsCompletedBranchOutputVisible(
        WorkflowNodeOutput output,
        WorkflowExecutionIdentity consumer,
        ISet<long> visibleTokenIds)
    {
        var commonLength = 0;
        while (commonLength < output.ScopeIds.Count
               && commonLength < consumer.ScopeIds.Count
               && output.ScopeIds[commonLength] == consumer.ScopeIds[commonLength])
            commonLength++;
        if (commonLength == output.ScopeIds.Count)
            return false;
        var additionalScopes = output.ScopeIds.Skip(commonLength).ToArray();
        if (additionalScopes.Length == 0
            || !_parallelScopes.TryGetValue(additionalScopes[0], out var outerScope)
            || !visibleTokenIds.Contains(outerScope.OwnerTokenId))
            return false;
        return additionalScopes.All(scopeId =>
            _parallelScopes.TryGetValue(scopeId, out var scope) && scope.IsCompleted);
    }

    private sealed class ParallelScopeVisibility
    {
        internal ParallelScopeVisibility(long ownerTokenId) => OwnerTokenId = ownerTokenId;

        internal long OwnerTokenId { get; }

        internal volatile bool IsCompleted;
    }
}
