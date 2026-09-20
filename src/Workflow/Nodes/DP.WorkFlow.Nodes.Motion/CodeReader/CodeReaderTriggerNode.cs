namespace DP.WorkFlow;

/// <summary>触发扫码设备采集。</summary>
[WorkflowNode("CodeReaderTrigger", DisplayName = "触发扫码枪", Category = "4.Motion/CodeReader")]
public sealed class CodeReaderTriggerNodeModel : WorkflowNodeModel, IRecoverableWorkflowNode
{
    /// <inheritdoc />
    public RecoverableInterruptOption Interrupt { get; set; } = new();
    /// <inheritdoc />
    public override string NodeType => "CodeReaderTrigger";
    /// <summary>获取或设置设备键。</summary>
    public string ReaderKey { get; set; } = string.Empty;
    /// <summary>获取或设置设备关闭时是否自动打开。</summary>
    public bool AutoOpenWhenClosed { get; set; } = true;
}

/// <summary>执行触发扫码节点。</summary>
public sealed class CodeReaderTriggerNodeHandler : WorkflowNodeHandler<CodeReaderTriggerNodeModel>
{
    /// <inheritdoc />
    protected override async ValueTask<NodeExecutionResult> ExecuteAsync(CodeReaderTriggerNodeModel node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
    {
        var service = CodeReaderOpenNodeHandler.GetService(context, node.ReaderKey);
        var success = await service.TriggerAsync(node.ReaderKey.Trim(), node.AutoOpenWhenClosed, cancellationToken).ConfigureAwait(false);
        if (!success) return WorkflowRecoverableNodeFailure.Create(node, "触发扫码枪失败: " + node.ReaderKey.Trim());
        return NodeExecutionResult.Continue(output: node.ReaderKey.Trim());
    }
}
