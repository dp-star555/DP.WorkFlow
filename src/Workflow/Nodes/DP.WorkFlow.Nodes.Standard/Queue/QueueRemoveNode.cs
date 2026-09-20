namespace DP.WorkFlow;

/// <summary>从强类型命名队列移除首项或指定值。</summary>
[WorkflowNode("QueueRemove", DisplayName = "队列移除节点", Category = "3.Control/队列")]
public sealed class QueueRemoveNodeModel : WorkflowNodeModel
{
    /// <inheritdoc />
    public override string NodeType => "QueueRemove";
    /// <summary>队列键。</summary>
    public string QueueKey { get; set; } = string.Empty;
    /// <summary>固定值类型。</summary>
    public E_SignalValueType ValueType { get; set; }
    /// <summary>移除模式。</summary>
    public E_WorkflowQueueRemoveMode RemoveMode { get; set; }
    /// <summary>目标值来源。</summary>
    public E_SignalValueSource ValueSource { get; set; }
    /// <summary>固定目标值。</summary>
    public string LiteralValue { get; set; } = string.Empty;
    /// <summary>绑定目标值。</summary>
    public WorkflowInput<object> BindingValue { get; set; } = WorkflowInput<object>.FromLiteral(null);
    /// <summary>缺失时是否自动创建。</summary>
    public bool AutoCreateIfMissing { get; set; } = true;
    /// <summary>结果变量键。</summary>
    public string ResultVarKey { get; set; } = "QueueRemoveResult";
}

/// <summary>执行队列移除。</summary>
public sealed class QueueRemoveNodeHandler : WorkflowNodeHandler<QueueRemoveNodeModel>
{
    /// <inheritdoc />
    protected override ValueTask<NodeExecutionResult> ExecuteAsync(QueueRemoveNodeModel node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var service = WorkflowQueueNodeRuntime.GetService(context, node.QueueKey);
        object? affected;
        bool changed;
        if (node.RemoveMode == E_WorkflowQueueRemoveMode.DequeueFirst) changed = service.TryDequeueValue(node.QueueKey.Trim(), node.ValueType, node.AutoCreateIfMissing, out affected);
        else { affected = WorkflowQueueNodeRuntime.Resolve(node.ValueSource, node.LiteralValue, node.BindingValue, node.ValueType, context); changed = service.RemoveValue(node.QueueKey.Trim(), affected, node.ValueType, node.AutoCreateIfMissing); }
        var output = WorkflowQueueNodeRuntime.BuildResult(node.QueueKey.Trim(), node.ValueType, service.ReadSnapshot(node.QueueKey.Trim()), changed, affected);
        if (!string.IsNullOrWhiteSpace(node.ResultVarKey)) context.SetVariable(node.ResultVarKey.Trim(), output);
        return ValueTask.FromResult(NodeExecutionResult.Continue(output: output));
    }
}
