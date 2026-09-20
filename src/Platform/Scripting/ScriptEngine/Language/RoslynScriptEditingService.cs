using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using ScriptEngine.Compilation;

namespace ScriptEngine.Language;

/// <summary>提供不依赖桌面 UI 的 C# 折叠区间和智能缩进计算。</summary>
internal sealed class RoslynScriptEditingService
{
    private const int DefaultIndentSize = 4;
    private readonly object _treeGate = new();
    private SyntaxTree? _latestTree;

    /// <summary>从容错 Roslyn 语法树提取可以折叠的多行结构。</summary>
    public IReadOnlyList<RoslynScriptFoldingSpan> GetFoldingSpans(
        string? source,
        CancellationToken cancellationToken)
    {
        var tree = ParseTree(source ?? string.Empty, cancellationToken);
        var root = tree.GetRoot(cancellationToken);
        var spans = new List<RoslynScriptFoldingSpan>();

        foreach (var node in root.DescendantNodes())
        {
            cancellationToken.ThrowIfCancellationRequested();
            switch (node)
            {
                case NamespaceDeclarationSyntax namespaceDeclaration:
                    AddSpan(
                        tree,
                        spans,
                        namespaceDeclaration.NamespaceKeyword.SpanStart,
                        namespaceDeclaration.CloseBraceToken,
                        RoslynScriptFoldingKind.Namespace);
                    break;
                case BaseTypeDeclarationSyntax typeDeclaration:
                    AddSpan(
                        tree,
                        spans,
                        typeDeclaration.SpanStart,
                        typeDeclaration.CloseBraceToken,
                        RoslynScriptFoldingKind.Type);
                    break;
                case AccessorListSyntax accessorList:
                    AddSpan(
                        tree,
                        spans,
                        accessorList.Parent?.SpanStart ?? accessorList.OpenBraceToken.SpanStart,
                        accessorList.CloseBraceToken,
                        RoslynScriptFoldingKind.Member);
                    break;
                case SwitchStatementSyntax switchStatement:
                    AddSpan(
                        tree,
                        spans,
                        switchStatement.SwitchKeyword.SpanStart,
                        switchStatement.CloseBraceToken,
                        RoslynScriptFoldingKind.Statement);
                    break;
                case BlockSyntax block:
                    AddSpan(
                        tree,
                        spans,
                        GetBlockHeaderStart(block),
                        block.CloseBraceToken,
                        ClassifyBlock(block));
                    break;
            }
        }

        AddRegionSpans(tree, root, spans, cancellationToken);
        return spans
            .GroupBy(span => new { span.StartLine, span.EndLine })
            .Select(group => group.OrderBy(span => span.Kind).First())
            .OrderBy(span => span.StartLine)
            .ThenByDescending(span => span.EndLine)
            .ToArray();
    }

