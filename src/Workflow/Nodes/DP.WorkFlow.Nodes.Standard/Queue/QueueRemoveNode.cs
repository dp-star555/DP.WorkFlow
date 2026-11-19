namespace DP.WorkFlow;

/// <summary>从强类型命名队列移除首项或指定值。</summary>
[WorkflowNode("QueueRemove", DisplayName = "队列移除节点", Category = "3.Control/队列")]
public sealed class QueueRemoveNodeModel : WorkflowNodeModel, IWorkflowPublicDataUser
{
    /// <inheritdoc />
    public override string NodeType => "QueueRemove";
    /// <summary>队列键。</summary>
    [WorkflowPropertyEditor(WorkflowPropertyEditorKeys.PublicDataQueue)]
    public string QueueKey { get; set; } = string.Empty;
    /// <summary>固定值类型。</summary>
    public E_SignalValueType ValueType { get; set; }
    /// <summary>移除模式。</summary>
    public E_WorkflowQueueRemoveMode RemoveMode { get; set; }
    /// <summary>按值移除时要移除的值。固定值按 <see cref="ValueType"/> 解析；绑定时来源值先转为文本再按类型转换。</summary>
    public WorkflowInput<string> Value { get; set; } = WorkflowInput<string>.FromLiteral(string.Empty);
    /// <summary>缺失时是否自动创建。</summary>
    public bool AutoCreateIfMissing { get; set; } = true;
    /// <inheritdoc />
    public IEnumerable<WorkflowPublicDataDeclaration> GetPublicDataUses() => PublicDataUses.Queue(QueueKey, ValueType);
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
        else { affected = WorkflowQueueNodeRuntime.Resolve(node.Value, node.ValueType, context); changed = service.RemoveValue(node.QueueKey.Trim(), affected, node.ValueType, node.AutoCreateIfMissing); }
        var output = WorkflowQueueNodeRuntime.BuildResult(node.QueueKey.Trim(), node.ValueType, service.ReadSnapshot(node.QueueKey.Trim()), changed, affected);
        return Polyfill.FromResult(NodeExecutionResult.Continue(output: output));
    }
}
