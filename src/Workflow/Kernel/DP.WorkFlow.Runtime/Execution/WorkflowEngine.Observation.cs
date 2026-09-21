using System.Collections.ObjectModel;
using System.Diagnostics;

namespace DP.WorkFlow;

public sealed partial class WorkflowEngine
{
    /// <summary>获取当前运行状态的深层集合快照。</summary>
    /// <param name="message">可选的快照触发原因或终态说明。</param>
    /// <returns>包含活动 Token、节点最近状态、并行进度和子流程状态的不可变快照。</returns>
    public WorkflowRuntimeSnapshot GetRuntimeSnapshot(string? message = null)
    {
        WorkflowRuntimeSnapshot snapshot;
        lock (_stateSync)
        {
            var now = DateTimeOffset.UtcNow;
            var nodes = new ReadOnlyDictionary<string, WorkflowNodeRuntimeInfo>(
                _nodeRuntime.ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value.ToSnapshot(now),
                    StringComparer.Ordinal));
            var scopes = new ReadOnlyDictionary<long, WorkflowParallelScopeInfo>(
                _parallelScopes.ToDictionary(pair => pair.Key, pair => pair.Value.ToSnapshot()));
            var tokens = new ReadOnlyDictionary<long, WorkflowActiveTokenInfo>(
                _activeTokens.ToDictionary(pair => pair.Key, pair => pair.Value));
            var children = new ReadOnlyDictionary<string, WorkflowChildRuntimeInfo>(
                _childWorkflows.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal));
            snapshot = new WorkflowRuntimeSnapshot(
                _runId,
                Interlocked.Increment(ref _snapshotSequence),
                now,
                _plan.Name,
                _state,
                _currentNodeId,
                Array.AsReadOnly(_activeNodeIds.OrderBy(id => id, StringComparer.Ordinal).ToArray()),
                tokens,
                _runStopwatch?.Elapsed ?? TimeSpan.Zero,
                nodes,
                scopes,
                children,
                Array.AsReadOnly(_externalHoldReasons.ToArray()),
                message,
                RunState.Faults,
                RunState.NodeOutputs);
        }
        RunState.Publish(snapshot);
        return snapshot;
    }

    /// <summary>获取内存中当前保留的不可变 Trace 批次。</summary>
    /// <returns>受 <see cref="WorkflowExecutionOptions.MaxTraceEntries"/> 限制的最近条目及最后序号。</returns>
    public WorkflowTraceBatch GetTraceBatch()
    {
        var entries = _traceEntries.ToArray();
        return new WorkflowTraceBatch(
            _runId,
            entries.LastOrDefault()?.Sequence ?? 0,
            Array.AsReadOnly(entries));
    }

    private void MarkNodeStarted(IWorkflowNodeModel node, WorkflowExecutionIdentity identity)
    {
        lock (_stateSync)
        {
            _currentNodeId = node.Id;
            _activeNodeIds.Add(node.Id);
            _activeTokens[identity.TokenId] = new WorkflowActiveTokenInfo(
                identity.TokenId,
                identity.AncestorTokenIds,
                identity.ScopeIds,
                node.Id);
            _nodeRuntime[node.Id] = new MutableNodeRuntimeInfo
            {
                NodeId = node.Id,
                State = E_NodeState.Running,
                ExecutionCount = identity.NodeExecutionCount,
                ExecutionSequence = ++_nodeExecutionSequence,
                StartedAt = DateTimeOffset.UtcNow
            };
        }
        PublishSnapshot();
    }

    private void BeginNodeTiming(string nodeId)
    {
        lock (_stateSync)
            if (_nodeRuntime.TryGetValue(nodeId, out var info))
                info.ExecutionStartedTimestamp = Stopwatch.GetTimestamp();
    }

    private void MarkNodeFinished(string nodeId, long tokenId, E_NodeState state, string? message)
    {
        lock (_stateSync)
        {
            if (_nodeRuntime.TryGetValue(nodeId, out var info))
            {
                info.State = state;
                info.CompletedAt = DateTimeOffset.UtcNow;
                info.Elapsed = info.ExecutionStartedTimestamp > 0
                    ? Stopwatch.GetElapsedTime(info.ExecutionStartedTimestamp)
                    : info.CompletedAt.Value - info.StartedAt.GetValueOrDefault(info.CompletedAt.Value);
                info.Message = message;
            }
            _activeTokens.Remove(tokenId);
            _activeNodeIds.Clear();
            foreach (var activeNodeId in _activeTokens.Values
                         .Select(token => token.CurrentNodeId)
                         .Where(id => !string.IsNullOrWhiteSpace(id)))
            {
                _activeNodeIds.Add(activeNodeId!);
            }
            _currentNodeId = _activeNodeIds.OrderBy(id => id, StringComparer.Ordinal).FirstOrDefault();
        }
        PublishSnapshot(message);
    }

    private void PublishSnapshot(string? message = null) =>
        SafeInvoke(SnapshotChanged, GetRuntimeSnapshot(message));

    private void WriteTrace(
        IWorkflowNodeModel node,
        WorkflowExecutionIdentity identity,
        string step,
        string? message,
        IReadOnlyDictionary<string, object?>? data)
    {
        var entry = new WorkflowTraceEntry(
            Interlocked.Increment(ref _traceSequence),
            DateTimeOffset.UtcNow,
            node.Id,
            node.NodeType,
            step,
            message,
            data is null
                ? null
                : new ReadOnlyDictionary<string, object?>(
                    new Dictionary<string, object?>(data, StringComparer.Ordinal)),
            identity.TokenId,
            Array.AsReadOnly(identity.ScopeIds.ToArray()));
        _traceEntries.Enqueue(entry);
        while (_traceEntries.Count > _options.MaxTraceEntries)
            _traceEntries.TryDequeue(out _);
        SafeInvoke(NodeTrace, entry);
    }

    private static void SafeInvoke<T>(Action<T>? handlers, T argument)
    {
        if (handlers is null)
            return;
        foreach (Action<T> handler in handlers.GetInvocationList())
        {
            try
            {
                handler(argument);
            }
            catch
            {
                // 观察者异常不得改变流程执行结果；宿主可在自己的事件处理器中记录异常。
            }
        }
    }

    private sealed class MutableNodeRuntimeInfo
    {
        public required string NodeId { get; init; }

        public E_NodeState State { get; set; }

        public int ExecutionCount { get; init; }

        public long ExecutionSequence { get; init; }

        public DateTimeOffset? StartedAt { get; init; }

        public DateTimeOffset? CompletedAt { get; set; }

        public long ExecutionStartedTimestamp { get; set; }

        public TimeSpan Elapsed { get; set; }

        public string? Message { get; set; }

        public WorkflowNodeRuntimeInfo ToSnapshot(DateTimeOffset now)
        {
            var elapsed = State == E_NodeState.Running && StartedAt.HasValue
                ? now - StartedAt.Value
                : Elapsed;
            return new WorkflowNodeRuntimeInfo(
                NodeId,
                State,
                ExecutionCount,
                ExecutionSequence,
                StartedAt,
                CompletedAt,
                elapsed,
                Message);
        }
    }
}
