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
/// <remarks>
/// 输出历史只保留仍可能被读取的执行实例：同一节点的新输出对所有消费者都遮蔽旧输出时，旧输出立即退出历史。
/// 环形流程因此按节点数而不是循环次数占用内存；被退出的输出交还调用方，以便释放其派生资源（例如图像帧租约）。
/// </remarks>
public sealed class WorkflowRunState
{
    private readonly object _syncRoot = new();
    private readonly List<WorkflowNodeFault> _faults = new();
    private readonly ConcurrentDictionary<string, WorkflowNodeOutput> _latestNodeOutputs = new(StringComparer.Ordinal);
    // 按执行序号递增排列；读写都在 _historySync 内进行，退出历史需要从中间移除。
    private readonly LinkedList<WorkflowNodeOutput> _nodeOutputHistory = new();
    private readonly object _historySync = new();
    private readonly ConcurrentDictionary<long, ParallelScopeVisibility> _parallelScopes = new();
    private readonly ConcurrentDictionary<long, byte> _invalidatedOutputs = new();
    private readonly List<WorkflowRecoveryEvent> _recoveryEvents = new();
    private long _nodeExecutionSequence;

    /// <summary>Gets the identity of this run.</summary>
    public Guid RunId { get; private set; }

    /// <summary>
    /// Gets the retained committed node outputs in execution order. An output superseded by a newer output of the
    /// same node that every consumer would read instead is no longer retained; invalidated outputs are kept.
    /// </summary>
    public IReadOnlyList<WorkflowNodeOutput> NodeOutputs
    {
        get { lock (_historySync) return _nodeOutputHistory.ToArray(); }
    }

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
        lock (_historySync)
        {
            foreach (var output in _nodeOutputHistory.Where(output => output.ExecutionSequence >= sequence))
                _invalidatedOutputs.TryAdd(output.ExecutionSequence, 0);
            _latestNodeOutputs.Clear();
            foreach (var output in _nodeOutputHistory.Where(output => !_invalidatedOutputs.ContainsKey(output.ExecutionSequence)))
                _latestNodeOutputs[output.NodeId] = output;
        }
    }

    internal IReadOnlyList<WorkflowNodeOutput> ValidNodeOutputs
    {
        get
        {
            lock (_historySync)
                return _nodeOutputHistory.Where(output => !_invalidatedOutputs.ContainsKey(output.ExecutionSequence)).ToArray();
        }
    }

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
            lock (_historySync)
                _nodeOutputHistory.Clear();
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
        object? value) => CommitNodeOutput(nodeId, identity, value, out _);

    /// <summary>提交节点输出，并让被它完全遮蔽的旧输出退出历史。</summary>
    /// <param name="nodeId">产生输出的节点 ID。</param>
    /// <param name="identity">节点执行实例身份。</param>
    /// <param name="value">允许为空的节点标准输出。</param>
    /// <param name="superseded">退出历史的旧输出；调用方据此释放其派生资源。</param>
    /// <remarks>
    /// 同一节点、同一 Token、同一并行 Scope 路径的输出对任意消费者的可见性完全相同，按最新优先只会读到新输出。
    /// 已失效的输出不参与遮蔽也不被移除：恢复重跑依赖它们保留历史事实。
    /// </remarks>
    internal WorkflowNodeOutput CommitNodeOutput(
        string nodeId,
        WorkflowExecutionIdentity identity,
        object? value,
        out IReadOnlyList<WorkflowNodeOutput> superseded)
    {
        lock (_historySync)
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
            superseded = RemoveFromHistory(candidate =>
                string.Equals(candidate.NodeId, nodeId, StringComparison.Ordinal)
                && candidate.TokenId == output.TokenId
                && candidate.ScopeIds.SequenceEqual(output.ScopeIds));
            _nodeOutputHistory.AddLast(output);
            _latestNodeOutputs[nodeId] = output;
            return output;
        }
    }

    internal bool TryGetVisibleNodeOutput(
        string nodeId,
        WorkflowExecutionIdentity consumer,
        out WorkflowNodeOutput? output)
    {
        var visibleTokenIds = new HashSet<long>(consumer.AncestorTokenIds) { consumer.TokenId };
        WorkflowNodeOutput[] history;
        lock (_historySync)
            history = _nodeOutputHistory.ToArray();
        foreach (var candidate in history.AsEnumerable().Reverse())
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

    internal void CompleteParallelScope(long scopeId) => CompleteParallelScope(scopeId, out _);

    /// <summary>标记并行作用域完成，并让被本作用域输出遮蔽的同父 Token 旧作用域输出退出历史。</summary>
    /// <param name="scopeId">完成的运行时作用域。</param>
    /// <param name="superseded">退出历史的旧输出；调用方据此释放其派生资源。</param>
    /// <remarks>
    /// 循环中每轮并行都会产生新的分支 Token，分支输出不会被同 Token 规则遮蔽。作用域完成后，
    /// 同一父 Token 下已完成的旧作用域输出与本作用域输出可见范围相同而序号更旧，因而不会再被读到。
    /// 不再被任何保留输出引用的已完成作用域同时移除。
    /// </remarks>
    internal void CompleteParallelScope(long scopeId, out IReadOnlyList<WorkflowNodeOutput> superseded)
    {
        superseded = Array.Empty<WorkflowNodeOutput>();
        if (!_parallelScopes.TryGetValue(scopeId, out var scope))
            return;
        scope.IsCompleted = true;
        lock (_historySync)
        {
            var depthByNode = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var output in _nodeOutputHistory)
            {
                var depth = IndexOf(output.ScopeIds, scopeId);
                if (depth >= 0 && !_invalidatedOutputs.ContainsKey(output.ExecutionSequence))
                    depthByNode.TryAdd(output.NodeId, depth);
            }
            if (depthByNode.Count == 0)
                return;
            superseded = RemoveFromHistory(candidate =>
                depthByNode.TryGetValue(candidate.NodeId, out var depth)
                && candidate.ScopeIds.Count > depth
                && candidate.ScopeIds[depth] != scopeId
                && _parallelScopes.TryGetValue(candidate.ScopeIds[depth], out var older)
                && older.IsCompleted
                && older.OwnerTokenId == scope.OwnerTokenId);
            if (superseded.Count == 0)
                return;
            var referenced = new HashSet<long>(_nodeOutputHistory.SelectMany(output => output.ScopeIds));
            foreach (var pair in _parallelScopes)
                if (pair.Value.IsCompleted && !referenced.Contains(pair.Key))
                    _parallelScopes.TryRemove(pair.Key, out _);
        }
    }

    /// <summary>从历史中移除匹配的有效输出；调用方必须持有 <see cref="_historySync"/>。</summary>
    private IReadOnlyList<WorkflowNodeOutput> RemoveFromHistory(Func<WorkflowNodeOutput, bool> predicate)
    {
        List<WorkflowNodeOutput>? removed = null;
        for (var node = _nodeOutputHistory.First; node is not null;)
        {
            var next = node.Next;
            if (!_invalidatedOutputs.ContainsKey(node.Value.ExecutionSequence) && predicate(node.Value))
            {
                _nodeOutputHistory.Remove(node);
                (removed ??= new List<WorkflowNodeOutput>()).Add(node.Value);
            }
            node = next;
        }
        return removed is null ? Array.Empty<WorkflowNodeOutput>() : removed;
    }

    private static int IndexOf(IReadOnlyList<long> scopeIds, long scopeId)
    {
        for (var index = 0; index < scopeIds.Count; index++)
            if (scopeIds[index] == scopeId)
                return index;
        return -1;
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
