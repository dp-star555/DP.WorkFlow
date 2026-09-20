namespace DP.WorkFlow;

/// <summary>告警处理出口：请求从故障节点重新执行。</summary>
[WorkflowNode("WarnRetryCurrentNode", DisplayName = "重试当前节点", Category = "8.Process/异常恢复")]
public sealed class RetryCurrentNodeModel : WorkflowNodeModel
{
    /// <inheritdoc />
    public override string NodeType => "WarnRetryCurrentNode";
    /// <summary>恢复裁决备注。</summary>
    public string Note { get; set; } = string.Empty;
}

/// <summary>生成重试当前节点裁决并结束告警处理路径。</summary>
public sealed class RetryCurrentNodeHandler : WorkflowNodeHandler<RetryCurrentNodeModel>
{
    /// <inheritdoc />
    protected override ValueTask<NodeExecutionResult> ExecuteAsync(RetryCurrentNodeModel node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var output = new WarningResolution { ExitAction = E_WarningExitAction.RetryCurrentNode, Note = node.Note };
        context.SetVariable(WorkflowRecoveryRuntimeKeys.WarningResolution, output);
        context.Trace("创建恢复决议", "创建重试当前节点恢复决议。", new Dictionary<string, object?> { ["Note"] = node.Note });
        return ValueTask.FromResult(NodeExecutionResult.Complete(output));
    }
}
