namespace DP.WorkFlow;

/// <summary>按旧版命令集写入数字输出。</summary>
[WorkflowNode("IOWrite", DisplayName = "写 IO", Category = "4.Motion/IO")]
public sealed class IoWriteNodeModel : WorkflowNodeModel, IRecoverableWorkflowNode
{
    /// <inheritdoc />
    public RecoverableInterruptOption Interrupt { get; set; } = new();
    /// <inheritdoc />
    public override string NodeType => "IOWrite";
    /// <summary>获取或设置驱动 ID。</summary>
    public string DriveId { get; set; } = string.Empty;
    /// <summary>获取或设置兼容旧 JSON 的字符串索引。</summary>
    public string Index { get; set; } = "0";
    /// <summary>获取或设置写入命令。</summary>
    public WorkflowIoWriteCommand Command { get; set; } = WorkflowIoWriteCommand.Toggle;
    /// <summary>获取或设置是否等待输出达到目标值。</summary>
    public bool WaitForSignal { get; set; }
    /// <summary>获取或设置等待超时毫秒数。</summary>
    public int TimeoutMs { get; set; } = 2000;
    /// <summary>获取或设置 Pulse/Delay 动作延时毫秒数。</summary>
    public int ActionDelayMs { get; set; } = 100;
    /// <summary>获取或设置 SetByValue 命令使用的固定值或绑定值。</summary>
    public WorkflowInput<bool> Value { get; set; } = WorkflowInput<bool>.FromLiteral(true);
}

/// <summary>执行 IO 写入节点。</summary>
public sealed class IoWriteNodeHandler : WorkflowNodeHandler<IoWriteNodeModel>
{
    /// <inheritdoc />
    protected override async ValueTask<NodeExecutionResult> ExecuteAsync(IoWriteNodeModel node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(node.DriveId)) throw new InvalidOperationException("DriveId 为空。");
        if (!int.TryParse(node.Index, out var index) || index < 0) throw new InvalidOperationException($"Invalid IO Index: {node.Index}");
        if (node.TimeoutMs < 0 || node.ActionDelayMs < 0) throw new InvalidOperationException("TimeoutMs/ActionDelayMs 不能小于 0。");
        var value = context.ResolveInput(node.Value);
        var request = new WorkflowIoWriteRequest(
            new WorkflowIoAddress(node.DriveId.Trim(), index, WorkflowIoPointType.Output),
            node.Command, value, node.WaitForSignal, node.TimeoutMs, node.ActionDelayMs);
        var output = await IoReadNodeHandler.GetService(context).WriteAsync(request, cancellationToken).ConfigureAwait(false);
        if (!output.Success) return WorkflowRecoverableNodeFailure.Create(node, output.Message ?? "IO write failed.");
        return NodeExecutionResult.Continue(output: output);
    }
}
