using System.Windows;
using System.Windows.Forms.Integration;
using System.Windows.Media;
using ScriptEngine.WinForms;
using ScriptEngine.Workspaces;
using DrawingColor = System.Drawing.Color;

namespace ScriptEngine.Wpf;

/// <summary>通过 WindowsFormsHost 在 WPF 中承载多文件脚本项目编辑器。</summary>
public sealed class RoslynScriptProjectEditorControl : System.Windows.Controls.UserControl
{
    private readonly ScriptEngine.WinForms.RoslynScriptProjectEditorControl _editor;

    /// <summary>初始化多文件项目编辑器。</summary>
    public RoslynScriptProjectEditorControl()
    {
        _editor = new ScriptEngine.WinForms.RoslynScriptProjectEditorControl { Dock = System.Windows.Forms.DockStyle.Fill };
        Content = new WindowsFormsHost { Child = _editor };
        _editor.ProjectChanged += (_, _) => ProjectChanged?.Invoke(this, EventArgs.Empty);
        _editor.ActiveFileChanged += (_, _) => ActiveFileChanged?.Invoke(this, EventArgs.Empty);
        _editor.DiagnosticsUpdated += (_, eventArgs) => DiagnosticsUpdated?.Invoke(this, eventArgs);
        _editor.ReferencesFound += (_, eventArgs) => ReferencesFound?.Invoke(this, eventArgs);
        Loaded += (_, _) => ApplyAppearance();
    }

    public event EventHandler? ProjectChanged;
    public event EventHandler? ActiveFileChanged;
    public event EventHandler<ScriptDiagnosticsUpdatedEventArgs>? DiagnosticsUpdated;
    public event EventHandler<ScriptReferencesFoundEventArgs>? ReferencesFound;

    public RoslynScriptProject Project { get => _editor.Project; set => _editor.Project = value; }
    public string ActiveFileName => _editor.ActiveFileName;
    public IReadOnlyList<string> FileNames => _editor.FileNames;
    public IReadOnlyList<string> ModifiedFileNames => _editor.ModifiedFileNames;
    public bool CanUndoProjectEdit => _editor.CanUndoProjectEdit;
    public bool CanRedoProjectEdit => _editor.CanRedoProjectEdit;
    public bool ReferencesPanelVisible => _editor.ReferencesPanelVisible;
    public RoslynScriptService ScriptService { get => _editor.ScriptService; set => _editor.ScriptService = value; }
    public RoslynScriptEnvironment ScriptEnvironment { get => _editor.ScriptEnvironment; set => _editor.ScriptEnvironment = value; }
    public IRoslynScriptProjectEditingService? ProjectEditingService { get => _editor.ProjectEditingService; set => _editor.ProjectEditingService = value; }
    public IRoslynScriptCompletionProvider? CompletionProvider { get => _editor.CompletionProvider; set => _editor.CompletionProvider = value; }
    public bool AutoCompletionEnabled { get => _editor.AutoCompletionEnabled; set => _editor.AutoCompletionEnabled = value; }
    public int AutoCompletionMinimumPrefixLength { get => _editor.AutoCompletionMinimumPrefixLength; set => _editor.AutoCompletionMinimumPrefixLength = value; }
    public int AutoCompletionDelay { get => _editor.AutoCompletionDelay; set => _editor.AutoCompletionDelay = value; }
    public bool LiveDiagnosticsEnabled { get => _editor.LiveDiagnosticsEnabled; set => _editor.LiveDiagnosticsEnabled = value; }
    public int LiveDiagnosticsDelay { get => _editor.LiveDiagnosticsDelay; set => _editor.LiveDiagnosticsDelay = value; }

    public void AddFile(string fileName, string source = "") => _editor.AddFile(fileName, source);
    public bool RemoveFile(string fileName) => _editor.RemoveFile(fileName);
    public bool RenameFile(string oldFileName, string newFileName) => _editor.RenameFile(oldFileName, newFileName);
    public bool SelectFile(string fileName) => _editor.SelectFile(fileName);
    public bool TryCloseFile(string fileName, Func<string, bool>? confirmDiscardChanges = null) =>
        _editor.TryCloseFile(fileName, confirmDiscardChanges);
    public void MarkProjectSaved() => _editor.MarkProjectSaved();
    public bool UndoProjectEdit() => _editor.UndoProjectEdit();
    public bool RedoProjectEdit() => _editor.RedoProjectEdit();
    public bool NavigateBackward() => _editor.NavigateBackward();
    public bool NavigateForward() => _editor.NavigateForward();
    public void HideReferencesPanel() => _editor.HideReferencesPanel();
    public Task<RoslynScriptCompilationResult> CompileProjectAsync(CancellationToken cancellationToken = default) =>
        _editor.CompileProjectAsync(cancellationToken);
    public Task<IReadOnlyList<RoslynScriptReferenceLocation>> FindReferencesAsync(
        string fileName,
        int position,
        CancellationToken cancellationToken = default) => _editor.FindReferencesAsync(fileName, position, cancellationToken);
    public Task<RoslynScriptWorkspaceEdit?> CreateRenameEditAsync(
        string fileName,
        int position,
        string newName,
        CancellationToken cancellationToken = default) => _editor.CreateRenameEditAsync(fileName, position, newName, cancellationToken);
    public Task<IReadOnlyList<RoslynScriptWorkspaceEdit>> GetProjectCodeFixesAsync(
        string fileName,
        RoslynScriptDiagnostic diagnostic,
        CancellationToken cancellationToken = default) => _editor.GetProjectCodeFixesAsync(fileName, diagnostic, cancellationToken);
    public void ApplyWorkspaceEdit(RoslynScriptWorkspaceEdit edit, bool allowConflicts = false) =>
        _editor.ApplyWorkspaceEdit(edit, allowConflicts);
    public Task AnalyzeProjectDiagnosticsAsync() => _editor.AnalyzeProjectDiagnosticsAsync();
    public void ShowDiagnostics(IEnumerable<RoslynScriptDiagnostic> diagnostics) => _editor.ShowDiagnostics(diagnostics);
    public void ScheduleDiagnostics() => _editor.ScheduleDiagnostics();
    public new bool Focus() => _editor.ActiveEditor.Focus();

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (_editor is not null && (e.Property == BackgroundProperty || e.Property == ForegroundProperty
            || e.Property == FontFamilyProperty || e.Property == FontSizeProperty))
            ApplyAppearance();
    }

    private void ApplyAppearance()
    {
        _editor.BackColor = ToDrawingColor(Background, DrawingColor.FromArgb(30, 30, 30));
        _editor.ForeColor = ToDrawingColor(Foreground, DrawingColor.FromArgb(226, 232, 240));
        var family = FontFamily?.Source ?? "Consolas";
        var size = Math.Max(8f, (float)(FontSize * 72d / 96d));
        _editor.Font = new System.Drawing.Font(family, size);
    }

    private static DrawingColor ToDrawingColor(System.Windows.Media.Brush? brush, DrawingColor fallback) => brush is SolidColorBrush solid
        ? DrawingColor.FromArgb(solid.Color.A, solid.Color.R, solid.Color.G, solid.Color.B)
        : fallback;
}