    /// <summary>根据当前容错语法结构计算光标所在行应使用的缩进列数。</summary>
    public int GetIndentation(string? source, int caretIndex, int indentSize)
    {
        var effectiveSource = source ?? string.Empty;
        var effectiveIndentSize = indentSize > 0 ? indentSize : DefaultIndentSize;
        var position = Clamp(caretIndex, 0, effectiveSource.Length);
        var sourceText = SourceText.From(effectiveSource);
        var currentLine = sourceText.Lines.GetLineFromPosition(position);
        var currentLineText = currentLine.ToString();
        var currentContent = currentLineText.TrimStart(' ', '\t');
        var tree = ParseTree(effectiveSource, CancellationToken.None);
        var root = tree.GetRoot();
        if (currentContent.StartsWith("{", StringComparison.Ordinal))
        {
            var leadingLength = currentLineText.Length - currentContent.Length;
            var bracePosition = Math.Min(currentLine.End, currentLine.Start + leadingLength);
            var braceToken = root.FindToken(bracePosition, findInsideTrivia: true);
            var headerStart = GetOpeningBraceHeaderStart(braceToken);
            if (headerStart is not null && headerStart.Value < currentLine.Start)
                return GetLineIndentation(sourceText, headerStart.Value, effectiveIndentSize);
        }
        var braceIndents = new Stack<int>();
        var delimiterIndents = new Stack<int>();
        SyntaxToken lastToken = default;

        foreach (var token in root.DescendantTokens())
        {
            if (token.IsMissing || token.SpanStart >= currentLine.Start) break;
            lastToken = token;
            switch (token.Kind())
            {
                case SyntaxKind.OpenBraceToken:
                    braceIndents.Push(GetLineIndentation(sourceText, token.SpanStart, effectiveIndentSize));
                    break;
                case SyntaxKind.CloseBraceToken:
                    if (braceIndents.Count > 0) braceIndents.Pop();
                    break;
                case SyntaxKind.OpenParenToken:
                case SyntaxKind.OpenBracketToken:
                    delimiterIndents.Push(GetLineIndentation(sourceText, token.SpanStart, effectiveIndentSize));
                    break;
                case SyntaxKind.CloseParenToken:
                case SyntaxKind.CloseBracketToken:
                    if (delimiterIndents.Count > 0) delimiterIndents.Pop();
                    break;
            }
        }

        if (currentContent.StartsWith("}", StringComparison.Ordinal))
            return braceIndents.Count > 0 ? braceIndents.Peek() : 0;
        if (currentContent.StartsWith(")", StringComparison.Ordinal)
            || currentContent.StartsWith("]", StringComparison.Ordinal))
            return delimiterIndents.Count > 0 ? delimiterIndents.Peek() : 0;

        var indentation = delimiterIndents.Count > 0
            ? delimiterIndents.Peek() + effectiveIndentSize
            : braceIndents.Count > 0
                ? braceIndents.Peek() + effectiveIndentSize
                : 0;

        var previousLine = FindPreviousContentLine(sourceText, currentLine.LineNumber);
        if (previousLine is null) return indentation;
        var previousText = previousLine.Value.ToString();
        var previousContent = RemoveLineComment(previousText).Trim();
        var previousIndentation = CountIndentColumns(previousText, effectiveIndentSize);

        if (RequiresIndentedFollowingLine(previousContent, lastToken))
            indentation = Math.Max(indentation, previousIndentation + effectiveIndentSize);

        return indentation;
    }

    /// <summary>为常见编译器诊断生成局部、可逆且不依赖 Workspaces 的文本修复。</summary>
    public IReadOnlyList<RoslynScriptCodeAction> GetCodeActions(
        string source,
        RoslynScriptDiagnostic diagnostic)
    {
        if (diagnostic.Origin != RoslynScriptDiagnosticOrigin.UserSource
            || diagnostic.Start < 0
            || diagnostic.Start > source.Length)
            return Array.Empty<RoslynScriptCodeAction>();

        switch (diagnostic.Id)
        {
            case "CS1002":
                return Insert("插入缺少的分号", ";");
            case "CS1026":
                return Insert("插入缺少的右圆括号", ")");
            case "CS1513":
                return Insert("插入缺少的右花括号", "}");
            case "CS1514":
                return Insert("插入缺少的左花括号", "{");
            case "CS0246":
            case "CS0103":
            {
                var identifier = diagnostic.Length > 0 && diagnostic.Start + diagnostic.Length <= source.Length
                    ? source.Substring(diagnostic.Start, diagnostic.Length)
                    : string.Empty;
                if (!CommonTypeNamespaces.TryGetValue(identifier, out var namespaceName)
                    || RoslynScriptService.GetUsings(source).Any(item =>
                        string.Equals(item, namespaceName, StringComparison.Ordinal)
                        || string.Equals(item, "global " + namespaceName, StringComparison.Ordinal)))
                    return Array.Empty<RoslynScriptCodeAction>();
                return new[]
                {
                    new RoslynScriptCodeAction(
                        $"添加 using {namespaceName}",
                        0,
                        0,
                        $"using {namespaceName};{Environment.NewLine}",
                        diagnostic.Id)
                };
            }
            case "CS8019":
            {
                var tree = CSharpSyntaxTree.ParseText(source);
                var root = tree.GetRoot();
                var node = root.FindNode(new TextSpan(
                    Math.Min(diagnostic.Start, source.Length),
                    Math.Min(diagnostic.Length, Math.Max(0, source.Length - diagnostic.Start))),
                    getInnermostNodeForTie: true);
                var usingDirective = node.AncestorsAndSelf().OfType<UsingDirectiveSyntax>().FirstOrDefault();
                if (usingDirective is null) return Array.Empty<RoslynScriptCodeAction>();
                return new[]
                {
                    new RoslynScriptCodeAction(
                        "删除不必要的 using",
                        usingDirective.FullSpan.Start,
                        usingDirective.FullSpan.Length,
                        string.Empty,
                        diagnostic.Id)
                };
            }
            default:
                return Array.Empty<RoslynScriptCodeAction>();
        }

        IReadOnlyList<RoslynScriptCodeAction> Insert(string title, string text) => new[]
        {
            new RoslynScriptCodeAction(
                title,
                diagnostic.Start,
                0,
                text,
                diagnostic.Id)
        };
    }

