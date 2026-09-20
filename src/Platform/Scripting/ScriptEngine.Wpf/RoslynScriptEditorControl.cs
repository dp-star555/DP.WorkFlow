using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms.Integration;
using System.Windows.Media;
using ScriptEngine.WinForms;
using ScriptEngine.Workspaces;
using DrawingColor = System.Drawing.Color;

namespace ScriptEngine.Wpf;

/// <summary>在 WPF 中承载与 WinForms 相同的 Scintilla + Roslyn 标准 C# 编辑器。</summary>
public sealed class RoslynScriptEditorControl : System.Windows.Controls.UserControl
{
    private readonly ScriptEngine.WinForms.RoslynScriptEditorControl _editor;
    private readonly WindowsFormsHost _host;

    public RoslynScriptEditorControl()
    {
        _editor = new ScriptEngine.WinForms.RoslynScriptEditorControl { Dock = System.Windows.Forms.DockStyle.Fill };
        _host = new WindowsFormsHost { Child = _editor };
        Content = _host;
        _editor.TextChanged += (_, _) => TextChanged?.Invoke(this, EventArgs.Empty);
        _editor.ModifiedChanged += (_, _) => ModifiedChanged?.Invoke(this, EventArgs.Empty);
        _editor.CharacterEntered += (_, character) => CharacterEntered?.Invoke(this, character.ToString());
        _editor.CompletionRequested += (_, _) =>
        {
            if (CompletionRequested is not null) CompletionRequested(this, EventArgs.Empty);
            else _ = ShowDefaultCompletionsAsync();
        };
        Loaded += (_, _) => ApplyAppearance();
    }

    public event EventHandler? TextChanged;
    public event EventHandler? ModifiedChanged;
    public event EventHandler<string>? CharacterEntered;
    public event EventHandler? CompletionRequested;

    public RoslynScriptService ScriptService
    {
        get => _editor.ScriptService;
        set => _editor.ScriptService = value;
    }

    public RoslynScriptEnvironment ScriptEnvironment
    {
        get => _editor.ScriptEnvironment;
        set => _editor.ScriptEnvironment = value;
    }

    public IRoslynScriptCompletionProvider? CompletionProvider
    {
        get => _editor.CompletionProvider;
        set => _editor.CompletionProvider = value;
    }

    public bool IsCompletionListVisible => _editor.IsCompletionListVisible;

    /// <summary>获取或设置是否在输入标识符或点号后自动显示代码补全。</summary>
    public bool AutoCompletionEnabled
    {
        get => _editor.AutoCompletionEnabled;
        set => _editor.AutoCompletionEnabled = value;
    }

    /// <summary>获取或设置自动弹出补全列表所需的最少标识符字符数。</summary>
    public int AutoCompletionMinimumPrefixLength
    {
        get => _editor.AutoCompletionMinimumPrefixLength;
        set => _editor.AutoCompletionMinimumPrefixLength = value;
    }

    /// <summary>获取或设置自动补全延迟毫秒数。</summary>
    public int AutoCompletionDelay
    {
        get => _editor.AutoCompletionDelay;
        set => _editor.AutoCompletionDelay = value;
    }

    /// <summary>获取或设置是否在编辑停止后自动刷新 Roslyn 诊断。</summary>
    public bool LiveDiagnosticsEnabled
    {
        get => _editor.LiveDiagnosticsEnabled;
        set => _editor.LiveDiagnosticsEnabled = value;
    }

    /// <summary>获取或设置实时诊断的防抖延迟毫秒数。</summary>
    public int LiveDiagnosticsDelay
    {
        get => _editor.LiveDiagnosticsDelay;
        set => _editor.LiveDiagnosticsDelay = value;
    }

    public string ScriptText
    {
        get => _editor.Text;
        set => _editor.Text = value;
    }

    public int CaretOffset => _editor.SelectionStart;
    public bool IsModified => _editor.IsModified;
    public void MarkSaved() => _editor.MarkSaved();

    public void Replace(int start, int length, string value)
    {
        _editor.Select(start, length);
        _editor.SelectedText = value;
        _editor.SelectionStart = start + value.Length;
    }

