namespace DP.WorkFlow;

/// <summary>使用专用告警子画布处理引擎故障并转换 WarningResolution。</summary>
public sealed class WorkflowWarningHandlerCoordinator : IWorkflowFaultRecoveryCoordinator
{
    private readonly WorkflowExecutionPlan _plan;
    private readonly WorkflowBoundExecutionPlan _boundPlan;
    private readonly WorkflowExecutionOptions _options;
    private readonly IWorkflowTreatmentRecoveryPolicy _treatmentPolicy;

    /// <summary>从告警处理块创建恢复协调器。</summary>
    public WorkflowWarningHandlerCoordinator(WarningHandlerBlockNodeModel block, WorkflowNodeCatalog catalog, WorkflowNodeHandlerCatalog handlers, WorkflowExecutionOptions? options = null, IWorkflowTreatmentRecoveryPolicy? treatmentPolicy = null)
    {
        ArgumentNullException.ThrowIfNull(block);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(handlers);
        if (string.IsNullOrWhiteSpace(block.SubDocument.EntryNodeId))
            throw new InvalidOperationException("告警处理块缺少文档入口。");
        _plan = new WorkflowCompiler(catalog).Compile(block.SubDocument);
        _boundPlan = new WorkflowRuntimeBinder(handlers).Bind(_plan);
        _treatmentPolicy = treatmentPolicy ?? new WorkflowOperatorTreatmentRecoveryPolicy();
        var sourceOptions = options ?? new WorkflowExecutionOptions();
        _options = new WorkflowExecutionOptions
        {
            MaxTotalNodeExecutions = sourceOptions.MaxTotalNodeExecutions,
            MaxNodeExecutions = sourceOptions.MaxNodeExecutions,
            MaxTraceEntries = sourceOptions.MaxTraceEntries,
            MaxRecoveryAttempts = sourceOptions.MaxRecoveryAttempts
        };
    }

