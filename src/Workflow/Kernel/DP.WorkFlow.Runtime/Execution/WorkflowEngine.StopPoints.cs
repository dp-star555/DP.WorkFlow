using System.Collections.Concurrent;

namespace DP.WorkFlow;

/// <summary>
/// 第 8 类：引擎只负责执行，对外只提供位置和控制动作——停住（Hold）、中断（Interrupt）、
/// 从某位置继续（继续原操作 / 重做 / 回到入口 / 结束本圈）、作废之后、终止。
/// 节点故障、被中断或被要求停下时，流程停在停点上，由 <see cref="IWorkflowStopPointHandler"/>（统管层的故障会话）给出去向。
/// 引擎不判断设备是否安全、是否允许继续；只保证位置合法、数据一致。
/// </summary>
public sealed partial class WorkflowEngine
{
    // 每次 Interrupt / RequestStop 递增；令牌记下自己看到过的值，落后时在下一个节点之前停下。
    private long _stopEpoch;
    private string? _stopMessage;
    private readonly AsyncManualResetEvent _stopSignal = new();
    // 正在执行的普通节点的取消源：Interrupt 时取消它们（复合节点不取消，让中断落到最里面的节点上）。
    private readonly ConcurrentDictionary<CancellationTokenSource, byte> _runningNodes = new();
    // 子画布作用域的进入令牌（父画布上执行复合节点的令牌），用于沿调用路径向外查找入口和循环。
    private ExecutionToken? _enclosingToken;

    /// <summary>
    /// 中断：立即取消正在执行的节点，流程停在这些节点上并记下"做到哪了"；在两个节点之间的路径在下一个节点之前停下。
    /// 用于急停和外部故障。没有停点处理方时，被中断的节点按故障结束运行。
    /// </summary>
    /// <param name="reason">中断原因。</param>
    public void Interrupt(string reason)
    {
        Guard.NotNullOrWhiteSpace(reason);
        if (_scopeRoot is { } root)
        {
            root.Interrupt(reason);
            return;
        }
        Volatile.Write(ref _stopMessage, reason.Trim());
        Interlocked.Exchange(ref _interruptEpoch, Interlocked.Increment(ref _stopEpoch));
        RecordRunEvent(WorkflowRunEventDraft.Lifecycle("InterruptRequested", reason.Trim(),
            new Dictionary<string, object?> { ["Reason"] = reason.Trim() }), WorkflowEventWriteMode.FlushRequested);
        foreach (var running in _runningNodes.Keys)
        {
            try { running.Cancel(); }
            catch (ObjectDisposedException) { }
        }
        _stopSignal.Set();
    }

    /// <summary>
    /// 要求停下：正在执行的节点做完，每条路径在下一个节点之前停下，等待去向（停点原因为 <see cref="WorkflowStopReason.Requested"/>）。
    /// 与 <see cref="AddExternalHold"/> 的区别：停住只是暂不调度，之后原样继续；要求停下会产生停点，可以被改到别的位置继续。
    /// 没有停点处理方时忽略。
    /// </summary>
    /// <param name="reason">原因。</param>
    public void RequestStop(string reason)
    {
        Guard.NotNullOrWhiteSpace(reason);
        if (_scopeRoot is { } root)
        {
            root.RequestStop(reason);
            return;
        }
        Volatile.Write(ref _stopMessage, reason.Trim());
        Interlocked.Increment(ref _stopEpoch);
        RecordRunEvent(WorkflowRunEventDraft.Lifecycle("StopRequested", reason.Trim(),
            new Dictionary<string, object?> { ["Reason"] = reason.Trim() }), WorkflowEventWriteMode.FlushRequested);
        _stopSignal.Set();
    }

    private IWorkflowStopPointHandler? StopPointHandler => Root._options.StopPointHandler;

    private long CurrentStopEpoch => Interlocked.Read(ref Root._stopEpoch);

