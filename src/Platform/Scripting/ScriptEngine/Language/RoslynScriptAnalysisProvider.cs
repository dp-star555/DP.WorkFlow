using System.Collections.Concurrent;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using ScriptEngine.Compilation;

namespace ScriptEngine.Language;

/// <summary>
/// 按“源码文件集合 + 环境”修订复用 Roslyn Compilation、语法根和 SemanticModel。
/// 新修订通过 SyntaxTree.WithChangedText 增量解析；诊断、补全、签名帮助、快速信息和高亮共享结果。
/// </summary>
internal sealed class RoslynScriptAnalysisProvider
{
    private readonly ConcurrentDictionary<string, Lazy<RoslynScriptAnalysisSnapshot>> _snapshots = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, SyntaxTree> _latestTrees = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentQueue<string> _insertionOrder = new();
    private readonly int _capacity;
    private long _hits;
    private long _misses;
    private long _incrementalParses;

    public RoslynScriptAnalysisProvider(int capacity)
    {
        if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
        _capacity = capacity;
    }

    public RoslynScriptAnalysisSnapshot Get(
        string? source,
        ScriptEnvironmentSnapshot environment,
        CancellationToken cancellationToken) => GetProject(
            new[] { new RoslynScriptSourceFile("Program.cs", source ?? string.Empty) },
            environment,
            cancellationToken);

    public RoslynScriptAnalysisSnapshot GetProject(
        IReadOnlyList<RoslynScriptSourceFile> files,
        ScriptEnvironmentSnapshot environment,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var revision = environment.ComputeProjectRevision(files);
        var created = new Lazy<RoslynScriptAnalysisSnapshot>(
            () => Create(files, environment, revision),
            LazyThreadSafetyMode.ExecutionAndPublication);
        var selected = _snapshots.GetOrAdd(revision, created);
        if (ReferenceEquals(created, selected))
        {
            Interlocked.Increment(ref _misses);
            _insertionOrder.Enqueue(revision);
            Trim();
        }
        else
        {
            Interlocked.Increment(ref _hits);
        }

        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            return selected.Value;
        }
        catch
        {
            _snapshots.TryRemove(revision, out _);
            throw;
        }
    }

    public RoslynScriptCacheStatistics GetStatistics() => new(
        Interlocked.Read(ref _hits),
        Interlocked.Read(ref _misses),
        _snapshots.Count,
        Interlocked.Read(ref _incrementalParses));

    public void Remove(string revision) => _snapshots.TryRemove(revision, out _);

    public void Clear()
    {
        _snapshots.Clear();
        _latestTrees.Clear();
        while (_insertionOrder.TryDequeue(out _)) { }
    }

    private RoslynScriptAnalysisSnapshot Create(
        IReadOnlyList<RoslynScriptSourceFile> files,
        ScriptEnvironmentSnapshot environment,
        string revision)
    {
        var trees = new List<SyntaxTree>(files.Count);
        foreach (var file in files)
        {
            var treeKey = environment.Fingerprint + "|" + file.FileName;
            _latestTrees.TryGetValue(treeKey, out var previous);
            if (previous is not null && !string.Equals(previous.GetText().ToString(), file.Source, StringComparison.Ordinal))
                Interlocked.Increment(ref _incrementalParses);
            var tree = RoslynCompilationFactory.ParseUserSource(file, previous);
            _latestTrees[treeKey] = tree;
            trees.Add(tree);
        }
        var compilation = RoslynCompilationFactory.Create(
            trees,
            environment,
            "DynamicCSharpProgram_" + revision.Substring(0, 16));
        var documents = trees.Select(tree =>
        {
            var root = tree.GetCompilationUnitRoot();
            var semanticModel = compilation.GetSemanticModel(tree, ignoreAccessibility: true);
            return new RoslynScriptAnalysisDocument(tree.FilePath, tree, root, semanticModel);
        }).ToArray();
        return new RoslynScriptAnalysisSnapshot(revision, compilation, documents);
    }

    private void Trim()
    {
        while (_snapshots.Count > _capacity && _insertionOrder.TryDequeue(out var revision))
            _snapshots.TryRemove(revision, out _);
        // 每个快照通常只有一个编辑文件；为多文件项目保留有限余量，避免文件名不断变化造成增长。
        var treeCapacity = Math.Max(_capacity * 8, 32);
        if (_latestTrees.Count <= treeCapacity) return;
        foreach (var key in _latestTrees.Keys.Take(_latestTrees.Count - treeCapacity))
            _latestTrees.TryRemove(key, out _);
    }
}

/// <summary>一个源码文件对应的语法和语义分析对象。</summary>
internal sealed record RoslynScriptAnalysisDocument(
    string FileName,
    SyntaxTree Tree,
    CompilationUnitSyntax Root,
    SemanticModel SemanticModel);

/// <summary>一个项目修订共享的不可变 Roslyn 分析对象。</summary>
internal sealed class RoslynScriptAnalysisSnapshot
{
    public RoslynScriptAnalysisSnapshot(
        string revision,
        CSharpCompilation compilation,
        IReadOnlyList<RoslynScriptAnalysisDocument> documents)
    {
        if (documents.Count == 0) throw new ArgumentException("分析快照必须包含源码文件。", nameof(documents));
        Revision = revision;
        Compilation = compilation;
        Documents = documents;
    }

    public string Revision { get; }
    public CSharpCompilation Compilation { get; }
    public IReadOnlyList<RoslynScriptAnalysisDocument> Documents { get; }
    public RoslynScriptAnalysisDocument PrimaryDocument => Documents[0];
    public SyntaxTree UserTree => PrimaryDocument.Tree;
    public CompilationUnitSyntax Root => PrimaryDocument.Root;
    public SemanticModel SemanticModel => PrimaryDocument.SemanticModel;
}
