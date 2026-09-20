namespace DP.WorkFlow;

/// <summary>告警处理出口：停止当前工站并交由人工或上位机处置。</summary>
[WorkflowNode("WarnStopStation", DisplayName = "停止当前工站", Category = "8.Process/异常恢复")]
public sealed class StopCurrentStationNodeModel : WorkflowNodeModel
{
    /// <inheritdoc />
    public override string NodeType => "WarnStopStation";
    /// <summary>停机原因。</summary>
    public string Reason { get; set; } = string.Empty;
}

/// <summary>生成停止工站裁决并发送停机请求。</summary>
public sealed class StopCurrentStationNodeHandler : WorkflowNodeHandler<StopCurrentStationNodeModel>
{
    /// <inheritdoc />
    protected override async ValueTask<NodeExecutionResult> ExecuteAsync(StopCurrentStationNodeModel node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
    {
        var reason = string.IsNullOrWhiteSpace(node.Reason) ? "告警处理流程请求停止当前工站。" : node.Reason.Trim();
        var output = new WarningResolution { ExitAction = E_WarningExitAction.StopCurrentStation, Note = reason };
        context.SetVariable(WorkflowRecoveryRuntimeKeys.WarningResolution, output);
        if (context.Services.GetService(typeof(IWorkflowRecoveryService)) is IWorkflowRecoveryService service)
        {
            var identity = context.TryGetVariable<WorkflowFaultRecoveryRequest>(WorkflowRecoveryRuntimeKeys.FaultRequest, out var fault)
                ? fault?.ExecutionIdentity ?? context.ExecutionIdentity
                : context.ExecutionIdentity;
            await service.RequestStationStopAsync(reason, identity, cancellationToken).ConfigureAwait(false);
        }
        context.Trace("请求停机", "异常处理流程请求停止当前工站。", new Dictionary<string, object?> { ["Reason"] = reason });
        return NodeExecutionResult.Complete(output);
    }
}
