using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.Completion;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.Rename;
using Microsoft.CodeAnalysis.Text;
using ScriptEngine.Compilation;
using ScriptEngine.Language;

namespace ScriptEngine.Workspaces;

/// <summary>
/// 可选的 Roslyn Workspaces 编辑模块。核心 ScriptEngine 无需引用 Workspaces；需要跨文件重命名、
/// 符号搜索和语义 using 修复的宿主只需引用本程序集。
/// </summary>
public sealed class RoslynScriptWorkspaceService : IRoslynScriptProjectEditingService, IRoslynScriptCompletionProvider, IDisposable
{
    private static readonly Lazy<MefHostServices> HostServices = new(
        () => MefHostServices.Create(MefHostServices.DefaultAssemblies),
        LazyThreadSafetyMode.ExecutionAndPublication);
    private readonly object _cacheGate = new();
    private readonly Dictionary<string, WorkspaceCacheEntry> _cache = new(StringComparer.Ordinal);
    private readonly LinkedList<string> _cacheLru = new();
    private readonly RoslynScriptWorkspaceOptions _options;
    private long _cacheHits;
    private long _cacheMisses;
    private long _incrementalDocumentUpdates;
    private bool _disposed;

    /// <summary>使用默认有界缓存配置创建 Workspaces 模块。</summary>
    public RoslynScriptWorkspaceService() : this(new RoslynScriptWorkspaceOptions())
    {
    }

    /// <summary>使用指定有界缓存配置创建 Workspaces 模块。</summary>
    /// <param name="options">缓存容量和补全结果上限。</param>
    public RoslynScriptWorkspaceService(RoslynScriptWorkspaceOptions options) =>
        _options = options ?? throw new ArgumentNullException(nameof(options));

    /// <summary>获取当前常驻 Workspace 缓存统计。</summary>
    public RoslynScriptWorkspaceCacheStatistics GetCacheStatistics()
    {
        lock (_cacheGate)
            return new RoslynScriptWorkspaceCacheStatistics(
                _cacheHits,
                _cacheMisses,
                _incrementalDocumentUpdates,
                _cache.Count);
    }

