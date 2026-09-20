namespace DP.WorkFlow;

/// <summary>读取强类型命名队列快照而不修改内容。</summary>
[WorkflowNode("QueueRead", DisplayName = "队列读取节点", Category = "3.Control/队列")]
public sealed class QueueReadNodeModel : WorkflowNodeModel
{
    /// <inheritdoc />
    public override string NodeType => "QueueRead";
    /// <summary>队列键。</summary>
    public string QueueKey { get; set; } = string.Empty;
    /// <summary>固定值类型。</summary>
    public E_SignalValueType ValueType { get; set; }
    /// <summary>缺失时是否自动创建。</summary>
    public bool AutoCreateIfMissing { get; set; } = true;
    /// <summary>结果变量键。</summary>
    public string ResultVarKey { get; set; } = "QueueReadResult";
}

/// <summary>执行队列读取。</summary>
public sealed class QueueReadNodeHandler : WorkflowNodeHandler<QueueReadNodeModel>
{
    /// <inheritdoc />
    protected override ValueTask<NodeExecutionResult> ExecuteAsync(QueueReadNodeModel node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var service = WorkflowQueueNodeRuntime.GetService(context, node.QueueKey);
        if (node.AutoCreateIfMissing) service.EnsureExists(node.QueueKey.Trim(), node.ValueType);
        var output = WorkflowQueueNodeRuntime.BuildResult(node.QueueKey.Trim(), node.ValueType, service.ReadSnapshot(node.QueueKey.Trim()), false, null);
        if (!string.IsNullOrWhiteSpace(node.ResultVarKey)) context.SetVariable(node.ResultVarKey.Trim(), output);
        return ValueTask.FromResult(NodeExecutionResult.Continue(output: output));
    }
}