    public Task<IReadOnlyList<RoslynScriptCompletionItem>> GetCompletionsAsync(CancellationToken cancellationToken = default) =>
        _editor.GetCompletionsAsync(cancellationToken);
    public Task<RoslynScriptSignatureHelp?> GetSignatureHelpAsync(CancellationToken cancellationToken = default) =>
        _editor.GetSignatureHelpAsync(cancellationToken);
    public Task<RoslynScriptQuickInfo?> GetQuickInfoAsync(int position, CancellationToken cancellationToken = default) =>
        _editor.GetQuickInfoAsync(position, cancellationToken);
    public Task<RoslynScriptDefinitionLocation?> GetDefinitionAsync(int position, CancellationToken cancellationToken = default) =>
        _editor.GetDefinitionAsync(position, cancellationToken);
    public IReadOnlyList<RoslynScriptCodeAction> GetCodeActions(RoslynScriptDiagnostic diagnostic) =>
        _editor.GetCodeActions(diagnostic);
    public void ApplyCodeAction(RoslynScriptCodeAction action) => _editor.ApplyCodeAction(action);

    public IReadOnlyList<RoslynScriptDiagnostic> GetDiagnostics() => _editor.GetDiagnostics();
    public Task<IReadOnlyList<RoslynScriptDiagnostic>> GetDiagnosticsAsync(CancellationToken cancellationToken = default) =>
        _editor.GetDiagnosticsAsync(cancellationToken);
    public IReadOnlyList<RoslynScriptDiagnostic> CompileProgram() => _editor.CompileProgram();
    public Task<RoslynScriptCompilationResult> CompileProgramAsync(CancellationToken cancellationToken = default) =>
        _editor.CompileProgramAsync(cancellationToken);
    public void ShowDiagnostics(IEnumerable<RoslynScriptDiagnostic> diagnostics) => _editor.ShowDiagnostics(diagnostics);
    public void ScheduleDiagnostics() => _editor.ScheduleDiagnostics();
    public Task AnalyzeDiagnosticsAsync() => _editor.AnalyzeDiagnosticsAsync();
    public void ShowCompletionList(IEnumerable<string> candidates) => _editor.ShowCompletionList(candidates);
    public void ShowCompletionItems(IEnumerable<RoslynScriptCompletionItem> candidates) => _editor.ShowCompletionItems(candidates);
    public bool CanUndo => _editor.CanUndo;
    public bool CanRedo => _editor.CanRedo;
    public int Zoom { get => _editor.Zoom; set => _editor.Zoom = value; }
    public bool ShowWhitespace { get => _editor.ShowWhitespace; set => _editor.ShowWhitespace = value; }
    public bool WordWrap { get => _editor.WordWrap; set => _editor.WordWrap = value; }
    public void Undo() => _editor.Undo();
    public void Redo() => _editor.Redo();
    public void Copy() => _editor.Copy();
    public void Cut() => _editor.Cut();
    public void Paste() => _editor.Paste();
    public void GoToLine(int lineNumber) => _editor.GoToLine(lineNumber);
    public void ShowFindReplace(bool showReplace = false) => _editor.ShowFindReplace(showReplace);
    public bool FindNext(string text, bool matchCase = false, bool wholeWord = false, bool wrap = true) =>
        _editor.FindNext(text, matchCase, wholeWord, wrap);
    public int ReplaceAll(string searchText, string replacement, bool matchCase = false, bool wholeWord = false) =>
        _editor.ReplaceAll(searchText, replacement, matchCase, wholeWord);
    public bool FormatDocument() => _editor.FormatDocument();
    public void ToggleLineComment() => _editor.ToggleLineComment();
    public int TrimTrailingWhitespace() => _editor.TrimTrailingWhitespace();
    public bool InsertSnippet(string shortcut) => _editor.InsertSnippet(shortcut);
    public void RefreshFolding() => _editor.RefreshFolding();
    public void ExpandAllFolds() => _editor.ExpandAllFolds();
    public void CollapseAllFolds() => _editor.CollapseAllFolds();
    public void ScheduleHighlight() => _editor.ScheduleHighlight();
    public Task HighlightAsync() => _editor.HighlightAsync();
    public new bool Focus() => _editor.Focus();

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (_editor is not null && (e.Property == BackgroundProperty || e.Property == ForegroundProperty
            || e.Property == FontFamilyProperty || e.Property == FontSizeProperty))
            ApplyAppearance();
    }

    private async Task ShowDefaultCompletionsAsync()
    {
        var source = ScriptText;
        var caret = CaretOffset;
        try
        {
            var items = await _editor.GetCompletionsAsync();
            if (string.Equals(source, ScriptText, StringComparison.Ordinal) && caret == CaretOffset)
                _editor.ShowCompletionItems(items);
        }
        catch (OperationCanceledException)
        {
            // 后续输入会触发新的补全请求。
        }
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
