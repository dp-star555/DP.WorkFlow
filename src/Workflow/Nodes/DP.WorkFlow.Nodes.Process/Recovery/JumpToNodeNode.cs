namespace DP.WorkFlow;

/// <summary>旧版跳转出口，仅用于保留旧配置；引擎拒绝该裁决，请改用命名恢复入口。</summary>
[WorkflowNode("WarnJumpToNode", DisplayName = "旧跳转（已禁用）", Category = "8.Process/异常恢复")]
public sealed class JumpToNodeNodeModel : WorkflowNodeModel
{
    /// <inheritdoc />
    public override string NodeType => "WarnJumpToNode";
    /// <summary>恢复后推进的目标节点 ID。</summary>
    public string TargetNodeId { get; set; } = string.Empty;
    /// <summary>恢复裁决备注。</summary>
    public string Note { get; set; } = string.Empty;
}

/// <summary>生成跳转节点裁决并结束告警处理路径。</summary>
public sealed class JumpToNodeNodeHandler : WorkflowNodeHandler<JumpToNodeNodeModel>
{
    /// <inheritdoc />
    protected override ValueTask<NodeExecutionResult> ExecuteAsync(JumpToNodeNodeModel node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var output = new WarningResolution { ExitAction = E_WarningExitAction.JumpToNode, TargetNodeId = string.IsNullOrWhiteSpace(node.TargetNodeId) ? null : node.TargetNodeId.Trim(), Note = node.Note };
        context.SetVariable(WorkflowRecoveryRuntimeKeys.WarningResolution, output);
        context.Trace("创建恢复决议", "创建跳转节点恢复决议。", new Dictionary<string, object?> { ["TargetNodeId"] = output.TargetNodeId, ["Note"] = node.Note });
        return ValueTask.FromResult(NodeExecutionResult.Complete(output));
    }
}
