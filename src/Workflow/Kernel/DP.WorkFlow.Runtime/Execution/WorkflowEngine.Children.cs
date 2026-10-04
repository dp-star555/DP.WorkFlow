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
            childContext, _options, null, false, cancellationToken);
    }

    private async Task<WorkflowRunResult> RunTrackedChildAsync(
        IWorkflowNodeModel parentNode, WorkflowExecutionIdentity parentExecution,
        WorkflowBoundExecutionPlan boundPlan, WorkflowContext childContext,
        WorkflowExecutionOptions options, string? excludedHold, bool prepare,
        CancellationToken cancellationToken)
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
