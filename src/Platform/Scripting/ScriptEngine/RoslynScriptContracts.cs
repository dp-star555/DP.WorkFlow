using System.Reflection;
using Microsoft.CodeAnalysis;

namespace ScriptEngine;

/// <summary>独立脚本服务使用的编译和设计期环境，不依赖工作流节点或具体 UI。</summary>
public sealed class RoslynScriptEnvironment
{
    /// <summary>脚本默认导入的常用命名空间。</summary>
    public static IReadOnlyList<string> StandardImports { get; } = new[]
    {
        "System", "System.Collections.Generic", "System.Linq", "System.Threading", "System.Threading.Tasks"
    };

    /// <summary>编译脚本时自动添加的命名空间。</summary>
    public IReadOnlyList<string> Imports { get; init; } = StandardImports;

    /// <summary>
    /// 为兼容现有宿主保留的已加载程序集引用。
    /// 新代码应优先使用 <see cref="ReferencePaths"/>，避免仅为编译而把 DLL 加载到宿主进程。
    /// </summary>
    public IReadOnlyList<Assembly> References { get; init; } = Array.Empty<Assembly>();

    /// <summary>需要参与编译和运行时解析的显式 DLL 路径。</summary>
    public IReadOnlyList<string> ReferencePaths { get; init; } = Array.Empty<string>();

    /// <summary>是否递归发现显式 DLL 同目录中的私有托管依赖，默认为 true。</summary>
    public bool ResolveReferenceDependencies { get; init; } = true;

    /// <summary>是否在编译前拒绝与当前进程架构不兼容的显式 DLL，默认为 true。</summary>
    public bool ValidateReferenceArchitecture { get; init; } = true;

    /// <summary>编译前执行的符号访问策略；默认信任脚本并不限制 BCL。</summary>
    public RoslynScriptSecurityPolicy SecurityPolicy { get; init; } = RoslynScriptSecurityPolicy.Trusted;

    /// <summary>仅在受信任进程内模式下向脚本开放的宿主服务容器。</summary>
    public IServiceProvider? Services { get; init; }

    /// <summary>脚本标准输出的目标。</summary>
    public TextWriter? Output { get; init; }

    /// <summary>编辑器额外展示的宿主上下文补全项。</summary>
    public IReadOnlyList<RoslynScriptContextItem> ContextItems { get; init; } = Array.Empty<RoslynScriptContextItem>();
}

/// <summary>
/// 进程内脚本的静态符号访问策略。该策略用于提前诊断，不等同于安全沙箱；
/// 不可信脚本仍应在独立低权限进程中执行。
/// </summary>
public sealed class RoslynScriptSecurityPolicy
{
    /// <summary>不限制命名空间和符号的受信任策略。</summary>
    public static RoslynScriptSecurityPolicy Trusted { get; } = new();

    /// <summary>适合普通业务计算的建议受限策略。</summary>
    public static RoslynScriptSecurityPolicy Restricted { get; } = new()
    {
        DeniedNamespacePrefixes = new[]
        {
            "System.IO", "System.Net", "System.Diagnostics", "System.Reflection",
            "Microsoft.Win32", "System.Runtime.InteropServices"
        },
        DeniedSymbolPrefixes = new[] { "System.Environment.Exit", "System.Activator.CreateInstance" }
    };

    /// <summary>禁止访问的命名空间前缀。</summary>
    public IReadOnlyList<string> DeniedNamespacePrefixes { get; init; } = Array.Empty<string>();

    /// <summary>禁止访问的完整符号名称前缀。</summary>
    public IReadOnlyList<string> DeniedSymbolPrefixes { get; init; } = Array.Empty<string>();
}

/// <summary>多文件脚本项目中的一个 C# 源文件。</summary>
public sealed record RoslynScriptSourceFile(string FileName, string Source);

/// <summary>由一个或多个普通 C# 源文件组成的脚本项目。</summary>
public sealed class RoslynScriptProject
{
    /// <summary>参与同一次编译的源码文件；文件名必须唯一且以 .cs 结尾。</summary>
    public IReadOnlyList<RoslynScriptSourceFile> SourceFiles { get; init; } = Array.Empty<RoslynScriptSourceFile>();

