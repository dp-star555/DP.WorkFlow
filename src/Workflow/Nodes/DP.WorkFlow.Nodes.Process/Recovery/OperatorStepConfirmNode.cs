namespace DP.WorkFlow;

/// <summary>弹出单步操作提示，人工确认后继续。</summary>
[WorkflowNode("OperatorStepConfirm", DisplayName = "单步确认节点", Category = "8.Process/异常恢复")]
public sealed class OperatorStepConfirmNodeModel : WorkflowNodeModel
{
    /// <inheritdoc />
    public override string NodeType => "OperatorStepConfirm";
    /// <summary>本步需要人工完成的操作说明。</summary>
    public string StepText { get; set; } = string.Empty;
}

/// <summary>执行人工单步确认。</summary>
public sealed class OperatorStepConfirmNodeHandler : WorkflowNodeHandler<OperatorStepConfirmNodeModel>
{
    /// <inheritdoc />
    protected override async ValueTask<NodeExecutionResult> ExecuteAsync(OperatorStepConfirmNodeModel node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
    {
        var text = string.IsNullOrWhiteSpace(node.StepText) ? "请确认本步操作已完成。" : node.StepText;
        var title = string.IsNullOrWhiteSpace(node.Title) ? "单步确认" : node.Title;
        await WarningPromptNodeHandler.GetService(context).ConfirmStepAsync(title, text, cancellationToken).ConfigureAwait(false);
        return NodeExecutionResult.Continue(output: true);
    }
}