    private static readonly IReadOnlyDictionary<string, string> CommonTypeNamespaces =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["List"] = "System.Collections.Generic",
            ["Dictionary"] = "System.Collections.Generic",
            ["HashSet"] = "System.Collections.Generic",
            ["Task"] = "System.Threading.Tasks",
            ["ValueTask"] = "System.Threading.Tasks",
            ["CancellationToken"] = "System.Threading",
            ["StringBuilder"] = "System.Text",
            ["Regex"] = "System.Text.RegularExpressions"
        };

    private SyntaxTree ParseTree(string source, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_treeGate)
        {
            _latestTree = RoslynCompilationFactory.ParseUserSource(
                new RoslynScriptSourceFile("Program.cs", source),
                _latestTree);
            return _latestTree;
        }
    }

    private static void AddSpan(
        SyntaxTree tree,
        ICollection<RoslynScriptFoldingSpan> spans,
        int startPosition,
        SyntaxToken closeBrace,
        RoslynScriptFoldingKind kind)
    {
        if (closeBrace.IsMissing) return;
        var startLine = tree.GetLineSpan(new TextSpan(startPosition, 0)).StartLinePosition.Line;
        var endLine = tree.GetLineSpan(closeBrace.Span).StartLinePosition.Line;
        if (endLine > startLine)
            spans.Add(new RoslynScriptFoldingSpan(startLine, endLine, kind));
    }

    private static int GetBlockHeaderStart(BlockSyntax block) => block.Parent switch
    {
        BaseMethodDeclarationSyntax declaration => declaration.SpanStart,
        LocalFunctionStatementSyntax localFunction => localFunction.SpanStart,
        AccessorDeclarationSyntax accessor => accessor.Keyword.SpanStart,
        AnonymousFunctionExpressionSyntax anonymousFunction => anonymousFunction.SpanStart,
        // 独立作用域块的父节点也是 BlockSyntax（同时属于 StatementSyntax）；必须使用自身左花括号，
        // 否则折叠头会错误地落到外层方法的左花括号上。
        BlockSyntax => block.OpenBraceToken.SpanStart,
        StatementSyntax statement => statement.SpanStart,
        CatchClauseSyntax catchClause => catchClause.CatchKeyword.SpanStart,
        FinallyClauseSyntax finallyClause => finallyClause.FinallyKeyword.SpanStart,
        ElseClauseSyntax elseClause => elseClause.ElseKeyword.SpanStart,
        _ => block.OpenBraceToken.SpanStart
    };

    private static int? GetOpeningBraceHeaderStart(SyntaxToken token)
    {
        if (!token.IsKind(SyntaxKind.OpenBraceToken)) return null;
        return token.Parent switch
        {
            BlockSyntax block => GetBlockHeaderStart(block),
            BaseTypeDeclarationSyntax type => type.SpanStart,
            NamespaceDeclarationSyntax namespaceDeclaration => namespaceDeclaration.SpanStart,
            SwitchStatementSyntax switchStatement => switchStatement.SwitchKeyword.SpanStart,
            AccessorListSyntax accessorList => accessorList.Parent?.SpanStart,
            _ => null
        };
    }

    private static RoslynScriptFoldingKind ClassifyBlock(BlockSyntax block) => block.Parent switch
    {
        BaseMethodDeclarationSyntax or LocalFunctionStatementSyntax or AccessorDeclarationSyntax
            or AnonymousFunctionExpressionSyntax => RoslynScriptFoldingKind.Member,
        _ => RoslynScriptFoldingKind.Statement
    };

    private static void AddRegionSpans(
        SyntaxTree tree,
        SyntaxNode root,
        ICollection<RoslynScriptFoldingSpan> spans,
        CancellationToken cancellationToken)
    {
        var openRegions = new Stack<RegionDirectiveTriviaSyntax>();
        var directives = root.DescendantTrivia(descendIntoTrivia: true)
            .Select(trivia => trivia.GetStructure())
            .OfType<DirectiveTriviaSyntax>()
            .OrderBy(directive => directive.SpanStart);
        foreach (var directive in directives)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (directive is RegionDirectiveTriviaSyntax region)
            {
                openRegions.Push(region);
            }
            else if (directive is EndRegionDirectiveTriviaSyntax && openRegions.Count > 0)
            {
                var start = openRegions.Pop();
                var startLine = tree.GetLineSpan(new TextSpan(start.SpanStart, 0)).StartLinePosition.Line;
                var endLine = tree.GetLineSpan(new TextSpan(directive.SpanStart, 0)).StartLinePosition.Line;
                if (endLine > startLine)
                    spans.Add(new RoslynScriptFoldingSpan(startLine, endLine, RoslynScriptFoldingKind.Region));
            }
        }
    }

    private static TextLine? FindPreviousContentLine(SourceText source, int currentLineNumber)
    {
        for (var lineNumber = currentLineNumber - 1; lineNumber >= 0; lineNumber--)
        {
            var line = source.Lines[lineNumber];
            if (!string.IsNullOrWhiteSpace(line.ToString())) return line;
        }
        return null;
    }

    private static bool RequiresIndentedFollowingLine(string previousContent, SyntaxToken lastToken)
    {
        if (previousContent.Length == 0 || previousContent.EndsWith("{", StringComparison.Ordinal)) return false;
        if (IsCaseLabel(previousContent)) return true;
        if (IsControlHeader(previousContent)) return true;
        return lastToken.IsKind(SyntaxKind.EqualsToken)
               || lastToken.IsKind(SyntaxKind.EqualsGreaterThanToken)
               || lastToken.IsKind(SyntaxKind.DotToken)
               || lastToken.IsKind(SyntaxKind.CommaToken)
               || lastToken.IsKind(SyntaxKind.PlusToken)
               || lastToken.IsKind(SyntaxKind.MinusToken)
               || lastToken.IsKind(SyntaxKind.AsteriskToken)
               || lastToken.IsKind(SyntaxKind.SlashToken)
               || lastToken.IsKind(SyntaxKind.AmpersandAmpersandToken)
               || lastToken.IsKind(SyntaxKind.BarBarToken)
               || lastToken.IsKind(SyntaxKind.QuestionToken)
               || lastToken.IsKind(SyntaxKind.ColonToken) && !IsCaseLabel(previousContent);
    }

    private static bool IsCaseLabel(string text) =>
        (StartsWithKeyword(text, "case") || StartsWithKeyword(text, "default"))
        && text.EndsWith(":", StringComparison.Ordinal);

    private static bool IsControlHeader(string text)
    {
        if (text is "else" or "do" or "try" or "finally") return true;
        if (!text.EndsWith(")", StringComparison.Ordinal)) return false;
        if (StartsWithKeyword(text, "else"))
            text = text.Substring("else".Length).TrimStart();
        return StartsWithKeyword(text, "if")
               || StartsWithKeyword(text, "for")
               || StartsWithKeyword(text, "foreach")
               || StartsWithKeyword(text, "while")
               || StartsWithKeyword(text, "using")
               || StartsWithKeyword(text, "lock")
               || StartsWithKeyword(text, "fixed")
               || StartsWithKeyword(text, "catch");
    }

    private static bool StartsWithKeyword(string text, string keyword) =>
        text.StartsWith(keyword, StringComparison.Ordinal)
        && (text.Length == keyword.Length || !SyntaxFacts.IsIdentifierPartCharacter(text[keyword.Length]));

    private static string RemoveLineComment(string text)
    {
        var comment = text.IndexOf("//", StringComparison.Ordinal);
        return comment < 0 ? text : text.Substring(0, comment);
    }

    private static int GetLineIndentation(SourceText source, int position, int tabSize)
    {
        var line = source.Lines.GetLineFromPosition(Clamp(position, 0, source.Length));
        return CountIndentColumns(line.ToString(), tabSize);
    }

    private static int CountIndentColumns(string text, int tabSize)
    {
        var columns = 0;
        foreach (var character in text)
        {
            if (character == ' ')
            {
                columns++;
            }
            else if (character == '\t')
            {
                columns += tabSize - columns % tabSize;
            }
            else
            {
                break;
            }
        }
        return columns;
    }

    private static int Clamp(int value, int minimum, int maximum) =>
        value < minimum ? minimum : value > maximum ? maximum : value;
}