    /// <summary>计算仅由规范化文件名和源码内容决定的稳定项目修订，用于检测异步编辑结果是否过期。</summary>
    public string ComputeSourceRevision()
    {
        var files = GetValidatedFiles();
        var material = string.Join("\n", files.Select(file => file.FileName + "\0" + file.Source.Length + "\0" + file.Source));
        return Compilation.ScriptEnvironmentSnapshot.ComputeHash(material);
    }

    internal IReadOnlyList<RoslynScriptSourceFile> GetValidatedFiles()
    {
        if (SourceFiles.Count == 0) throw new ArgumentException("脚本项目至少需要一个 C# 源文件。", nameof(SourceFiles));
        var normalized = new List<RoslynScriptSourceFile>(SourceFiles.Count);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in SourceFiles)
        {
            if (file is null || string.IsNullOrWhiteSpace(file.FileName))
                throw new ArgumentException("脚本源文件名不能为空。", nameof(SourceFiles));
            var name = file.FileName.Trim().Replace('\\', '/');
            if (Path.IsPathRooted(name) || name.Split('/').Any(part => part == ".."))
                throw new ArgumentException($"脚本源文件名不能是绝对路径或包含 ..：{name}", nameof(SourceFiles));
            if (!name.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException($"脚本源文件必须以 .cs 结尾：{name}", nameof(SourceFiles));
            if (name.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException($".g.cs 名称保留给脚本引擎生成文件：{name}", nameof(SourceFiles));
            if (!names.Add(name))
                throw new ArgumentException($"脚本项目包含重复文件名：{name}", nameof(SourceFiles));
            normalized.Add(new RoslynScriptSourceFile(name, file.Source ?? string.Empty));
        }
        return normalized.OrderBy(file => file.FileName, StringComparer.OrdinalIgnoreCase).ToArray();
    }
}

/// <summary>源码或元数据符号的定义位置。</summary>
public sealed record RoslynScriptDefinitionLocation(
    string DisplayText,
    string? SourceName,
    int Start,
    int Length,
    int Line,
    int Column,
    bool IsSource);

/// <summary>可直接应用到源码的轻量代码修复。</summary>
public sealed record RoslynScriptCodeAction(
    string Title,
    int Start,
    int Length,
    string Replacement,
    string DiagnosticId);

/// <summary>普通 C# 程序的固定执行入口。</summary>
public interface ICSharpProgram
{
    /// <summary>执行脚本程序。</summary>
    /// <param name="context">宿主提供的脚本上下文。</param>
    /// <param name="cancellationToken">本次执行的协作式取消令牌。</param>
    /// <returns>脚本返回值。</returns>
    ValueTask<object?> ExecuteAsync(CSharpProgramContext context, CancellationToken cancellationToken);
}

/// <summary>宿主向独立 C# 程序传递的运行上下文。</summary>
public sealed class CSharpProgramContext
{
    internal CSharpProgramContext(object? hostContext, IServiceProvider? services, TextWriter output)
    {
        HostContext = hostContext;
        Services = services;
        Output = output;
    }

    /// <summary>宿主传入的业务上下文；仅受信任进程内脚本可以直接访问。</summary>
    public object? HostContext { get; }

    /// <summary>宿主服务容器；仅受信任进程内脚本可以直接访问。</summary>
    public IServiceProvider? Services { get; }

    /// <summary>当前脚本的输出目标。</summary>
    public TextWriter Output { get; }

    /// <summary>按强类型读取宿主上下文。</summary>
    /// <typeparam name="T">期望的上下文类型。</typeparam>
    /// <returns>类型匹配的宿主上下文。</returns>
    public T GetHostContext<T>() where T : class =>
        HostContext as T ?? throw new InvalidOperationException($"当前宿主上下文不是 {typeof(T).FullName}。");

