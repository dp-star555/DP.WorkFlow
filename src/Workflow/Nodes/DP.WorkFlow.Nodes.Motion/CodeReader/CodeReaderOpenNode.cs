namespace DP.WorkFlow;

/// <summary>打开扫码设备连接。</summary>
[WorkflowNode("CodeReaderOpen", DisplayName = "打开扫码枪", Category = "4.Motion/CodeReader")]
public sealed class CodeReaderOpenNodeModel : WorkflowNodeModel, IRecoverableWorkflowNode
{
    /// <inheritdoc />
    public RecoverableInterruptOption Interrupt { get; set; } = new();
    /// <inheritdoc />
    public override string NodeType => "CodeReaderOpen";
    /// <summary>获取或设置设备键。</summary>
    public string ReaderKey { get; set; } = string.Empty;
}

/// <summary>执行打开扫码设备节点。</summary>
public sealed class CodeReaderOpenNodeHandler : WorkflowNodeHandler<CodeReaderOpenNodeModel>
{
    /// <inheritdoc />
    protected override async ValueTask<NodeExecutionResult> ExecuteAsync(CodeReaderOpenNodeModel node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
    {
        var service = GetService(context, node.ReaderKey);
        var success = await service.OpenAsync(node.ReaderKey.Trim(), cancellationToken).ConfigureAwait(false);
        if (!success) return WorkflowRecoverableNodeFailure.Create(node, "打开扫码枪失败: " + node.ReaderKey.Trim());
        return NodeExecutionResult.Continue(output: node.ReaderKey.Trim());
    }

    internal static IWorkflowCodeReaderService GetService(IWorkflowNodeExecutionContext context, string key)
    {
        if (string.IsNullOrWhiteSpace(key)) throw new InvalidOperationException("扫码节点没有配置 ReaderKey。");
        return context.GetRequiredCapability<IWorkflowCodeReaderService>();
    }
}
