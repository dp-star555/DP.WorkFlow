namespace DP.WorkFlow;

/// <summary>等待当前工作流上下文中的全部一次性信号完成。</summary>
[WorkflowNode("WaitSignals", DisplayName = "信号等待节点", Category = "3.Control/信号")]
public sealed class WaitSignalsNodeModel : WorkflowNodeModel
{
    /// <inheritdoc />
    public override string NodeType => "WaitSignals";
    /// <summary>待等待信号键。</summary>
    public List<string> WaitSignalKeys { get; set; } = new();
    /// <summary>超时毫秒数；0 表示不超时。</summary>
    public int WaitTimeoutMs { get; set; }
}

/// <summary>执行工作流上下文多信号等待。</summary>
public sealed class WaitSignalsNodeHandler : WorkflowNodeHandler<WaitSignalsNodeModel>
{
    /// <inheritdoc />
    protected override async ValueTask<NodeExecutionResult> ExecuteAsync(WaitSignalsNodeModel node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
    {
        var keys = node.WaitSignalKeys.Select(key => key?.Trim()).Where(key => !string.IsNullOrWhiteSpace(key)).Cast<string>().Distinct(StringComparer.Ordinal).ToArray();
        if (keys.Length == 0) throw new InvalidOperationException("至少需要配置一个等待信号。");
        TimeSpan? timeout = node.WaitTimeoutMs > 0 ? TimeSpan.FromMilliseconds(node.WaitTimeoutMs) : null;
        var success = await context.WaitAllWorkflowSignalsAsync(keys, timeout, cancellationToken).ConfigureAwait(false);
        return NodeExecutionResult.Continue(success ? WorkflowPorts.Success : WorkflowPorts.Timeout, success);
    }
}