    /// <summary>等待允许调度下一个节点；被要求停下时提前返回，由调用方在节点之前停下。</summary>
    private async Task WaitForAdmissionAsync(ExecutionToken token, CancellationToken cancellationToken)
    {
        var root = Root;
        while (!root._resumeGate.IsSet && !StopPending(token))
        {
            await Task.WhenAny(root._resumeGate.WaitAsync(CancellationToken.None), root._stopSignal.WaitAsync(CancellationToken.None))
                .WaitAsync(cancellationToken).ConfigureAwait(false);
            if (!StopPending(token))
                root._stopSignal.Reset();
        }
        cancellationToken.ThrowIfCancellationRequested();
    }

    private bool StopPending(ExecutionToken token)
    {
        if (token.SeenStopEpoch >= CurrentStopEpoch)
            return false;
        if (StopPointHandler is null)
        {
            // 没有处理方：要求停下没有人接，忽略。
            token.SeenStopEpoch = CurrentStopEpoch;
            return false;
        }
        return true;
    }

    /// <summary>为一次节点执行登记可被中断的取消源；复合节点（运行子画布）不登记，中断落到子画布里正在执行的节点上。</summary>
    private CancellationTokenSource BeginInterruptible(IWorkflowNodeModel node, ExecutionToken token, CancellationToken cancellationToken)
    {
        var source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (node is not IWorkflowSubDocumentNode)
        {
            var root = Root;
            root._runningNodes[source] = 0;
            // 节点刚通过调度、还没登记时到达的中断，同样作用于这次执行。
            if (token.SeenStopEpoch < Interlocked.Read(ref root._interruptEpoch))
                source.Cancel();
        }
        return source;
    }

    // 最近一次 Interrupt 时的停下请求编号。
    private long _interruptEpoch;

    private void EndInterruptible(CancellationTokenSource source)
    {
        Root._runningNodes.TryRemove(source, out _);
        source.Dispose();
    }

