namespace DP.WorkFlow;

/// <summary>按旧版触发与匹配规则等待扫码结果。</summary>
[WorkflowNode("CodeReaderWaitScan", DisplayName = "等待扫码结果", Category = "4.Motion/CodeReader")]
public sealed class CodeReaderWaitScanNodeModel : WorkflowNodeModel, IRecoverableWorkflowNode
{
    /// <inheritdoc />
    public RecoverableInterruptOption Interrupt { get; set; } = new();
    /// <inheritdoc />
    public override string NodeType => "CodeReaderWaitScan";
    /// <summary>获取或设置设备键。</summary>
    public string ReaderKey { get; set; } = string.Empty;
    /// <summary>获取或设置等待超时毫秒数。</summary>
    public int TimeoutMs { get; set; } = 5000;
    /// <summary>获取或设置等待前是否触发。</summary>
    public bool TriggerBeforeWait { get; set; }
    /// <summary>获取或设置设备关闭时是否自动打开。</summary>
    public bool AutoOpenWhenClosed { get; set; } = true;
    /// <summary>获取或设置扫码匹配模式。</summary>
    public CodeReaderScanMatchMode MatchMode { get; set; }
    /// <summary>获取或设置期望码值。</summary>
    public string ExpectedCode { get; set; } = string.Empty;
    /// <summary>获取或设置结果变量键。</summary>
    public string ResultVarKey { get; set; } = "CodeReaderWaitScan.Result";
    /// <summary>获取或设置超时是否走 Timeout 分支。</summary>
    public bool TimeoutAsFalseBranch { get; set; } = true;
}

/// <summary>执行等待扫码结果节点。</summary>
public sealed class CodeReaderWaitScanNodeHandler : WorkflowNodeHandler<CodeReaderWaitScanNodeModel>
{
    /// <inheritdoc />
    protected override async ValueTask<NodeExecutionResult> ExecuteAsync(CodeReaderWaitScanNodeModel node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
    {
        var service = CodeReaderOpenNodeHandler.GetService(context, node.ReaderKey);
        var request = new CodeReaderWaitRequest(node.ReaderKey.Trim(), node.TimeoutMs, node.TriggerBeforeWait, node.AutoOpenWhenClosed, node.MatchMode, node.ExpectedCode);
        var output = await service.WaitScanAsync(request, cancellationToken).ConfigureAwait(false);
        if (output is null)
        {
            if (!node.TimeoutAsFalseBranch)
                return WorkflowRecoverableNodeFailure.Create(node, "等待扫码结果超时: " + node.ReaderKey.Trim());
            return NodeExecutionResult.Continue(WorkflowPorts.Timeout);
        }
        if (!string.IsNullOrWhiteSpace(node.ResultVarKey)) context.SetVariable(node.ResultVarKey.Trim(), output);
        return NodeExecutionResult.Continue(WorkflowPorts.Success, output);
    }
}
