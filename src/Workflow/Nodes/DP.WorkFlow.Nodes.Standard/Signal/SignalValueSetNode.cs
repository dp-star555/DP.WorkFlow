using System.Globalization;

namespace DP.WorkFlow;

/// <summary>写入强类型数据信号，支持直接写入、自加和自减。</summary>
[WorkflowNode("SignalValueSet", DisplayName = "数据信号设置节点", Category = "3.Control/信号")]
public sealed class SignalValueSetNodeModel : WorkflowNodeModel, IWorkflowPublicDataUser
{
    /// <inheritdoc />
    public override string NodeType => "SignalValueSet";
    /// <summary>信号键。</summary>
    [WorkflowPropertyEditor(WorkflowPropertyEditorKeys.PublicDataValue)]
    public string SignalKey { get; set; } = string.Empty;
    /// <summary>值类型。</summary>
    public E_SignalValueType ValueType { get; set; }
    /// <summary>写入模式。</summary>
    public E_SignalValueWriteMode WriteMode { get; set; }
    /// <summary>写入值或加减量。固定值按 <see cref="ValueType"/> 解析；绑定时来源值先转为文本再按类型转换。</summary>
    public WorkflowInput<string> Value { get; set; } = WorkflowInput<string>.FromLiteral(string.Empty);
    /// <inheritdoc />
    public IEnumerable<WorkflowPublicDataDeclaration> GetPublicDataUses() => PublicDataUses.Value(SignalKey, ValueType);
}

/// <summary>执行数据信号写入。</summary>
public sealed class SignalValueSetNodeHandler : WorkflowNodeHandler<SignalValueSetNodeModel>
{
    /// <inheritdoc />
    protected override ValueTask<NodeExecutionResult> ExecuteAsync(SignalValueSetNodeModel node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var service = GetService(context, node.SignalKey);
        var operand = ConvertValue(context.ResolveInput(node.Value), node.ValueType);
        var value = node.WriteMode == E_SignalValueWriteMode.Set ? operand : Calculate(service.ReadSnapshot(node.SignalKey.Trim())?.Value, operand, node.ValueType, node.WriteMode);
        var snapshot = service.WriteValue(node.SignalKey.Trim(), value);
        var output = BuildResult(snapshot, node.ValueType, node.WriteMode, true);
        return Polyfill.FromResult(NodeExecutionResult.Continue(output: output));
    }
    internal static IWorkflowValueSignalService GetService(IWorkflowNodeExecutionContext context, string key)
    {
        if (string.IsNullOrWhiteSpace(key)) throw new InvalidOperationException("SignalKey 为空。");
        return context.GetRequiredCapability<IWorkflowValueSignalService>();
    }
    internal static object ConvertValue(object? value, E_SignalValueType type) => WorkflowPublicDataValues.Convert(value, type);
    internal static bool Compare(object? left, object right, E_SignalValueType type, E_SignalWaitOperator op)
    {
        var l = ConvertValue(left, type);
        var order = type switch
        {
            E_SignalValueType.String => string.CompareOrdinal((string)l, (string)right),
            E_SignalValueType.Int32 => ((int)l).CompareTo((int)right),
            E_SignalValueType.Int64 => ((long)l).CompareTo((long)right),
            E_SignalValueType.Double => ((double)l).CompareTo((double)right),
            E_SignalValueType.Boolean => ((bool)l).CompareTo((bool)right),
            _ => throw new ArgumentOutOfRangeException(nameof(type))
        };
        if (type is E_SignalValueType.String or E_SignalValueType.Boolean && op is not (E_SignalWaitOperator.Equal or E_SignalWaitOperator.NotEqual)) throw new InvalidOperationException("String/Boolean 仅支持 Equal 或 NotEqual。");
        return op switch { E_SignalWaitOperator.Equal => order == 0, E_SignalWaitOperator.NotEqual => order != 0, E_SignalWaitOperator.GreaterThan => order > 0, E_SignalWaitOperator.GreaterOrEqual => order >= 0, E_SignalWaitOperator.LessThan => order < 0, E_SignalWaitOperator.LessOrEqual => order <= 0, _ => false };
    }
    internal static SignalValueNodeResult BuildResult(WorkflowValueSignalSnapshot snapshot, E_SignalValueType type, E_SignalValueWriteMode mode, bool success)
    {
        var value = snapshot.Value;
        return new(snapshot.SignalKey, type, success, snapshot.Version, snapshot.HasValue, Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty, type == E_SignalValueType.Int32 ? (int)value! : 0, type == E_SignalValueType.Int64 ? (long)value! : type == E_SignalValueType.Int32 ? (int)value! : 0, type == E_SignalValueType.Double ? (double)value! : type == E_SignalValueType.Int64 ? (long)value! : type == E_SignalValueType.Int32 ? (int)value! : 0, type == E_SignalValueType.Boolean && (bool)value!, mode);
    }
    private static object Calculate(object? current, object operand, E_SignalValueType type, E_SignalValueWriteMode mode)
    {
        if (type is not (E_SignalValueType.Int32 or E_SignalValueType.Int64 or E_SignalValueType.Double)) throw new InvalidOperationException("Increment/Decrement 仅支持数值类型。");
        var sign = mode == E_SignalValueWriteMode.Increment ? 1 : -1;
        return type switch { E_SignalValueType.Int32 => (object)(Convert.ToInt32(current ?? 0, CultureInfo.InvariantCulture) + sign * (int)operand), E_SignalValueType.Int64 => (object)(Convert.ToInt64(current ?? 0L, CultureInfo.InvariantCulture) + sign * (long)operand), _ => (object)(Convert.ToDouble(current ?? 0d, CultureInfo.InvariantCulture) + sign * (double)operand) };
    }
}
