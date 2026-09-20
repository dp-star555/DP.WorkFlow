using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ScriptEngine.Language;

/// <summary>在 Emit 和运行前根据语义符号执行轻量访问策略检查。</summary>
internal static class RoslynScriptSecurityPolicyEvaluator
{
    public static IReadOnlyList<RoslynScriptDiagnostic> Evaluate(
        RoslynScriptAnalysisSnapshot analysis,
        RoslynScriptSecurityPolicy policy,
        CancellationToken cancellationToken)
    {
        if (policy.DeniedNamespacePrefixes.Count == 0 && policy.DeniedSymbolPrefixes.Count == 0)
            return Array.Empty<RoslynScriptDiagnostic>();

        var diagnostics = new List<RoslynScriptDiagnostic>();
        foreach (var document in analysis.Documents)
        {
            var reportedStarts = new HashSet<int>();
            foreach (var node in EnumeratePolicyNodes(document.Root))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var symbol = document.SemanticModel.GetSymbolInfo(node, cancellationToken).Symbol;
                if (symbol is null) continue;
                var symbolName = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                    .Replace("global::", string.Empty);
                var namespaceName = symbol is INamespaceSymbol namespaceSymbol
                    ? namespaceSymbol.ToDisplayString()
                    : symbol.ContainingNamespace?.ToDisplayString() ?? string.Empty;
                var denied = policy.DeniedNamespacePrefixes.FirstOrDefault(prefix =>
                                 IsPrefix(namespaceName, prefix))
                             ?? policy.DeniedSymbolPrefixes.FirstOrDefault(prefix =>
                                 IsPrefix(symbolName, prefix));
                if (denied is null || !reportedStarts.Add(node.SpanStart)) continue;
                var line = document.Tree.GetLineSpan(node.Span).StartLinePosition;
                diagnostics.Add(new RoslynScriptDiagnostic(
                    "SE2001",
                    DiagnosticSeverity.Error,
                    $"脚本安全策略禁止访问 {symbolName}（规则：{denied}）。",
                    node.SpanStart,
                    node.Span.Length,
                    line.Line + 1,
                    line.Character + 1,
                    document.FileName,
                    RoslynScriptDiagnosticOrigin.UserSource));
            }
        }
        return diagnostics;
    }

    private static IEnumerable<SyntaxNode> EnumeratePolicyNodes(SyntaxNode root)
    {
        foreach (var node in root.DescendantNodes())
        {
            switch (node)
            {
                case UsingDirectiveSyntax usingDirective when usingDirective.Name is not null:
                    yield return usingDirective.Name;
                    break;
                case InvocationExpressionSyntax invocation:
                    yield return invocation.Expression;
                    break;
                case ObjectCreationExpressionSyntax creation:
                    yield return creation.Type;
                    break;
                case MemberAccessExpressionSyntax memberAccess
                    when memberAccess.Parent is not MemberAccessExpressionSyntax
                         && memberAccess.Parent is not InvocationExpressionSyntax:
                    yield return memberAccess;
                    break;
            }
        }
    }

    private static bool IsPrefix(string value, string prefix) =>
        value.Equals(prefix, StringComparison.Ordinal)
        || value.StartsWith(prefix + ".", StringComparison.Ordinal);
}
