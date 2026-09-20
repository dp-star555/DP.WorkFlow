using Microsoft.CodeAnalysis;
using ScriptEngine;

namespace DP.WorkFlow.UI;

/// <summary>脚本编辑会话对外暴露的编译状态。</summary>
public enum WorkflowScriptCompilationState
{
    NotCompiled,
    Compiling,
    Succeeded,
    Failed,
    ModifiedAfterCompilation
}

/// <summary>
/// 管理脚本文本、引用和编译诊断的状态机。WinForms/WPF 只负责呈现，不再各自复制状态转换逻辑。
/// </summary>
public sealed class WorkflowCSharpScriptEditorSession
{
    private readonly WorkflowScriptEditorPageModel _page;
    private IReadOnlyList<RoslynScriptDiagnostic> _diagnostics = Array.Empty<RoslynScriptDiagnostic>();
    private string? _lastCompiledSource;
    private string? _lastCompiledReferenceKey;
    private long _editVersion;

    public WorkflowCSharpScriptEditorSession(WorkflowScriptEditorPageModel page) =>
        _page = page ?? throw new ArgumentNullException(nameof(page));

    public event EventHandler? Changed;

    public string Source => _page.Script;
    public IReadOnlyList<string> ReferencePaths => _page.ReferencePaths;
    public WorkflowScriptCompilationState State { get; private set; } = WorkflowScriptCompilationState.NotCompiled;
    public IReadOnlyList<RoslynScriptDiagnostic> Diagnostics => _diagnostics;
    public bool DiagnosticsAreStale => State == WorkflowScriptCompilationState.ModifiedAfterCompilation
        || (State == WorkflowScriptCompilationState.Compiling
            && _lastCompiledSource is not null
            && (!string.Equals(_lastCompiledSource, Source, StringComparison.Ordinal)
                || !string.Equals(_lastCompiledReferenceKey, CreateReferenceKey(ReferencePaths), StringComparison.Ordinal)));

    /// <summary>更新正文；已有诊断会保留并标记为上次编译结果。</summary>
    public void SetSource(string source)
    {
        source ??= string.Empty;
        if (string.Equals(Source, source, StringComparison.Ordinal)) return;
        _page.SetScript(source);
        _editVersion++;
        State = _diagnostics.Count > 0 || _lastCompiledSource is not null
            ? WorkflowScriptCompilationState.ModifiedAfterCompilation
            : WorkflowScriptCompilationState.NotCompiled;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>更新 DLL 引用；已有诊断会保留但不再视为当前环境的结果。</summary>
    public void SetReferencePaths(IEnumerable<string> paths)
    {
        var before = CreateReferenceKey(ReferencePaths);
        _page.SetReferencePaths(paths);
        if (string.Equals(before, CreateReferenceKey(ReferencePaths), StringComparison.Ordinal)) return;
        _editVersion++;
        State = _diagnostics.Count > 0 || _lastCompiledSource is not null
            ? WorkflowScriptCompilationState.ModifiedAfterCompilation
            : WorkflowScriptCompilationState.NotCompiled;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>编译当前快照；编译期间发生编辑时，结果保留为过期诊断而不覆盖新文本。</summary>
    public async Task<RoslynScriptCompilationResult> CompileAsync(
        RoslynScriptService scriptService,
        CancellationToken cancellationToken = default)
    {
        if (scriptService is null) throw new ArgumentNullException(nameof(scriptService));
        var source = Source;
        var referenceKey = CreateReferenceKey(ReferencePaths);
        var version = _editVersion;
        State = WorkflowScriptCompilationState.Compiling;
        Changed?.Invoke(this, EventArgs.Empty);

        RoslynScriptCompilationResult result;
        try
        {
            result = await scriptService.CompileProgramAsync(
                source,
                WorkflowCSharpScriptEditorModel.CreateEnvironment(
                    ReferencePaths,
                    _page.Node.GetType().Assembly),
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            State = _diagnostics.Count > 0 || _lastCompiledSource is not null
                ? WorkflowScriptCompilationState.ModifiedAfterCompilation
                : WorkflowScriptCompilationState.NotCompiled;
            Changed?.Invoke(this, EventArgs.Empty);
            throw;
        }
        _diagnostics = result.Diagnostics;
        _lastCompiledSource = source;
        _lastCompiledReferenceKey = referenceKey;

        var isCurrent = version == _editVersion
                        && string.Equals(source, Source, StringComparison.Ordinal)
                        && string.Equals(referenceKey, CreateReferenceKey(ReferencePaths), StringComparison.Ordinal);
        if (isCurrent)
        {
            _page.RecordCompilation(source, result.Success);
            State = result.Success
                ? WorkflowScriptCompilationState.Succeeded
                : WorkflowScriptCompilationState.Failed;
        }
        else
        {
            State = WorkflowScriptCompilationState.ModifiedAfterCompilation;
        }
        Changed?.Invoke(this, EventArgs.Empty);
        return result;
    }

    public string GetStatusText()
    {
        var errors = _diagnostics.Count(item => item.Severity == DiagnosticSeverity.Error);
        var warnings = _diagnostics.Count(item => item.Severity == DiagnosticSeverity.Warning);
        return State switch
        {
            WorkflowScriptCompilationState.Compiling => "正在后台编译…",
            WorkflowScriptCompilationState.Succeeded => warnings == 0
                ? "编译成功，可以保存"
                : $"编译成功：{warnings} 个警告",
            WorkflowScriptCompilationState.Failed => warnings == 0
                ? $"编译失败：{errors} 个错误"
                : $"编译失败：{errors} 个错误，{warnings} 个警告",
            WorkflowScriptCompilationState.ModifiedAfterCompilation => _diagnostics.Count == 0
                ? "代码已修改，请重新编译"
                : "代码已修改；下方显示上次编译结果",
            _ => "尚未编译当前代码"
        };
    }

    private static string CreateReferenceKey(IEnumerable<string> paths) => string.Join(
        "\n",
        paths.Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase));
}
