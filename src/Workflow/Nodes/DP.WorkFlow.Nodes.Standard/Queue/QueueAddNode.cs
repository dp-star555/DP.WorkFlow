namespace DP.WorkFlow;

/// <summary>向强类型命名队列添加数据。</summary>
[WorkflowNode("QueueAdd", DisplayName = "队列添加节点", Category = "3.Control/队列")]
public sealed class QueueAddNodeModel : WorkflowNodeModel
{
    /// <inheritdoc />
    public override string NodeType => "QueueAdd";
    /// <summary>队列键。</summary>
    public string QueueKey { get; set; } = string.Empty;
    /// <summary>固定值类型。</summary>
    public E_SignalValueType ValueType { get; set; }
    /// <summary>值来源。</summary>
    public E_SignalValueSource ValueSource { get; set; }
    /// <summary>固定值。</summary>
    public string LiteralValue { get; set; } = string.Empty;
    /// <summary>绑定值。</summary>
    public WorkflowInput<object> BindingValue { get; set; } = WorkflowInput<object>.FromLiteral(null);
    /// <summary>是否防止重复。</summary>
    public bool PreventDuplicate { get; set; } = true;
    /// <summary>缺失时是否自动创建。</summary>
    public bool AutoCreateIfMissing { get; set; } = true;
    /// <summary>结果变量键。</summary>
    public string ResultVarKey { get; set; } = "QueueAddResult";
}

/// <summary>执行队列添加。</summary>
public sealed class QueueAddNodeHandler : WorkflowNodeHandler<QueueAddNodeModel>
{
    /// <inheritdoc />
    protected override ValueTask<NodeExecutionResult> ExecuteAsync(QueueAddNodeModel node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var service = WorkflowQueueNodeRuntime.GetService(context, node.QueueKey);
        var value = WorkflowQueueNodeRuntime.Resolve(node.ValueSource, node.LiteralValue, node.BindingValue, node.ValueType, context);
        var changed = service.EnqueueValue(node.QueueKey.Trim(), value, node.ValueType, node.PreventDuplicate, node.AutoCreateIfMissing);
        var output = WorkflowQueueNodeRuntime.BuildResult(node.QueueKey.Trim(), node.ValueType, service.ReadSnapshot(node.QueueKey.Trim()), changed, value);
        if (!string.IsNullOrWhiteSpace(node.ResultVarKey)) context.SetVariable(node.ResultVarKey.Trim(), output);
        return ValueTask.FromResult(NodeExecutionResult.Continue(output: output));
    }
}
