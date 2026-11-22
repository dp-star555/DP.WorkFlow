using System.Collections.ObjectModel;
using System.Diagnostics;

namespace DP.WorkFlow;

public sealed partial class WorkflowEngine
{
    /// <summary>获取当前运行状态的深层集合快照。</summary>
    /// <param name="message">可选的快照触发原因或终态说明。</param>
    /// <returns>包含活动 Token、节点最近状态、并行进度和子流程状态的不可变快照；不携带随运行增长的历史。</returns>
    /// <remarks>
    /// 这是**纯查询**：不改变引擎状态，也不把结果写回 <see cref="WorkflowRunState"/>。
    /// 快照只在调用方需要时构造（R1-04）：界面按帧率比较 <see cref="ChangeVersion"/>，变了才来取。
    /// 正在运行的子运行的状态在这里向子引擎拉取，子引擎不再逐层推送。
    /// </remarks>
    public WorkflowRuntimeSnapshot GetRuntimeSnapshot(string? message = null)
    {
        message ??= Volatile.Read(ref _changeMessage);
        ActiveChildWorkflow[] activeChildList;
        lock (_stateSync)
            activeChildList = _activeChildWorkflows.Values.ToArray();
        // 子引擎有自己的锁；在本引擎锁外读取，避免父子锁嵌套。
        var activeChildren = new ReadOnlyDictionary<string, WorkflowChildRuntimeInfo>(
            activeChildList.ToDictionary(child => child.Key, child => child.Capture(), StringComparer.Ordinal));
        WorkflowRuntimeSnapshot snapshot;
        lock (_stateSync)
        {
            var now = WorkflowClock.UtcNow;
            var nodes = new ReadOnlyDictionary<string, WorkflowNodeRuntimeInfo>(
                _nodeRuntime.ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value.ToSnapshot(now),
                    StringComparer.Ordinal));
            var scopes = new ReadOnlyDictionary<long, WorkflowParallelScopeInfo>(
                _parallelScopes.ToDictionary(pair => pair.Key, pair => pair.Value.ToSnapshot()));
            var tokens = new ReadOnlyDictionary<long, WorkflowActiveTokenInfo>(
                _activeTokens.ToDictionary(pair => pair.Key, pair => pair.Value));
            var latestChildren = new ReadOnlyDictionary<string, WorkflowChildRuntimeInfo>(
                _latestChildWorkflowByParentNode.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal));
            snapshot = new WorkflowRuntimeSnapshot(
                RunState.RunId,
                Interlocked.Increment(ref _snapshotSequence),
                now,
                _plan.Name,
                IsScope && _state == E_WorkflowExecutionState.Running && Root.State == E_WorkflowExecutionState.Paused
                    ? E_WorkflowExecutionState.Paused
                    : _state,
                _currentNodeId,
                Array.AsReadOnly(_activeNodeIds.OrderBy(id => id, StringComparer.Ordinal).ToArray()),
                tokens,
                _runStopwatch?.Elapsed ?? TimeSpan.Zero,
                nodes,
                scopes,
                activeChildren,
                latestChildren,
                Array.AsReadOnly((IsScope ? Root.ExternalHoldReasons : _externalHoldReasons).ToArray()),
                message,
                _currentFault,
                _currentRecovery);
        }
        return snapshot;
    }

    /// <summary>获取内存中当前保留的不可变 Trace 批次（只含主画布）。</summary>
    /// <returns>来自 Recorder 最近事件窗口、按事件序号升序排列的条目及最后序号。</returns>
    public WorkflowTraceBatch GetTraceBatch() => GetTraceBatch(includeChildRuns: false);

    /// <summary>获取内存中当前保留的 Trace 批次，可选包含子画布（子流程、循环块各圈）的轨迹。</summary>
    /// <param name="includeChildRuns">
    /// 为真时同时返回子画布的条目；这些条目的 <see cref="WorkflowTraceEntry.Parents"/> 标明所属复合节点和圈序号。
    /// 子画布与主画布在同一运行、同一份记录里（R3-a），序号统一递增。
    /// </param>
    /// <returns>按事件序号排列的条目。</returns>
    public WorkflowTraceBatch GetTraceBatch(bool includeChildRuns) => GetTraceBatch(includeChildRuns, null);

    /// <summary>
    /// 增量读取 Trace：只返回 <paramref name="since"/> 之后新产生的条目，批次的
    /// <see cref="WorkflowTraceBatch.Cursor"/> 是下次读取的位置。监视界面周期刷新时用它代替整窗读取，
    /// 开销与新增条目数成正比，而不是与保留窗口大小成正比。
    /// </summary>
    /// <param name="includeChildRuns">是否包含子画布的轨迹，见 <see cref="GetTraceBatch(bool)"/>。</param>
    /// <param name="since">上次读取的位置；为空时返回整个保留窗口。</param>
    public WorkflowTraceBatch GetTraceBatch(bool includeChildRuns, WorkflowTraceCursor? since)
    {
        var cursor = since ?? WorkflowTraceCursor.Empty;
        var recorder = Root._recorder;
        var currentRunId = Root._runId;
        var batch = recorder?.GetRecent(cursor.LastSequence(currentRunId));
        // 以记录器的运行标识为准：新一轮刚开始、记录器尚未替换时，条目仍属于上一轮。
        if (batch is not null && batch.RunId != currentRunId)
            batch = recorder!.GetRecent(cursor.LastSequence(batch.RunId));
        var runId = batch?.RunId ?? currentRunId;
        var all = batch?.Events.Select(@event => ToTraceEntry(@event, runId)).ToArray() ?? Array.Empty<WorkflowTraceEntry>();
        var lastSequence = all.Length == 0 ? cursor.LastSequence(runId) : all[^1].Sequence;
        var entries = includeChildRuns ? all : all.Where(entry => entry.Parents is null).ToArray();
        var visible = new Dictionary<Guid, long>();
        if (runId != Guid.Empty) visible[runId] = lastSequence;
        return new WorkflowTraceBatch(runId, lastSequence, Array.AsReadOnly(entries)) { Cursor = WorkflowTraceCursor.From(visible) };
    }

    private static WorkflowTraceEntry ToTraceEntry(WorkflowRunEvent @event, Guid runId)
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
            Array.AsReadOnly(@event.ExecutionIdentity?.ScopeIds.ToArray() ?? Array.Empty<long>()))
        {
            RunId = runId,
            Parents = @event.Parents is { Count: > 0 } parents ? parents : null
        };
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
                StartedAt = WorkflowClock.UtcNow
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
                info.CompletedAt = WorkflowClock.UtcNow;
                info.Elapsed = info.ExecutionStartedTimestamp > 0
                    ? Polyfill.GetElapsedTime(info.ExecutionStartedTimestamp)
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

    /// <summary>
    /// 标记一次可观察的状态迁移（R1-04）：只递增 <see cref="ChangeVersion"/> 并记下说明，不构造快照、不调用订阅方。
    /// </summary>
    /// <param name="message">下一份快照携带的触发原因或终态说明。</param>
    private void PublishSnapshot(string? message = null)
    {
        Volatile.Write(ref _changeMessage, message);
        MarkChanged();
    }

    /// <summary>递增变化计数，并沿父链递增：子画布内部的变化同样让顶层界面在下一帧重新取快照。</summary>
    private void MarkChanged()
    {
        Interlocked.Increment(ref _changeVersion);
        Volatile.Read(ref _changeOwner)?.MarkChanged();
    }

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
        // 子画布作用域的事件写入根运行的记录，并标明所在画布（R3-a）。
        if (_scopeRoot is { } root)
            return root.TryRecordRunEvent(draft with { Parents = draft.Parents ?? _scopeParents }, writeMode);
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
        WorkflowEventWriteMode writeMode = WorkflowEventWriteMode.Buffered) =>
        TryRecordRunEvent(WorkflowRunEventDraft.Trace(step, node, identity, message, data), writeMode);

    /// <summary>把一次回调交给后台分发（R1-04）；没有订阅方时不分配。</summary>
    private void Notify<T>(Action<T>? handlers, T argument)
    {
        if (handlers is null)
            return;
        // 每个订阅方单独一条：一个订阅方抛异常不影响同一事件的其他订阅方。
        foreach (Action<T> handler in handlers.GetInvocationList())
            _notifications.Post(() => handler(argument));
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
