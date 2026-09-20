namespace DP.WorkFlow;

/// <summary>按旧版组合和保持规则等待多条 IO 条件。</summary>
[WorkflowNode("IOMultiWait", DisplayName = "等待多 IO", Category = "4.Motion/IO")]
public sealed class IoMultiWaitNodeModel : WorkflowNodeModel, IRecoverableWorkflowNode
{
    /// <inheritdoc />
    public RecoverableInterruptOption Interrupt { get; set; } = new();
    /// <inheritdoc />
    public override string NodeType => "IOMultiWait";
    /// <summary>获取或设置 All/Any 组合模式。</summary>
    public WorkflowIoMatchMode Mode { get; set; }
    /// <summary>获取或设置超时毫秒数；0 表示不超时。</summary>
    public int TimeoutMs { get; set; }
    /// <summary>获取或设置轮询间隔毫秒数。</summary>
    public int PollIntervalMs { get; set; } = 50;
    /// <summary>获取或设置瞬时/保持模式。</summary>
    public WorkflowIoPassMode PassMode { get; set; }
    /// <summary>获取或设置保持时长毫秒数。</summary>
    public int HoldMs { get; set; } = 200;
    /// <summary>获取或设置结果变量键；空值时使用节点输出。</summary>
    public string ResultVarKey { get; set; } = string.Empty;
    /// <summary>获取条件列表。</summary>
    public List<WorkflowIoExpectation> Conditions { get; } = new();
    /// <summary>获取或设置超时是否走 Timeout 分支。</summary>
    public bool TimeoutAsFalseBranch { get; set; } = true;
}

/// <summary>执行多 IO 等待节点。</summary>
public sealed class IoMultiWaitNodeHandler : WorkflowNodeHandler<IoMultiWaitNodeModel>
{
    /// <inheritdoc />
    protected override async ValueTask<NodeExecutionResult> ExecuteAsync(IoMultiWaitNodeModel node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
    {
        if (node.PollIntervalMs <= 0) throw new InvalidOperationException("PollIntervalMs must be greater than 0.");
        if (node.Conditions.Count == 0) throw new InvalidOperationException("Conditions is empty.");
        foreach (var item in node.Conditions) _ = item.ToAddress();
        var output = await IoReadNodeHandler.GetService(context).WaitManyAsync(
            node.Conditions, node.Mode, node.PassMode, node.HoldMs, node.TimeoutMs, node.PollIntervalMs, cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(node.ResultVarKey)) context.SetVariable(node.ResultVarKey.Trim(), output);
        if (!output.Matched && !node.TimeoutAsFalseBranch)
            return WorkflowRecoverableNodeFailure.Create(node, output.Message ?? "Multi IO wait timeout.");
        return NodeExecutionResult.Continue(output.Matched ? WorkflowPorts.Success : WorkflowPorts.Timeout, output);
    }
}
