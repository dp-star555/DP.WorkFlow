using System.Globalization;

namespace DP.WorkFlow;

/// <summary>等待命名队列满足有数据、首项比较或数量条件。</summary>
[WorkflowNode("QueueWait", DisplayName = "队列等待节点", Category = "3.Control/队列")]
public sealed class QueueWaitNodeModel : WorkflowNodeModel, IWorkflowPublicDataUser
{
    /// <inheritdoc />
    public override string NodeType => "QueueWait";
    /// <summary>队列键。</summary>
    [WorkflowPropertyEditor(WorkflowPropertyEditorKeys.PublicDataQueue)]
    public string QueueKey { get; set; } = string.Empty;
    /// <summary>固定值类型。</summary>
    public E_SignalValueType ValueType { get; set; }
    /// <summary>等待条件。</summary>
    public E_WorkflowQueueWaitCondition Condition { get; set; }
    /// <summary>首项比较的目标值。固定值按 <see cref="ValueType"/> 解析；绑定时来源值先转为文本再按类型转换。</summary>
    public WorkflowInput<string> Value { get; set; } = WorkflowInput<string>.FromLiteral(string.Empty);
    /// <summary>缺失时是否自动创建。</summary>
    public bool AutoCreateIfMissing { get; set; } = true;
    /// <summary>数量阈值。</summary>
    public int CountThreshold { get; set; }
    /// <summary>超时毫秒数；0 表示不超时。</summary>
    public int TimeoutMs { get; set; } = 5000;
    /// <inheritdoc />
    public IEnumerable<WorkflowPublicDataDeclaration> GetPublicDataUses() => PublicDataUses.Queue(QueueKey, ValueType);
}

/// <summary>执行队列条件等待。</summary>
public sealed class QueueWaitNodeHandler : WorkflowNodeHandler<QueueWaitNodeModel>
{
    /// <inheritdoc />
    protected override async ValueTask<NodeExecutionResult> ExecuteAsync(QueueWaitNodeModel node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
    {
        var service = WorkflowQueueNodeRuntime.GetService(context, node.QueueKey);
        object? expected = node.Condition is E_WorkflowQueueWaitCondition.FirstEquals or E_WorkflowQueueWaitCondition.FirstNotEquals ? WorkflowQueueNodeRuntime.Resolve(node.Value, node.ValueType, context) : null;
        bool Match(WorkflowQueueSnapshot snapshot) => node.Condition switch { E_WorkflowQueueWaitCondition.HasData => snapshot.Count > 0, E_WorkflowQueueWaitCondition.FirstEquals => snapshot.HasFirstValue && Equals(snapshot.FirstValue, expected), E_WorkflowQueueWaitCondition.FirstNotEquals => snapshot.HasFirstValue && !Equals(snapshot.FirstValue, expected), E_WorkflowQueueWaitCondition.CountGreaterThan => snapshot.Count > node.CountThreshold, _ => false };
        TimeSpan? timeout = node.TimeoutMs > 0 ? TimeSpan.FromMilliseconds(node.TimeoutMs) : null;
        using var wait = context.BeginWait($"队列 {node.QueueKey.Trim()} {node.Condition}", timeout);
        var snapshot = await service.WaitAsync(node.QueueKey.Trim(), node.ValueType, Match, timeout, node.AutoCreateIfMissing, cancellationToken).ConfigureAwait(false);
        wait.End(snapshot is not null);
        var first = snapshot?.FirstValue;
        static T ConvertOr<T>(object? value, T fallback, Func<object, T> convert)
        {
            try { return value is null ? fallback : convert(value); }
            catch (Exception exception) when (exception is FormatException or InvalidCastException or OverflowException) { return fallback; }
        }
        var output = new QueueWaitNodeResult(node.QueueKey.Trim(), snapshot?.ValueType ?? node.ValueType, node.Condition, snapshot is not null, snapshot?.Count ?? 0, snapshot?.Version ?? 0, snapshot?.HasFirstValue == true, Convert.ToString(first, CultureInfo.InvariantCulture) ?? string.Empty, ConvertOr(first, 0, Convert.ToInt32), ConvertOr(first, 0L, Convert.ToInt64), ConvertOr(first, 0d, Convert.ToDouble), ConvertOr(first, false, Convert.ToBoolean));
        return NodeExecutionResult.Continue(snapshot is not null ? WorkflowPorts.Success : WorkflowPorts.Timeout, output);
    }
}
