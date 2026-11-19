namespace DP.WorkFlow;

/// <summary>等待强类型数据信号满足比较条件。</summary>
[WorkflowNode("SignalValueWait", DisplayName = "数据信号等待节点", Category = "3.Control/信号")]
public sealed class SignalValueWaitNodeModel : WorkflowNodeModel, IWorkflowPublicDataUser
{
    /// <inheritdoc />
    public override string NodeType => "SignalValueWait";
    /// <summary>信号键。</summary>
    [WorkflowPropertyEditor(WorkflowPropertyEditorKeys.PublicDataValue)]
    public string SignalKey { get; set; } = string.Empty;
    /// <summary>值类型。</summary>
    public E_SignalValueType ValueType { get; set; }
    /// <summary>等待模式。</summary>
    public E_SignalValueWaitMode WaitMode { get; set; }
    /// <summary>比较操作。</summary>
    public E_SignalWaitOperator Operator { get; set; }
    /// <summary>比较值。固定值按 <see cref="ValueType"/> 解析；绑定时来源值先转为文本再按类型转换。</summary>
    public WorkflowInput<string> CompareValue { get; set; } = WorkflowInput<string>.FromLiteral(string.Empty);
    /// <summary>超时毫秒数；0 表示不超时。</summary>
    public int TimeoutMs { get; set; } = 5000;
    /// <inheritdoc />
    public IEnumerable<WorkflowPublicDataDeclaration> GetPublicDataUses() => PublicDataUses.Value(SignalKey, ValueType);
}

/// <summary>执行数据信号等待。</summary>
public sealed class SignalValueWaitNodeHandler : WorkflowNodeHandler<SignalValueWaitNodeModel>
{
    /// <inheritdoc />
    protected override async ValueTask<NodeExecutionResult> ExecuteAsync(SignalValueWaitNodeModel node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
    {
        var service = SignalValueSetNodeHandler.GetService(context, node.SignalKey);
        var compare = SignalValueSetNodeHandler.ConvertValue(context.ResolveInput(node.CompareValue), node.ValueType);
        var current = service.ReadSnapshot(node.SignalKey.Trim());
        WorkflowValueSignalSnapshot? matched = null;
        if (node.WaitMode == E_SignalValueWaitMode.ExistingOrNextWrite && current?.HasValue == true && SignalValueSetNodeHandler.Compare(current.Value, compare, node.ValueType, node.Operator)) matched = current;
        if (matched is null)
        {
            TimeSpan? timeout = node.TimeoutMs > 0 ? TimeSpan.FromMilliseconds(node.TimeoutMs) : null;
            matched = await service.WaitValueAsync(node.SignalKey.Trim(), current?.Version ?? 0, value => SignalValueSetNodeHandler.Compare(value, compare, node.ValueType, node.Operator), timeout, cancellationToken).ConfigureAwait(false);
        }
        var snapshot = matched ?? current ?? new WorkflowValueSignalSnapshot(node.SignalKey.Trim(), node.ValueType switch { E_SignalValueType.String => string.Empty, E_SignalValueType.Int32 => 0, E_SignalValueType.Int64 => 0L, E_SignalValueType.Double => 0d, E_SignalValueType.Boolean => false, _ => string.Empty }, 0, false);
        var output = SignalValueSetNodeHandler.BuildResult(snapshot, node.ValueType, E_SignalValueWriteMode.Set, matched is not null);
        return NodeExecutionResult.Continue(matched is not null ? WorkflowPorts.Success : WorkflowPorts.Timeout, output);
    }
}
