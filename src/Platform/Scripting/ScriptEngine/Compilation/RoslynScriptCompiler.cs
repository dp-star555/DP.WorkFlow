using System.Collections.Concurrent;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;
using ScriptEngine.Language;

namespace ScriptEngine.Compilation;

/// <summary>只负责 Roslyn 编译和入口契约验证；编译阶段不会加载脚本程序集。</summary>
internal sealed class RoslynScriptCompiler
{
    private readonly ConcurrentDictionary<string, RoslynScriptCompilationResult> _completedCache = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<string> _insertionOrder = new();
    private readonly RoslynScriptAnalysisProvider _analysisProvider;
    private readonly int _cacheCapacity;

    public RoslynScriptCompiler(RoslynScriptAnalysisProvider analysisProvider, int cacheCapacity)
    {
        _analysisProvider = analysisProvider ?? throw new ArgumentNullException(nameof(analysisProvider));
        if (cacheCapacity < 1) throw new ArgumentOutOfRangeException(nameof(cacheCapacity));
        _cacheCapacity = cacheCapacity;
    }

    /// <summary>异步编译脚本。</summary>
    /// <param name="source">完整 C# 程序源码。</param>
    /// <param name="environment">不可变编译环境。</param>
    /// <param name="cancellationToken">取消编译的令牌。</param>
    /// <returns>结构化诊断和可选编译产物。</returns>
    public Task<RoslynScriptCompilationResult> CompileAsync(
        string source,
        ScriptEnvironmentSnapshot environment,
        CancellationToken cancellationToken) => CompileProjectAsync(
            new[] { new RoslynScriptSourceFile("Program.cs", source ?? string.Empty) },
            environment,
            cancellationToken);

    public async Task<RoslynScriptCompilationResult> CompileProjectAsync(
        IReadOnlyList<RoslynScriptSourceFile> files,
        ScriptEnvironmentSnapshot environment,
        CancellationToken cancellationToken)
    {
        var revision = environment.ComputeProjectRevision(files);
        if (_completedCache.TryGetValue(revision, out var cached)) return cached;

        var result = await Task.Run(
            () => CompileCore(files, environment, revision, cancellationToken),
            cancellationToken).ConfigureAwait(false);
        if (_completedCache.TryAdd(revision, result))
        {
            _insertionOrder.Enqueue(revision);
            TrimCache();
        }
        return result;
    }

    /// <summary>移除已经不再被任何脚本版本使用的编译缓存。</summary>
    /// <param name="revision">待移除的脚本修订。</param>
    public void RemoveCachedRevision(string revision)
    {
        _completedCache.TryRemove(revision, out _);
        _analysisProvider.Remove(revision);
    }

    private RoslynScriptCompilationResult CompileCore(
        IReadOnlyList<RoslynScriptSourceFile> files,
        ScriptEnvironmentSnapshot environment,
        string revision,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (files.All(file => string.IsNullOrWhiteSpace(file.Source)))
        {
            return new RoslynScriptCompilationResult(revision, null, new[]
            {
                new RoslynScriptDiagnostic(
                    "CS0000",
                    DiagnosticSeverity.Error,
                    "C# 源代码不能为空。",
                    0,
                    0,
                    SourceName: files[0].FileName,
                    Origin: RoslynScriptDiagnosticOrigin.UserSource)
            });
        }

        var assemblyName = "DynamicCSharpProgram_" + revision.Substring(0, 16);
        var analysis = _analysisProvider.GetProject(files, environment, cancellationToken);
        var compilation = analysis.Compilation;
        var compilationDiagnostics = RoslynDiagnosticMapper.Map(compilation.GetDiagnostics(cancellationToken))
            .Concat(RoslynScriptSecurityPolicyEvaluator.Evaluate(
                analysis,
                environment.SecurityPolicy,
                cancellationToken))
            .ToArray();
        if (compilationDiagnostics.Any(item => item.Severity == DiagnosticSeverity.Error))
            return new RoslynScriptCompilationResult(revision, null, compilationDiagnostics);

        var entryValidation = FindEntryType(compilation, cancellationToken);
        if (entryValidation.Diagnostic is not null)
            return new RoslynScriptCompilationResult(
                revision,
                null,
                compilationDiagnostics.Concat(new[] { entryValidation.Diagnostic }).ToArray());

        using var pe = new MemoryStream();
        using var pdb = new MemoryStream();
        var emitOptions = new EmitOptions(debugInformationFormat: DebugInformationFormat.PortablePdb);
        var emit = compilation.Emit(
            peStream: pe,
            pdbStream: pdb,
            options: emitOptions,
            cancellationToken: cancellationToken);
        var emitDiagnostics = RoslynDiagnosticMapper.Map(emit.Diagnostics);
        var diagnostics = compilationDiagnostics
            .Concat(emitDiagnostics)
            .GroupBy(item => new { item.Id, item.Message, item.Start, item.Length, item.SourceName })
            .Select(group => group.First())
            .ToArray();
        if (!emit.Success)
            return new RoslynScriptCompilationResult(revision, null, diagnostics);

        var artifact = new RoslynScriptCompilationArtifact(
            revision,
            revision,
            assemblyName,
            entryValidation.EntryTypeName!,
            pe.ToArray(),
            pdb.ToArray(),
            environment.RuntimeReferencePaths);
        return new RoslynScriptCompilationResult(revision, artifact, diagnostics);
    }

