namespace DP.WorkFlow;

/// <summary>等待强类型数据信号满足比较条件。</summary>
[WorkflowNode("SignalValueWait", DisplayName = "数据信号等待节点", Category = "3.Control/信号")]
public sealed class SignalValueWaitNodeModel : WorkflowNodeModel
{
    /// <inheritdoc />
    public override string NodeType => "SignalValueWait";
    /// <summary>信号键。</summary>
    public string SignalKey { get; set; } = string.Empty;
    /// <summary>值类型。</summary>
    public E_SignalValueType ValueType { get; set; }
    /// <summary>等待模式。</summary>
    public E_SignalValueWaitMode WaitMode { get; set; }
    /// <summary>比较操作。</summary>
    public E_SignalWaitOperator Operator { get; set; }
    /// <summary>比较值来源。</summary>
    public E_SignalValueSource CompareValueSource { get; set; }
    /// <summary>固定比较值。</summary>
    public string CompareLiteralValue { get; set; } = string.Empty;
    /// <summary>绑定比较值。</summary>
    public WorkflowInput<object> CompareBinding { get; set; } = WorkflowInput<object>.FromLiteral(null);
    /// <summary>超时毫秒数；0 表示不超时。</summary>
    public int TimeoutMs { get; set; } = 5000;
    /// <summary>结果变量键。</summary>
    public string ResultVarKey { get; set; } = "SignalValueWaitResult";
}

/// <summary>执行数据信号等待。</summary>
public sealed class SignalValueWaitNodeHandler : WorkflowNodeHandler<SignalValueWaitNodeModel>
{
    /// <inheritdoc />
    protected override async ValueTask<NodeExecutionResult> ExecuteAsync(SignalValueWaitNodeModel node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
    {
        var service = SignalValueSetNodeHandler.GetService(context, node.SignalKey);
        var compare = SignalValueSetNodeHandler.ConvertValue(node.CompareValueSource == E_SignalValueSource.Binding ? context.ResolveInput(node.CompareBinding) : node.CompareLiteralValue, node.ValueType);
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
        if (!string.IsNullOrWhiteSpace(node.ResultVarKey)) context.SetVariable(node.ResultVarKey.Trim(), output);
        return NodeExecutionResult.Continue(matched is not null ? WorkflowPorts.Success : WorkflowPorts.Timeout, output);
    }
}
