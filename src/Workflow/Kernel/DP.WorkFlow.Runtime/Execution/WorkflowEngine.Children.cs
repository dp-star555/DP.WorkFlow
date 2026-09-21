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
        void UpdateChildSnapshot(WorkflowRuntimeSnapshot snapshot)
        {
            lock (_stateSync)
                _childWorkflows[key] = new WorkflowChildRuntimeInfo(parentNode.Id, parentExecution, snapshot);
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

        try
        {
            if (prepare)
            {
                WorkflowRuntimeCapabilityValidator.Validate(boundPlan, childContext.Services);
                if (childContext.Services.GetService(typeof(IWorkflowRunPreparationService)) is IWorkflowRunPreparationService preparation)
                    await preparation.PrepareAsync(
                        new WorkflowRunPreparationContext(
                            EnumerateRecoveryNodes(boundPlan.Plan).ToArray(),
                            WorkflowRunScopeKind.Nested,
                            parentNode.Id,
                            childContext.Services),
                        cancellationToken).ConfigureAwait(false);
            }
            var result = await childEngine.RunAsync(cancellationToken).ConfigureAwait(false);
            UpdateChildSnapshot(childEngine.GetRuntimeSnapshot(result.Message));
            return result;
        }
        finally
        {
            childEngine.SnapshotChanged -= UpdateChildSnapshot;
            lock (_stateSync)
                _activeChildEngines.Remove(key);
        }
    }
}