    /// <summary>
    /// 在停点上等待去向：记下停点、让本流程其他路径在节点之间停住、询问处理方；去向不被允许时带着原因再问。
    /// 返回已经核对过的去向和它的目标（回到入口、结束本圈时）。
    /// </summary>
    private async Task<ResolvedDirective> StopAtAsync(
        IWorkflowNodeModel node,
        ExecutionToken token,
        WorkflowStopReason reason,
        string? message,
        int alarmCode,
        string? progress,
        CancellationToken cancellationToken)
    {
        var handler = StopPointHandler!;
        var root = Root;
        token.SeenStopEpoch = CurrentStopEpoch;
        var id = Guid.NewGuid();
        var hold = $"停点 {node.Id}（{id.ToString("N").Substring(0, 8)}）";
        var category = WorkflowRecoveryCategories.Of(node);
        var canContinue = _boundPlan.GetHandler(node.Id) is IWorkflowContinuableNodeHandler;
        var position = PositionOf(node.Id, token);
        RecordRunEvent(new WorkflowRunEventDraft(
            WorkflowRunEventCategory.Recovery,
            "StopPointReached",
            message,
            node.Id,
            node.NodeType,
            IdentityOf(token),
            RecoveryCaseId: id,
            Data: new Dictionary<string, object?>
            {
                ["Reason"] = reason.ToString(),
                ["AlarmCode"] = alarmCode,
                ["Category"] = category.ToString(),
                ["Progress"] = progress,
                ["Position"] = position.ToString()
            }), WorkflowEventWriteMode.FlushRequested);
        SetCurrentStop(new WorkflowRecoveryEvent(id, node.Id, 0, "Stopped", message, null, WorkflowClock.UtcNow));
        root.AddExternalHold(hold);
        try
        {
            string? rejection = null;
            var attempt = 0;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var entries = ReachableEntries(token);
                var point = new WorkflowStopPoint
                {
                    Id = id,
                    FlowName = root._plan.Name,
                    Position = position,
                    NodeTitle = string.IsNullOrWhiteSpace(node.Title) ? node.Id : node.Title,
                    NodeType = node.NodeType,
                    Reason = reason,
                    Message = message,
                    AlarmCode = alarmCode,
                    Category = category,
                    Progress = progress,
                    CanContinueOperation = canContinue,
                    CanEndLap = FindLap(token) is not null,
                    Entries = entries.Select(entry => entry.Entry).ToArray(),
                    Rejection = rejection
                };
                attempt++;
                var directive = await handler.OnStoppedAsync(root, point, cancellationToken).WaitAsync(cancellationToken).ConfigureAwait(false)
                    ?? WorkflowResumeDirective.Stop("处理方没有给出去向。");
                if (!point.Allows(directive, out rejection))
                {
                    RecordRunEvent(new WorkflowRunEventDraft(
                        WorkflowRunEventCategory.Recovery, "ResumeRejected", rejection, node.Id, node.NodeType, IdentityOf(token),
                        RecoveryCaseId: id,
                        Data: new Dictionary<string, object?> { ["Directive"] = directive.DisplayName, ["Attempt"] = attempt }),
                        WorkflowEventWriteMode.FlushRequested);
                    SetCurrentStop(new WorkflowRecoveryEvent(id, node.Id, attempt, "Rejected", rejection, null, WorkflowClock.UtcNow));
                    continue;
                }
                var resolved = new ResolvedDirective(directive,
                    directive.Kind == WorkflowResumeKind.EnterAt
                        ? entries.First(entry => string.Equals(entry.Entry.Key, directive.EntryKey, StringComparison.Ordinal)).Target
                        : directive.Kind == WorkflowResumeKind.EndLap ? FindLap(token) : null);
                RecordRunEvent(new WorkflowRunEventDraft(
                    WorkflowRunEventCategory.Recovery, "ResumeApplied", directive.Message ?? directive.DisplayName, node.Id, node.NodeType,
                    IdentityOf(token), RecoveryCaseId: id,
                    Data: new Dictionary<string, object?> { ["Directive"] = directive.DisplayName, ["Attempt"] = attempt }),
                    WorkflowEventWriteMode.FlushRequested);
                SetCurrentStop(new WorkflowRecoveryEvent(id, node.Id, attempt,
                    directive.Kind == WorkflowResumeKind.Stop ? "Stopped" : "Applied", directive.DisplayName, null, WorkflowClock.UtcNow));
                return resolved;
            }
        }
        finally
        {
            root.RemoveExternalHold(hold);
        }
    }

    private void SetCurrentStop(WorkflowRecoveryEvent stop)
    {
        RunState.RecordRecovery(stop);
        lock (_stateSync)
            _currentRecovery = stop;
        PublishSnapshot();
    }

    private WorkflowExecutionIdentity IdentityOf(ExecutionToken token) =>
        new(_runId, token.TokenId, token.AncestorTokenIds, token.ScopeIds, 0);

    /// <summary>节点在该令牌上的位置。</summary>
    private WorkflowPosition PositionOf(string nodeId, ExecutionToken token) =>
        new(_scopeParents.ToArray(), nodeId, token.CopyLoopFrames(), token.ScopeIds.ToArray(), token.TokenId);

    /// <summary>
    /// 沿调用路径向外（本令牌 → 并行的父令牌 → 子画布的进入令牌）列出本圈已经经过的恢复入口。
    /// 同一入口键在内层和外层都有时，内层优先。
    /// </summary>
    private List<(WorkflowReachableEntry Entry, ResumeTarget Target)> ReachableEntries(ExecutionToken token)
    {
        var result = new List<(WorkflowReachableEntry, ResumeTarget)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var current = token; current is not null; current = current.Parent)
        {
            foreach (var visit in current.ValidAnchors())
            {
                if (!seen.Add(visit.Key))
                    continue;
                var position = current.Owner.PositionOf(visit.NodeId, current) with { LoopIterations = visit.LoopFrames };
                result.Add((new WorkflowReachableEntry(visit.Key, position), new ResumeTarget(current, visit, null, null)));
            }
        }
        return result;
    }

    /// <summary>最内层可以结束本圈的循环：本令牌自己进入的画布循环，或所在的循环块这一圈；都没有时向外找。</summary>
    private ResumeTarget? FindLap(ExecutionToken token)
    {
        for (var current = token; current is not null; current = current.Parent)
        {
            if (current.InnermostOwnLoop() is { } loopNodeId)
                return new ResumeTarget(current, null, loopNodeId, null);
            if (current.IsCanvasRoot && current.Owner is { IsScope: true } scope && scope._scopeParents[^1].Cycle > 0)
                return new ResumeTarget(current, null, null, scope);
        }
        return null;
    }

    /// <summary>
    /// 在拥有目标的那一层（令牌）执行去向：作废入口之后的输出、恢复入口时的循环圈次，返回接着执行的节点。
    /// </summary>
    private string ApplyAtThisLevel(ResumeTarget target, ExecutionToken token)
    {
        if (target.Anchor is { } anchor)
        {
            InvalidateFrom(anchor.OutputSequence);
            token.RestoreLoopFrames(anchor.LoopFrames, anchor.LoopOrder);
            token.ForgetAnchorsFrom(anchor.OutputSequence);
            return anchor.NodeId;
        }
        var loopNodeId = target.LoopNodeId!;
        // 结束本圈：本圈（循环节点上一次执行之后）产生的结果作废，回到循环节点进入下一圈。
        if (RunState.TryGetLatestNodeOutput(loopNodeId, out var lapStart) && lapStart is not null)
        {
            InvalidateFrom(lapStart.ExecutionSequence + 1);
            token.ForgetAnchorsFrom(lapStart.ExecutionSequence + 1);
        }
        return loopNodeId;
    }

    /// <summary>作废之后（InvalidateFrom）：该序号及之后的输出标为无效，绑定读不到，历史记录保留；派生资源归还。</summary>
    private void InvalidateFrom(long sequence)
    {
        RunState.InvalidateOutputsFrom(sequence);
        foreach (var pair in _outputProjections.Where(pair => pair.Key >= sequence).ToArray())
        {
            if (_outputProjections.TryRemove(pair.Key, out var entry))
                RetireProjection(entry.NodeId, entry.Projection);
        }
        // 输出失效必须同步撤销派生投影，否则界面会继续显示已不可绑定的旧结果。
        if (Context.Services.GetService(typeof(IWorkflowNodeOutputProjectionSink)) is IWorkflowNodeOutputProjectionSink projectionSink)
            projectionSink.InvalidateFrom(sequence);
        TryRecordRunEvent(WorkflowRunEventDraft.Lifecycle("OutputsInvalidated", "之后的结果作废。",
            new Dictionary<string, object?> { ["FromSequence"] = sequence }));
    }

    /// <summary>已核对的去向；回到入口、结束本圈时带目标。</summary>
    private sealed record ResolvedDirective(WorkflowResumeDirective Directive, ResumeTarget? Target);

    /// <summary>去向的目标：哪一层令牌上的入口 / 循环节点，或哪个循环块这一圈。</summary>
    private sealed record ResumeTarget(ExecutionToken Token, AnchorVisit? Anchor, string? LoopNodeId, WorkflowEngine? LapScope);

    /// <summary>目标在外层时，沿调用路径向外传递，直到拥有目标的那一层接住；途经的子画布和并行组随之关闭。</summary>
    private sealed class ResumeRedirectException(ResumeTarget target) : Exception("恢复去向转到外层。")
    {
        public ResumeTarget Target { get; } = target;
    }

    /// <summary>流程经过的一个恢复入口。</summary>
    private sealed record AnchorVisit(
        string Key,
        string NodeId,
        long OutputSequence,
        IReadOnlyDictionary<string, int> LoopFrames,
        IReadOnlyList<string> LoopOrder);

    /// <summary>
    /// 一条执行路径（令牌）。记下画布循环的圈次（及进入顺序）、经过的恢复入口和看到过的停下请求。
    /// </summary>
    private sealed class ExecutionToken
    {
        private readonly Dictionary<string, int> _loopFrames;
        // 本令牌自己进入的画布循环，按进入先后；分支令牌从父令牌复制来的循环不在这里。
        private readonly List<string> _ownLoops = new();
        private readonly Dictionary<string, AnchorVisit> _anchors = new(StringComparer.Ordinal);
        private readonly object _sync = new();

        public ExecutionToken(
            long tokenId,
            IReadOnlyList<long> ancestorTokenIds,
            IReadOnlyList<long> scopeIds,
            WorkflowEngine owner,
            ExecutionToken? parent,
            bool isCanvasRoot,
            long seenStopEpoch,
            IReadOnlyDictionary<string, int>? loopFrames = null)
        {
            TokenId = tokenId;
            AncestorTokenIds = ancestorTokenIds;
            ScopeIds = scopeIds;
            Owner = owner;
            Parent = parent;
            IsCanvasRoot = isCanvasRoot;
            SeenStopEpoch = seenStopEpoch;
            _loopFrames = loopFrames is null
                ? new Dictionary<string, int>(StringComparer.Ordinal)
                : loopFrames.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        }

        public long TokenId { get; }

        public IReadOnlyList<long> AncestorTokenIds { get; }

        public IReadOnlyList<long> ScopeIds { get; }

        /// <summary>令牌所在画布的引擎（根运行或子画布作用域）。</summary>
        public WorkflowEngine Owner { get; }

        /// <summary>外层令牌：并行分支的父令牌，或子画布根令牌的进入令牌；主画布根令牌为空。</summary>
        public ExecutionToken? Parent { get; }

        /// <summary>是否为画布（主画布或子画布）的根令牌。</summary>
        public bool IsCanvasRoot { get; }

        public long SeenStopEpoch { get; set; }

        public int PrepareLoopIteration(string nodeId)
        {
            lock (_sync)
                return _loopFrames.TryGetValue(nodeId, out var completedIterations) ? completedIterations + 1 : 1;
        }

        public void CommitLoopIteration(string nodeId, int iteration)
        {
            lock (_sync)
            {
                if (!_loopFrames.ContainsKey(nodeId))
                    _ownLoops.Add(nodeId);
                _loopFrames[nodeId] = iteration;
            }
        }

        public void CompleteLoop(string nodeId)
        {
            lock (_sync)
            {
                _loopFrames.Remove(nodeId);
                _ownLoops.Remove(nodeId);
            }
        }

        public IReadOnlyDictionary<string, int> CopyLoopFrames()
        {
            lock (_sync)
                return new Dictionary<string, int>(_loopFrames, StringComparer.Ordinal);
        }

        public string? InnermostOwnLoop()
        {
            lock (_sync)
                return _ownLoops.Count == 0 ? null : _ownLoops[^1];
        }

        public void RecordAnchor(string key, string nodeId, long outputSequence)
        {
            lock (_sync)
                _anchors[key] = new AnchorVisit(key, nodeId, outputSequence,
                    new Dictionary<string, int>(_loopFrames, StringComparer.Ordinal), _ownLoops.ToArray());
        }

        /// <summary>本圈仍然有效的入口：经过入口时所在的各层循环，现在仍在同一圈。</summary>
        public IReadOnlyList<AnchorVisit> ValidAnchors()
        {
            lock (_sync)
                return _anchors.Values
                    .Where(visit => visit.LoopFrames.All(pair => _loopFrames.TryGetValue(pair.Key, out var current) && current == pair.Value))
                    .OrderBy(visit => visit.OutputSequence)
                    .ToArray();
        }

        public void ForgetAnchorsFrom(long outputSequence)
        {
            lock (_sync)
                foreach (var key in _anchors.Where(pair => pair.Value.OutputSequence >= outputSequence).Select(pair => pair.Key).ToArray())
                    _anchors.Remove(key);
        }

        public void RestoreLoopFrames(IReadOnlyDictionary<string, int> frames, IReadOnlyList<string> order)
        {
            lock (_sync)
            {
                _loopFrames.Clear();
                foreach (var pair in frames)
                    _loopFrames[pair.Key] = pair.Value;
                _ownLoops.Clear();
                _ownLoops.AddRange(order);
            }
        }
    }
}
