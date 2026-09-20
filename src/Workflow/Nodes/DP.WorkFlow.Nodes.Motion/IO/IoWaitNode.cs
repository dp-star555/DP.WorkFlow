namespace DP.WorkFlow;

/// <summary>等待指定 DriveId/Index IO 达到期望值。</summary>
[WorkflowNode("IOWait", DisplayName = "等待 IO 等于", Category = "4.Motion/IO")]
public sealed class IoWaitNodeModel : WorkflowNodeModel, IRecoverableWorkflowNode
{
    /// <inheritdoc />
    public RecoverableInterruptOption Interrupt { get; set; } = new();
    /// <inheritdoc />
    public override string NodeType => "IOWait";
    /// <summary>获取或设置 IO 类型。</summary>
    public WorkflowIoPointType IOType { get; set; } = WorkflowIoPointType.Input;
    /// <summary>获取或设置驱动 ID。</summary>
    public string DriveId { get; set; } = string.Empty;
    /// <summary>获取或设置字符串索引。</summary>
    public string Index { get; set; } = "0";
    /// <summary>获取或设置期望值。</summary>
    public bool ExpectedValue { get; set; } = true;
    /// <summary>获取或设置超时毫秒数；0 表示不超时。</summary>
    public int TimeoutMs { get; set; }
    /// <summary>获取或设置瞬时/保持判定模式。</summary>
    public WorkflowIoPassMode PassMode { get; set; }
    /// <summary>获取或设置保持时长毫秒数。</summary>
    public int HoldMs { get; set; } = 200;
    /// <summary>获取或设置轮询间隔毫秒数。</summary>
    public int PollIntervalMs { get; set; } = 50;
    /// <summary>获取或设置超时是否走 Timeout 分支。</summary>
    public bool TimeoutAsFalseBranch { get; set; } = true;
}

/// <summary>执行 IO 等待节点。</summary>
public sealed class IoWaitNodeHandler : WorkflowNodeHandler<IoWaitNodeModel>
{
    /// <inheritdoc />
    protected override async ValueTask<NodeExecutionResult> ExecuteAsync(IoWaitNodeModel node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
    {
        if (node.PollIntervalMs <= 0) throw new InvalidOperationException("PollIntervalMs must be greater than 0.");
        var expectation = new WorkflowIoExpectation { DriveId = node.DriveId, Index = node.Index, IOType = node.IOType, ExpectedValue = node.ExpectedValue };
        _ = expectation.ToAddress();
        var output = await IoReadNodeHandler.GetService(context).WaitAsync(
            expectation, node.PassMode, node.HoldMs, node.TimeoutMs, node.PollIntervalMs, cancellationToken).ConfigureAwait(false);
        if (!output.Success && !node.TimeoutAsFalseBranch)
            return WorkflowRecoverableNodeFailure.Create(node, output.Message ?? $"等待 IO 超时：{node.DriveId}[{node.Index}] -> {node.ExpectedValue}");
        var port = output.Success ? WorkflowPorts.Success : WorkflowPorts.Timeout;
        return NodeExecutionResult.Continue(port, output);
    }
}
