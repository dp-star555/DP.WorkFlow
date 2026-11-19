namespace DP.WorkFlow;

/// <summary>向强类型命名队列添加数据。</summary>
[WorkflowNode("QueueAdd", DisplayName = "队列添加节点", Category = "3.Control/队列")]
public sealed class QueueAddNodeModel : WorkflowNodeModel, IWorkflowPublicDataUser
{
    /// <inheritdoc />
    public override string NodeType => "QueueAdd";
    /// <summary>队列键。</summary>
    [WorkflowPropertyEditor(WorkflowPropertyEditorKeys.PublicDataQueue)]
    public string QueueKey { get; set; } = string.Empty;
    /// <summary>固定值类型。</summary>
    public E_SignalValueType ValueType { get; set; }
    /// <summary>入队的值。固定值按 <see cref="ValueType"/> 解析；绑定时来源值先转为文本再按类型转换。</summary>
    public WorkflowInput<string> Value { get; set; } = WorkflowInput<string>.FromLiteral(string.Empty);
    /// <summary>是否防止重复。</summary>
    public bool PreventDuplicate { get; set; } = true;
    /// <summary>缺失时是否自动创建。</summary>
    public bool AutoCreateIfMissing { get; set; } = true;
    /// <inheritdoc />
    public IEnumerable<WorkflowPublicDataDeclaration> GetPublicDataUses() => PublicDataUses.Queue(QueueKey, ValueType);
}

/// <summary>执行队列添加。</summary>
public sealed class QueueAddNodeHandler : WorkflowNodeHandler<QueueAddNodeModel>
{
    /// <inheritdoc />
    protected override ValueTask<NodeExecutionResult> ExecuteAsync(QueueAddNodeModel node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var service = WorkflowQueueNodeRuntime.GetService(context, node.QueueKey);
        var value = WorkflowQueueNodeRuntime.Resolve(node.Value, node.ValueType, context);
        var changed = service.EnqueueValue(node.QueueKey.Trim(), value, node.ValueType, node.PreventDuplicate, node.AutoCreateIfMissing);
        var output = WorkflowQueueNodeRuntime.BuildResult(node.QueueKey.Trim(), node.ValueType, service.ReadSnapshot(node.QueueKey.Trim()), changed, value);
        return Polyfill.FromResult(NodeExecutionResult.Continue(output: output));
    }
}
