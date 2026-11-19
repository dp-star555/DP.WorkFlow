namespace DP.WorkFlow;

public sealed partial class WorkflowEngine
{
    private Task<WorkflowRunResult> RunChildWorkflowAsync(
        IWorkflowNodeModel parentNode,
        WorkflowExecutionIdentity parentExecution,
        WorkflowExecutionPlan childDefinition,
        WorkflowContext childContext,
        CancellationToken cancellationToken)
    {
        return RunTrackedChildAsync(parentNode, parentExecution, _boundPlan.GetChildPlan(parentNode.Id),
            childContext, _options, null, false, cancellationToken, adoptOutputs: true);
    }

    /// <summary>为循环块打开可反复运行同一子计划的会话：子引擎只创建一次，每圈调用一次 RunAsync。</summary>
    /// <param name="parentNode">拥有子画布的父节点。</param>
    /// <param name="parentExecution">父节点本次执行身份。</param>
    /// <param name="childContext">整个会话期间复用的子上下文。</param>
    /// <returns>调用方负责释放的会话。</returns>
    private IWorkflowChildWorkflowSession OpenChildWorkflowSession(
        IWorkflowNodeModel parentNode,
        WorkflowExecutionIdentity parentExecution,
        WorkflowContext childContext)
    {
        var key = $"{parentNode.Id}:{parentExecution.TokenId}:{parentExecution.NodeExecutionCount}";
        var childEngine = new WorkflowEngine(_boundPlan.GetChildPlan(parentNode.Id), childContext, _options);
        childEngine.PlanPath = WorkflowRunPreparationPlanner.ChildPath(PlanPath, parentNode.Id);
        childEngine.BindingScopeId = BindingScopeId;
        childEngine._deferRecordingCompletion = true;
        childEngine.SetParentContext(_runId, parentExecution);
        var traceSource = new ChildTraceSource(parentNode.Id, childEngine);
        var session = new ChildWorkflowSession(this, parentNode, parentExecution, key, childEngine, traceSource);

        bool isPaused;
        string[] holdReasons;
        lock (_stateSync)
        {
            // 整个会话期间保持登记，两圈之间到达的暂停、Hold 同样传给子引擎。
            _activeChildEngines.Add(key, childEngine);
            _childTraceSources.Add(key, traceSource);
            _activeChildWorkflows[key] = new ActiveChildWorkflow(key, parentNode.Id, parentExecution, childEngine, session.CurrentCycle);
            isPaused = _manualPauseRequested;
            holdReasons = _externalHoldReasons.ToArray();
        }
        Volatile.Write(ref childEngine._changeOwner, this);
        PublishSnapshot();
        if (isPaused)
            childEngine.Pause();
        foreach (var holdReason in holdReasons)
            childEngine.AddExternalHold(holdReason);
        return session;
    }

    /// <summary>
    /// 一个正在运行的子运行的登记项。父快照需要它时才向子引擎取快照（R1-04），子引擎不再逐次推送。
    /// </summary>
    private sealed class ActiveChildWorkflow(
        string key,
        string parentNodeId,
        WorkflowExecutionIdentity parentExecution,
        WorkflowEngine engine,
        Func<int> cycle)
    {
        public string Key { get; } = key;
        public WorkflowEngine Engine { get; } = engine;

        /// <summary>子运行结束后最后一次的说明（成功、失败原因）；运行中为空。</summary>
        public string? LastMessage { get; set; }

        public WorkflowChildRuntimeInfo Capture() =>
            new(parentNodeId, parentExecution, Engine.GetRuntimeSnapshot(LastMessage), cycle());
    }

    /// <summary>
    /// 子运行一圈（或一次）结束后，把它的轨迹按父节点有界保留，并停止实时合并该来源；
    /// 运行监控因此能看到已结束各圈的节点开始、完成、耗时，而不是只有父节点的开始。
    /// </summary>
    private void ArchiveChildTrace(ChildTraceSource source)
    {
        IReadOnlyList<WorkflowTraceEntry> entries;
        Guid? runId = null;
        try
        {
            var batch = source.Engine.GetTraceBatch(includeChildRuns: true);
            entries = batch.Entries;
            runId = batch.RunId;
        }
        catch (Exception exception)
        {
            // 轨迹只供观察，归档失败不改变子运行结果。
            ReportRecordingDegraded($"归档子运行轨迹失败：{exception.Message}", source.ParentNodeId);
            entries = Array.Empty<WorkflowTraceEntry>();
        }
        lock (_stateSync)
        {
            source.ArchivedRunId = runId;
            if (!_childTraceHistory.TryGetValue(source.ParentNodeId, out var history))
                _childTraceHistory[source.ParentNodeId] = history = new Queue<WorkflowTraceEntry>();
            foreach (var entry in entries)
            {
                history.Enqueue(TagChildTrace(entry, source.ParentNodeId, source.Cycle));
                while (history.Count > _options.MaxTraceEntries)
                    history.Dequeue();
            }
        }
    }