    /// <summary>从宿主服务容器解析服务。</summary>
    /// <typeparam name="T">服务类型。</typeparam>
    /// <returns>找到的服务；未注册时返回 <see langword="null"/>。</returns>
    public T? GetService<T>() where T : class => Services?.GetService(typeof(T)) as T;
}

/// <summary>编辑器中额外展示的宿主上下文补全项。</summary>
public sealed record RoslynScriptContextItem(string InsertionText, string Description);

/// <summary>脚本编辑器补全项。</summary>
public sealed record RoslynScriptCompletionItem(
    string DisplayText,
    string InsertionText,
    string? Description = null,
    RoslynScriptCompletionKind Kind = RoslynScriptCompletionKind.None,
    int CaretOffset = 0,
    string? FilterText = null,
    string? SortText = null,
    string? InlineDescription = null,
    int? ReplacementStart = null,
    int? ReplacementLength = null,
    int? NewCaretPosition = null,
    IReadOnlyList<RoslynScriptTextChange>? TextChanges = null,
    int MatchPriority = 0);

/// <summary>补全提交时需要原子应用到活动文档的一项文本变化。</summary>
public sealed record RoslynScriptTextChange(int Start, int Length, string NewText);

/// <summary>补全项的语义类别，可供桌面编辑器选择图标和提交行为。</summary>
public enum RoslynScriptCompletionKind
{
    None,
    Keyword,
    Type,
    Method,
    Property,
    Field,
    Event,
    Namespace,
    Variable,
    Snippet,
    HostContext,
    Class,
    Interface,
    Struct,
    Enum,
    Delegate,
    Constant
}

/// <summary>调用签名中的一个参数。</summary>
public sealed record RoslynScriptParameterInfo(string DisplayText, string Name, string? Documentation = null);

/// <summary>光标所在调用表达式的参数签名帮助。</summary>
public sealed record RoslynScriptSignatureHelp(
    string DisplayText,
    IReadOnlyList<RoslynScriptParameterInfo> Parameters,
    int ActiveParameter,
    int ApplicableSpanStart,
    IReadOnlyList<string> Overloads);

/// <summary>源码符号的快速信息。</summary>
public sealed record RoslynScriptQuickInfo(
    string DisplayText,
    string? Documentation,
    int Start,
    int Length);

/// <summary>Roslyn 分析快照缓存统计。</summary>
public sealed record RoslynScriptCacheStatistics(
    long Hits,
    long Misses,
    int EntryCount,
    long IncrementalParses = 0);

/// <summary>脚本服务的有界缓存配置。</summary>
public sealed class RoslynScriptServiceOptions
{
    private int _analysisCacheCapacity = 12;
    private int _compilationCacheCapacity = 32;
    private int _runtimeSlotCapacity = 64;

    /// <summary>诊断、补全、高亮等共享的 Roslyn 分析快照上限。</summary>
    public int AnalysisCacheCapacity
    {
        get => _analysisCacheCapacity;
        init => _analysisCacheCapacity = value is >= 1 and <= 256
            ? value
            : throw new ArgumentOutOfRangeException(nameof(value));
    }

    /// <summary>Emit 编译结果缓存上限。</summary>
    public int CompilationCacheCapacity
    {
        get => _compilationCacheCapacity;
        init => _compilationCacheCapacity = value is >= 1 and <= 1024
            ? value
            : throw new ArgumentOutOfRangeException(nameof(value));
    }

    /// <summary>同时保留的逻辑脚本热版本槽上限，超出后按最早创建顺序回收。</summary>
    public int RuntimeSlotCapacity
    {
        get => _runtimeSlotCapacity;
        init => _runtimeSlotCapacity = value is >= 1 and <= 4096
            ? value
            : throw new ArgumentOutOfRangeException(nameof(value));
    }
}

/// <summary>脚本语义高亮区间。</summary>
public sealed record RoslynScriptHighlightSpan(int Start, int Length, RoslynScriptHighlightKind Kind);

/// <summary>可在编辑器中收缩或展开的 C# 源码区间；行号从零开始且包含结束行。</summary>
public sealed record RoslynScriptFoldingSpan(
    int StartLine,
    int EndLine,
    RoslynScriptFoldingKind Kind);

/// <summary>脚本折叠区间的语法类别。</summary>
public enum RoslynScriptFoldingKind
{
    Namespace,
    Type,
    Member,
    Statement,
    Region
}

/// <summary>诊断所在源码的类别。</summary>
public enum RoslynScriptDiagnosticOrigin
{
    /// <summary>用户可编辑的主源码。</summary>
    UserSource,

