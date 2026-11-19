namespace DP.WorkFlow;

/// <summary>批量创建或清空强类型命名队列。</summary>
[WorkflowNode("QueueInitialize", DisplayName = "队列初始化节点", Category = "3.Control/队列")]
public sealed class QueueInitializeNodeModel : WorkflowNodeModel, IWorkflowPublicDataUser
{
    /// <inheritdoc />
    public override string NodeType => "QueueInitialize";
    /// <summary>初始化项。</summary>
    public List<QueueInitializeItem> Items { get; set; } = new();
    /// <inheritdoc />
    public IEnumerable<WorkflowPublicDataDeclaration> GetPublicDataUses() => Items.Where(item => item is not null).SelectMany(item => PublicDataUses.Queue(item.QueueKey, item.ValueType));
}

/// <summary>执行批量队列初始化。</summary>
public sealed class QueueInitializeNodeHandler : WorkflowNodeHandler<QueueInitializeNodeModel>
{
    /// <inheritdoc />
    protected override ValueTask<NodeExecutionResult> ExecuteAsync(QueueInitializeNodeModel node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
    {
        var service = context.GetRequiredCapability<IWorkflowQueueService>();
        var created = 0; var cleared = 0;
        foreach (var item in node.Items.Where(item => item is not null))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(item.QueueKey)) throw new InvalidOperationException("初始化项 QueueKey 为空。");
            if (service.EnsureExists(item.QueueKey.Trim(), item.ValueType)) created++;
            if (service.Clear(item.QueueKey.Trim(), item.ValueType, false)) cleared++;
        }
        var output = new QueueInitializeResult(node.Items.Count, created, cleared, string.Join(",", node.Items.Select(item => item.QueueKey).Distinct(StringComparer.Ordinal)));
        return Polyfill.FromResult(NodeExecutionResult.Continue(output: output));
    }
}
