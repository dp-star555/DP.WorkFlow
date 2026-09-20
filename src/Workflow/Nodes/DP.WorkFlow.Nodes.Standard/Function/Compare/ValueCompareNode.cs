using System.Globalization;
using System.Numerics;

namespace DP.WorkFlow;

/// <summary>指定值比较所使用的数据类型。名称保留旧版兼容。</summary>
public enum E_ValueCompareDataType
{
    Number = 0,
    Int64 = 1,
    Double = 2,
    Decimal = 3,
    DateTime = 4,
    Boolean = 5
}

/// <summary>指定值比较操作。</summary>
public enum E_ValueCompareOperator
{
    Equal = 0,
    NotEqual = 1,
    GreaterThan = 2,
    GreaterOrEqual = 3,
    LessThan = 4,
    LessOrEqual = 5
}

/// <summary>比较两个固定值或上游绑定值，并选择 True/False 端口。</summary>
[WorkflowNode("ValueCompare", DisplayName = "数据比较节点", Category = "2.Function/判断")]
public sealed class ValueCompareNodeModel : WorkflowNodeModel
{
    public override string NodeType => "ValueCompare";

    public E_ValueCompareDataType DataType { get; set; } = E_ValueCompareDataType.Number;

    public WorkflowInput<string?> Left { get; set; } = WorkflowInput<string?>.FromLiteral(string.Empty);

    public E_ValueCompareOperator Operator { get; set; } = E_ValueCompareOperator.Equal;

    public WorkflowInput<string?> Right { get; set; } = WorkflowInput<string?>.FromLiteral(string.Empty);

    public double EqualityTolerance { get; set; }
}

/// <summary>执行强类型值比较。</summary>
public sealed class ValueCompareNodeHandler : WorkflowNodeHandler<ValueCompareNodeModel>
{
    protected override ValueTask<NodeExecutionResult> ExecuteAsync(
        ValueCompareNodeModel node,
        IWorkflowNodeExecutionContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Validate(node);
        var leftText = context.ResolveInput(node.Left);
        var rightText = context.ResolveInput(node.Right);
        var matched = node.DataType switch
        {
            E_ValueCompareDataType.Number or E_ValueCompareDataType.Decimal =>
                CompareDecimal(ParseDecimal(leftText, "左值"), ParseDecimal(rightText, "右值"), node.Operator, node.EqualityTolerance),
            E_ValueCompareDataType.Int64 =>
                CompareOrdered(ParseInt64(leftText, "左值").CompareTo(ParseInt64(rightText, "右值")), node.Operator),
            E_ValueCompareDataType.Double =>
                CompareDouble(ParseDouble(leftText, "左值"), ParseDouble(rightText, "右值"), node.Operator, node.EqualityTolerance),
            E_ValueCompareDataType.DateTime =>
                CompareOrdered(ParseDateTime(leftText, "左值").CompareTo(ParseDateTime(rightText, "右值")), node.Operator),
            E_ValueCompareDataType.Boolean => CompareBoolean(
                ParseBoolean(leftText, "左值"), ParseBoolean(rightText, "右值"), node.Operator),
            _ => throw new NotSupportedException($"不支持的数据比较类型：{node.DataType}。")
        };
        var output = new CompareNodeResult(
            matched,
            leftText,
            rightText,
            node.Operator.ToString(),
            node.DataType.ToString());
        var port = matched ? WorkflowPorts.True : WorkflowPorts.False;
        context.Trace("Compare", $"值比较结果为 {matched}，选择 {port} 端口。", new Dictionary<string, object?>
        {
            ["DataType"] = node.DataType.ToString(),
            ["Operator"] = node.Operator.ToString(),
            ["Left"] = leftText,
            ["Right"] = rightText,
            ["Matched"] = matched
        });
        return ValueTask.FromResult(NodeExecutionResult.Continue(port, output));
    }