    private void TrimCache()
    {
        while (_completedCache.Count > _cacheCapacity && _insertionOrder.TryDequeue(out var revision))
            _completedCache.TryRemove(revision, out _);
    }

    private static EntryValidationResult FindEntryType(CSharpCompilation compilation, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var contract = compilation.GetTypeByMetadataName(typeof(ICSharpProgram).FullName!);
        if (contract is null)
        {
            return EntryValidationResult.Failed(new RoslynScriptDiagnostic(
                "SE0003",
                DiagnosticSeverity.Error,
                $"无法解析脚本入口契约 {typeof(ICSharpProgram).FullName}。",
                0,
                0,
                Origin: RoslynScriptDiagnosticOrigin.Infrastructure));
        }

        var candidates = EnumerateTypes(compilation.Assembly.GlobalNamespace)
            .Where(type => type.TypeKind == TypeKind.Class
                           && !type.IsAbstract
                           && type.AllInterfaces.Any(item => SymbolEqualityComparer.Default.Equals(item, contract)))
            .ToArray();
        if (candidates.Length != 1)
        {
            var message = candidates.Length == 0
                ? $"源代码必须包含一个实现 {nameof(ICSharpProgram)} 的非抽象类型。"
                : $"源代码只能包含一个实现 {nameof(ICSharpProgram)} 的非抽象类型。";
            return EntryValidationResult.Failed(new RoslynScriptDiagnostic(
                "SE0001",
                DiagnosticSeverity.Error,
                message,
                0,
                0,
                Origin: RoslynScriptDiagnosticOrigin.Infrastructure));
        }

        var entry = candidates[0];
        var hasPublicParameterlessConstructor = entry.InstanceConstructors.Any(constructor =>
            constructor.Parameters.Length == 0
            && constructor.DeclaredAccessibility == Accessibility.Public);
        if (!hasPublicParameterlessConstructor)
        {
            return EntryValidationResult.Failed(new RoslynScriptDiagnostic(
                "SE0002",
                DiagnosticSeverity.Error,
                "C# 程序必须具有公共无参数构造函数。",
                0,
                0,
                Origin: RoslynScriptDiagnosticOrigin.Infrastructure));
        }

        return EntryValidationResult.Succeeded(GetReflectionTypeName(entry));
    }

    private static string GetReflectionTypeName(INamedTypeSymbol type)
    {
        var nestedNames = new Stack<string>();
        for (var current = type; current is not null; current = current.ContainingType)
            nestedNames.Push(current.MetadataName);
        var typeName = string.Join("+", nestedNames);
        return type.ContainingNamespace is { IsGlobalNamespace: false } containingNamespace
            ? containingNamespace.ToDisplayString() + "." + typeName
            : typeName;
    }

    private static IEnumerable<INamedTypeSymbol> EnumerateTypes(INamespaceSymbol root)
    {
        foreach (var type in root.GetTypeMembers())
        {
            yield return type;
            foreach (var nested in EnumerateNestedTypes(type)) yield return nested;
        }
        foreach (var child in root.GetNamespaceMembers())
            foreach (var type in EnumerateTypes(child)) yield return type;
    }

    private static IEnumerable<INamedTypeSymbol> EnumerateNestedTypes(INamedTypeSymbol root)
    {
        foreach (var type in root.GetTypeMembers())
        {
            yield return type;
            foreach (var nested in EnumerateNestedTypes(type)) yield return nested;
        }
    }