    /// <inheritdoc />
    public async ValueTask<WorkflowFaultRecoveryDecision> RecoverAsync(WorkflowFaultRecoveryRequest request, IWorkflowFaultRecoveryContext context, CancellationToken cancellationToken)
    {
        var interrupt = BuildInterruptContext(request, context);
        var childContext = new WorkflowContext(context.Services);
        childContext.SetVariable(WorkflowRecoveryRuntimeKeys.InterruptContext, interrupt);
        childContext.SetVariable(WorkflowRecoveryRuntimeKeys.FaultRequest, request);
        var executionOptions = new WorkflowExecutionOptions
        {
            MaxTotalNodeExecutions = _options.MaxTotalNodeExecutions,
            MaxNodeExecutions = _options.MaxNodeExecutions,
            MaxTraceEntries = _options.MaxTraceEntries,
            MaxRecoveryAttempts = _options.MaxRecoveryAttempts,
            RecoveryCaseId = request.CaseId,
            RecoveryCoordinator = new TreatmentStepCoordinator(request, _treatmentPolicy)
        };
        WorkflowRunResult result;
        if (context is IWorkflowRecoverySubflowContext supervised)
            result = await supervised.RunRecoverySubflowAsync(_boundPlan, childContext, executionOptions, cancellationToken).ConfigureAwait(false);
        else
        {
            WorkflowRuntimeCapabilityValidator.Validate(_boundPlan, context.Services);
            if (context.Services.GetService(typeof(IWorkflowRunPreparationService)) is IWorkflowRunPreparationService preparation)
                await preparation.PrepareAsync(new WorkflowRunPreparationContext(EnumerateNodes(_plan).ToArray()), cancellationToken).ConfigureAwait(false);
            result = await new WorkflowEngine(_boundPlan, childContext, executionOptions).RunAsync(cancellationToken).ConfigureAwait(false);
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (!result.Success) throw new InvalidOperationException("告警处理子流程失败: " + result.Message);
        if (!childContext.TryGetVariable<WarningResolution>(WorkflowRecoveryRuntimeKeys.WarningResolution, out var resolution) || resolution is null)
            return WorkflowFaultRecoveryDecision.Stop("告警处理子流程没有产生 WarningResolution。");
        context.SetVariable(WorkflowRecoveryRuntimeKeys.WarningResolution, resolution);
        return resolution.ExitAction switch
        {
            E_WarningExitAction.ContinueOperation => WorkflowFaultRecoveryDecision.Continue(resolution.Note),
            E_WarningExitAction.RestartFromEntry when !string.IsNullOrWhiteSpace(resolution.RecoveryEntryKey) => WorkflowFaultRecoveryDecision.Restart(resolution.RecoveryEntryKey, resolution.Note),
            E_WarningExitAction.RestartFromEntry => WorkflowFaultRecoveryDecision.Stop("恢复裁决缺少入口键。"),
            E_WarningExitAction.RetryCurrentNode => WorkflowFaultRecoveryDecision.Retry(resolution.Note),
            E_WarningExitAction.JumpToNode when !string.IsNullOrWhiteSpace(resolution.TargetNodeId) => WorkflowFaultRecoveryDecision.Jump(resolution.TargetNodeId, resolution.Note),
            E_WarningExitAction.JumpToNode => WorkflowFaultRecoveryDecision.Stop("JumpToNode 裁决缺少 TargetNodeId。"),
            E_WarningExitAction.StopCurrentStation => WorkflowFaultRecoveryDecision.Stop(resolution.Note),
            _ => WorkflowFaultRecoveryDecision.Stop("未知 WarningResolution。")
        };
    }

    private sealed class TreatmentStepCoordinator(WorkflowFaultRecoveryRequest original, IWorkflowTreatmentRecoveryPolicy policy)
        : IWorkflowFaultRecoveryCoordinator
    {
        public async ValueTask<WorkflowFaultRecoveryDecision> RecoverAsync(WorkflowFaultRecoveryRequest request,
            IWorkflowFaultRecoveryContext context, CancellationToken cancellationToken)
        {
            if (!request.CanContinueOperation || request.OperationId is null || request.CommitStarted)
                return WorkflowFaultRecoveryDecision.Stop("处置步骤不可继续，禁止重放处置图：" + request.Message);
            var decision = await policy.RecoverStepAsync(original, request, context, cancellationToken).ConfigureAwait(false);
            return decision?.Action is WorkflowFaultRecoveryAction.ContinueOperation or WorkflowFaultRecoveryAction.Stop
                ? decision : WorkflowFaultRecoveryDecision.Stop("处置步骤只允许继续原操作或终止，禁止递归恢复和跳转。");
        }
    }

    private static IEnumerable<IWorkflowNodeModel> EnumerateNodes(WorkflowExecutionPlan plan)
    {
        foreach (var id in plan.NodeIds) yield return plan.GetNodeOrThrow(id);
        foreach (var child in plan.ChildPlans.Values)
        foreach (var node in EnumerateNodes(child)) yield return node;
    }

    private static WorkflowInterruptContext BuildInterruptContext(WorkflowFaultRecoveryRequest request, IWorkflowFaultRecoveryContext context)
    {
        context.TryGetVariable<WorkflowSafePointContext>(WorkflowRecoveryRuntimeKeys.CurrentSafePoint, out var safePoint);
        var interrupt = new WorkflowInterruptContext
        {
            Source = E_WorkflowInterruptSource.Node,
            Severity = E_WorkflowFaultSeverity.Recoverable,
            AlarmCode = request.AlarmCode,
            WorkflowName = request.WorkflowName,
            Category = "NodeFault",
            ReasonCode = "NodeExecutionFailed",
            FaultNodeId = request.FaultNodeId,
            FaultNodeTitle = request.FaultNodeTitle,
            Message = request.RecoveryFailure is null ? request.Message : request.Message + "\n上次处理未完成：" + request.RecoveryFailure,
            SafePoint = safePoint,
            Time = DateTime.Now
        };
        interrupt.Payload["RecoveryAttempt"] = request.Attempt;
        interrupt.Payload["CaseId"] = request.CaseId;
        if (request.OperationId is Guid operationId) interrupt.Payload["OperationId"] = operationId;
        if (request.RecoveryFailure is not null) interrupt.Payload["RecoveryFailure"] = request.RecoveryFailure;
        if (request.ExecutionIdentity is not null)
        {
            interrupt.Payload["RunId"] = request.ExecutionIdentity.RunId;
            interrupt.Payload["TokenId"] = request.ExecutionIdentity.TokenId;
            interrupt.Payload["NodeExecutionCount"] = request.ExecutionIdentity.NodeExecutionCount;
        }
        return interrupt;
    }
}
