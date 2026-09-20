using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using ScriptEngine.Compilation;
using ScriptEngine.Language;
using ScriptEngine.Runtime;

namespace ScriptEngine;

/// <summary>
/// 独立 C# 脚本中间件门面，统一提供编译、热版本执行、诊断、补全和分类能力。
/// 编译阶段不会加载程序集；同一脚本修订会保持热加载，修订变化时再替换旧版本。
/// </summary>
public sealed class RoslynScriptService : IDisposable
{
    private readonly RoslynScriptEnvironment _defaultEnvironment = new();
    private readonly RoslynScriptAnalysisProvider _analysisProvider;
    private readonly RoslynScriptCompiler _compiler;
    private readonly RoslynScriptLanguageService _languageService;
    private readonly RoslynScriptEditingService _editingService = new();
    private readonly ScriptRuntimeRegistry _runtimeRegistry;
    private readonly ConditionalWeakTable<RoslynScriptEnvironment, StableEnvironmentSnapshotCache> _stableSnapshots = new();
    private bool _disposed;

    /// <summary>进程内共享的默认脚本服务，适合标准节点处理器复用热点脚本。</summary>
    public static RoslynScriptService Shared { get; } = new();

    /// <summary>初始化脚本服务并关联有界分析缓存与旧修订清理。</summary>
    /// <param name="options">缓存容量配置；为 null 时采用适合桌面编辑器的默认值。</param>
    public RoslynScriptService(RoslynScriptServiceOptions? options = null)
    {
        var effectiveOptions = options ?? new RoslynScriptServiceOptions();
        _analysisProvider = new RoslynScriptAnalysisProvider(effectiveOptions.AnalysisCacheCapacity);
        _compiler = new RoslynScriptCompiler(_analysisProvider, effectiveOptions.CompilationCacheCapacity);
        _languageService = new RoslynScriptLanguageService(_analysisProvider);
        _runtimeRegistry = new ScriptRuntimeRegistry(effectiveOptions.RuntimeSlotCapacity);
        _runtimeRegistry.RevisionUnloaded += _compiler.RemoveCachedRevision;
    }

    /// <summary>同步获取源码诊断；UI 应优先使用 <see cref="GetDiagnosticsAsync"/>。</summary>
    public IReadOnlyList<RoslynScriptDiagnostic> GetDiagnostics(
        string? source,
        RoslynScriptEnvironment? environment = null)
    {
        ThrowIfDisposed();
        try
        {
            return _languageService.GetDiagnostics(
                source,
                CreateSnapshot(environment),
                CancellationToken.None);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new[] { CreateEnvironmentDiagnostic(exception) };
        }
    }