    private static WorkflowTraceEntry TagChildTrace(WorkflowTraceEntry entry, string parentNodeId, int cycle) =>
        entry with
        {
            Parents = Array.AsReadOnly(
                new[] { new WorkflowTraceParent(parentNodeId, cycle) }
                    .Concat(entry.Parents ?? Array.Empty<WorkflowTraceParent>())
                    .ToArray())
        };

    /// <summary>
    /// 查找本运行或其子运行（正在运行的，或每个父节点最近一次完成的）中节点的最近有效输出。
    /// 子画布按自身子快照的运行标识查询，因此循环块、子流程内部节点的结果也能在界面上实时查看。
    /// </summary>
    /// <param name="runId">节点所属运行的标识。</param>
    /// <param name="nodeId">节点 ID。</param>
    /// <returns>最近一次未失效的输出；该运行已不在本引擎可见范围内时为空。</returns>
    public WorkflowNodeOutput? FindLatestNodeOutput(Guid runId, string nodeId)
    {
        Guard.NotNullOrWhiteSpace(nodeId);
        if (runId == RunState.RunId)
            return RunState.TryGetLatestNodeOutput(nodeId, out var output) ? output : null;
        WorkflowEngine[] children;
        lock (_stateSync)
            children = _activeChildEngines.Values.Concat(_latestChildEngineByParentNode.Values).Distinct().ToArray();
        foreach (var child in children)
        {
            if (child.FindLatestNodeOutput(runId, nodeId) is { } found)
                return found;
        }
        return null;
    }

    /// <summary>
    /// 一个仍在登记的子引擎的轨迹来源。子引擎当前运行的轨迹已归档（<see cref="ArchivedRunId"/>）时不再实时合并，
    /// 循环块两圈之间因此不会把上一圈重复显示为下一圈。
    /// </summary>
    private sealed class ChildTraceSource(string parentNodeId, WorkflowEngine engine)
    {
        public string ParentNodeId { get; } = parentNodeId;
        public WorkflowEngine Engine { get; } = engine;
        public int Cycle { get; set; }
        public Guid? ArchivedRunId { get; set; }
    }

    /// <summary>循环块的子流程会话；释放时结束登记，并等待最后一圈的运行记录收尾。</summary>
    private sealed class ChildWorkflowSession(
        WorkflowEngine owner,
        IWorkflowNodeModel parentNode,
        WorkflowExecutionIdentity parentExecution,
        string key,
        WorkflowEngine engine,
        ChildTraceSource trace) : IWorkflowChildWorkflowSession
    {
        private bool _keepLastCycleOutputs;
        private bool _disposed;
        private int _cycle;

        internal Func<int> CurrentCycle => () => Volatile.Read(ref _cycle);

        public async Task<WorkflowRunResult> RunCycleAsync(CancellationToken cancellationToken)
        {
            Guard.NotDisposed(_disposed, this);
            // 圈与圈之间不传递数据，上一圈剩余输出不会再被读取，开始新一圈前释放其资源。
            owner.RetireChildOutputProjections(parentNode.Id, engine.DetachOutputProjections());
            _keepLastCycleOutputs = false;
            var cycle = Interlocked.Increment(ref _cycle);
            lock (owner._stateSync)
            {
                trace.Cycle = cycle;
                if (owner._activeChildWorkflows.TryGetValue(key, out var running))
                    running.LastMessage = null;
            }
            try
            {
                var result = await engine.RunAsync(cancellationToken).ConfigureAwait(false);
                lock (owner._stateSync)
                {
                    if (owner._activeChildWorkflows.TryGetValue(key, out var active))
                        active.LastMessage = result.Message;
                }
                owner.PublishSnapshot();
                return result;
            }
            finally
            {
                owner.ArchiveChildTrace(trace);
            }
        }

        public void KeepLastCycleOutputs() => _keepLastCycleOutputs = true;

        public async ValueTask DisposeAsync()
        {
            if (_disposed)
                return;
            _disposed = true;
            try
            {
                owner.AdoptChildOutputProjections(parentNode.Id, parentExecution.TokenId,
                    engine.DetachOutputProjections(), _keepLastCycleOutputs);
                await engine._pendingRecordingCompletion.ConfigureAwait(false);
            }
            finally
            {
                owner.RetireActiveChild(key, parentNode.Id, engine);
            }
        }
    }