    /// <summary>使用 Roslyn CompletionService 获取上下文感知补全和精确提交文本变化。</summary>
    public async Task<IReadOnlyList<RoslynScriptCompletionItem>> GetCompletionsAsync(
        RoslynScriptProject project,
        string activeFileName,
        int position,
        RoslynScriptEnvironment? environment = null,
        CancellationToken cancellationToken = default)
    {
        if (project is null) throw new ArgumentNullException(nameof(project));
        var effectiveEnvironment = environment ?? new RoslynScriptEnvironment();
        using var context = await Task.Run(
            () => CreateContext(project, effectiveEnvironment),
            cancellationToken).ConfigureAwait(false);
        var document = context.GetDocument(activeFileName);
        var source = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
        if (position < 0 || position > source.Length) throw new ArgumentOutOfRangeException(nameof(position));
        var completionService = CompletionService.GetService(document);
        if (completionService is null) return Array.Empty<RoslynScriptCompletionItem>();
        var completionList = await completionService.GetCompletionsAsync(
            document,
            position,
            CompletionTrigger.Invoke,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (completionList is null) return Array.Empty<RoslynScriptCompletionItem>();

        var filterSpan = completionList.Span;
        var filterText = filterSpan.Start >= 0 && filterSpan.End <= source.Length
            ? source.ToString(filterSpan)
            : string.Empty;
        var candidates = completionList.ItemsList
            .Select(item => new { Item = item, Score = RoslynScriptCompletionMatcher.GetScore(item.FilterText, filterText) })
            .OrderBy(item => item.Score)
            .ThenByDescending(item => item.Item.Rules.MatchPriority)
            .ThenBy(item => item.Item.SortText, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Item.DisplayText, StringComparer.OrdinalIgnoreCase)
            .Take(_options.MaximumCompletionItems)
            .Select(item => item.Item)
            .ToArray();
        using var concurrency = new SemaphoreSlim(8, 8);
        var resolved = await Task.WhenAll(candidates.Select(async item =>
        {
            await concurrency.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var change = await completionService.GetChangeAsync(
                    document,
                    item,
                    commitCharacter: null,
                    cancellationToken).ConfigureAwait(false);
                var description = await completionService.GetDescriptionAsync(
                    document,
                    item,
                    cancellationToken).ConfigureAwait(false);
                var textChange = change.TextChange;
                return new RoslynScriptCompletionItem(
                    item.DisplayTextPrefix + item.DisplayText + item.DisplayTextSuffix,
                    textChange.NewText ?? item.DisplayText,
                    description?.Text,
                    GetCompletionKind(item.Tags),
                    CaretOffset: 0,
                    item.FilterText,
                    item.SortText,
                    item.InlineDescription,
                    textChange.Span.Start,
                    textChange.Span.Length,
                    change.NewPosition,
                    change.TextChanges.IsDefaultOrEmpty
                        ? null
                        : change.TextChanges.Select(text => new RoslynScriptTextChange(
                            text.Span.Start,
                            text.Span.Length,
                            text.NewText ?? string.Empty)).ToArray(),
                    item.Rules.MatchPriority);
            }
            finally
            {
                concurrency.Release();
            }
        })).ConfigureAwait(false);

        return resolved
            .Where(item => IsCompletionAllowed(item, effectiveEnvironment.SecurityPolicy))
            .Concat(effectiveEnvironment.ContextItems.Select(item => new RoslynScriptCompletionItem(
                item.InsertionText,
                item.InsertionText,
                item.Description,
                RoslynScriptCompletionKind.HostContext,
                FilterText: item.InsertionText,
                SortText: item.InsertionText)))
            .GroupBy(item => item.DisplayText + "\0" + item.InsertionText, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
    }

    /// <summary>为指定项目诊断生成语义代码修复。</summary>
    /// <param name="project">待编辑的多文件脚本项目。</param>
    /// <param name="activeFileName">诊断所属源码文件名。</param>
    /// <param name="diagnostic">核心 ScriptEngine 返回的诊断。</param>
    /// <param name="environment">项目引用、导入和访问策略。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>可以原子应用的项目编辑；没有安全修复时返回空集合。</returns>
    public async Task<IReadOnlyList<RoslynScriptWorkspaceEdit>> GetCodeFixesAsync(
        RoslynScriptProject project,
        string activeFileName,
        RoslynScriptDiagnostic diagnostic,
        RoslynScriptEnvironment? environment = null,
        CancellationToken cancellationToken = default)
    {
        if (project is null) throw new ArgumentNullException(nameof(project));
        if (diagnostic is null) throw new ArgumentNullException(nameof(diagnostic));
        if (diagnostic.Origin != RoslynScriptDiagnosticOrigin.UserSource)
            return Array.Empty<RoslynScriptWorkspaceEdit>();

        using var context = await Task.Run(
            () => CreateContext(project, environment ?? new RoslynScriptEnvironment()),
            cancellationToken).ConfigureAwait(false);
        var document = context.GetDocument(activeFileName);
        return diagnostic.Id switch
        {
            "CS0246" or "CS0103" => await CreateAddImportEditsAsync(
                context,
                document,
                diagnostic,
                environment ?? new RoslynScriptEnvironment(),
                cancellationToken).ConfigureAwait(false),
            "CS8019" => await CreateRemoveImportEditsAsync(
                context,
                document,
                diagnostic,
                cancellationToken).ConfigureAwait(false),
            _ => Array.Empty<RoslynScriptWorkspaceEdit>()
        };
    }

    /// <summary>查找光标所在符号在项目中的声明和全部源码引用。</summary>
    /// <param name="project">待搜索的多文件脚本项目。</param>
    /// <param name="activeFileName">符号所在文件名。</param>
    /// <param name="position">符号内的零基字符位置。</param>
    /// <param name="environment">项目引用和默认导入。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>按文件和位置排序的声明及引用；没有可解析符号时返回空集合。</returns>
    public async Task<IReadOnlyList<RoslynScriptReferenceLocation>> FindReferencesAsync(
        RoslynScriptProject project,
        string activeFileName,
        int position,
        RoslynScriptEnvironment? environment = null,
        CancellationToken cancellationToken = default)
    {
        if (project is null) throw new ArgumentNullException(nameof(project));
        using var context = await Task.Run(
            () => CreateContext(project, environment ?? new RoslynScriptEnvironment()),
            cancellationToken).ConfigureAwait(false);
        var symbol = await FindSourceSymbolAsync(
            context.GetDocument(activeFileName),
            position,
            requireSourceDefinition: false,
            cancellationToken).ConfigureAwait(false);
        if (symbol is null) return Array.Empty<RoslynScriptReferenceLocation>();

        var output = new List<RoslynScriptReferenceLocation>();
        var references = await SymbolFinder.FindReferencesAsync(
            symbol,
            context.Project.Solution,
            cancellationToken).ConfigureAwait(false);
        foreach (var referencedSymbol in references)
        {
            foreach (var location in referencedSymbol.Definition.Locations.Where(item => item.IsInSource))
                AddLocation(output, location.SourceTree?.FilePath, location, isDefinition: true);
            foreach (var reference in referencedSymbol.Locations)
                AddLocation(output, reference.Document.FilePath ?? reference.Document.Name, reference.Location, isDefinition: false);
        }
        return output
            .Where(item => !item.FileName.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase))
            .Distinct()
            .OrderBy(item => item.FileName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Start)
            .ToArray();
    }

