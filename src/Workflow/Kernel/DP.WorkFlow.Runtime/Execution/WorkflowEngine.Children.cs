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
        var session = new ChildWorkflowSession(this, parentNode, parentExecution, key, childEngine);

        bool isPaused;
        string[] holdReasons;
        lock (_stateSync)
        {
            // 整个会话期间保持登记，两圈之间到达的暂停、Hold 同样传给子引擎。
            _activeChildEngines.Add(key, childEngine);
            isPaused = _manualPauseRequested;
            holdReasons = _externalHoldReasons.ToArray();
        }
        childEngine.SnapshotChanged += session.OnSnapshotChanged;
        if (isPaused)
            childEngine.Pause();
        foreach (var holdReason in holdReasons)
            childEngine.AddExternalHold(holdReason);
        return session;
    }

    private void UpdateChildWorkflowSnapshot(
        string key,
        IWorkflowNodeModel parentNode,
        WorkflowExecutionIdentity parentExecution,
        WorkflowRuntimeSnapshot snapshot)
    {
        lock (_stateSync)
            _activeChildWorkflows[key] = new WorkflowChildRuntimeInfo(parentNode.Id, parentExecution, snapshot);
        PublishSnapshot();
    }

    /// <summary>循环块的子流程会话；释放时结束登记，并等待最后一圈的运行记录收尾。</summary>
    private sealed class ChildWorkflowSession(
        WorkflowEngine owner,
        IWorkflowNodeModel parentNode,
        WorkflowExecutionIdentity parentExecution,
        string key,
        WorkflowEngine engine) : IWorkflowChildWorkflowSession
    {
        private bool _keepLastCycleOutputs;
        private bool _disposed;

        internal void OnSnapshotChanged(WorkflowRuntimeSnapshot snapshot) =>
            owner.UpdateChildWorkflowSnapshot(key, parentNode, parentExecution, snapshot);

        public async Task<WorkflowRunResult> RunCycleAsync(CancellationToken cancellationToken)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            // 圈与圈之间不传递数据，上一圈剩余输出不会再被读取，开始新一圈前释放其资源。
            owner.RetireChildOutputProjections(parentNode.Id, engine.DetachOutputProjections());
            _keepLastCycleOutputs = false;
            var result = await engine.RunAsync(cancellationToken).ConfigureAwait(false);
            OnSnapshotChanged(engine.GetRuntimeSnapshot(result.Message));
            return result;
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
                engine.SnapshotChanged -= OnSnapshotChanged;
                lock (owner._stateSync)
                {
                    owner._activeChildEngines.Remove(key);
                    if (owner._activeChildWorkflows.Remove(key, out var completed))
                        owner._latestChildWorkflowByParentNode[parentNode.Id] = completed;
                }
                owner.PublishSnapshot();
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
        void UpdateChildSnapshot(WorkflowRuntimeSnapshot snapshot)
        {
            lock (_stateSync)
                _activeChildWorkflows[key] = new WorkflowChildRuntimeInfo(parentNode.Id, parentExecution, snapshot);
            PublishSnapshot();
        }

        bool isPaused;
        string[] holdReasons;
        lock (_stateSync)
        {
            _activeChildEngines.Add(key, childEngine);
            isPaused = _manualPauseRequested;
            holdReasons = _externalHoldReasons.Where(reason => !string.Equals(reason, excludedHold, StringComparison.Ordinal)).ToArray();
        }
        childEngine.SnapshotChanged += UpdateChildSnapshot;
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
            UpdateChildSnapshot(childEngine.GetRuntimeSnapshot(result.Message));
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
                childEngine.SnapshotChanged -= UpdateChildSnapshot;
                lock (_stateSync)
                {
                    _activeChildEngines.Remove(key);
                    // 完成后从活动集合移除，并按父节点覆盖保存最近一次快照；更早历史只进入事件流。
                    if (_activeChildWorkflows.Remove(key, out var completed))
                        _latestChildWorkflowByParentNode[parentNode.Id] = completed;
                }
                PublishSnapshot();
            }
        }
    }
}