    private async Task<WorkflowRunResult> RunTrackedChildAsync(
        IWorkflowNodeModel parentNode, WorkflowExecutionIdentity parentExecution,
        WorkflowBoundExecutionPlan boundPlan, WorkflowContext childContext,
        WorkflowExecutionOptions options, string? excludedHold, bool prepare,
        CancellationToken cancellationToken, bool adoptOutputs = false)
    {
        var key = $"{parentNode.Id}:{parentExecution.TokenId}:{parentExecution.NodeExecutionCount}"
            + (excludedHold is null ? string.Empty : ":Recovery");
        var childEngine = new WorkflowEngine(boundPlan, childContext, options);
        childEngine.PlanPath = WorkflowRunPreparationPlanner.ChildPath(PlanPath, parentNode.Id);
        childEngine.BindingScopeId = prepare ? Guid.NewGuid() : BindingScopeId;
        childEngine.SetParentContext(_runId, parentExecution);
        var traceSource = new ChildTraceSource(parentNode.Id, childEngine);
        bool isPaused;
        string[] holdReasons;
        lock (_stateSync)
        {
            _activeChildEngines.Add(key, childEngine);
            _childTraceSources[key] = traceSource;
            _activeChildWorkflows[key] = new ActiveChildWorkflow(key, parentNode.Id, parentExecution, childEngine, static () => 0);
            isPaused = _manualPauseRequested;
            holdReasons = _externalHoldReasons.Where(reason => !string.Equals(reason, excludedHold, StringComparison.Ordinal)).ToArray();
        }
        Volatile.Write(ref childEngine._changeOwner, this);
        PublishSnapshot();
        if (isPaused)
            childEngine.Pause();
        foreach (var holdReason in holdReasons)
            childEngine.AddExternalHold(holdReason);

        IWorkflowPreparedRun? prepared = null;
        try
        {
            if (prepare)
            {
                WorkflowRuntimeCapabilityValidator.Validate(boundPlan, childContext.Services);
                var preparationContext = new WorkflowRunPreparationContext(
                    EnumerateRecoveryNodes(boundPlan.Plan).ToArray(), WorkflowRunScopeKind.Nested, parentNode.Id, childContext.Services,
                    WorkflowRunPreparationPlanner.Enumerate(boundPlan.Plan, childEngine.PlanPath), childEngine.BindingScopeId);
                if (childContext.Services.GetService(typeof(IWorkflowRunPreparationService)) is IWorkflowTransactionalRunPreparationService transactional)
                    prepared = await transactional.PrepareRunAsync(preparationContext, cancellationToken).ConfigureAwait(false);
                else if (childContext.Services.GetService(typeof(IWorkflowRunPreparationService)) is IWorkflowRunPreparationService preparation)
                    await preparation.PrepareAsync(preparationContext, cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                prepared?.Commit();
            }
            var result = await childEngine.RunAsync(cancellationToken).ConfigureAwait(false);
            lock (_stateSync)
            {
                if (_activeChildWorkflows.TryGetValue(key, out var active))
                    active.LastMessage = result.Message;
            }
            // 子运行的映射输出可能引用其节点输出（例如图像帧），资源随父节点本次输出的生命周期释放。
            if (adoptOutputs)
                AdoptChildOutputProjections(parentNode.Id, parentExecution.TokenId,
                    childEngine.DetachOutputProjections(), result.Success);
            return result;
        }
        finally
        {
            try { if (prepared is not null) await prepared.DisposeAsync().ConfigureAwait(false); }
            finally
            {
                ArchiveChildTrace(traceSource);
                RetireActiveChild(key, parentNode.Id, childEngine);
            }
        }
    }

    /// <summary>
    /// 子运行结束：从活动集合移除，并按父节点覆盖保存最后一份快照（只在这里构造一次）；更早历史只进入事件流。
    /// </summary>
    private void RetireActiveChild(string key, string parentNodeId, WorkflowEngine childEngine)
    {
        ActiveChildWorkflow? completed;
        lock (_stateSync)
        {
            _activeChildEngines.Remove(key);
            _childTraceSources.Remove(key);
            _latestChildEngineByParentNode[parentNodeId] = childEngine;
            _activeChildWorkflows.Remove(key, out completed);
        }
        if (completed is not null)
        {
            // 子引擎有自己的锁，在本引擎锁外取快照。
            var info = completed.Capture();
            lock (_stateSync)
                _latestChildWorkflowByParentNode[parentNodeId] = info;
        }
        Volatile.Write(ref childEngine._changeOwner, null);
        PublishSnapshot();
    }
}
