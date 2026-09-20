using System.Text.RegularExpressions;

namespace DP.WorkFlow;

/// <summary>指定字符串比较操作。</summary>
public enum E_StringCompareOperator
{
    Equals = 0,
    NotEquals = 1,
    Contains = 2,
    NotContains = 3,
    StartsWith = 4,
    EndsWith = 5,
    Like = 6,
    IsNullOrEmpty = 7,
    IsNullOrWhiteSpace = 8
}

/// <summary>比较两个固定字符串或上游绑定值，并选择 True/False 端口。</summary>
[WorkflowNode("StringCompare", DisplayName = "字符串比较节点", Category = "2.Function/判断")]
public sealed class StringCompareNodeModel : WorkflowNodeModel
{
    public override string NodeType => "StringCompare";

    public WorkflowInput<string?> Left { get; set; } = WorkflowInput<string?>.FromLiteral(string.Empty);

    public E_StringCompareOperator Operator { get; set; } = E_StringCompareOperator.Equals;

    public WorkflowInput<string?> Right { get; set; } = WorkflowInput<string?>.FromLiteral(string.Empty);

    public bool IgnoreCase { get; set; } = true;

    public bool TrimBeforeCompare { get; set; }
}

/// <summary>执行确定性的 Ordinal 字符串比较和通配符匹配。</summary>
public sealed class StringCompareNodeHandler : WorkflowNodeHandler<StringCompareNodeModel>
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);

    protected override ValueTask<NodeExecutionResult> ExecuteAsync(
        StringCompareNodeModel node,
        IWorkflowNodeExecutionContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (node.Left.Validate() is { } leftError)
            throw new InvalidOperationException($"左值配置无效：{leftError}");
        if (RequiresRight(node.Operator) && node.Right.Validate() is { } rightError)
            throw new InvalidOperationException($"右值配置无效：{rightError}");

        var left = context.ResolveInput(node.Left);
        var right = RequiresRight(node.Operator) ? context.ResolveInput(node.Right) : null;
        if (node.TrimBeforeCompare)
        {
            left = left?.Trim();
            right = right?.Trim();
        }
        var comparison = node.IgnoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var matched = node.Operator switch
        {
            E_StringCompareOperator.Equals => string.Equals(left, right, comparison),
            E_StringCompareOperator.NotEquals => !string.Equals(left, right, comparison),
            E_StringCompareOperator.Contains => (left ?? string.Empty).Contains(right ?? string.Empty, comparison),
            E_StringCompareOperator.NotContains => !(left ?? string.Empty).Contains(right ?? string.Empty, comparison),
            E_StringCompareOperator.StartsWith => (left ?? string.Empty).StartsWith(right ?? string.Empty, comparison),
            E_StringCompareOperator.EndsWith => (left ?? string.Empty).EndsWith(right ?? string.Empty, comparison),
            E_StringCompareOperator.Like => MatchLike(left ?? string.Empty, right ?? string.Empty, node.IgnoreCase),
            E_StringCompareOperator.IsNullOrEmpty => string.IsNullOrEmpty(left),
            E_StringCompareOperator.IsNullOrWhiteSpace => string.IsNullOrWhiteSpace(left),
            _ => throw new NotSupportedException($"不支持的字符串比较操作：{node.Operator}。")
        };
        var output = new CompareNodeResult(matched, left, right, node.Operator.ToString(), "String");
        var port = matched ? WorkflowPorts.True : WorkflowPorts.False;
        context.Trace("Compare", $"字符串比较结果为 {matched}，选择 {port} 端口。", new Dictionary<string, object?>
        {
            ["Operator"] = node.Operator.ToString(),
            ["Matched"] = matched
        });
        return ValueTask.FromResult(NodeExecutionResult.Continue(port, output));
    }

    private static bool RequiresRight(E_StringCompareOperator operation) =>
        operation is not (E_StringCompareOperator.IsNullOrEmpty or E_StringCompareOperator.IsNullOrWhiteSpace);

    private static bool MatchLike(string input, string wildcard, bool ignoreCase)
    {
        var pattern = "^" + Regex.Escape(wildcard).Replace("\\*", ".*").Replace("\\?", ".") + "$";
        var options = RegexOptions.CultureInvariant | RegexOptions.NonBacktracking;
        if (ignoreCase)
            options |= RegexOptions.IgnoreCase;
        return Regex.IsMatch(input, pattern, options, RegexTimeout);
    }
}