    /// <summary>脚本引擎生成的导入或包装源码。</summary>
    GeneratedSource,

    /// <summary>没有具体源码位置的编译、引用或入口契约诊断。</summary>
    Infrastructure
}

/// <summary>脚本编译诊断。</summary>
public sealed record RoslynScriptDiagnostic(
    string Id,
    DiagnosticSeverity Severity,
    string Message,
    int Start,
    int Length,
    int Line = 0,
    int Column = 0,
    string? SourceName = null,
    RoslynScriptDiagnosticOrigin Origin = RoslynScriptDiagnosticOrigin.UserSource);

/// <summary>一次脚本编译产生的、尚未加载到运行时的不可变产物。</summary>
public sealed class RoslynScriptCompilationArtifact
{
    internal RoslynScriptCompilationArtifact(
        string artifactId,
        string revision,
        string assemblyName,
        string entryTypeName,
        byte[] assemblyBytes,
        byte[] pdbBytes,
        IReadOnlyDictionary<string, string> runtimeReferencePaths)
    {
        ArtifactId = artifactId;
        Revision = revision;
        AssemblyName = assemblyName;
        EntryTypeName = entryTypeName;
        AssemblyBytes = assemblyBytes;
        PdbBytes = pdbBytes;
        RuntimeReferencePaths = runtimeReferencePaths;
    }

    /// <summary>由源码、编译配置和全部引用内容共同决定的产物标识。</summary>
    public string ArtifactId { get; }

    /// <summary>当前脚本内容修订标识。</summary>
    public string Revision { get; }

    /// <summary>动态程序集名称。</summary>
    public string AssemblyName { get; }

    /// <summary>实现 <see cref="ICSharpProgram"/> 的入口类型全名。</summary>
    public string EntryTypeName { get; }

    /// <summary>尚未加载的 PE 字节。</summary>
    public byte[] AssemblyBytes { get; }

    /// <summary>与 PE 匹配的便携式 PDB 字节。</summary>
    public byte[] PdbBytes { get; }

    /// <summary>按简单程序集名称索引的运行时私有依赖路径。</summary>
    internal IReadOnlyDictionary<string, string> RuntimeReferencePaths { get; }
}

/// <summary>脚本编译结果。</summary>
public sealed class RoslynScriptCompilationResult
{
    internal RoslynScriptCompilationResult(
        string revision,
        RoslynScriptCompilationArtifact? artifact,
        IReadOnlyList<RoslynScriptDiagnostic> diagnostics)
    {
        Revision = revision;
        Artifact = artifact;
        Diagnostics = diagnostics;
    }

    /// <summary>当前源码和环境的修订标识。</summary>
    public string Revision { get; }

    /// <summary>成功编译时生成的产物。</summary>
    public RoslynScriptCompilationArtifact? Artifact { get; }

    /// <summary>编译错误和警告。</summary>
    public IReadOnlyList<RoslynScriptDiagnostic> Diagnostics { get; }

    /// <summary>是否已成功产生可执行产物。</summary>
    public bool Success => Artifact is not null && !Diagnostics.Any(item => item.Severity == DiagnosticSeverity.Error);
}

/// <summary>脚本执行结果。</summary>
public sealed record RoslynScriptExecutionResult(
    object? ReturnValue,
    IReadOnlyList<RoslynScriptDiagnostic> Diagnostics,
    string? Revision = null);

/// <summary>脚本语义高亮类别。</summary>
public enum RoslynScriptHighlightKind
{
    Plain,
    Keyword,
    String,
    Number,
    Comment,
    Type,
    Method,
    Property,
    XmlDocTag
}