    private static void Validate(ValueCompareNodeModel node)
    {
        if (node.Left.Validate() is { } leftError)
            throw new InvalidOperationException($"左值配置无效：{leftError}");
        if (node.Right.Validate() is { } rightError)
            throw new InvalidOperationException($"右值配置无效：{rightError}");
        if (double.IsNaN(node.EqualityTolerance) || double.IsInfinity(node.EqualityTolerance) || node.EqualityTolerance < 0)
            throw new InvalidOperationException("相等容差必须是大于或等于 0 的有限数值。");
        if (node.DataType == E_ValueCompareDataType.Boolean
            && node.Operator is not (E_ValueCompareOperator.Equal or E_ValueCompareOperator.NotEqual))
        {
            throw new InvalidOperationException("布尔类型仅支持 Equal 和 NotEqual。");
        }
    }

    private static decimal ParseDecimal(string? value, string name) =>
        decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : throw new FormatException($"{name}“{value}”不是有效 Decimal。");

    private static long ParseInt64(string? value, string name) =>
        long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : throw new FormatException($"{name}“{value}”不是有效 Int64。");

    private static double ParseDouble(string? value, string name)
    {
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            || double.IsNaN(parsed)
            || double.IsInfinity(parsed))
        {
            throw new FormatException($"{name}“{value}”不是有效有限 Double。");
        }
        return parsed;
    }

    private static DateTime ParseDateTime(string? value, string name) =>
        DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : throw new FormatException($"{name}“{value}”不是有效 DateTime。");

    private static bool ParseBoolean(string? value, string name) =>
        bool.TryParse(value, out var parsed)
            ? parsed
            : throw new FormatException($"{name}“{value}”不是有效 Boolean。");

    private static bool CompareDecimal(decimal left, decimal right, E_ValueCompareOperator operation, double tolerance)
    {
        decimal decimalTolerance;
        try
        {
            decimalTolerance = Convert.ToDecimal(tolerance, CultureInfo.InvariantCulture);
        }
        catch (OverflowException exception)
        {
            throw new InvalidOperationException("相等容差超出 Decimal 范围。", exception);
        }
        return CompareDifference(left - right, decimalTolerance, operation);
    }

    private static bool CompareDouble(double left, double right, E_ValueCompareOperator operation, double tolerance) =>
        CompareDifference(left - right, tolerance, operation);

    private static bool CompareDifference<T>(T difference, T tolerance, E_ValueCompareOperator operation)
        where T : INumber<T> => operation switch
    {
        E_ValueCompareOperator.Equal => T.Abs(difference) <= tolerance,
        E_ValueCompareOperator.NotEqual => T.Abs(difference) > tolerance,
        E_ValueCompareOperator.GreaterThan => difference > tolerance,
        E_ValueCompareOperator.GreaterOrEqual => difference >= -tolerance,
        E_ValueCompareOperator.LessThan => difference < -tolerance,
        E_ValueCompareOperator.LessOrEqual => difference <= tolerance,
        _ => throw new NotSupportedException($"不支持的值比较操作：{operation}。")
    };

    private static bool CompareOrdered(int order, E_ValueCompareOperator operation) => operation switch
    {
        E_ValueCompareOperator.Equal => order == 0,
        E_ValueCompareOperator.NotEqual => order != 0,
        E_ValueCompareOperator.GreaterThan => order > 0,
        E_ValueCompareOperator.GreaterOrEqual => order >= 0,
        E_ValueCompareOperator.LessThan => order < 0,
        E_ValueCompareOperator.LessOrEqual => order <= 0,
        _ => throw new NotSupportedException($"不支持的值比较操作：{operation}。")
    };

    private static bool CompareBoolean(bool left, bool right, E_ValueCompareOperator operation) => operation switch
    {
        E_ValueCompareOperator.Equal => left == right,
        E_ValueCompareOperator.NotEqual => left != right,
        _ => throw new InvalidOperationException("布尔类型仅支持 Equal 和 NotEqual。")
    };
}
