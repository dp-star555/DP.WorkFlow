using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Xml.Linq;
using ScriptEngine.Compilation;

namespace ScriptEngine.Language;

/// <summary>为脚本编辑器提供诊断、基础语义补全和语义分类。</summary>
internal sealed class RoslynScriptLanguageService
{
    private readonly RoslynScriptAnalysisProvider _analysisProvider;
    private readonly object _syntacticTreeGate = new();
    private SyntaxTree? _latestSyntacticTree;

    public RoslynScriptLanguageService(RoslynScriptAnalysisProvider analysisProvider)
    {
        _analysisProvider = analysisProvider ?? throw new ArgumentNullException(nameof(analysisProvider));
    }

    private static readonly IReadOnlyList<RoslynScriptCompletionItem> KeywordCompletions =
        Enum.GetValues(typeof(SyntaxKind))
            .Cast<SyntaxKind>()
            .Where(SyntaxFacts.IsKeywordKind)
            .Select(SyntaxFacts.GetText)
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .Distinct(StringComparer.Ordinal)
            .Select(text => new RoslynScriptCompletionItem(text, text, "C# 关键字", RoslynScriptCompletionKind.Keyword))
            .ToArray();

    /// <summary>同步获取诊断；仅保留给兼容调用，UI 应使用异步接口。</summary>
    public IReadOnlyList<RoslynScriptDiagnostic> GetDiagnostics(
        string? source,
        ScriptEnvironmentSnapshot environment,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return new[]
            {
                new RoslynScriptDiagnostic(
                    "CS0000",
                    DiagnosticSeverity.Error,
                    "C# 源代码不能为空。",
                    0,
                    0,
                    SourceName: "Program.cs")
            };
        }

