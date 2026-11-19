using System.ComponentModel;
using System.Globalization;

namespace DP.WorkFlow;

/// <summary>类型转换标准结果。</summary>
public sealed record ConvertValueNodeResult([property: DisplayName("目标类型")] E_SignalValueType TargetType, [property: DisplayName("是否成功")] bool Success, [property: DisplayName("原始文本")] string OriginalText, [property: DisplayName("转换结果")] string ConvertedValueText, [property: DisplayName("字符串值")] string StringValue, [property: DisplayName("整数值")] int Int32Value, [property: DisplayName("长整数值")] long Int64Value, [property: DisplayName("浮点值")] double DoubleValue, [property: DisplayName("布尔值")] bool BoolValue);

/// <summary>将常量或绑定值转换为五种旧版标准类型。</summary>
[WorkflowNode("ConvertValue", DisplayName = "类型转换节点", Category = "2.Function/转换", OutputType = typeof(ConvertValueNodeResult), Icon = "convert")]
public sealed class ConvertValueNodeModel : WorkflowNodeModel
{
    /// <inheritdoc />
    public override string NodeType => "ConvertValue";
    /// <summary>要转换的值：固定文本或绑定（来源值先转为文本）。</summary>
    public WorkflowInput<string> Value { get; set; } = WorkflowInput<string>.FromLiteral(string.Empty);
    /// <summary>目标类型。</summary>
    public E_SignalValueType TargetType { get; set; }
}

/// <summary>执行类型转换。</summary>
public sealed class ConvertValueNodeHandler : WorkflowNodeHandler<ConvertValueNodeModel>
{
    /// <inheritdoc />
    protected override ValueTask<NodeExecutionResult> ExecuteAsync(ConvertValueNodeModel node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var raw = context.ResolveInput(node.Value) ?? string.Empty;
        var converted = SignalValueSetNodeHandler.ConvertValue(raw, node.TargetType);
        static T Try<T>(object value, Func<object, T> convert)
        {
            try { return convert(value); }
            catch (Exception exception) when (exception is FormatException or InvalidCastException or OverflowException) { return default!; }
        }
        var output = new ConvertValueNodeResult(node.TargetType, true, Convert.ToString(raw, CultureInfo.InvariantCulture) ?? string.Empty, Convert.ToString(converted, CultureInfo.InvariantCulture) ?? string.Empty, Convert.ToString(converted, CultureInfo.InvariantCulture) ?? string.Empty, Try(converted, Convert.ToInt32), Try(converted, Convert.ToInt64), Try(converted, Convert.ToDouble), Try(converted, Convert.ToBoolean));
        context.Trace("ConvertValue", $"已转换为 {node.TargetType}。", new Dictionary<string, object?> { ["OriginalText"] = output.OriginalText, ["ConvertedValueText"] = output.ConvertedValueText });
        return Polyfill.FromResult(NodeExecutionResult.Continue(output: output));
    }
}