    /// <summary>异步获取源码诊断，避免阻塞桌面 UI。</summary>
    public async Task<IReadOnlyList<RoslynScriptDiagnostic>> GetDiagnosticsAsync(
        string? source,
        RoslynScriptEnvironment? environment = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        try
        {
            // 首次环境快照可能需要读取平台和外部程序集元数据，必须与诊断本身一起移出 UI 线程。
            var snapshot = await Task.Run(
                () => CreateSnapshot(environment),
                cancellationToken).ConfigureAwait(false);
            return await _languageService.GetDiagnosticsAsync(source, snapshot, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new[] { CreateEnvironmentDiagnostic(exception) };
        }
    }

    /// <summary>异步获取多文件脚本项目的联合诊断。</summary>
    public async Task<IReadOnlyList<RoslynScriptDiagnostic>> GetProjectDiagnosticsAsync(
        RoslynScriptProject project,
        RoslynScriptEnvironment? environment = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (project is null) throw new ArgumentNullException(nameof(project));
        try
        {
            var files = project.GetValidatedFiles();
            var snapshot = await Task.Run(() => CreateSnapshot(environment), cancellationToken).ConfigureAwait(false);
            return await Task.Run(
                () => _languageService.GetProjectDiagnostics(files, snapshot, cancellationToken),
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new[] { CreateEnvironmentDiagnostic(exception) };
        }
    }

    /// <summary>执行完整 Emit，但不加载程序集；适合编辑器“编译”按钮调用。</summary>
    public IReadOnlyList<RoslynScriptDiagnostic> CompileProgram(
        string source,
        RoslynScriptEnvironment? environment = null) =>
        CompileProgramAsync(source, environment).GetAwaiter().GetResult().Diagnostics;

    /// <summary>异步编译完整 C# 程序，成功时返回可执行产物。</summary>
    public async Task<RoslynScriptCompilationResult> CompileProgramAsync(
        string source,
        RoslynScriptEnvironment? environment = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        try
        {
            var snapshot = CreateSnapshot(environment);
            return await _compiler.CompileAsync(source, snapshot, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            var revision = ScriptEnvironmentSnapshot.ComputeHash(source ?? string.Empty);
            return new RoslynScriptCompilationResult(
                revision,
                null,
                new[] { CreateEnvironmentDiagnostic(exception) });
        }
    }

    /// <summary>异步编译多文件 C# 脚本项目，成功时返回统一可执行产物。</summary>
    public async Task<RoslynScriptCompilationResult> CompileProjectAsync(
        RoslynScriptProject project,
        RoslynScriptEnvironment? environment = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (project is null) throw new ArgumentNullException(nameof(project));
        var files = project.GetValidatedFiles();
        try
        {
            var snapshot = CreateSnapshot(environment);
            return await _compiler.CompileProjectAsync(files, snapshot, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            var material = string.Join("\n", files.Select(file => file.FileName + "\n" + file.Source));
            return new RoslynScriptCompilationResult(
                ScriptEnvironmentSnapshot.ComputeHash(material),
                null,
                new[] { CreateEnvironmentDiagnostic(exception) });
        }
    }

    /// <summary>按稳定脚本标识编译并执行多文件项目。</summary>
    public async Task<RoslynScriptExecutionResult> ExecuteProjectAsync(
        string scriptId,
        RoslynScriptProject project,
        object? hostContext = null,
        RoslynScriptEnvironment? environment = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(scriptId)) throw new ArgumentException("脚本标识不能为空。", nameof(scriptId));
        var effectiveEnvironment = environment ?? _defaultEnvironment;
        var compilation = await CompileProjectAsync(project, effectiveEnvironment, cancellationToken).ConfigureAwait(false);
        return await ExecuteCompiledAsync(
            scriptId,
            compilation,
            hostContext,
            effectiveEnvironment,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 执行匿名脚本。匿名脚本按修订缓存，无法表达“同一逻辑脚本被新版本替换”；
    /// 工作流节点应使用包含稳定脚本标识的重载。
    /// </summary>
    public async Task<RoslynScriptExecutionResult> ExecuteAsync(
        string source,
        object? hostContext = null,
        RoslynScriptEnvironment? environment = null,
        CancellationToken cancellationToken = default)
    {
        var compilation = await CompileProgramAsync(source, environment, cancellationToken).ConfigureAwait(false);
        var scriptId = "anonymous:" + compilation.Revision;
        return await ExecuteCompiledAsync(
            scriptId,
            compilation,
            hostContext,
            environment ?? _defaultEnvironment,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>按稳定脚本标识执行程序；修订变化时会原子替换旧热版本。</summary>
    public async Task<RoslynScriptExecutionResult> ExecuteAsync(
        string scriptId,
        string source,
        object? hostContext = null,
        RoslynScriptEnvironment? environment = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(scriptId)) throw new ArgumentException("脚本标识不能为空。", nameof(scriptId));
        var effectiveEnvironment = environment ?? _defaultEnvironment;
        var compilation = await CompileProgramAsync(source, effectiveEnvironment, cancellationToken).ConfigureAwait(false);
        return await ExecuteCompiledAsync(
            scriptId,
            compilation,
            hostContext,
            effectiveEnvironment,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>移除指定脚本的当前热版本；活动执行结束后再完成卸载。</summary>
    public void RemoveProgram(string scriptId)
    {
        ThrowIfDisposed();
        _runtimeRegistry.Remove(scriptId);
    }

    /// <summary>异步获取指定光标位置的语义补全项。</summary>
    public async Task<IReadOnlyList<RoslynScriptCompletionItem>> GetCompletionsAsync(
        string source,
        int caretIndex,
        RoslynScriptEnvironment? environment = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var effectiveEnvironment = environment ?? _defaultEnvironment;
        try
        {
            var snapshot = CreateSnapshot(effectiveEnvironment);
            return await _languageService.GetCompletionsAsync(
                source,
                caretIndex,
                snapshot,
                effectiveEnvironment.ContextItems,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            System.Diagnostics.Debug.WriteLine($"脚本补全分析失败：{exception}");
            return Array.Empty<RoslynScriptCompletionItem>();
        }
    }

    /// <summary>获取当前 Roslyn 分析快照缓存的命中和容量统计。</summary>
    public RoslynScriptCacheStatistics GetCacheStatistics()
    {
        ThrowIfDisposed();
        return _analysisProvider.GetStatistics();
    }

    /// <summary>异步获取光标所在调用的方法签名和活动参数。</summary>
    public async Task<RoslynScriptSignatureHelp?> GetSignatureHelpAsync(
        string source,
        int caretIndex,
        RoslynScriptEnvironment? environment = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        try
        {
            var snapshot = await Task.Run(() => CreateSnapshot(environment), cancellationToken).ConfigureAwait(false);
            return await _languageService.GetSignatureHelpAsync(
                source ?? string.Empty,
                caretIndex,
                snapshot,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            System.Diagnostics.Debug.WriteLine($"脚本签名分析失败：{exception}");
            return null;
        }
    }

    /// <summary>异步获取指定源码位置的符号签名和 XML 文档摘要。</summary>
    public async Task<RoslynScriptQuickInfo?> GetQuickInfoAsync(
        string source,
        int position,
        RoslynScriptEnvironment? environment = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        try
        {
            var snapshot = await Task.Run(() => CreateSnapshot(environment), cancellationToken).ConfigureAwait(false);
            return await _languageService.GetQuickInfoAsync(
                source ?? string.Empty,
                position,
                snapshot,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            System.Diagnostics.Debug.WriteLine($"脚本快速信息分析失败：{exception}");
            return null;
        }
    }

    /// <summary>异步定位指定位置符号的源码或元数据定义。</summary>
    public async Task<RoslynScriptDefinitionLocation?> GetDefinitionAsync(
        string source,
        int position,
        RoslynScriptEnvironment? environment = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        try
        {
            var snapshot = await Task.Run(() => CreateSnapshot(environment), cancellationToken).ConfigureAwait(false);
            return await _languageService.GetDefinitionAsync(
                source ?? string.Empty,
                position,
                snapshot,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            System.Diagnostics.Debug.WriteLine($"脚本定义定位失败：{exception}");
            return null;
        }
    }

    /// <summary>根据 Roslyn 诊断生成无需 Workspaces 依赖的安全文本修复。</summary>
    public IReadOnlyList<RoslynScriptCodeAction> GetCodeActions(string source, RoslynScriptDiagnostic diagnostic)
    {
        ThrowIfDisposed();
        if (diagnostic is null) throw new ArgumentNullException(nameof(diagnostic));
        return _editingService.GetCodeActions(source ?? string.Empty, diagnostic);
    }

    /// <summary>将代码修复应用到对应源码快照。</summary>
    public static string ApplyCodeAction(string source, RoslynScriptCodeAction action)
    {
        if (source is null) throw new ArgumentNullException(nameof(source));
        if (action is null) throw new ArgumentNullException(nameof(action));
        if (action.Start < 0 || action.Length < 0 || action.Start + action.Length > source.Length)
            throw new ArgumentOutOfRangeException(nameof(action), "代码修复范围超出源码边界。");
        return source.Substring(0, action.Start) + action.Replacement + source.Substring(action.Start + action.Length);
    }

    /// <summary>异步获取多文件项目中活动文件的跨文件语义补全。</summary>
    public async Task<IReadOnlyList<RoslynScriptCompletionItem>> GetProjectCompletionsAsync(
        RoslynScriptProject project,
        string activeFileName,
        int caretIndex,
        RoslynScriptEnvironment? environment = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (project is null) throw new ArgumentNullException(nameof(project));
        var files = project.GetValidatedFiles();
        var effectiveEnvironment = environment ?? _defaultEnvironment;
        try
        {
            var snapshot = await Task.Run(() => CreateSnapshot(effectiveEnvironment), cancellationToken).ConfigureAwait(false);
            return await _languageService.GetProjectCompletionsAsync(
                files,
                activeFileName,
                caretIndex,
                snapshot,
                effectiveEnvironment.ContextItems,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException and not ArgumentException)
        {
            System.Diagnostics.Debug.WriteLine($"多文件脚本补全失败：{exception}");
            return Array.Empty<RoslynScriptCompletionItem>();
        }
    }

    /// <summary>异步定位多文件项目中活动文件符号的跨文件定义。</summary>
    public async Task<RoslynScriptDefinitionLocation?> GetProjectDefinitionAsync(
        RoslynScriptProject project,
        string activeFileName,
        int position,
        RoslynScriptEnvironment? environment = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (project is null) throw new ArgumentNullException(nameof(project));
        var files = project.GetValidatedFiles();
        try
        {
            var snapshot = await Task.Run(() => CreateSnapshot(environment), cancellationToken).ConfigureAwait(false);
            return await _languageService.GetProjectDefinitionAsync(
                files,
                activeFileName,
                position,
                snapshot,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException and not ArgumentException)
        {
            System.Diagnostics.Debug.WriteLine($"多文件脚本定义定位失败：{exception}");
            return null;
        }
    }

    /// <summary>
    /// 获取不依赖程序集元数据的即时语法高亮。该调用适合在 UI 线程首次显示和输入时执行，
    /// 完整的类型、方法和属性颜色仍由 <see cref="GetHighlightSpansAsync"/> 在后台补充。
    /// </summary>
    public IReadOnlyList<RoslynScriptHighlightSpan> GetSyntacticHighlightSpans(
        string source,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return _languageService.GetSyntacticHighlightSpans(source, cancellationToken);
    }

    /// <summary>异步计算源码的完整语义高亮区间。</summary>
    public async Task<IReadOnlyList<RoslynScriptHighlightSpan>> GetHighlightSpansAsync(
        string source,
        RoslynScriptEnvironment? environment = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        try
        {
            // 首次环境快照可能需要读取平台元数据；连同语义分析一起移出 UI 线程。
            var snapshot = await Task.Run(
                () => CreateSnapshot(environment),
                cancellationToken).ConfigureAwait(false);
            return await _languageService.GetHighlightSpansAsync(
                source,
                snapshot,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            System.Diagnostics.Debug.WriteLine($"脚本高亮分析失败：{exception}");
            return Array.Empty<RoslynScriptHighlightSpan>();
        }
    }

    /// <summary>同步计算类、成员、语句块和 region 的可折叠行区间。</summary>
    public IReadOnlyList<RoslynScriptFoldingSpan> GetFoldingSpans(
        string? source,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return _editingService.GetFoldingSpans(source, cancellationToken);
    }

    /// <summary>计算光标所在行应使用的智能缩进列数。</summary>
    /// <param name="source">包含光标所在行的当前完整源码。</param>
    /// <param name="caretIndex">光标字符索引。</param>
    /// <param name="indentSize">一个缩进层级包含的列数。</param>
    /// <returns>当前行从第零列开始计算的目标缩进列数。</returns>
    public int GetIndentation(string? source, int caretIndex, int indentSize = 4)
    {
        ThrowIfDisposed();
        return _editingService.GetIndentation(source, caretIndex, indentSize);
    }

    /// <summary>使用 Roslyn 容错语法树格式化源码；存在语法错误时保留原文，避免破坏正在编辑的内容。</summary>
    public static string FormatSource(string? source, int indentSize = 4, string? endOfLine = null)
    {
        var effectiveSource = source ?? string.Empty;
        var root = CSharpSyntaxTree.ParseText(effectiveSource).GetCompilationUnitRoot();
        if (root.ContainsDiagnostics) return effectiveSource;
        return root.NormalizeWhitespace(
            indentation: new string(' ', Math.Max(1, indentSize)),
            eol: endOfLine ?? Environment.NewLine,
            elasticTrivia: false).ToFullString();
    }

    /// <summary>读取源码中的 using 项；普通、static、别名和 global using 均会保留语义。</summary>
    public static IReadOnlyList<string> GetUsings(string? source) =>
        CSharpSyntaxTree.ParseText(source ?? string.Empty)
            .GetCompilationUnitRoot()
            .Usings
            .Select(FormatUsingEntry)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    /// <summary>替换源码的 using 区域，不重新格式化用户正文。</summary>
    public static string SetUsings(string? source, IEnumerable<string> namespaces)
    {
        var root = CSharpSyntaxTree.ParseText(source ?? string.Empty).GetCompilationUnitRoot();
        var directives = namespaces
            .Select(item => item?.Trim() ?? string.Empty)
            .Where(item => item.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(item => item.StartsWith("global ", StringComparison.Ordinal) ? 0 : 1)
            .ThenBy(item => item, StringComparer.Ordinal)
            .Select(ParseUsingEntry)
            .Select(item => item.WithTrailingTrivia(SyntaxFactory.EndOfLine(Environment.NewLine)))
            .ToArray();
        return root.WithUsings(SyntaxFactory.List(directives)).ToFullString();
    }

    /// <summary>计算当前补全单词的起始字符位置。</summary>
    public static int GetCompletionStart(string source, int caretIndex)
    {
        var index = Clamp(caretIndex, 0, source.Length);
        while (index > 0 && (char.IsLetterOrDigit(source[index - 1]) || source[index - 1] == '_')) index--;
        return index;
    }

    /// <summary>释放所有当前热脚本版本。</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _runtimeRegistry.RevisionUnloaded -= _compiler.RemoveCachedRevision;
        _runtimeRegistry.Dispose();
        _analysisProvider.Clear();
    }

    private async Task<RoslynScriptExecutionResult> ExecuteCompiledAsync(
        string scriptId,
        RoslynScriptCompilationResult compilation,
        object? hostContext,
        RoslynScriptEnvironment environment,
        CancellationToken cancellationToken)
    {
        if (!compilation.Success || compilation.Artifact is null)
            return new RoslynScriptExecutionResult(null, compilation.Diagnostics, compilation.Revision);

        using var lease = _runtimeRegistry.Acquire(scriptId, compilation.Artifact);
        var program = lease.CreateProgram();
        var context = new CSharpProgramContext(
            hostContext,
            environment.Services,
            environment.Output ?? TextWriter.Null);
        var value = await program.ExecuteAsync(context, cancellationToken).ConfigureAwait(false);
        return new RoslynScriptExecutionResult(value, compilation.Diagnostics, compilation.Revision);
    }

    private ScriptEnvironmentSnapshot CreateSnapshot(RoslynScriptEnvironment? environment)
    {
        var effectiveEnvironment = environment ?? _defaultEnvironment;
        if (effectiveEnvironment.ReferencePaths.Count > 0)
        {
            // 外部 DLL 可能在进程运行期间被原位替换，每次检查文件指纹才能及时生成新 Revision。
            return ScriptEnvironmentSnapshot.Create(effectiveEnvironment);
        }

        // 不含可替换外部 DLL 的环境在对象生命周期内视为不可变，复用快照可让热点脚本跳过
        // 平台引用枚举、路径归一化和程序集元数据读取。弱表不会延长临时编辑环境的生命周期。
        return _stableSnapshots.GetValue(
            effectiveEnvironment,
            static _ => new StableEnvironmentSnapshotCache()).GetOrCreate(effectiveEnvironment);
    }

    /// <summary>
    /// 缓存不含外部 DLL 路径的环境快照，同时逐项检查 IReadOnlyList 内容，
    /// 防止调用方把可变 List 赋给 init 属性后修改列表而误用旧快照。
    /// </summary>
    private sealed class StableEnvironmentSnapshotCache
    {
        private readonly object _gate = new();
        private string[]? _imports;
        private System.Reflection.Assembly[]? _references;
        private string[]? _deniedNamespaces;
        private string[]? _deniedSymbols;
        private bool _resolveReferenceDependencies;
        private bool _validateReferenceArchitecture;
        private ScriptEnvironmentSnapshot? _snapshot;

        public ScriptEnvironmentSnapshot GetOrCreate(RoslynScriptEnvironment environment)
        {
            lock (_gate)
            {
                if (_snapshot is not null && Matches(environment)) return _snapshot;
                var snapshot = ScriptEnvironmentSnapshot.Create(environment);
                _imports = environment.Imports.ToArray();
                _references = environment.References.ToArray();
                _deniedNamespaces = environment.SecurityPolicy.DeniedNamespacePrefixes.ToArray();
                _deniedSymbols = environment.SecurityPolicy.DeniedSymbolPrefixes.ToArray();
                _resolveReferenceDependencies = environment.ResolveReferenceDependencies;
                _validateReferenceArchitecture = environment.ValidateReferenceArchitecture;
                _snapshot = snapshot;
                return snapshot;
            }
        }

        private bool Matches(RoslynScriptEnvironment environment)
        {
            if (_imports is null || _references is null || _deniedNamespaces is null || _deniedSymbols is null
                || _imports.Length != environment.Imports.Count
                || _references.Length != environment.References.Count
                || _deniedNamespaces.Length != environment.SecurityPolicy.DeniedNamespacePrefixes.Count
                || _deniedSymbols.Length != environment.SecurityPolicy.DeniedSymbolPrefixes.Count
                || _resolveReferenceDependencies != environment.ResolveReferenceDependencies
                || _validateReferenceArchitecture != environment.ValidateReferenceArchitecture)
                return false;
            for (var index = 0; index < _imports.Length; index++)
                if (!string.Equals(_imports[index], environment.Imports[index], StringComparison.Ordinal))
                    return false;
            for (var index = 0; index < _references.Length; index++)
                if (!ReferenceEquals(_references[index], environment.References[index]))
                    return false;
            for (var index = 0; index < _deniedNamespaces.Length; index++)
                if (!string.Equals(_deniedNamespaces[index], environment.SecurityPolicy.DeniedNamespacePrefixes[index], StringComparison.Ordinal))
                    return false;
            for (var index = 0; index < _deniedSymbols.Length; index++)
                if (!string.Equals(_deniedSymbols[index], environment.SecurityPolicy.DeniedSymbolPrefixes[index], StringComparison.Ordinal))
                    return false;
            return true;
        }
    }

    private static RoslynScriptDiagnostic CreateEnvironmentDiagnostic(Exception exception) => new(
        "SE1001",
        DiagnosticSeverity.Error,
        exception.Message,
        0,
        0,
        Origin: RoslynScriptDiagnosticOrigin.Infrastructure);

    private static string FormatUsingEntry(UsingDirectiveSyntax item)
    {
        var prefix = item.GlobalKeyword.IsKind(SyntaxKind.GlobalKeyword) ? "global " : string.Empty;
        if (item.Alias is not null) return prefix + item.Alias.Name + " = " + item.Name;
        if (item.StaticKeyword.IsKind(SyntaxKind.StaticKeyword)) return prefix + "static " + item.Name;
        return prefix + item.Name;
    }

    private static UsingDirectiveSyntax ParseUsingEntry(string entry)
    {
        var text = entry.Trim();
        string source;
        if (text.StartsWith("global using ", StringComparison.Ordinal))
            source = text.EndsWith(";", StringComparison.Ordinal) ? text : text + ";";
        else if (text.StartsWith("global ", StringComparison.Ordinal))
            source = "global using " + text.Substring("global ".Length) + ";";
        else if (text.StartsWith("using ", StringComparison.Ordinal))
            source = text.EndsWith(";", StringComparison.Ordinal) ? text : text + ";";
        else
            source = "using " + text.TrimEnd(';') + ";";
        var root = CSharpSyntaxTree.ParseText(source).GetCompilationUnitRoot();
        if (root.Usings.Count != 1 || root.ContainsDiagnostics)
            throw new FormatException($"无效的 using 项：{entry}");
        return root.Usings[0].WithoutTrivia();
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(RoslynScriptService));
    }

    private static int Clamp(int value, int minimum, int maximum) =>
        value < minimum ? minimum : value > maximum ? maximum : value;
}