        var analysis = _analysisProvider.Get(source, environment, cancellationToken);
        return RoslynDiagnosticMapper.Map(analysis.Compilation.GetDiagnostics(cancellationToken))
            .Concat(RoslynScriptSecurityPolicyEvaluator.Evaluate(
                analysis,
                environment.SecurityPolicy,
                cancellationToken))
            .ToArray();
    }

    /// <summary>获取多文件脚本项目诊断。</summary>
    public IReadOnlyList<RoslynScriptDiagnostic> GetProjectDiagnostics(
        IReadOnlyList<RoslynScriptSourceFile> files,
        ScriptEnvironmentSnapshot environment,
        CancellationToken cancellationToken)
    {
        var analysis = _analysisProvider.GetProject(files, environment, cancellationToken);
        return RoslynDiagnosticMapper.Map(analysis.Compilation.GetDiagnostics(cancellationToken))
            .Concat(RoslynScriptSecurityPolicyEvaluator.Evaluate(
                analysis,
                environment.SecurityPolicy,
                cancellationToken))
            .ToArray();
    }

    /// <summary>在后台线程获取诊断，避免阻塞 WinForms/WPF 消息循环。</summary>
    public Task<IReadOnlyList<RoslynScriptDiagnostic>> GetDiagnosticsAsync(
        string? source,
        ScriptEnvironmentSnapshot environment,
        CancellationToken cancellationToken) => Task.Run(
            () => GetDiagnostics(source, environment, cancellationToken),
            cancellationToken);

    /// <summary>异步获取光标位置的基础语义补全项。</summary>
    public Task<IReadOnlyList<RoslynScriptCompletionItem>> GetCompletionsAsync(
        string source,
        int caretIndex,
        ScriptEnvironmentSnapshot environment,
        IReadOnlyList<RoslynScriptContextItem> contextItems,
        CancellationToken cancellationToken) => Task.Run<IReadOnlyList<RoslynScriptCompletionItem>>(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var position = Clamp(caretIndex, 0, source.Length);
            var analysis = _analysisProvider.Get(source, environment, cancellationToken);
            return GetCompletions(
                source,
                position,
                analysis.PrimaryDocument,
                contextItems,
                cancellationToken);
        }, cancellationToken);

    /// <summary>异步获取多文件项目中活动文件的跨文件语义补全。</summary>
    public Task<IReadOnlyList<RoslynScriptCompletionItem>> GetProjectCompletionsAsync(
        IReadOnlyList<RoslynScriptSourceFile> files,
        string activeFileName,
        int caretIndex,
        ScriptEnvironmentSnapshot environment,
        IReadOnlyList<RoslynScriptContextItem> contextItems,
        CancellationToken cancellationToken) => Task.Run<IReadOnlyList<RoslynScriptCompletionItem>>(() =>
        {
            var file = files.FirstOrDefault(item => string.Equals(item.FileName, activeFileName, StringComparison.OrdinalIgnoreCase))
                       ?? throw new ArgumentException($"脚本项目中不存在文件：{activeFileName}", nameof(activeFileName));
            var analysis = _analysisProvider.GetProject(files, environment, cancellationToken);
            var document = GetDocument(analysis, file.FileName);
            var position = Clamp(caretIndex, 0, file.Source.Length);
            return GetCompletions(file.Source, position, document, contextItems, cancellationToken);
        }, cancellationToken);

    /// <summary>异步获取光标所在调用表达式的方法签名和当前参数。</summary>
    public Task<RoslynScriptSignatureHelp?> GetSignatureHelpAsync(
        string source,
        int caretIndex,
        ScriptEnvironmentSnapshot environment,
        CancellationToken cancellationToken) => Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var position = Clamp(caretIndex, 0, source.Length);
            var analysis = _analysisProvider.Get(source, environment, cancellationToken);
            var tokenPosition = Math.Max(0, Math.Min(position == 0 ? 0 : position - 1, Math.Max(0, source.Length - 1)));
            var argumentList = analysis.Root.FindToken(tokenPosition, findInsideTrivia: true)
                .Parent?.AncestorsAndSelf()
                .OfType<BaseArgumentListSyntax>()
                .FirstOrDefault(list => list.SpanStart <= position);
            if (argumentList is null) return null;

            var methods = GetCallableSymbols(argumentList.Parent, analysis.SemanticModel, cancellationToken)
                .OfType<IMethodSymbol>()
                .Distinct<IMethodSymbol>(SymbolEqualityComparer.Default)
                .ToArray();
            if (methods.Length == 0) return null;

            var activeParameter = argumentList.Arguments.GetSeparators()
                .Count(separator => separator.SpanStart < position);
            var selected = methods
                .OrderBy(method => Math.Abs(method.Parameters.Length - (activeParameter + 1)))
                .ThenBy(method => method.Parameters.Length)
                .First();
            var parameters = selected.Parameters
                .Select(parameter => new RoslynScriptParameterInfo(
                    parameter.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                    parameter.Name,
                    GetDocumentation(parameter)))
                .ToArray();
            var overloads = methods
                .Select(method => method.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            return new RoslynScriptSignatureHelp(
                selected.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                parameters,
                Math.Min(activeParameter, Math.Max(0, parameters.Length - 1)),
                argumentList.SpanStart,
                overloads);
        }, cancellationToken);

    /// <summary>异步获取标识符、成员或类型的签名和 XML 文档摘要。</summary>
    public Task<RoslynScriptQuickInfo?> GetQuickInfoAsync(
        string source,
        int position,
        ScriptEnvironmentSnapshot environment,
        CancellationToken cancellationToken) => Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (source.Length == 0) return null;
            var analysis = _analysisProvider.Get(source, environment, cancellationToken);
            var effectivePosition = Clamp(position, 0, source.Length - 1);
            var token = analysis.Root.FindToken(effectivePosition, findInsideTrivia: true);
            if (!token.IsKind(SyntaxKind.IdentifierToken) || token.Parent is null) return null;
            var symbol = analysis.SemanticModel.GetSymbolInfo(token.Parent, cancellationToken).Symbol
                         ?? analysis.SemanticModel.GetDeclaredSymbol(token.Parent, cancellationToken);
            if (symbol is null) return null;
            return new RoslynScriptQuickInfo(
                symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                GetDocumentation(symbol),
                token.SpanStart,
                token.Span.Length);
        }, cancellationToken);

    /// <summary>异步定位光标符号的源码定义；元数据符号返回只读描述位置。</summary>
    public Task<RoslynScriptDefinitionLocation?> GetDefinitionAsync(
        string source,
        int position,
        ScriptEnvironmentSnapshot environment,
        CancellationToken cancellationToken) => Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (source.Length == 0) return null;
            var analysis = _analysisProvider.Get(source, environment, cancellationToken);
            return GetDefinition(
                analysis,
                analysis.PrimaryDocument,
                Clamp(position, 0, source.Length - 1),
                cancellationToken);
        }, cancellationToken);

    /// <summary>异步定位多文件项目中活动文件符号的跨文件定义。</summary>
    public Task<RoslynScriptDefinitionLocation?> GetProjectDefinitionAsync(
        IReadOnlyList<RoslynScriptSourceFile> files,
        string activeFileName,
        int position,
        ScriptEnvironmentSnapshot environment,
        CancellationToken cancellationToken) => Task.Run(() =>
        {
            var file = files.FirstOrDefault(item => string.Equals(item.FileName, activeFileName, StringComparison.OrdinalIgnoreCase))
                       ?? throw new ArgumentException($"脚本项目中不存在文件：{activeFileName}", nameof(activeFileName));
            if (file.Source.Length == 0) return null;
            var analysis = _analysisProvider.GetProject(files, environment, cancellationToken);
            return GetDefinition(
                analysis,
                GetDocument(analysis, file.FileName),
                Clamp(position, 0, file.Source.Length - 1),
                cancellationToken);
        }, cancellationToken);

    /// <summary>
    /// 仅根据语法树计算可立即显示的高亮，不创建 Compilation，也不读取平台程序集元数据。
    /// 编辑器先同步应用这批结果，再由后台语义分析补充类型、方法和属性颜色。
    /// </summary>
    public IReadOnlyList<RoslynScriptHighlightSpan> GetSyntacticHighlightSpans(
        string source,
        CancellationToken cancellationToken)
    {
        SyntaxTree tree;
        lock (_syntacticTreeGate)
        {
            tree = RoslynCompilationFactory.ParseUserSource(
                new RoslynScriptSourceFile("Program.cs", source ?? string.Empty),
                _latestSyntacticTree);
            _latestSyntacticTree = tree;
        }
        var root = tree.GetRoot(cancellationToken);
        var spans = new List<RoslynScriptHighlightSpan>();
        foreach (var token in root.DescendantTokens(descendIntoTrivia: true))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var kind = ClassifySyntacticToken(token);
            if (kind != RoslynScriptHighlightKind.Plain && token.Span.Length > 0)
                spans.Add(new RoslynScriptHighlightSpan(token.SpanStart, token.Span.Length, kind));
        }
        foreach (var trivia in root.DescendantTrivia(descendIntoTrivia: true))
            AddCommentHighlights(trivia, spans);
        return spans.OrderBy(item => item.Start).ToArray();
    }

    /// <summary>异步计算源码的完整语义高亮区间。</summary>
    public Task<IReadOnlyList<RoslynScriptHighlightSpan>> GetHighlightSpansAsync(
        string source,
        ScriptEnvironmentSnapshot environment,
        CancellationToken cancellationToken) => Task.Run<IReadOnlyList<RoslynScriptHighlightSpan>>(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var analysis = _analysisProvider.Get(source, environment, cancellationToken);
            var root = analysis.Root;
            var model = analysis.SemanticModel;
            var spans = new List<RoslynScriptHighlightSpan>();
            foreach (var token in root.DescendantTokens(descendIntoTrivia: true))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var kind = ClassifyToken(token, model, cancellationToken);
                if (kind != RoslynScriptHighlightKind.Plain && token.Span.Length > 0)
                    spans.Add(new RoslynScriptHighlightSpan(token.SpanStart, token.Span.Length, kind));
                foreach (var trivia in token.LeadingTrivia.Concat(token.TrailingTrivia))
                    AddCommentHighlights(trivia, spans);
            }
            return spans;
        }, cancellationToken);

    private static IReadOnlyList<RoslynScriptCompletionItem> GetCompletions(
        string source,
        int position,
        RoslynScriptAnalysisDocument document,
        IReadOnlyList<RoslynScriptContextItem> contextItems,
        CancellationToken cancellationToken)
    {
        var prefixStart = RoslynScriptService.GetCompletionStart(source, position);
        var prefix = source.Substring(prefixStart, position - prefixStart);
        var token = document.Root.FindToken(Math.Max(0, prefixStart - 1), findInsideTrivia: true);
        var memberAccess = token.IsKind(SyntaxKind.DotToken)
            ? token.Parent?.AncestorsAndSelf().OfType<MemberAccessExpressionSyntax>().FirstOrDefault()
            : null;
        IEnumerable<ISymbol> symbols;
        if (memberAccess is not null)
        {
            var type = document.SemanticModel.GetTypeInfo(memberAccess.Expression, cancellationToken).Type;
            symbols = type is null
                ? Array.Empty<ISymbol>()
                : document.SemanticModel.LookupSymbols(position, type, includeReducedExtensionMethods: true);
        }
        else
        {
            symbols = document.SemanticModel.LookupSymbols(position);
        }

        var keywordItems = memberAccess is null
            ? KeywordCompletions.Where(item => RoslynScriptCompletionMatcher.GetScore(item.InsertionText, prefix) < int.MaxValue)
            : Array.Empty<RoslynScriptCompletionItem>();
        return symbols
            .Where(item => item.CanBeReferencedByName
                           && RoslynScriptCompletionMatcher.GetScore(item.Name, prefix) < int.MaxValue)
            .Select(ToCompletionItem)
            .Concat(contextItems
                .Where(item => RoslynScriptCompletionMatcher.GetScore(item.InsertionText, prefix) < int.MaxValue)
                .Select(item => new RoslynScriptCompletionItem(
                    item.InsertionText,
                    item.InsertionText,
                    item.Description,
                    RoslynScriptCompletionKind.HostContext)))
            .Concat(keywordItems)
            .GroupBy(item => item.InsertionText, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var first = group.First();
                var count = group.Count();
                return count <= 1
                    ? first
                    : first with { Description = first.Description + Environment.NewLine + $"另有 {count - 1} 个重载" };
            })
            .OrderBy(item => RoslynScriptCompletionMatcher.GetScore(item.FilterText ?? item.DisplayText, prefix))
            .ThenByDescending(item => item.MatchPriority)
            .ThenBy(item => item.SortText ?? item.DisplayText, StringComparer.OrdinalIgnoreCase)
            .Take(250)
            .ToArray();
    }

    private static RoslynScriptDefinitionLocation? GetDefinition(
        RoslynScriptAnalysisSnapshot analysis,
        RoslynScriptAnalysisDocument document,
        int position,
        CancellationToken cancellationToken)
    {
        var token = document.Root.FindToken(position, findInsideTrivia: true);
        if (token.Parent is null) return null;
        var symbolInfo = document.SemanticModel.GetSymbolInfo(token.Parent, cancellationToken);
        var symbol = symbolInfo.Symbol
                     ?? symbolInfo.CandidateSymbols.FirstOrDefault()
                     ?? document.SemanticModel.GetDeclaredSymbol(token.Parent, cancellationToken);
        if (symbol is null) return null;
        var display = symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
        var syntaxReference = symbol.DeclaringSyntaxReferences.FirstOrDefault(reference =>
            analysis.Documents.Any(item => ReferenceEquals(item.Tree, reference.SyntaxTree)));
        if (syntaxReference is null)
        {
            return new RoslynScriptDefinitionLocation(
                display,
                symbol.ContainingAssembly?.Name,
                0,
                0,
                0,
                0,
                false);
        }
        var syntax = syntaxReference.GetSyntax(cancellationToken);
        var locationSpan = syntax switch
        {
            BaseTypeDeclarationSyntax type => type.Identifier.Span,
            MethodDeclarationSyntax method => method.Identifier.Span,
            PropertyDeclarationSyntax property => property.Identifier.Span,
            VariableDeclaratorSyntax variable => variable.Identifier.Span,
            ParameterSyntax parameter => parameter.Identifier.Span,
            _ => syntax.Span
        };
        var line = syntax.SyntaxTree.GetLineSpan(locationSpan).StartLinePosition;
        return new RoslynScriptDefinitionLocation(
            display,
            syntax.SyntaxTree.FilePath,
            locationSpan.Start,
            locationSpan.Length,
            line.Line + 1,
            line.Character + 1,
            true);
    }

    private static RoslynScriptAnalysisDocument GetDocument(
        RoslynScriptAnalysisSnapshot analysis,
        string fileName) => analysis.Documents.FirstOrDefault(item =>
            string.Equals(item.FileName, fileName, StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException($"Roslyn 分析快照中不存在文件：{fileName}");

    private static IEnumerable<ISymbol> GetCallableSymbols(
        SyntaxNode? callable,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        switch (callable)
        {
            case InvocationExpressionSyntax invocation:
            {
                var info = semanticModel.GetSymbolInfo(invocation.Expression, cancellationToken);
                if (info.Symbol is not null) yield return info.Symbol;
                foreach (var candidate in info.CandidateSymbols) yield return candidate;
                break;
            }
            case ObjectCreationExpressionSyntax creation:
            {
                var info = semanticModel.GetSymbolInfo(creation, cancellationToken);
                if (info.Symbol is not null) yield return info.Symbol;
                foreach (var candidate in info.CandidateSymbols) yield return candidate;
                break;
            }
            case ConstructorInitializerSyntax initializer:
            {
                var info = semanticModel.GetSymbolInfo(initializer, cancellationToken);
                if (info.Symbol is not null) yield return info.Symbol;
                foreach (var candidate in info.CandidateSymbols) yield return candidate;
                break;
            }
            case ElementAccessExpressionSyntax elementAccess:
            {
                var info = semanticModel.GetSymbolInfo(elementAccess, cancellationToken);
                if (info.Symbol is not null) yield return info.Symbol;
                foreach (var candidate in info.CandidateSymbols) yield return candidate;
                break;
            }
        }
    }

    private static RoslynScriptCompletionItem ToCompletionItem(ISymbol symbol)
    {
        var isMethod = symbol is IMethodSymbol method && method.MethodKind != MethodKind.PropertyGet;
        var suffix = isMethod ? "()" : string.Empty;
        var signature = symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
        var documentation = GetDocumentation(symbol);
        return new RoslynScriptCompletionItem(
            symbol.Name + suffix,
            symbol.Name + suffix,
            string.IsNullOrWhiteSpace(documentation) ? signature : signature + Environment.NewLine + documentation,
            ClassifyCompletion(symbol),
            isMethod ? -1 : 0);
    }

    private static RoslynScriptCompletionKind ClassifyCompletion(ISymbol symbol) => symbol switch
    {
        INamespaceSymbol => RoslynScriptCompletionKind.Namespace,
        INamedTypeSymbol { TypeKind: TypeKind.Class } => RoslynScriptCompletionKind.Class,
        INamedTypeSymbol { TypeKind: TypeKind.Interface } => RoslynScriptCompletionKind.Interface,
        INamedTypeSymbol { TypeKind: TypeKind.Struct } => RoslynScriptCompletionKind.Struct,
        INamedTypeSymbol { TypeKind: TypeKind.Enum } => RoslynScriptCompletionKind.Enum,
        INamedTypeSymbol { TypeKind: TypeKind.Delegate } => RoslynScriptCompletionKind.Delegate,
        INamedTypeSymbol => RoslynScriptCompletionKind.Type,
        IMethodSymbol => RoslynScriptCompletionKind.Method,
        IPropertySymbol => RoslynScriptCompletionKind.Property,
        IFieldSymbol { IsConst: true } => RoslynScriptCompletionKind.Constant,
        IFieldSymbol => RoslynScriptCompletionKind.Field,
        IEventSymbol => RoslynScriptCompletionKind.Event,
        ILocalSymbol or IParameterSymbol => RoslynScriptCompletionKind.Variable,
        _ => RoslynScriptCompletionKind.None
    };

    private static RoslynScriptHighlightKind ClassifyToken(
        SyntaxToken token,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        var syntacticKind = ClassifySyntacticToken(token);
        if (syntacticKind != RoslynScriptHighlightKind.Plain) return syntacticKind;
        if (!token.IsKind(SyntaxKind.IdentifierToken)) return RoslynScriptHighlightKind.Plain;
        var symbol = model.GetSymbolInfo(token.Parent!, cancellationToken).Symbol
                     ?? model.GetDeclaredSymbol(token.Parent!, cancellationToken);
        return symbol switch
        {
            INamedTypeSymbol => RoslynScriptHighlightKind.Type,
            IMethodSymbol => RoslynScriptHighlightKind.Method,
            IPropertySymbol or IFieldSymbol => RoslynScriptHighlightKind.Property,
            _ => RoslynScriptHighlightKind.Plain
        };
    }

    private static RoslynScriptHighlightKind ClassifySyntacticToken(SyntaxToken token)
    {
        if (SyntaxFacts.IsKeywordKind(token.Kind())) return RoslynScriptHighlightKind.Keyword;
        if (token.IsKind(SyntaxKind.StringLiteralToken)
            || token.IsKind(SyntaxKind.Utf8StringLiteralToken)
            || token.IsKind(SyntaxKind.CharacterLiteralToken)
            || token.IsKind(SyntaxKind.InterpolatedStringTextToken))
            return RoslynScriptHighlightKind.String;
        if (token.IsKind(SyntaxKind.NumericLiteralToken)) return RoslynScriptHighlightKind.Number;
        return RoslynScriptHighlightKind.Plain;
    }

    private static void AddCommentHighlights(
        SyntaxTrivia trivia,
        ICollection<RoslynScriptHighlightSpan> spans)
    {
        if (!IsComment(trivia)) return;
        spans.Add(new RoslynScriptHighlightSpan(
            trivia.SpanStart,
            trivia.Span.Length,
            RoslynScriptHighlightKind.Comment));
        if (!trivia.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia)
            && !trivia.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia))
            return;

        var structure = trivia.GetStructure();
        if (structure is null) return;
        foreach (var node in structure.DescendantNodes())
        {
            if (node is XmlElementStartTagSyntax or XmlElementEndTagSyntax or XmlEmptyElementSyntax)
                spans.Add(new RoslynScriptHighlightSpan(
                    node.SpanStart,
                    node.Span.Length,
                    RoslynScriptHighlightKind.XmlDocTag));
        }
    }

    private static string? GetDocumentation(ISymbol symbol)
    {
        var xml = symbol.GetDocumentationCommentXml(expandIncludes: true);
        if (string.IsNullOrWhiteSpace(xml)) return null;
        try
        {
            var root = XElement.Parse("<documentation>" + xml + "</documentation>");
            var summary = root.Descendants("summary").FirstOrDefault()?.Value
                          ?? root.Value;
            var normalized = string.Join(" ", summary
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            return normalized.Length == 0 ? null : normalized;
        }
        catch
        {
            return null;
        }
    }

    private static bool IsComment(SyntaxTrivia trivia) =>
        trivia.IsKind(SyntaxKind.SingleLineCommentTrivia)
        || trivia.IsKind(SyntaxKind.MultiLineCommentTrivia)
        || trivia.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia)
        || trivia.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia);

    private static int Clamp(int value, int minimum, int maximum) =>
        value < minimum ? minimum : value > maximum ? maximum : value;
}
