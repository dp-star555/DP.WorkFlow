namespace ScriptEngine.Workspaces;

/// <summary>由项目级语义编辑模块产生的编辑类别。</summary>
public enum RoslynScriptWorkspaceEditKind
{
    /// <summary>添加缺失的 using 指令。</summary>
    AddImport,

    /// <summary>删除不必要的 using 指令。</summary>
    RemoveImport,

    /// <summary>跨文件重命名符号及其引用。</summary>
    Rename
}

/// <summary>可原子替换当前编辑器项目的项目级语义编辑结果。</summary>
public sealed record RoslynScriptWorkspaceEdit(
    string Title,
    RoslynScriptWorkspaceEditKind Kind,
    RoslynScriptProject Project,
    IReadOnlyList<string> ChangedFiles,
    string? DiagnosticId = null,
    bool HasConflicts = false,
    string? OriginalProjectRevision = null,
    IReadOnlyList<RoslynScriptWorkspaceFileChange>? FileChanges = null);

/// <summary>项目级编辑对单个文件产生的可预览文本变化。</summary>
public sealed record RoslynScriptWorkspaceFileChange(
    string FileName,
    string OriginalSource,
    string ChangedSource,
    int Start,
    int OriginalLength,
    string NewText);

/// <summary>跨文件符号声明或引用位置；行列从 1 开始。</summary>
public sealed record RoslynScriptReferenceLocation(
    string FileName,
    int Start,
    int Length,
    int Line,
    int Column,
    bool IsDefinition);

/// <summary>
/// 项目编辑器使用的可选语义编辑接口。基础编辑器只依赖此小接口，完整实现由
/// ScriptEngine.Workspaces 提供，因此运行和普通编辑场景不会加载 Workspaces。
/// </summary>
public interface IRoslynScriptCompletionProvider
{
    /// <summary>使用完整 Roslyn CompletionService 获取上下文感知的项目补全。</summary>
    Task<IReadOnlyList<RoslynScriptCompletionItem>> GetCompletionsAsync(
        RoslynScriptProject project,
        string activeFileName,
        int position,
        RoslynScriptEnvironment? environment = null,
        CancellationToken cancellationToken = default);
}

/// <summary>可选 Workspaces 模块的有界缓存配置。</summary>
public sealed class RoslynScriptWorkspaceOptions
{
    private int _cacheCapacity = 6;
    private int _maximumCompletionItems = 256;

    /// <summary>常驻 AdhocWorkspace 项目形状缓存上限。</summary>
    public int CacheCapacity
    {
        get => _cacheCapacity;
        init => _cacheCapacity = value is >= 1 and <= 64
            ? value
            : throw new ArgumentOutOfRangeException(nameof(value));
    }

    /// <summary>单次返回给编辑器的最大补全项数。</summary>
    public int MaximumCompletionItems
    {
        get => _maximumCompletionItems;
        init => _maximumCompletionItems = value is >= 16 and <= 2048
            ? value
            : throw new ArgumentOutOfRangeException(nameof(value));
    }
}

/// <summary>Workspaces 常驻缓存命中和增量更新统计。</summary>
public sealed record RoslynScriptWorkspaceCacheStatistics(
    long Hits,
    long Misses,
    long IncrementalDocumentUpdates,
    int EntryCount);

public interface IRoslynScriptProjectEditingService
{
    /// <summary>为项目诊断生成语义代码修复。</summary>
    Task<IReadOnlyList<RoslynScriptWorkspaceEdit>> GetCodeFixesAsync(
        RoslynScriptProject project,
        string activeFileName,
        RoslynScriptDiagnostic diagnostic,
        RoslynScriptEnvironment? environment = null,
        CancellationToken cancellationToken = default);

    /// <summary>查找光标符号的声明和全部项目引用。</summary>
    Task<IReadOnlyList<RoslynScriptReferenceLocation>> FindReferencesAsync(
        RoslynScriptProject project,
        string activeFileName,
        int position,
        RoslynScriptEnvironment? environment = null,
        CancellationToken cancellationToken = default);

    /// <summary>跨文件重命名光标所在源码符号。</summary>
    Task<RoslynScriptWorkspaceEdit?> RenameSymbolAsync(
        RoslynScriptProject project,
        string activeFileName,
        int position,
        string newName,
        RoslynScriptEnvironment? environment = null,
        CancellationToken cancellationToken = default);
}