    /// <summary>跨项目重命名光标所在的源码符号及其全部源码引用。</summary>
    /// <param name="project">待编辑的多文件脚本项目。</param>
    /// <param name="activeFileName">符号所在文件名。</param>
    /// <param name="position">符号内的零基字符位置。</param>
    /// <param name="newName">新的 C# 标识符，不包含 @ 前缀。</param>
    /// <param name="environment">项目引用和默认导入。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>项目级原子编辑；位置没有可重命名的源码符号时返回 null。</returns>
    public async Task<RoslynScriptWorkspaceEdit?> RenameSymbolAsync(
        RoslynScriptProject project,
        string activeFileName,
        int position,
        string newName,
        RoslynScriptEnvironment? environment = null,
        CancellationToken cancellationToken = default)
    {
        if (project is null) throw new ArgumentNullException(nameof(project));
        if (string.IsNullOrWhiteSpace(newName)
            || !SyntaxFacts.IsValidIdentifier(newName)
            || SyntaxFacts.GetKeywordKind(newName) != SyntaxKind.None
            || SyntaxFacts.GetContextualKeywordKind(newName) != SyntaxKind.None)
            throw new ArgumentException("新名称必须是不含 @ 前缀的有效 C# 非关键字标识符。", nameof(newName));

        using var context = await Task.Run(
            () => CreateContext(project, environment ?? new RoslynScriptEnvironment()),
            cancellationToken).ConfigureAwait(false);
        var document = context.GetDocument(activeFileName);
        var symbol = await FindSourceSymbolAsync(
            document,
            position,
            requireSourceDefinition: true,
            cancellationToken).ConfigureAwait(false);
        if (symbol is null) return null;

        var renamed = await Renamer.RenameSymbolAsync(
            context.Project.Solution,
            symbol,
            new SymbolRenameOptions(),
            newName,
            cancellationToken).ConfigureAwait(false);
        return await context.CreateEditAsync(
            renamed,
            $"将 {symbol.Name} 重命名为 {newName}",
            RoslynScriptWorkspaceEditKind.Rename,
            diagnosticId: null,
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task<ISymbol?> FindSourceSymbolAsync(
        Document document,
        int position,
        bool requireSourceDefinition,
        CancellationToken cancellationToken)
    {
        var source = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
        if (position < 0 || position > source.Length) throw new ArgumentOutOfRangeException(nameof(position));
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (root is null || semanticModel is null || source.Length == 0) return null;
        var tokenPosition = position == source.Length ? position - 1 : position;
        var token = root.FindToken(Math.Max(0, tokenPosition), findInsideTrivia: true);
        if (!token.IsKind(SyntaxKind.IdentifierToken)
            || !token.Span.Contains(tokenPosition)
            || token.Parent is null)
            return null;
        var symbol = semanticModel.GetSymbolInfo(token.Parent, cancellationToken).Symbol
                     ?? semanticModel.GetDeclaredSymbol(token.Parent, cancellationToken);
        if (symbol is null)
        {
            foreach (var ancestor in token.Parent.Ancestors())
            {
                symbol = semanticModel.GetDeclaredSymbol(ancestor, cancellationToken);
                if (symbol is not null) break;
            }
        }
        return requireSourceDefinition && symbol?.Locations.Any(location => location.IsInSource) != true
            ? null
            : symbol;
    }

    private static void AddLocation(
        ICollection<RoslynScriptReferenceLocation> output,
        string? fileName,
        Location location,
        bool isDefinition)
    {
        if (!location.IsInSource || string.IsNullOrWhiteSpace(fileName)) return;
        var line = location.GetLineSpan().StartLinePosition;
        output.Add(new RoslynScriptReferenceLocation(
            fileName!.Replace('\\', '/'),
            location.SourceSpan.Start,
            location.SourceSpan.Length,
            line.Line + 1,
            line.Character + 1,
            isDefinition));
    }

    private static async Task<IReadOnlyList<RoslynScriptWorkspaceEdit>> CreateAddImportEditsAsync(
        WorkspaceContext context,
        Document document,
        RoslynScriptDiagnostic diagnostic,
        RoslynScriptEnvironment environment,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false) as CompilationUnitSyntax;
        if (root is null || diagnostic.Start < 0 || diagnostic.Start >= root.FullSpan.End)
            return Array.Empty<RoslynScriptWorkspaceEdit>();
        var token = root.FindToken(diagnostic.Start, findInsideTrivia: true);
        var simpleName = token.Parent?.AncestorsAndSelf().OfType<SimpleNameSyntax>().FirstOrDefault();
        var identifier = simpleName?.Identifier.ValueText ?? token.ValueText;
        if (string.IsNullOrWhiteSpace(identifier)) return Array.Empty<RoslynScriptWorkspaceEdit>();
        var arity = simpleName is GenericNameSyntax genericName ? genericName.Arity : 0;
        var compilation = await context.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false);
        if (compilation is null) return Array.Empty<RoslynScriptWorkspaceEdit>();

        var declarations = await SymbolFinder.FindDeclarationsAsync(
            context.Project,
            identifier,
            ignoreCase: false,
            SymbolFilter.Type,
            cancellationToken).ConfigureAwait(false);
        var imported = root.Usings
            .Where(item => item.Alias is null && item.StaticKeyword.IsKind(SyntaxKind.None))
            .Select(item => item.Name?.ToString())
            .Where(item => item is not null)
            .Cast<string>()
            .Concat(environment.Imports)
            .ToHashSet(StringComparer.Ordinal);
        var namespaces = declarations
            .OfType<INamedTypeSymbol>()
            .Where(type => arity == 0 || type.Arity == arity)
            .Where(type => compilation.IsSymbolAccessibleWithin(type, compilation.Assembly))
            .Select(type => type.ContainingNamespace)
            .Where(item => item is { IsGlobalNamespace: false })
            .Select(item => item.ToDisplayString())
            .Where(item => !imported.Contains(item))
            .Where(item => !environment.SecurityPolicy.DeniedNamespacePrefixes.Any(denied =>
                item.Equals(denied, StringComparison.Ordinal)
                || item.StartsWith(denied + ".", StringComparison.Ordinal)))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(item => item, StringComparer.Ordinal)
            .Take(20)
            .ToArray();

        var edits = new List<RoslynScriptWorkspaceEdit>(namespaces.Length);
        foreach (var namespaceName in namespaces)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directive = SyntaxFactory.UsingDirective(SyntaxFactory.ParseName(namespaceName))
                .WithAdditionalAnnotations(Formatter.Annotation);
            var changedRoot = root.AddUsings(directive);
            var changedDocument = document.WithSyntaxRoot(changedRoot);
            changedDocument = await Formatter.FormatAsync(
                changedDocument,
                Formatter.Annotation,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            var edit = await context.CreateEditAsync(
                changedDocument.Project.Solution,
                $"添加 using {namespaceName}",
                RoslynScriptWorkspaceEditKind.AddImport,
                diagnostic.Id,
                cancellationToken).ConfigureAwait(false);
            if (edit is not null) edits.Add(edit);
        }
        return edits;
    }