    private sealed record EntryValidationResult(string? EntryTypeName, RoslynScriptDiagnostic? Diagnostic)
    {
        public static EntryValidationResult Succeeded(string entryTypeName) => new(entryTypeName, null);
        public static EntryValidationResult Failed(RoslynScriptDiagnostic diagnostic) => new(null, diagnostic);
    }
}

/// <summary>创建编译器和语言服务共同使用的 Roslyn Compilation。</summary>
internal static class RoslynCompilationFactory
{
    public static CSharpParseOptions ParseOptions { get; } = new(
        LanguageVersion.Latest,
        kind: SourceCodeKind.Regular,
        documentationMode: DocumentationMode.Parse);

    public static SyntaxTree ParseUserSource(RoslynScriptSourceFile file, SyntaxTree? previousTree = null)
    {
        var source = file.Source ?? string.Empty;
        if (previousTree is null || !string.Equals(previousTree.FilePath, file.FileName, StringComparison.OrdinalIgnoreCase))
            return CSharpSyntaxTree.ParseText(SourceText.From(source, Encoding.UTF8), ParseOptions, file.FileName);

        var previousText = previousTree.GetText();
        if (string.Equals(previousText.ToString(), source, StringComparison.Ordinal)) return previousTree;
        var prefix = 0;
        var sharedLength = Math.Min(previousText.Length, source.Length);
        while (prefix < sharedLength && previousText[prefix] == source[prefix]) prefix++;
        var suffix = 0;
        while (suffix < sharedLength - prefix
               && previousText[previousText.Length - suffix - 1] == source[source.Length - suffix - 1])
            suffix++;
        var changedText = previousText.WithChanges(new TextChange(
            new TextSpan(prefix, previousText.Length - prefix - suffix),
            source.Substring(prefix, source.Length - prefix - suffix)));
        return previousTree.WithChangedText(changedText);
    }

    public static CSharpCompilation Create(
        IReadOnlyList<SyntaxTree> userTrees,
        ScriptEnvironmentSnapshot environment,
        string assemblyName)
    {
        var importsSource = string.Join(
            Environment.NewLine,
            environment.Imports.Select(item => $"global using {item};"));
        var importsTree = CSharpSyntaxTree.ParseText(
            SourceText.From(importsSource, Encoding.UTF8),
            ParseOptions,
            "ScriptEngine.Imports.g.cs");
        var options = new CSharpCompilationOptions(
            OutputKind.DynamicallyLinkedLibrary,
            optimizationLevel: OptimizationLevel.Release,
            nullableContextOptions: NullableContextOptions.Enable,
            deterministic: true,
            allowUnsafe: false,
            specificDiagnosticOptions: new Dictionary<string, ReportDiagnostic>
            {
                ["CS1998"] = ReportDiagnostic.Suppress
            });
        var compilation = CSharpCompilation.Create(
            assemblyName,
            new[] { importsTree }.Concat(userTrees),
            environment.MetadataReferences,
            options);
        return NetFrameworkCompatibilitySource.AddMissingTypes(compilation, ParseOptions);
    }
}

/// <summary>统一把 Roslyn 诊断映射为可跨 UI 使用的数据。</summary>
internal static class RoslynDiagnosticMapper
{
    public static IReadOnlyList<RoslynScriptDiagnostic> Map(IEnumerable<Diagnostic> diagnostics) => diagnostics
        .Where(item => item.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning)
        .Select(item =>
        {
            var span = item.Location.IsInSource ? item.Location.SourceSpan : default;
            var lineSpan = item.Location.IsInSource ? item.Location.GetLineSpan().StartLinePosition : default;
            var sourceName = item.Location.IsInSource ? item.Location.SourceTree?.FilePath : null;
            var origin = !item.Location.IsInSource
                ? RoslynScriptDiagnosticOrigin.Infrastructure
                : sourceName?.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase) == true
                    ? RoslynScriptDiagnosticOrigin.GeneratedSource
                    : RoslynScriptDiagnosticOrigin.UserSource;
            return new RoslynScriptDiagnostic(
                item.Id,
                item.Severity,
                item.GetMessage(),
                span.Start,
                span.Length,
                item.Location.IsInSource ? lineSpan.Line + 1 : 0,
                item.Location.IsInSource ? lineSpan.Character + 1 : 0,
                sourceName,
                origin);
        })
        .ToArray();
}
