namespace DP.WorkFlow;

/// <summary>告警处理子流程入口，暴露当前中断上下文。</summary>
[WorkflowNode("WarningHandlerStart", DisplayName = "告警处理起点", Category = "8.Process/异常恢复")]
public sealed class WarningHandlerStartNodeModel : WorkflowNodeModel
{
    /// <inheritdoc />
    public override string NodeType => "WarningHandlerStart";
}

/// <summary>加载告警中断上下文。</summary>
public sealed class WarningHandlerStartNodeHandler : WorkflowNodeHandler<WarningHandlerStartNodeModel>
{
    /// <inheritdoc />
    protected override ValueTask<NodeExecutionResult> ExecuteAsync(WarningHandlerStartNodeModel node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        context.TryGetVariable<WorkflowInterruptContext>(WorkflowRecoveryRuntimeKeys.InterruptContext, out var interruptContext);
        context.Trace("加载中断上下文", "告警处理起点加载当前中断上下文。", new Dictionary<string, object?> { ["HasInterruptContext"] = interruptContext is not null, ["ReasonCode"] = interruptContext?.ReasonCode, ["FaultNodeId"] = interruptContext?.FaultNodeId });
        return ValueTask.FromResult(NodeExecutionResult.Continue(output: interruptContext));
    }
}