    private static async Task<IReadOnlyList<RoslynScriptWorkspaceEdit>> CreateRemoveImportEditsAsync(
        WorkspaceContext context,
        Document document,
        RoslynScriptDiagnostic diagnostic,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false) as CompilationUnitSyntax;
        if (root is null || diagnostic.Start < 0 || diagnostic.Start > root.FullSpan.End)
            return Array.Empty<RoslynScriptWorkspaceEdit>();
        var span = new TextSpan(
            diagnostic.Start,
            Math.Min(diagnostic.Length, Math.Max(0, root.FullSpan.End - diagnostic.Start)));
        var usingDirective = root.FindNode(span, getInnermostNodeForTie: true)
            .AncestorsAndSelf()
            .OfType<UsingDirectiveSyntax>()
            .FirstOrDefault();
        if (usingDirective is null) return Array.Empty<RoslynScriptWorkspaceEdit>();
        var changedRoot = root.RemoveNode(usingDirective, SyntaxRemoveOptions.KeepExteriorTrivia);
        if (changedRoot is null) return Array.Empty<RoslynScriptWorkspaceEdit>();
        var changedDocument = document.WithSyntaxRoot(changedRoot);
        var edit = await context.CreateEditAsync(
            changedDocument.Project.Solution,
            "删除不必要的 using",
            RoslynScriptWorkspaceEditKind.RemoveImport,
            diagnostic.Id,
            cancellationToken).ConfigureAwait(false);
        return edit is null ? Array.Empty<RoslynScriptWorkspaceEdit>() : new[] { edit };
    }

    private WorkspaceContext CreateContext(
        RoslynScriptProject sourceProject,
        RoslynScriptEnvironment environment)
    {
        var files = sourceProject.GetValidatedFiles();
        var snapshot = ScriptEnvironmentSnapshot.Create(environment);
        var key = snapshot.Fingerprint + "\n" + string.Join("\n", files.Select(file => file.FileName.ToUpperInvariant()));
        lock (_cacheGate)
        {
            ThrowIfDisposed();
            if (!_cache.TryGetValue(key, out var entry))
            {
                entry = CreateCacheEntry(key, files, snapshot);
                _cache.Add(key, entry);
                entry.LruNode = _cacheLru.AddFirst(key);
                _cacheMisses++;
            }
            else
            {
                _cacheHits++;
                TouchEntry(entry);
                var solution = entry.Workspace.CurrentSolution;
                var changedFiles = new List<RoslynScriptSourceFile>();
                foreach (var file in files)
                {
                    if (entry.Sources.TryGetValue(file.FileName, out var previous)
                        && string.Equals(previous, file.Source, StringComparison.Ordinal))
                        continue;
                    solution = solution.WithDocumentText(
                        entry.DocumentIds[file.FileName],
                        SourceText.From(file.Source, Encoding.UTF8),
                        PreservationMode.PreserveIdentity);
                    changedFiles.Add(file);
                }
                if (changedFiles.Count > 0)
                {
                    if (!entry.Workspace.TryApplyChanges(solution))
                        throw new InvalidOperationException("无法增量更新脚本 Workspaces 项目。");
                    foreach (var file in changedFiles) entry.Sources[file.FileName] = file.Source;
                }
                _incrementalDocumentUpdates += changedFiles.Count;
            }

            entry.ActiveLeases++;
            TrimWorkspaceCache(key);
            var project = entry.Workspace.CurrentSolution.GetProject(entry.ProjectId)
                          ?? throw new InvalidOperationException("无法加载脚本 Workspaces 项目。");
            return new WorkspaceContext(
                project,
                files,
                entry.DocumentIds,
                sourceProject.ComputeSourceRevision(),
                () => ReleaseEntry(entry));
        }
    }

    private static WorkspaceCacheEntry CreateCacheEntry(
        string key,
        IReadOnlyList<RoslynScriptSourceFile> files,
        ScriptEnvironmentSnapshot snapshot)
    {
        var workspace = new AdhocWorkspace(HostServices.Value);
        var projectId = ProjectId.CreateNewId("ScriptProject");
        var projectInfo = ProjectInfo.Create(
            projectId,
            VersionStamp.Create(),
            "ScriptProject",
            "ScriptProject",
            LanguageNames.CSharp,
            parseOptions: RoslynCompilationFactory.ParseOptions,
            compilationOptions: new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release,
                nullableContextOptions: NullableContextOptions.Enable,
                allowUnsafe: false));
        var solution = workspace.CurrentSolution.AddProject(projectInfo);
        foreach (var reference in snapshot.MetadataReferences)
            solution = solution.AddMetadataReference(projectId, reference);

        var documentIds = new Dictionary<string, DocumentId>(StringComparer.OrdinalIgnoreCase);
        var sources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            var documentId = DocumentId.CreateNewId(projectId, file.FileName);
            documentIds[file.FileName] = documentId;
            sources[file.FileName] = file.Source;
            solution = solution.AddDocument(
                documentId,
                file.FileName,
                SourceText.From(file.Source, Encoding.UTF8),
                filePath: file.FileName);
        }
        if (snapshot.Imports.Count > 0)
        {
            var imports = string.Join(Environment.NewLine, snapshot.Imports.Select(item => $"global using {item};"));
            solution = solution.AddDocument(
                DocumentId.CreateNewId(projectId, "ScriptEngine.Imports.g.cs"),
                "ScriptEngine.Imports.g.cs",
                SourceText.From(imports, Encoding.UTF8),
                filePath: "ScriptEngine.Imports.g.cs");
        }
        if (!workspace.TryApplyChanges(solution))
        {
            workspace.Dispose();
            throw new InvalidOperationException("无法创建脚本 Workspaces 项目。");
        }
        return new WorkspaceCacheEntry(key, workspace, projectId, documentIds, sources);
    }

    private void TouchEntry(WorkspaceCacheEntry entry)
    {
        if (entry.LruNode is not null) _cacheLru.Remove(entry.LruNode);
        entry.LruNode = _cacheLru.AddFirst(entry.Key);
    }

    private void TrimWorkspaceCache(string protectedKey)
    {
        while (_cache.Count > _options.CacheCapacity)
        {
            var node = _cacheLru.Last;
            while (node is not null && string.Equals(node.Value, protectedKey, StringComparison.Ordinal))
                node = node.Previous;
            if (node is null) return;
            _cacheLru.Remove(node);
            var entry = _cache[node.Value];
            _cache.Remove(node.Value);
            entry.Evicted = true;
            if (entry.ActiveLeases == 0) entry.Workspace.Dispose();
        }
    }

    private void ReleaseEntry(WorkspaceCacheEntry entry)
    {
        lock (_cacheGate)
        {
            if (entry.ActiveLeases > 0) entry.ActiveLeases--;
            if (entry.Evicted && entry.ActiveLeases == 0) entry.Workspace.Dispose();
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(RoslynScriptWorkspaceService));
    }

    /// <summary>释放常驻 Workspace 缓存；正在执行的操作会在归还租约后释放。</summary>
    public void Dispose()
    {
        lock (_cacheGate)
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var entry in _cache.Values)
            {
                entry.Evicted = true;
                if (entry.ActiveLeases == 0) entry.Workspace.Dispose();
            }
            _cache.Clear();
            _cacheLru.Clear();
        }
    }

    private static RoslynScriptCompletionKind GetCompletionKind(IEnumerable<string> tags)
    {
        var values = tags as ISet<string> ?? new HashSet<string>(tags, StringComparer.OrdinalIgnoreCase);
        if (values.Contains("Class")) return RoslynScriptCompletionKind.Class;
        if (values.Contains("Interface")) return RoslynScriptCompletionKind.Interface;
        if (values.Contains("Structure")) return RoslynScriptCompletionKind.Struct;
        if (values.Contains("Enum")) return RoslynScriptCompletionKind.Enum;
        if (values.Contains("Delegate")) return RoslynScriptCompletionKind.Delegate;
        if (values.Contains("Method") || values.Contains("ExtensionMethod")) return RoslynScriptCompletionKind.Method;
        if (values.Contains("Property")) return RoslynScriptCompletionKind.Property;
        if (values.Contains("Field")) return RoslynScriptCompletionKind.Field;
        if (values.Contains("Constant")) return RoslynScriptCompletionKind.Constant;
        if (values.Contains("Event")) return RoslynScriptCompletionKind.Event;
        if (values.Contains("Namespace")) return RoslynScriptCompletionKind.Namespace;
        if (values.Contains("Keyword")) return RoslynScriptCompletionKind.Keyword;
        if (values.Contains("Local") || values.Contains("Parameter") || values.Contains("RangeVariable"))
            return RoslynScriptCompletionKind.Variable;
        return RoslynScriptCompletionKind.None;
    }

    private static bool IsCompletionAllowed(
        RoslynScriptCompletionItem item,
        RoslynScriptSecurityPolicy policy)
    {
        var text = item.Description ?? item.DisplayText;
        return !policy.DeniedNamespacePrefixes.Any(denied =>
                   text.IndexOf(denied, StringComparison.Ordinal) >= 0)
               && !policy.DeniedSymbolPrefixes.Any(denied =>
                   text.IndexOf(denied, StringComparison.Ordinal) >= 0);
    }

    private sealed class WorkspaceCacheEntry
    {
        public WorkspaceCacheEntry(
            string key,
            AdhocWorkspace workspace,
            ProjectId projectId,
            IReadOnlyDictionary<string, DocumentId> documentIds,
            Dictionary<string, string> sources)
        {
            Key = key;
            Workspace = workspace;
            ProjectId = projectId;
            DocumentIds = documentIds;
            Sources = sources;
        }

        public string Key { get; }
        public AdhocWorkspace Workspace { get; }
        public ProjectId ProjectId { get; }
        public IReadOnlyDictionary<string, DocumentId> DocumentIds { get; }
        public Dictionary<string, string> Sources { get; }
        public LinkedListNode<string>? LruNode { get; set; }
        public int ActiveLeases { get; set; }
        public bool Evicted { get; set; }
    }

    private sealed class WorkspaceContext : IDisposable
    {
        private readonly IReadOnlyList<RoslynScriptSourceFile> _files;
        private readonly IReadOnlyDictionary<string, DocumentId> _documentIds;
        private readonly string _originalProjectRevision;
        private Action? _release;

        public WorkspaceContext(
            Project project,
            IReadOnlyList<RoslynScriptSourceFile> files,
            IReadOnlyDictionary<string, DocumentId> documentIds,
            string originalProjectRevision,
            Action release)
        {
            Project = project;
            _files = files;
            _documentIds = documentIds;
            _originalProjectRevision = originalProjectRevision;
            _release = release;
        }

        public Project Project { get; }

        public Document GetDocument(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName) || !_documentIds.TryGetValue(fileName, out var documentId))
                throw new ArgumentException($"脚本项目中不存在文件：{fileName}", nameof(fileName));
            return Project.Solution.GetDocument(documentId)
                   ?? throw new InvalidOperationException($"无法加载脚本文件：{fileName}");
        }

        public async Task<RoslynScriptWorkspaceEdit?> CreateEditAsync(
            Solution changedSolution,
            string title,
            RoslynScriptWorkspaceEditKind kind,
            string? diagnosticId,
            CancellationToken cancellationToken)
        {
            var changedFiles = new List<string>();
            var outputFiles = new List<RoslynScriptSourceFile>(_files.Count);
            var hasConflicts = false;
            foreach (var file in _files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var id = _documentIds[file.FileName];
                var document = changedSolution.GetDocument(id)
                               ?? throw new InvalidOperationException($"编辑结果缺少脚本文件：{file.FileName}");
                var source = (await document.GetTextAsync(cancellationToken).ConfigureAwait(false)).ToString();
                var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
                hasConflicts |= root?.GetAnnotatedNodesAndTokens(ConflictAnnotation.Kind).Any() == true;
                outputFiles.Add(new RoslynScriptSourceFile(file.FileName, source));
                if (!string.Equals(file.Source, source, StringComparison.Ordinal)) changedFiles.Add(file.FileName);
            }
            if (changedFiles.Count == 0) return null;
            var originalCompilation = await Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false);
            var changedCompilation = await changedSolution.GetProject(Project.Id)!
                .GetCompilationAsync(cancellationToken).ConfigureAwait(false);
            if (originalCompilation is not null && changedCompilation is not null)
            {
                var originalErrors = originalCompilation.GetDiagnostics(cancellationToken)
                    .Where(item => item.Severity == DiagnosticSeverity.Error)
                    .GroupBy(item => item.Id, StringComparer.Ordinal)
                    .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
                hasConflicts |= changedCompilation.GetDiagnostics(cancellationToken)
                    .Where(item => item.Severity == DiagnosticSeverity.Error)
                    .GroupBy(item => item.Id, StringComparer.Ordinal)
                    .Any(group => group.Count() > (originalErrors.TryGetValue(group.Key, out var count) ? count : 0));
            }
            var fileChanges = outputFiles
                .Join(
                    _files,
                    changed => changed.FileName,
                    original => original.FileName,
                    (changed, original) => CreateFileChange(original, changed),
                    StringComparer.OrdinalIgnoreCase)
                .Where(change => change is not null)
                .Cast<RoslynScriptWorkspaceFileChange>()
                .ToArray();
            return new RoslynScriptWorkspaceEdit(
                title,
                kind,
                new RoslynScriptProject { SourceFiles = outputFiles },
                changedFiles,
                diagnosticId,
                hasConflicts,
                _originalProjectRevision,
                fileChanges);
        }

        private static RoslynScriptWorkspaceFileChange? CreateFileChange(
            RoslynScriptSourceFile original,
            RoslynScriptSourceFile changed)
        {
            if (string.Equals(original.Source, changed.Source, StringComparison.Ordinal)) return null;
            var prefix = 0;
            var limit = Math.Min(original.Source.Length, changed.Source.Length);
            while (prefix < limit && original.Source[prefix] == changed.Source[prefix]) prefix++;
            var originalSuffix = original.Source.Length;
            var changedSuffix = changed.Source.Length;
            while (originalSuffix > prefix && changedSuffix > prefix
                   && original.Source[originalSuffix - 1] == changed.Source[changedSuffix - 1])
            {
                originalSuffix--;
                changedSuffix--;
            }
            return new RoslynScriptWorkspaceFileChange(
                original.FileName,
                original.Source,
                changed.Source,
                prefix,
                originalSuffix - prefix,
                changed.Source.Substring(prefix, changedSuffix - prefix));
        }

        public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
    }
}
