using System.Collections.ObjectModel;
using System.Diagnostics;

namespace DP.WorkFlow;

public sealed partial class WorkflowEngine
{
    /// <summary>获取当前运行状态的深层集合快照。</summary>
    /// <param name="message">可选的快照触发原因或终态说明。</param>
    /// <returns>包含活动 Token、节点最近状态、并行进度和子流程状态的不可变快照；不携带随运行增长的历史。</returns>
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
            var activeChildren = new ReadOnlyDictionary<string, WorkflowChildRuntimeInfo>(
                _activeChildWorkflows.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal));
            var latestChildren = new ReadOnlyDictionary<string, WorkflowChildRuntimeInfo>(
                _latestChildWorkflowByParentNode.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal));
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
                activeChildren,
                latestChildren,
                Array.AsReadOnly(_externalHoldReasons.ToArray()),
                message,
                _currentFault,
                _currentRecovery);
        }
        RunState.Publish(snapshot);
        return snapshot;
    }

    /// <summary>获取内存中当前保留的不可变 Trace 批次。</summary>
    /// <returns>来自 Recorder 最近事件窗口、按事件序号升序排列的条目及最后序号。</returns>
    public WorkflowTraceBatch GetTraceBatch()
    {
        var batch = _recorder?.GetRecent();
        if (batch is null)
            return new WorkflowTraceBatch(_runId, 0, Array.Empty<WorkflowTraceEntry>());
        var entries = batch.Events.Select(ToTraceEntry).ToArray();
        return new WorkflowTraceBatch(_runId, entries.Length == 0 ? 0 : entries[^1].Sequence, Array.AsReadOnly(entries));
    }

    private static WorkflowTraceEntry ToTraceEntry(WorkflowRunEvent @event)
    {
        string? message = null;
        IReadOnlyDictionary<string, object?>? data = null;
        if (@event.Data is { Count: > 0 })
        {
            var restored = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var pair in @event.Data)
            {
                if (string.Equals(pair.Key, "Message", StringComparison.Ordinal))
                {
                    message = pair.Value.Text;
                    continue;
                }
                restored[pair.Key] = pair.Value.ToObject();
            }
            if (restored.Count > 0)
                data = new ReadOnlyDictionary<string, object?>(restored);
        }
        return new WorkflowTraceEntry(
            @event.Sequence,
            @event.Timestamp,
            @event.NodeId ?? string.Empty,
            @event.NodeType ?? string.Empty,
            @event.EventType,
            message,
            data,
            @event.ExecutionIdentity?.TokenId ?? 0,
            Array.AsReadOnly(@event.ExecutionIdentity?.ScopeIds.ToArray() ?? Array.Empty<long>()));
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

    /// <summary>向 Recorder 提交一条事件草稿；未配置记录器时静默忽略，绝不改变运行结果。</summary>
    private void RecordRunEvent(WorkflowRunEventDraft draft, WorkflowEventWriteMode writeMode = WorkflowEventWriteMode.Buffered) =>
        TryRecordRunEvent(draft, writeMode);

    /// <summary>
    /// 提交一条事件并吸收记录链路的任何异常。
    /// </summary>
    /// <remarks>
    /// 记录链路必须完全 fail-open：调用方提供的结构化数据、Payload 编码和批次推送都可能抛异常，
    /// 这些异常一旦穿过节点执行就会把 Run 变成 Fault，违反"记录异常不改变 Workflow 业务结果"。
    /// 这里只降级记录健康度，并把失败事件当作"未记录"返回空回执。
    /// </remarks>
    /// <param name="draft">待提交的事件草稿。</param>
    /// <param name="writeMode">写入模式。</param>
    /// <returns>成功时的事件回执；记录失败或未配置记录器时为 <see langword="null"/>。</returns>
    private WorkflowRunEventReceipt? TryRecordRunEvent(
        WorkflowRunEventDraft draft,
        WorkflowEventWriteMode writeMode = WorkflowEventWriteMode.Buffered)
    {
        try
        {
            return _recorder?.Record(draft, writeMode);
        }
        catch (Exception exception)
        {
            ReportRecordingDegraded($"记录 {draft.EventType} 事件失败：{exception.Message}");
            return null;
        }
    }

    private void WriteTrace(
        IWorkflowNodeModel node,
        WorkflowExecutionIdentity identity,
        string step,
        string? message,
        IReadOnlyDictionary<string, object?>? data,
        WorkflowEventWriteMode writeMode = WorkflowEventWriteMode.Buffered)
    {
        var receipt = TryRecordRunEvent(WorkflowRunEventDraft.Trace(step, node, identity, message, data), writeMode);
        SafeInvoke(NodeTrace, new WorkflowTraceEntry(
            receipt?.Sequence ?? 0,
            DateTimeOffset.UtcNow,
            node.Id,
            node.NodeType,
            step,
            message,
            TryCopyTraceData(data),
            identity.TokenId,
            Array.AsReadOnly(identity.ScopeIds.ToArray())));
    }

    /// <summary>复制一份跟踪数据快照；数据自身的枚举异常不得改变节点执行结果。</summary>
    /// <param name="data">调用方提供的结构化跟踪数据。</param>
    /// <returns>副本；数据为空或无法枚举时为 <see langword="null"/>。</returns>
    private static IReadOnlyDictionary<string, object?>? TryCopyTraceData(IReadOnlyDictionary<string, object?>? data)
    {
        if (data is null)
            return null;
        try
        {
            return new ReadOnlyDictionary<string, object?>(
                new Dictionary<string, object?>(data, StringComparer.Ordinal));
        }
        catch
        {
            return null;
        }
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
