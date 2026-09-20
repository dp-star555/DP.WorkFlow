using System.Buffers;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using ScintillaNET;
using ScriptEngine.Workspaces;

namespace ScriptEngine.WinForms;

/// <summary>
/// 基于 ScintillaNET 渲染、由 Roslyn 提供语义能力的标准 C# 编辑器。
/// Scintilla 宿主布局位于同名 Designer.cs，语义高亮和补全行为保留在本文件。
/// </summary>
public sealed partial class RoslynScriptEditorControl : UserControl
{
    private const int ErrorIndicator = 0;
    private const int WarningIndicator = 1;
    private const int FoldMargin = 1;
    private const int FoldLevelBase = 0x400;
    private const int SemanticStyleBase = 40;
    private readonly System.Windows.Forms.Timer _highlightTimer = new() { Interval = 220 };
    private readonly System.Windows.Forms.Timer _completionTimer = new() { Interval = 300 };
    private readonly System.Windows.Forms.Timer _diagnosticTimer = new() { Interval = 600 };
    private readonly ToolTip _completionToolTip;
    private readonly ScriptCompletionPopup _completionPopup;
    private readonly ScriptEditorInputBehavior _inputBehavior = new();
    private CancellationTokenSource? _highlightCancellation;
    private CancellationTokenSource? _completionCancellation;
    private CancellationTokenSource? _diagnosticCancellation;
    private CancellationTokenSource? _signatureCancellation;
    private CancellationTokenSource? _quickInfoCancellation;
    private IReadOnlyList<RoslynScriptDiagnostic> _diagnostics = Array.Empty<RoslynScriptDiagnostic>();
    private bool _insertingPair;
    private CallTipPurpose _callTipPurpose;
    private ScriptFindReplaceDialog? _findReplaceDialog;
    private bool _autoCompletionEnabled = true;
    private int _autoCompletionMinimumPrefixLength = 2;
    private bool _liveDiagnosticsEnabled = true;
    private RoslynScriptEnvironment _scriptEnvironment = new();
    private IRoslynScriptCompletionProvider? _completionProvider;
    private RoslynScriptService _scriptService = new();
    private bool _ownsScriptService = true;
    private bool _settingText;
    private bool _resourcesDisposed;

    public RoslynScriptEditorControl()
    {
        InitializeComponent();
        _completionToolTip = new ToolTip(components)
        {
            AutoPopDelay = 12000,
            InitialDelay = 0,
            ReshowDelay = 0,
            ShowAlways = true
        };
        _completionPopup = new ScriptCompletionPopup();
        _completionPopup.ItemAccepted += (_, item) => CommitCompletion(item);

        _editor.Margins[0].Type = MarginType.Number;
        _editor.Margins[0].Sensitive = false;
        ConfigureFoldingMargin();
        _editor.MouseDwellTime = 450;
        ConfigureEditorContextMenu();
        _editor.Indicators[ErrorIndicator].Style = IndicatorStyle.Squiggle;
        _editor.Indicators[ErrorIndicator].ForeColor = Color.FromArgb(245, 80, 80);
        _editor.Indicators[ErrorIndicator].Under = true;
        _editor.Indicators[WarningIndicator].Style = IndicatorStyle.Squiggle;
        _editor.Indicators[WarningIndicator].ForeColor = Color.FromArgb(245, 180, 60);
        _editor.Indicators[WarningIndicator].Under = true;
        _diagnosticOverview.LineRequested += (_, lineIndex) => NavigateToLine(lineIndex);
        _editor.MarginClick += (_, eventArgs) => ToggleFoldAtMargin(eventArgs);
        _editor.HandleCreated += (_, _) => ApplyNativeDarkChrome();
        _editor.UpdateUI += (_, _) => UpdateBraceHighlighting();
        _editor.DwellStart += async (_, eventArgs) => await ShowHoverAsync(eventArgs.Position);
        _editor.DwellEnd += (_, _) => HideHoverCallTip();
        _editor.MouseDown += (_, _) => ClearCompletionState();
        _editor.SavePointLeft += (_, _) => ModifiedChanged?.Invoke(this, EventArgs.Empty);
        _editor.SavePointReached += (_, _) => ModifiedChanged?.Invoke(this, EventArgs.Empty);
        ApplyVisualStyles();

        _highlightTimer.Tick += async (_, _) =>
        {
            _highlightTimer.Stop();
            try
            {
                ApplyFolding();
                await HighlightAsync();
            }
            catch (OperationCanceledException)
            {
                // 新文本分析已经取代本次请求，不向 UI 报告取消。
            }
            catch (Exception exception)
            {
                Debug.WriteLine($"脚本语义分析失败：{exception}");
            }
        };
        _completionTimer.Tick += (_, _) =>
        {
            _completionTimer.Stop();
            _ = ShowAutomaticCompletionsAsync();
        };
        _diagnosticTimer.Tick += (_, _) =>
        {
            _diagnosticTimer.Stop();
            _ = AnalyzeDiagnosticsAsync();
        };
        _editor.TextChanged += (_, _) =>
        {
            ResetPendingSnippet();
            if (_settingText) return;
            _completionTimer.Stop();
            _completionCancellation?.Cancel();
            if (_completionPopup.IsOpen) _completionPopup.UpdateFilter(GetCurrentCompletionFilter());
            _diagnosticTimer.Stop();
            _diagnosticCancellation?.Cancel();
            _diagnostics = Array.Empty<RoslynScriptDiagnostic>();
            ClearDiagnosticIndicators();
            UpdateLineNumberMargin();
            ApplySyntacticHighlighting();
            ScheduleHighlight();
            ScheduleDiagnostics();
            OnTextChanged(EventArgs.Empty);
        };
        _editor.CharAdded += (_, eventArgs) =>
        {
            if (_insertingPair) return;
            var character = Convert.ToChar(eventArgs.Char);
            InsertAutomaticPair(character);
            ApplyAutomaticIndent(character);
            CharacterEntered?.Invoke(this, character);
            ScheduleAutomaticCompletion(character);
            if (character is '(' or ',') _ = ShowSignatureHelpAsync();
            else if (character == ')' && _callTipPurpose == CallTipPurpose.Signature) CancelCallTip();
        };
        _editor.KeyDown += (_, eventArgs) =>
        {
            if (eventArgs.KeyCode == Keys.Tab && TryHandleSnippetTab(eventArgs)) return;
            if (eventArgs.KeyCode != Keys.Tab) ResetPendingSnippet();
            if (!eventArgs.Control && !eventArgs.Alt && _completionPopup.HandleNavigationKey(eventArgs.KeyCode))
            {
                eventArgs.SuppressKeyPress = true;
                eventArgs.Handled = true;
                return;
            }
            if (_completionPopup.IsOpen && eventArgs.KeyCode is Keys.Left or Keys.Right)
                ClearCompletionState();
            if (HandlePairedKey(eventArgs)) return;
            if (eventArgs.Control && eventArgs.KeyCode == Keys.Space)
            {
                _completionTimer.Stop();
                _completionCancellation?.Cancel();
                if (CompletionRequested is not null) CompletionRequested(this, EventArgs.Empty);
                else _ = ShowAutomaticCompletionsAsync();
                eventArgs.SuppressKeyPress = true;
                eventArgs.Handled = true;
            }
            else if (eventArgs.Control && !eventArgs.Shift && eventArgs.KeyCode == Keys.OemQuestion)
            {
                ToggleLineComment();
                eventArgs.SuppressKeyPress = true;
                eventArgs.Handled = true;
            }
            else if (eventArgs.Control && eventArgs.KeyCode is Keys.F or Keys.H)
            {
                ShowFindReplace(eventArgs.KeyCode == Keys.H);
                eventArgs.SuppressKeyPress = true;
                eventArgs.Handled = true;
            }
            else if (eventArgs.Control && eventArgs.KeyCode == Keys.G)
            {
                ScriptGoToLineDialog.Show(FindForm(), this);
                eventArgs.SuppressKeyPress = true;
                eventArgs.Handled = true;
            }
            else if (eventArgs.Control && eventArgs.KeyCode == Keys.OemPeriod && CodeFixRequested is not null)
            {
                RequestCodeFix();
                eventArgs.SuppressKeyPress = true;
                eventArgs.Handled = true;
            }
            else if (eventArgs.Shift && eventArgs.KeyCode == Keys.F12 && FindReferencesRequested is not null)
            {
                RequestFindReferences();
                eventArgs.SuppressKeyPress = true;
                eventArgs.Handled = true;
            }
            else if (eventArgs.KeyCode == Keys.F2 && RenameRequested is not null)
            {
                RequestRename();
                eventArgs.SuppressKeyPress = true;
                eventArgs.Handled = true;
            }
            else if (eventArgs.KeyCode == Keys.F12)
            {
                RequestDefinition();
                eventArgs.SuppressKeyPress = true;
                eventArgs.Handled = true;
            }
            OnKeyDown(eventArgs);
        };
        _editor.KeyPress += (_, eventArgs) =>
        {
            if (_completionPopup.IsOpen
                && (eventArgs.KeyChar == '.'
                    || eventArgs.KeyChar == '(' && _completionPopup.SelectedItem?.InsertionText.EndsWith(")", StringComparison.Ordinal) != true))
                _completionPopup.TryAcceptSelection();
            if (!TryWrapSelection(eventArgs.KeyChar)) OnKeyPress(eventArgs);
            else eventArgs.Handled = true;
        };
        _editor.KeyUp += (_, eventArgs) => OnKeyUp(eventArgs);
        _editor.Enter += (_, _) => OnEnter(EventArgs.Empty);
        _editor.Leave += (_, _) =>
        {
            ClearCompletionState();
            OnLeave(EventArgs.Empty);
        };
    }

    private void ApplyNativeDarkChrome()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || !_editor.IsHandleCreated) return;
        _ = SetWindowTheme(_editor.Handle, "DarkMode_Explorer", null);
    }

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr window, string? subAppName, string? subIdList);

    public event EventHandler<char>? CharacterEntered;

    /// <summary>用户按 F12 请求跳转定义时发生；未订阅时由单文件编辑器自行处理。</summary>
    public event EventHandler<ScriptDefinitionRequestedEventArgs>? DefinitionRequested;

    /// <summary>用户按 F2 请求项目级重命名时发生。</summary>
    public event EventHandler<ScriptSymbolRequestedEventArgs>? RenameRequested;

    /// <summary>用户按 Shift+F12 请求查找项目引用时发生。</summary>
    public event EventHandler<ScriptSymbolRequestedEventArgs>? FindReferencesRequested;

    /// <summary>用户按 Ctrl+. 请求项目级代码修复时发生。</summary>
    public event EventHandler<ScriptSymbolRequestedEventArgs>? CodeFixRequested;

    /// <summary>文档进入或离开已保存状态时发生。</summary>
    public event EventHandler? ModifiedChanged;

    /// <summary>用户按 Ctrl+Space 请求手动补全时发生；未订阅时控件会直接显示内置补全。</summary>
    public event EventHandler? CompletionRequested;

    /// <summary>获取或设置是否在输入标识符或点号后自动显示代码补全。</summary>
    public bool AutoCompletionEnabled
    {
        get => _autoCompletionEnabled;
        set
        {
            _autoCompletionEnabled = value;
            if (value) return;
            _completionTimer.Stop();
            _completionCancellation?.Cancel();
            ClearCompletionState();
        }
    }

    /// <summary>获取或设置自动弹出补全列表所需的最少标识符字符数，默认为 2。</summary>
    public int AutoCompletionMinimumPrefixLength
    {
        get => _autoCompletionMinimumPrefixLength;
        set => _autoCompletionMinimumPrefixLength = value is >= 1 and <= 10
            ? value
            : throw new ArgumentOutOfRangeException(nameof(value), "自动补全前缀长度必须在 1 到 10 之间。");
    }

    /// <summary>获取或设置标识符输入停止后弹出补全列表的延迟毫秒数。</summary>
    public int AutoCompletionDelay
    {
        get => _completionTimer.Interval;
        set => _completionTimer.Interval = value is >= 50 and <= 2000
            ? value
            : throw new ArgumentOutOfRangeException(nameof(value), "自动补全延迟必须在 50 到 2000 毫秒之间。");
    }

    /// <summary>获取或设置是否在编辑停止后自动刷新 Roslyn 错误和警告。</summary>
    public bool LiveDiagnosticsEnabled
    {
        get => _liveDiagnosticsEnabled;
        set
        {
            _liveDiagnosticsEnabled = value;
            if (value)
            {
                ScheduleDiagnostics();
                return;
            }
            _diagnosticTimer.Stop();
            _diagnosticCancellation?.Cancel();
        }
    }

    /// <summary>获取或设置编辑停止后运行实时诊断的延迟毫秒数。</summary>
    public int LiveDiagnosticsDelay
    {
        get => _diagnosticTimer.Interval;
        set => _diagnosticTimer.Interval = value is >= 200 and <= 5000
            ? value
            : throw new ArgumentOutOfRangeException(nameof(value), "实时诊断延迟必须在 200 到 5000 毫秒之间。");
    }

    public RoslynScriptService ScriptService
    {
        get => _scriptService;
        set
        {
            if (value is null) throw new ArgumentNullException(nameof(value));
            if (ReferenceEquals(_scriptService, value)) return;
            _highlightCancellation?.Cancel();
            _completionCancellation?.Cancel();
            _diagnosticCancellation?.Cancel();
            if (_ownsScriptService) _scriptService.Dispose();
            _scriptService = value;
            _ownsScriptService = false;
            ScheduleHighlight();
            ScheduleDiagnostics();
        }
    }

    /// <summary>
    /// 可选的完整 Roslyn CompletionService 适配器；未设置时继续使用轻量核心补全，
    /// 因而基础编辑器不会强制加载 Workspaces/Features 程序集。
    /// </summary>
    public IRoslynScriptCompletionProvider? CompletionProvider
    {
        get => _completionProvider;
        set
        {
            if (ReferenceEquals(_completionProvider, value)) return;
            _completionCancellation?.Cancel();
            _completionProvider = value;
            ClearCompletionState();
        }
    }

    public RoslynScriptEnvironment ScriptEnvironment
    {
        get => _scriptEnvironment;
        set
        {
            _scriptEnvironment = value ?? throw new ArgumentNullException(nameof(value));
            _highlightCancellation?.Cancel();
            _completionCancellation?.Cancel();
            _diagnosticCancellation?.Cancel();
            if (_editor is not null)
            {
                ScheduleHighlight();
                ScheduleDiagnostics();
            }
        }
    }

#if !NETFRAMEWORK
    [System.Diagnostics.CodeAnalysis.AllowNull]
#endif
    public override string Text
    {
        get => _editor?.Text ?? string.Empty;
        set
        {
            if (_editor is null || string.Equals(_editor.Text, value, StringComparison.Ordinal)) return;
            _completionTimer.Stop();
            _completionCancellation?.Cancel();
            ClearCompletionState();
            _settingText = true;
            try { _editor.Text = value ?? string.Empty; }
            finally { _settingText = false; }
            UpdateLineNumberMargin();
            ApplySyntacticHighlighting();
            ApplyFolding();
            ScheduleHighlight();
            ScheduleDiagnostics();
        }
    }

    /// <summary>文档是否包含尚未标记为已保存的修改。</summary>
    public bool IsModified => _editor.Modified;

    /// <summary>将当前文档版本标记为已保存。</summary>
    public void MarkSaved() => _editor.SetSavePoint();

    public int SelectionStart
    {
        get => _editor.SelectionStart;
        set => _editor.SetSelection(Clamp(value, 0, _editor.TextLength), Clamp(value, 0, _editor.TextLength));
    }

    public int SelectionLength => _editor.SelectionEnd - _editor.SelectionStart;
    public int TextLength => _editor.TextLength;

    public string SelectedText
    {
        get => _editor.SelectedText;
        set => _editor.ReplaceSelection(value ?? string.Empty);
    }

    public void Select(int start, int length) =>
        _editor.SetSelection(Clamp(start + length, 0, TextLength), Clamp(start, 0, TextLength));

    public void SelectAll() => _editor.SelectAll();
    public new bool Focus() => _editor.Focus();

    public Point GetPositionFromCharIndex(int position) => new(
        _editor.PointXFromPosition(Clamp(position, 0, TextLength)),
        _editor.PointYFromPosition(Clamp(position, 0, TextLength)));

    public Task<IReadOnlyList<RoslynScriptCompletionItem>> GetCompletionsAsync(CancellationToken cancellationToken = default) =>
        GetCompletionsAsync(Text, SelectionStart, cancellationToken);

    /// <summary>获取当前调用表达式的方法签名和活动参数。</summary>
    public Task<RoslynScriptSignatureHelp?> GetSignatureHelpAsync(CancellationToken cancellationToken = default) =>
        ScriptService.GetSignatureHelpAsync(Text, SelectionStart, ScriptEnvironment, cancellationToken);

    /// <summary>获取指定字符位置的符号快速信息。</summary>
    public Task<RoslynScriptQuickInfo?> GetQuickInfoAsync(int position, CancellationToken cancellationToken = default) =>
        ScriptService.GetQuickInfoAsync(Text, position, ScriptEnvironment, cancellationToken);

    /// <summary>定位指定位置符号的定义。</summary>
    public Task<RoslynScriptDefinitionLocation?> GetDefinitionAsync(int position, CancellationToken cancellationToken = default) =>
        ScriptService.GetDefinitionAsync(Text, position, ScriptEnvironment, cancellationToken);

    /// <summary>获取诊断对应的可用代码修复。</summary>
    public IReadOnlyList<RoslynScriptCodeAction> GetCodeActions(RoslynScriptDiagnostic diagnostic) =>
        ScriptService.GetCodeActions(Text, diagnostic);

    /// <summary>应用代码修复并将光标放到修改内容之后。</summary>
    public void ApplyCodeAction(RoslynScriptCodeAction action)
    {
        var source = Text;
        Text = RoslynScriptService.ApplyCodeAction(source, action);
        SelectionStart = Math.Min(action.Start + action.Replacement.Length, TextLength);
    }

    public IReadOnlyList<RoslynScriptDiagnostic> GetDiagnostics() =>
        ScriptService.GetDiagnostics(Text, ScriptEnvironment);

    public Task<IReadOnlyList<RoslynScriptDiagnostic>> GetDiagnosticsAsync(CancellationToken cancellationToken = default) =>
        ScriptService.GetDiagnosticsAsync(Text, ScriptEnvironment, cancellationToken);

    public IReadOnlyList<RoslynScriptDiagnostic> CompileProgram() =>
        ScriptService.CompileProgram(Text, ScriptEnvironment);

    public async Task<RoslynScriptCompilationResult> CompileProgramAsync(CancellationToken cancellationToken = default) =>
        await ScriptService.CompileProgramAsync(Text, ScriptEnvironment, cancellationToken).ConfigureAwait(true);

    public void ShowCompletionList(IEnumerable<string> candidates) => ShowCompletionItems(
        candidates
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => new RoslynScriptCompletionItem(item, item)));

    /// <summary>显示带分类列、内联说明、文档预览和模糊过滤的补全项。</summary>
    public void ShowCompletionItems(IEnumerable<RoslynScriptCompletionItem> candidates)
    {
        var items = candidates
            .Where(item => !string.IsNullOrWhiteSpace(item.DisplayText))
            .Select(item => item with { DisplayText = NormalizeCompletionDisplayText(item.DisplayText) })
            .Where(item => item.DisplayText.Length > 0)
            .GroupBy(item => item.DisplayText + "\0" + item.InsertionText, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
        if (items.Length == 0)
        {
            ClearCompletionState();
            return;
        }
        var caret = GetPositionFromCharIndex(SelectionStart);
        var screen = _editor.PointToScreen(new Point(caret.X, caret.Y + Math.Max(18, _editor.Font.Height)));
        _completionPopup.Open(_editor, screen, items, GetCurrentCompletionFilter(), BackColor, ForeColor);
    }

    /// <summary>自定义补全窗口当前是否可见。</summary>
    public bool IsCompletionListVisible => _completionPopup.IsOpen;

    /// <summary>是否可以撤销。</summary>
    public bool CanUndo => _editor.CanUndo;

    /// <summary>是否可以重做。</summary>
    public bool CanRedo => _editor.CanRedo;

    /// <summary>编辑器缩放级别，零表示默认大小。</summary>
    public int Zoom
    {
        get => _editor.Zoom;
        set => _editor.Zoom = Clamp(value, -10, 20);
    }

    /// <summary>是否显示空格和制表符标记。</summary>
    public bool ShowWhitespace
    {
        get => _editor.ViewWhitespace != WhitespaceMode.Invisible;
        set => _editor.ViewWhitespace = value ? WhitespaceMode.VisibleAlways : WhitespaceMode.Invisible;
    }

    /// <summary>是否启用长行自动换行。</summary>
    public bool WordWrap
    {
        get => _editor.WrapMode != WrapMode.None;
        set => _editor.WrapMode = value ? WrapMode.Word : WrapMode.None;
    }

    /// <summary>撤销最近一次编辑。</summary>
    public void Undo() => _editor.Undo();

    /// <summary>恢复最近一次撤销。</summary>
    public void Redo() => _editor.Redo();

    /// <summary>复制选区。</summary>
    public void Copy() => _editor.Copy();

    /// <summary>剪切选区。</summary>
    public void Cut() => _editor.Cut();

    /// <summary>粘贴剪贴板文本。</summary>
    public void Paste() => _editor.Paste();

    /// <summary>跳转到从 1 开始的源码行。</summary>
    public void GoToLine(int lineNumber) => NavigateToLine(Clamp(lineNumber - 1, 0, Math.Max(0, _editor.Lines.Count - 1)));

    /// <summary>显示非模态查找或替换窗口。</summary>
    public void ShowFindReplace(bool showReplace = false)
    {
        _findReplaceDialog?.Close();
        _findReplaceDialog?.Dispose();
        var dialog = new ScriptFindReplaceDialog(this, showReplace);
        _findReplaceDialog = dialog;
        dialog.FormClosed += (_, _) =>
        {
            if (ReferenceEquals(_findReplaceDialog, dialog)) _findReplaceDialog = null;
            dialog.Dispose();
        };
        dialog.ActivateSearch(showReplace);
    }

    /// <summary>查找下一个文本并选中。</summary>
    public bool FindNext(string text, bool matchCase = false, bool wholeWord = false, bool wrap = true)
    {
        if (string.IsNullOrEmpty(text)) return false;
        var comparison = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var source = Text;
        var start = Math.Max(_editor.SelectionStart, _editor.SelectionEnd);
        var found = FindText(source, text, start, comparison, wholeWord);
        if (found < 0 && wrap) found = FindText(source, text, 0, comparison, wholeWord);
        if (found < 0) return false;
        Select(found, text.Length);
        _editor.ScrollCaret();
        return true;
    }

    /// <summary>替换全文匹配项并返回替换次数。</summary>
    public int ReplaceAll(string searchText, string replacement, bool matchCase = false, bool wholeWord = false)
    {
        if (string.IsNullOrEmpty(searchText)) return 0;
        var comparison = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var source = Text;
        var builder = new System.Text.StringBuilder(source.Length);
        var offset = 0;
        var count = 0;
        while (offset < source.Length)
        {
            var found = FindText(source, searchText, offset, comparison, wholeWord);
            if (found < 0) break;
            builder.Append(source, offset, found - offset).Append(replacement ?? string.Empty);
            offset = found + searchText.Length;
            count++;
        }
        if (count == 0) return 0;
        builder.Append(source, offset, source.Length - offset);
        Text = builder.ToString();
        return count;
    }

    /// <summary>使用 Roslyn 容错语法树格式化当前文档。</summary>
    public bool FormatDocument()
    {
        var formatted = RoslynScriptService.FormatSource(Text, _editor.IndentWidth, Environment.NewLine);
        if (string.Equals(formatted, Text, StringComparison.Ordinal)) return false;
        var caret = SelectionStart;
        Text = formatted;
        SelectionStart = Math.Min(caret, TextLength);
        return true;
    }

    /// <summary>切换选中行的双斜线注释。</summary>
    public void ToggleLineComment()
    {
        var startLine = _editor.LineFromPosition(Math.Min(_editor.SelectionStart, _editor.SelectionEnd));
        var selectionEnd = Math.Max(_editor.SelectionStart, _editor.SelectionEnd);
        var endLine = _editor.LineFromPosition(selectionEnd);
        if (selectionEnd > 0 && selectionEnd == _editor.Lines[endLine].Position && endLine > startLine) endLine--;
        var lines = Enumerable.Range(startLine, endLine - startLine + 1)
            .Select(index => _editor.Lines[index])
            .Where(line => !string.IsNullOrWhiteSpace(line.Text))
            .ToArray();
        if (lines.Length == 0) return;
        var uncomment = lines.All(line => line.Text.TrimStart().StartsWith("//", StringComparison.Ordinal));
        _editor.BeginUndoAction();
        try
        {
            for (var lineIndex = lines.Length - 1; lineIndex >= 0; lineIndex--)
            {
                var line = lines[lineIndex];
                var contentStart = line.Position + line.Text.TakeWhile(char.IsWhiteSpace).Count();
                if (uncomment)
                {
                    _editor.TargetStart = contentStart;
                    _editor.TargetEnd = Math.Min(contentStart + 2, line.EndPosition);
                    if (_editor.GetTextRange(contentStart, Math.Min(2, line.EndPosition - contentStart)) == "//")
                        _editor.ReplaceTarget(string.Empty);
                }
                else
                {
                    _editor.InsertText(contentStart, "// ");
                }
            }
        }
        finally
        {
            _editor.EndUndoAction();
        }
    }

    /// <summary>移除所有行末空格和制表符。</summary>
    public int TrimTrailingWhitespace()
    {
        var source = Text;
        var trimmed = System.Text.RegularExpressions.Regex.Replace(source, "[\\t ]+(?=\\r?$)", string.Empty,
            System.Text.RegularExpressions.RegexOptions.Multiline);
        if (string.Equals(source, trimmed, StringComparison.Ordinal)) return 0;
        var removed = source.Length - trimmed.Length;
        Text = trimmed;
        return removed;
    }

    /// <summary>插入内置 C# 代码片段。</summary>
    public bool InsertSnippet(string shortcut) => TryInsertSnippet(shortcut);

    /// <summary>立即重新计算并应用当前源码的折叠层级。</summary>
    public void RefreshFolding() => ApplyFolding();

    /// <summary>展开全部可折叠的 C# 结构。</summary>
    public void ExpandAllFolds() => _editor.FoldAll(FoldAction.Expand);

    /// <summary>收缩全部可折叠的 C# 结构，并在标题行显示省略标记。</summary>
    public void CollapseAllFolds()
    {
        for (var lineIndex = _editor.Lines.Count - 1; lineIndex >= 0; lineIndex--)
        {
            var line = _editor.Lines[lineIndex];
            if ((line.FoldLevelFlags & FoldLevelFlags.Header) != 0 && line.Expanded)
                line.ToggleFoldShowText("  …");
        }
    }

    public void ScheduleHighlight()
    {
        _highlightTimer.Stop();
        _highlightTimer.Start();
    }

    /// <summary>按配置的防抖延迟安排一次实时 Roslyn 诊断。</summary>
    public void ScheduleDiagnostics()
    {
        _diagnosticTimer.Stop();
        if (!LiveDiagnosticsEnabled) return;
        _diagnosticTimer.Start();
    }

    /// <summary>立即分析当前编辑快照并显示错误、警告和文档概览标记。</summary>
    public async Task AnalyzeDiagnosticsAsync()
    {
        var source = Text;
        var environment = ScriptEnvironment;
        _diagnosticCancellation?.Cancel();
        _diagnosticCancellation?.Dispose();
        var cancellation = _diagnosticCancellation = new CancellationTokenSource();
        try
        {
            var diagnostics = await ScriptService.GetDiagnosticsAsync(source, environment, cancellation.Token);
            if (IsDisposed || cancellation.IsCancellationRequested
                || !ReferenceEquals(environment, ScriptEnvironment)
                || !string.Equals(source, Text, StringComparison.Ordinal))
                return;
            ShowDiagnostics(diagnostics);
        }
        catch (OperationCanceledException)
        {
            // 用户继续输入后，下一次分析会替代当前请求。
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"脚本实时诊断失败：{exception}");
        }
    }

    public async Task HighlightAsync()
    {
        var source = Text;
        _highlightCancellation?.Cancel();
        _highlightCancellation?.Dispose();
        var cancellation = _highlightCancellation = new CancellationTokenSource();
        IReadOnlyList<RoslynScriptHighlightSpan> spans;
        try
        {
            spans = await ScriptService.GetHighlightSpansAsync(source, ScriptEnvironment, cancellation.Token);
        }
        catch (OperationCanceledException) { return; }
        if (IsDisposed || cancellation.IsCancellationRequested || !string.Equals(source, Text, StringComparison.Ordinal)) return;
        ApplyHighlightSpans(source, spans, preserveSemanticStyles: false);
    }

    private void ScheduleAutomaticCompletion(char character)
    {
        if (!AutoCompletionEnabled) return;
        if (character == '.')
        {
            _completionTimer.Stop();
            ClearCompletionState();
            _ = ShowAutomaticCompletionsAsync();
            return;
        }

        var prefixLength = SelectionStart - RoslynScriptService.GetCompletionStart(Text, SelectionStart);
        var isIdentifierCharacter = character == '_'
                                    || char.IsLetter(character)
                                    || char.IsDigit(character) && prefixLength > 1;
        if (isIdentifierCharacter && prefixLength >= AutoCompletionMinimumPrefixLength)
        {
            _completionTimer.Stop();
            _completionTimer.Start();
            return;
        }

        _completionTimer.Stop();
        _completionCancellation?.Cancel();
        ClearCompletionState();
    }

    private Task<IReadOnlyList<RoslynScriptCompletionItem>> GetCompletionsAsync(
        string source,
        int caret,
        CancellationToken cancellationToken)
    {
        var provider = CompletionProvider;
        if (provider is null)
            return ScriptService.GetCompletionsAsync(source, caret, ScriptEnvironment, cancellationToken);
        return provider.GetCompletionsAsync(
            new RoslynScriptProject
            {
                SourceFiles = new[] { new RoslynScriptSourceFile("Program.cs", source) }
            },
            "Program.cs",
            caret,
            ScriptEnvironment,
            cancellationToken);
    }

    private async Task ShowAutomaticCompletionsAsync()
    {
        // 立即触发的请求也应消费尚未到期的防抖，避免重复请求。
        _completionTimer.Stop();
        var source = Text;
        var caret = SelectionStart;
        _completionCancellation?.Cancel();
        _completionCancellation?.Dispose();
        var cancellation = _completionCancellation = new CancellationTokenSource();
        try
        {
            var items = await GetCompletionsAsync(source, caret, cancellation.Token);
            if (IsDisposed || cancellation.IsCancellationRequested
                || !string.Equals(source, Text, StringComparison.Ordinal)
                || caret != SelectionStart)
                return;
            ShowCompletionItems(ShouldIncludeSnippetCompletions(source, caret)
                ? items.Concat(GetSnippetCompletions())
                : items);
        }
        catch (OperationCanceledException)
        {
            // 用户继续输入后，下一次补全请求会替代当前请求。
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"脚本自动补全失败：{exception}");
        }
    }

    /// <summary>使用 Scintilla 原生波浪线和右侧文档概览标记 Roslyn 错误与警告。</summary>
    public void ShowDiagnostics(IEnumerable<RoslynScriptDiagnostic> diagnostics)
    {
        _diagnostics = diagnostics.Where(item => (item.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning)
                                                  && item.Origin == RoslynScriptDiagnosticOrigin.UserSource
                                                  && item.Start >= 0).ToArray();
        ClearDiagnosticIndicators();
        foreach (var diagnostic in _diagnostics)
        {
            if (TextLength == 0) continue;
            _editor.IndicatorCurrent = diagnostic.Severity == DiagnosticSeverity.Error
                ? ErrorIndicator
                : WarningIndicator;
            var start = diagnostic.Length == 0
                ? Clamp(diagnostic.Start - 1, 0, TextLength - 1)
                : Clamp(diagnostic.Start, 0, TextLength - 1);
            var length = diagnostic.Length == 0 ? 1 : Math.Min(diagnostic.Length, TextLength - start);
            _editor.IndicatorFillRange(start, Math.Max(1, length));
        }
        _diagnosticOverview.SetDiagnostics(_diagnostics, _editor.Lines.Count);
    }

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        if (_editor is not null) ApplyVisualStyles();
    }

    protected override void OnBackColorChanged(EventArgs e)
    {
        base.OnBackColorChanged(e);
        if (_editor is not null) ApplyVisualStyles();
    }

    protected override void OnForeColorChanged(EventArgs e)
    {
        base.OnForeColorChanged(e);
        if (_editor is not null) ApplyVisualStyles();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_resourcesDisposed)
        {
            _resourcesDisposed = true;
            _highlightTimer.Dispose();
            _completionTimer.Dispose();
            _diagnosticTimer.Dispose();
            _highlightCancellation?.Cancel();
            _highlightCancellation?.Dispose();
            _highlightCancellation = null;
            _completionCancellation?.Cancel();
            _completionCancellation?.Dispose();
            _completionCancellation = null;
            _diagnosticCancellation?.Cancel();
            _diagnosticCancellation?.Dispose();
            _diagnosticCancellation = null;
            _signatureCancellation?.Cancel();
            _signatureCancellation?.Dispose();
            _signatureCancellation = null;
            _quickInfoCancellation?.Cancel();
            _quickInfoCancellation?.Dispose();
            _quickInfoCancellation = null;
            _findReplaceDialog?.Close();
            _findReplaceDialog?.Dispose();
            _findReplaceDialog = null;
            _completionPopup.Dispose();
            if (_ownsScriptService) _scriptService.Dispose();
            components.Dispose();
        }
        base.Dispose(disposing);
    }

    private void ConfigureFoldingMargin()
    {
        var margin = _editor.Margins[FoldMargin];
        margin.Type = MarginType.Symbol;
        margin.Mask = Marker.MaskFolders;
        margin.Sensitive = true;
        margin.Width = 18;

        _editor.Markers[Marker.Folder].Symbol = MarkerSymbol.Arrow;
        _editor.Markers[Marker.FolderOpen].Symbol = MarkerSymbol.ArrowDown;
        _editor.Markers[Marker.FolderEnd].Symbol = MarkerSymbol.Arrow;
        _editor.Markers[Marker.FolderOpenMid].Symbol = MarkerSymbol.ArrowDown;
        _editor.Markers[Marker.FolderMidTail].Symbol = MarkerSymbol.Empty;
        _editor.Markers[Marker.FolderSub].Symbol = MarkerSymbol.Empty;
        _editor.Markers[Marker.FolderTail].Symbol = MarkerSymbol.Empty;
        _editor.AutomaticFold = AutomaticFold.Show | AutomaticFold.Change;
        // 不绘制收缩块下方的横线，折叠状态只由箭头和行内省略号表达。
        _editor.SetFoldFlags((FoldFlags)0);
        _editor.FoldDisplayTextSetStyle(FoldDisplayText.Boxed);
    }

    private void ToggleFoldAtMargin(MarginClickEventArgs eventArgs)
    {
        if (eventArgs.Margin != FoldMargin) return;
        var lineIndex = _editor.LineFromPosition(eventArgs.Position);
        if (lineIndex < 0 || lineIndex >= _editor.Lines.Count) return;
        var line = _editor.Lines[lineIndex];
        if ((line.FoldLevelFlags & FoldLevelFlags.Header) == 0) return;
        line.ToggleFoldShowText("  …");
    }

    private void NavigateToLine(int lineIndex)
    {
        if (_editor.Lines.Count == 0) return;
        var line = _editor.Lines[Clamp(lineIndex, 0, _editor.Lines.Count - 1)];
        line.EnsureVisible();
        line.Goto();
        _editor.Focus();
    }

    private void ApplyFoldingColors()
    {
        var foreground = Blend(BackColor, ForeColor, 0.72);
        _editor.SetFoldMarginColor(true, BackColor);
        _editor.SetFoldMarginHighlightColor(true, BackColor);

        using var collapsedChevron = CreateFoldChevron(expanded: false, foreground);
        using var expandedChevron = CreateFoldChevron(expanded: true, foreground);
        _editor.Markers[Marker.Folder].DefineRgbaImage(collapsedChevron);
        _editor.Markers[Marker.FolderEnd].DefineRgbaImage(collapsedChevron);
        _editor.Markers[Marker.FolderOpen].DefineRgbaImage(expandedChevron);
        _editor.Markers[Marker.FolderOpenMid].DefineRgbaImage(expandedChevron);
        foreach (var markerIndex in new[] { Marker.FolderMidTail, Marker.FolderTail, Marker.FolderSub })
            _editor.Markers[markerIndex].Symbol = MarkerSymbol.Empty;
    }

    private static Bitmap CreateFoldChevron(bool expanded, Color color)
    {
        var bitmap = new Bitmap(14, 14, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.Transparent);
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var pen = new Pen(color, 1.7F)
        {
            StartCap = System.Drawing.Drawing2D.LineCap.Round,
            EndCap = System.Drawing.Drawing2D.LineCap.Round,
            LineJoin = System.Drawing.Drawing2D.LineJoin.Round
        };
        var points = expanded
            ? new[] { new PointF(3F, 5F), new PointF(7F, 9F), new PointF(11F, 5F) }
            : new[] { new PointF(5F, 3F), new PointF(9F, 7F), new PointF(5F, 11F) };
        graphics.DrawLines(pen, points);
        return bitmap;
    }

    private void ApplyFolding()
    {
        if (_editor is null || IsDisposed) return;
        var lineCount = _editor.Lines.Count;
        if (lineCount == 0) return;
        var spans = ScriptService.GetFoldingSpans(Text);
        var collapsedLines = new List<int>();
        for (var lineIndex = 0; lineIndex < lineCount; lineIndex++)
        {
            var line = _editor.Lines[lineIndex];
            if ((line.FoldLevelFlags & FoldLevelFlags.Header) != 0 && !line.Expanded)
            {
                collapsedLines.Add(lineIndex);
                line.FoldLine(FoldAction.Expand);
            }
        }

        var depthChanges = new int[lineCount + 1];
        var headers = new bool[lineCount];
        foreach (var span in spans)
        {
            var startLine = Clamp(span.StartLine, 0, lineCount - 1);
            var endLine = Clamp(span.EndLine, startLine, lineCount - 1);
            if (endLine <= startLine) continue;
            headers[startLine] = true;
            depthChanges[startLine + 1]++;
            if (endLine + 1 < depthChanges.Length) depthChanges[endLine + 1]--;
        }

        var depth = 0;
        for (var lineIndex = 0; lineIndex < lineCount; lineIndex++)
        {
            depth += depthChanges[lineIndex];
            var line = _editor.Lines[lineIndex];
            line.FoldLevel = FoldLevelBase + Math.Min(depth, 0xFFF - FoldLevelBase);
            // 空白行必须保持普通层级标志；White 会把块结束后的连续空行也归入前一个收缩区。
            line.FoldLevelFlags = headers[lineIndex]
                ? FoldLevelFlags.Header
                : (FoldLevelFlags)0;
        }

        foreach (var lineIndex in collapsedLines)
            if (lineIndex < lineCount && (_editor.Lines[lineIndex].FoldLevelFlags & FoldLevelFlags.Header) != 0
                && _editor.Lines[lineIndex].Expanded)
                _editor.Lines[lineIndex].ToggleFoldShowText("  …");
    }

    private void ApplyVisualStyles()
    {
        if (_editor is null) return;
        var font = Font.Name == Control.DefaultFont.Name ? "Consolas" : Font.Name;
        var size = Font.Name == Control.DefaultFont.Name ? 10.5f : Font.SizeInPoints;
        _editor.Styles[Style.Default].Font = font;
        _editor.Styles[Style.Default].SizeF = size;
        _editor.Styles[Style.Default].ForeColor = ForeColor;
        _editor.Styles[Style.Default].BackColor = BackColor;
        _editor.StyleClearAll();

        ConfigureStyle(RoslynScriptHighlightKind.Keyword, Color.FromArgb(86, 156, 214));
        ConfigureStyle(RoslynScriptHighlightKind.String, Color.FromArgb(206, 145, 120));
        ConfigureStyle(RoslynScriptHighlightKind.Comment, Color.FromArgb(106, 153, 85));
        ConfigureStyle(RoslynScriptHighlightKind.Type, Color.FromArgb(78, 201, 176));
        ConfigureStyle(RoslynScriptHighlightKind.Number, Color.FromArgb(181, 206, 168));
        ConfigureStyle(RoslynScriptHighlightKind.Method, Color.FromArgb(220, 220, 170));
        ConfigureStyle(RoslynScriptHighlightKind.Property, Color.FromArgb(156, 220, 254));
        ConfigureStyle(RoslynScriptHighlightKind.XmlDocTag, Color.FromArgb(128, 128, 128));

        _editor.Styles[Style.LineNumber].Font = font;
        _editor.Styles[Style.LineNumber].SizeF = size;
        _editor.Styles[Style.LineNumber].ForeColor = Color.FromArgb(150, 150, 150);
        _editor.Styles[Style.LineNumber].BackColor = BackColor;
        _editor.Styles[Style.IndentGuide].ForeColor = Blend(BackColor, ForeColor, 0.2);
        _editor.Styles[Style.IndentGuide].BackColor = BackColor;
        _editor.Styles[Style.FoldDisplayText].ForeColor = MutedColor(ForeColor);
        _editor.Styles[Style.FoldDisplayText].BackColor = Blend(BackColor, ForeColor, 0.08);
        _editor.Styles[Style.BraceLight].ForeColor = Color.FromArgb(255, 210, 80);
        _editor.Styles[Style.BraceLight].BackColor = Blend(BackColor, ForeColor, 0.12);
        _editor.Styles[Style.BraceLight].Bold = true;
        _editor.Styles[Style.BraceBad].ForeColor = Color.FromArgb(245, 80, 80);
        _editor.Styles[Style.BraceBad].Bold = true;
        _editor.CallTipSetForeHlt(Color.FromArgb(86, 156, 214));
        _editor.IndentationGuides = IndentView.LookBoth;
        ApplyFoldingColors();
        _editor.SetSelectionBackColor(true, Color.FromArgb(0, 122, 204));
        _editor.SetSelectionForeColor(true, Color.White);
        // Scintilla 默认使用黑色 1px 光标，在深色背景上几乎不可见。
        _editor.CaretForeColor = ForeColor;
        _editor.CaretWidth = 2;
        _editor.CaretPeriod = 500;
        // 显式设置颜色可避免原生控件继承未初始化底色，同时提供轻量的当前行定位。
        _editor.CaretLineBackColor = Blend(BackColor, ForeColor, 0.07);
        _editor.CaretLineBackColorAlpha = 256;
        _editor.CaretLineVisibleAlways = false;
        _editor.CaretLineVisible = true;
        _diagnosticOverview.BackColor = BackColor;
        _diagnosticOverview.ForeColor = MutedColor(ForeColor);
        _diagnosticOverview.ErrorColor = _editor.Indicators[ErrorIndicator].ForeColor;
        _diagnosticOverview.WarningColor = _editor.Indicators[WarningIndicator].ForeColor;
        UpdateLineNumberMargin();
        ApplySyntacticHighlighting();
        ScheduleHighlight();
    }

    private void ConfigureStyle(RoslynScriptHighlightKind kind, Color foreground)
    {
        var style = _editor.Styles[StyleIndex(kind)];
        style.ForeColor = foreground;
        style.BackColor = BackColor;
        style.Font = _editor.Styles[Style.Default].Font;
        style.SizeF = _editor.Styles[Style.Default].SizeF;
    }

    /// <summary>同步应用不依赖平台程序集的语法色，确保编辑器首帧和输入期间不会显示为纯白文本。</summary>
    private void ApplySyntacticHighlighting()
    {
        if (_editor is null || IsDisposed) return;
        var source = Text;
        var spans = ScriptService.GetSyntacticHighlightSpans(source);
        ApplyHighlightSpans(source, spans, preserveSemanticStyles: true);
    }

    /// <summary>
    /// 只提交实际变化的样式区间。即时语法阶段保留上一轮类型、方法和属性颜色，
    /// 避免每输入一个字符都把整篇代码刷白，再等待后台语义颜色恢复。
    /// </summary>
    private void ApplyHighlightSpans(
        string source,
        IEnumerable<RoslynScriptHighlightSpan> spans,
        bool preserveSemanticStyles)
    {
        if (_editor is null || !string.Equals(source, Text, StringComparison.Ordinal)) return;
        var length = _editor.TextLength;
        if (length == 0) return;
        var desiredStyles = ArrayPool<int>.Shared.Rent(length);
        try
        {
            for (var index = 0; index < length; index++)
            {
                var current = _editor.GetStyleAt(index);
                desiredStyles[index] = preserveSemanticStyles && IsSemanticStyle(current)
                    ? current
                    : (int)Style.Default;
            }
            foreach (var span in spans)
            {
                if (span.Start < 0 || span.Start >= length || span.Length <= 0) continue;
                var end = Math.Min(length, span.Start + span.Length);
                var style = StyleIndex(span.Kind);
                for (var index = span.Start; index < end; index++) desiredStyles[index] = style;
            }

            var position = 0;
            while (position < length)
            {
                var desired = desiredStyles[position];
                if (_editor.GetStyleAt(position) == desired)
                {
                    position++;
                    continue;
                }
                var start = position++;
                while (position < length
                       && desiredStyles[position] == desired
                       && _editor.GetStyleAt(position) != desired)
                    position++;
                _editor.StartStyling(start);
                _editor.SetStyling(position - start, desired);
            }
        }
        finally
        {
            ArrayPool<int>.Shared.Return(desiredStyles);
        }
    }

    private static bool IsSemanticStyle(int style) =>
        style is >= SemanticStyleBase + (int)RoslynScriptHighlightKind.Type
            and <= SemanticStyleBase + (int)RoslynScriptHighlightKind.Property;

    private void UpdateLineNumberMargin()
    {
        if (_editor is null) return;
        var digits = Math.Max(2, _editor.Lines.Count.ToString(System.Globalization.CultureInfo.InvariantCulture).Length);
        _editor.Margins[0].Width = _editor.TextWidth(Style.LineNumber, new string('9', digits + 1));
    }

    private void ApplyAutomaticIndent(char addedCharacter)
    {
        var currentPosition = _editor.CurrentPosition;
        var lineIndex = _editor.LineFromPosition(currentPosition);
        var line = _editor.Lines[lineIndex];
        if (!ScriptEditorInputBehavior.ShouldApplyAutomaticIndent(addedCharacter, line.Text.Trim())) return;

        var indentation = ScriptService.GetIndentation(Text, currentPosition, Math.Max(1, _editor.IndentWidth));
        line.Indentation = indentation;
        var caretOffset = addedCharacter is '{' or '}' ? 1 : 0;
        _editor.SetEmptySelection(Math.Min(line.EndPosition, line.Position + indentation + caretOffset));
    }

    private void ConfigureEditorContextMenu()
    {
        var menu = new ContextMenuStrip(components)
        {
            BackColor = Color.FromArgb(37, 37, 38),
            ForeColor = Color.White,
            ShowImageMargin = false
        };
        menu.Opening += (_, _) =>
        {
            menu.Items.Clear();
            var caret = SelectionStart;
            var diagnostic = _diagnostics.FirstOrDefault(item =>
                caret >= item.Start && caret <= item.Start + Math.Max(1, item.Length));
            if (diagnostic is not null)
            {
                foreach (var action in GetCodeActions(diagnostic))
                {
                    var selectedAction = action;
                    menu.Items.Add("快速修复: " + action.Title, null, (_, _) => ApplyCodeAction(selectedAction));
                }
                if (menu.Items.Count > 0) menu.Items.Add(new ToolStripSeparator());
            }
            menu.Items.Add("转到定义 (F12)", null, (_, _) => RequestDefinition());
            if (RenameRequested is not null)
                menu.Items.Add("重命名符号… (F2)", null, (_, _) => RequestRename());
            if (FindReferencesRequested is not null)
                menu.Items.Add("查找所有引用 (Shift+F12)", null, (_, _) => RequestFindReferences());
            if (CodeFixRequested is not null)
                menu.Items.Add("项目代码修复… (Ctrl+.)", null, (_, _) => RequestCodeFix());
            menu.Items.Add("格式化文档", null, (_, _) => FormatDocument());
            menu.Items.Add("切换行注释 (Ctrl+/)", null, (_, _) => ToggleLineComment());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("查找… (Ctrl+F)", null, (_, _) => ShowFindReplace());
            menu.Items.Add("替换… (Ctrl+H)", null, (_, _) => ShowFindReplace(showReplace: true));
        };
        _editor.ContextMenuStrip = menu;
    }

    private void RequestDefinition()
    {
        if (DefinitionRequested is not null)
            DefinitionRequested(this, new ScriptDefinitionRequestedEventArgs(SelectionStart));
        else
            _ = NavigateToDefinitionAsync();
    }

    private void RequestRename() =>
        RenameRequested?.Invoke(this, new ScriptSymbolRequestedEventArgs(SelectionStart));

    private void RequestFindReferences() =>
        FindReferencesRequested?.Invoke(this, new ScriptSymbolRequestedEventArgs(SelectionStart));

    private void RequestCodeFix() =>
        CodeFixRequested?.Invoke(this, new ScriptSymbolRequestedEventArgs(SelectionStart));

    private async Task NavigateToDefinitionAsync()
    {
        var source = Text;
        var caret = SelectionStart;
        var definition = await ScriptService.GetDefinitionAsync(source, caret, ScriptEnvironment);
        if (definition is null || IsDisposed || !string.Equals(source, Text, StringComparison.Ordinal)) return;
        if (definition.IsSource
            && string.Equals(definition.SourceName, "Program.cs", StringComparison.OrdinalIgnoreCase))
        {
            Select(definition.Start, Math.Max(1, definition.Length));
            _editor.ScrollCaret();
            _editor.Focus();
            return;
        }
        _callTipPurpose = CallTipPurpose.Hover;
        var location = definition.IsSource ? definition.SourceName : "元数据: " + definition.SourceName;
        _editor.CallTipShow(caret, definition.DisplayText + "\n" + location);
    }

    internal static Bitmap CreateCompletionImage(RoslynScriptCompletionKind kind)
    {
        var (color, glyph, round) = kind switch
        {
            RoslynScriptCompletionKind.Method => (Color.FromArgb(138, 105, 190), "M", true),
            RoslynScriptCompletionKind.Property => (Color.FromArgb(49, 132, 191), "P", false),
            RoslynScriptCompletionKind.Field => (Color.FromArgb(49, 132, 191), "F", false),
            RoslynScriptCompletionKind.Constant => (Color.FromArgb(75, 145, 190), "C", false),
            RoslynScriptCompletionKind.Class or RoslynScriptCompletionKind.Type => (Color.FromArgb(40, 155, 145), "C", false),
            RoslynScriptCompletionKind.Interface => (Color.FromArgb(45, 145, 165), "I", false),
            RoslynScriptCompletionKind.Struct => (Color.FromArgb(55, 135, 180), "S", false),
            RoslynScriptCompletionKind.Enum => (Color.FromArgb(196, 127, 55), "E", false),
            RoslynScriptCompletionKind.Delegate => (Color.FromArgb(155, 105, 175), "D", false),
            RoslynScriptCompletionKind.Event => (Color.FromArgb(196, 127, 55), "E", true),
            RoslynScriptCompletionKind.Namespace => (Color.FromArgb(175, 145, 55), "N", false),
            RoslynScriptCompletionKind.Variable => (Color.FromArgb(90, 145, 185), "V", true),
            RoslynScriptCompletionKind.Keyword => (Color.FromArgb(160, 100, 165), "K", false),
            RoslynScriptCompletionKind.Snippet => (Color.FromArgb(185, 105, 70), "S", false),
            RoslynScriptCompletionKind.HostContext => (Color.FromArgb(40, 145, 175), "H", true),
            _ => (Color.FromArgb(110, 125, 145), "·", true)
        };
        var bitmap = new Bitmap(16, 16, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.Transparent);
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        using var brush = new SolidBrush(color);
        using var border = new Pen(Blend(color, Color.White, 0.3), 1F);
        if (round)
        {
            graphics.FillEllipse(brush, 1.5F, 1.5F, 12F, 12F);
            graphics.DrawEllipse(border, 1.5F, 1.5F, 12F, 12F);
        }
        else
        {
            graphics.FillRectangle(brush, 1.5F, 1.5F, 12F, 12F);
            graphics.DrawRectangle(border, 1.5F, 1.5F, 12F, 12F);
        }
        using var font = new Font("Segoe UI", 7F, FontStyle.Bold, GraphicsUnit.Point);
        using var textBrush = new SolidBrush(Color.White);
        using var format = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
            FormatFlags = StringFormatFlags.NoWrap
        };
        graphics.DrawString(glyph, font, textBrush, new RectangleF(1F, 0F, 13F, 15F), format);
        return bitmap;
    }

    private void CommitCompletion(RoslynScriptCompletionItem item)
    {
        var edit = _inputBehavior.CreateCompletionEdit(item, Text, SelectionStart);
        if (item.Kind == RoslynScriptCompletionKind.Snippet)
        {
            var change = edit.Changes.Single();
            TryInsertSnippet(item.InsertionText, change.Start, change.Start + change.Length);
        }
        else
        {
            _insertingPair = true;
            _editor.BeginUndoAction();
            try
            {
                foreach (var change in edit.Changes.OrderByDescending(change => change.Start))
                {
                    _editor.SetSelection(change.Start + change.Length, change.Start);
                    _editor.ReplaceSelection(change.NewText);
                }
                _editor.SetEmptySelection(edit.NewCaretPosition);
            }
            finally
            {
                _editor.EndUndoAction();
                _insertingPair = false;
            }
        }
        ClearCompletionState();
    }

    private void ClearCompletionState()
    {
        _completionPopup.Dismiss();
        _completionToolTip.Hide(_editor);
    }

    private async Task ShowSignatureHelpAsync()
    {
        var source = Text;
        var caret = SelectionStart;
        _signatureCancellation?.Cancel();
        _signatureCancellation?.Dispose();
        var cancellation = _signatureCancellation = new CancellationTokenSource();
        try
        {
            var help = await ScriptService.GetSignatureHelpAsync(
                source,
                caret,
                ScriptEnvironment,
                cancellation.Token);
            if (help is null || IsDisposed || cancellation.IsCancellationRequested
                || caret != SelectionStart || !string.Equals(source, Text, StringComparison.Ordinal))
                return;
            var text = help.DisplayText;
            if (help.Overloads.Count > 1) text += $"\n（另有 {help.Overloads.Count - 1} 个重载）";
            _callTipPurpose = CallTipPurpose.Signature;
            _editor.CallTipShow(help.ApplicableSpanStart, text);
            if (help.Parameters.Count > 0)
            {
                var parameter = help.Parameters[Math.Min(help.ActiveParameter, help.Parameters.Count - 1)];
                var highlightStart = text.IndexOf(parameter.DisplayText, StringComparison.Ordinal);
                if (highlightStart >= 0)
                    _editor.CallTipSetHlt(highlightStart, highlightStart + parameter.DisplayText.Length);
            }
        }
        catch (OperationCanceledException)
        {
            // 后续按键会替代本次签名请求。
        }
    }

    private async Task ShowHoverAsync(int position)
    {
        if (position < 0 || _callTipPurpose == CallTipPurpose.Signature) return;
        var diagnostic = _diagnostics.FirstOrDefault(item =>
        {
            var start = item.Length == 0 ? Math.Max(0, item.Start - 1) : item.Start;
            return position >= start && position < start + Math.Max(1, item.Length);
        });
        if (diagnostic is not null)
        {
            var actions = GetCodeActions(diagnostic);
            var actionHint = actions.Count == 0 ? string.Empty : $"\n快速修复: {actions[0].Title}（右键）";
            _callTipPurpose = CallTipPurpose.Hover;
            _editor.CallTipShow(position, $"{diagnostic.Id}: {diagnostic.Message}\n第 {diagnostic.Line} 行，第 {diagnostic.Column} 列{actionHint}");
            return;
        }

        var source = Text;
        _quickInfoCancellation?.Cancel();
        _quickInfoCancellation?.Dispose();
        var cancellation = _quickInfoCancellation = new CancellationTokenSource();
        try
        {
            var info = await ScriptService.GetQuickInfoAsync(source, position, ScriptEnvironment, cancellation.Token);
            if (info is null || IsDisposed || cancellation.IsCancellationRequested
                || !string.Equals(source, Text, StringComparison.Ordinal))
                return;
            var text = string.IsNullOrWhiteSpace(info.Documentation)
                ? info.DisplayText
                : info.DisplayText + "\n" + info.Documentation;
            _callTipPurpose = CallTipPurpose.Hover;
            _editor.CallTipShow(info.Start, text.Replace("\r", string.Empty));
        }
        catch (OperationCanceledException)
        {
            // 鼠标移动后取消旧的快速信息。
        }
    }

    private void HideHoverCallTip()
    {
        _quickInfoCancellation?.Cancel();
        if (_callTipPurpose == CallTipPurpose.Hover) CancelCallTip();
    }

    private void CancelCallTip()
    {
        if (_editor.CallTipActive) _editor.CallTipCancel();
        _callTipPurpose = CallTipPurpose.None;
    }

    private void InsertAutomaticPair(char character)
    {
        if (character == '\n')
        {
            ExpandPairedBraceOnNewLine();
            return;
        }
        var caret = _editor.CurrentPosition;
        var close = ScriptEditorInputBehavior.GetAutomaticClosingCharacter(
            character,
            SelectionLength,
            caret < TextLength ? Text[caret] : null,
            character is not ('"' or '\'') || ShouldPairQuote(character, caret));
        if (close == '\0') return;
        _insertingPair = true;
        try
        {
            _editor.InsertText(caret, close.ToString());
            _editor.SetEmptySelection(caret);
        }
        finally
        {
            _insertingPair = false;
        }
    }

    private bool ShouldPairQuote(char quote, int caret)
    {
        var line = _editor.Lines[_editor.LineFromPosition(caret)];
        var before = Text.Substring(line.Position, Math.Max(0, caret - line.Position - 1));
        var unescaped = 0;
        for (var index = 0; index < before.Length; index++)
        {
            if (before[index] != quote) continue;
            var slashCount = 0;
            for (var previous = index - 1; previous >= 0 && before[previous] == '\\'; previous--) slashCount++;
            if (slashCount % 2 == 0) unescaped++;
        }
        var style = caret > 1 ? _editor.GetStyleAt(caret - 2) : (int)Style.Default;
        return unescaped % 2 == 0 && style != StyleIndex(RoslynScriptHighlightKind.Comment);
    }

    private void ExpandPairedBraceOnNewLine()
    {
        var caret = _editor.CurrentPosition;
        if (!ScriptEditorInputBehavior.TryGetPairedBraceOpening(Text, caret, out var openingPosition)) return;
        var openingLine = _editor.Lines[_editor.LineFromPosition(openingPosition)];
        _insertingPair = true;
        try
        {
            _editor.InsertText(caret, Environment.NewLine);
            var closingLineIndex = _editor.LineFromPosition(caret + Environment.NewLine.Length);
            _editor.Lines[closingLineIndex].Indentation = openingLine.Indentation;
            _editor.SetEmptySelection(caret);
        }
        finally
        {
            _insertingPair = false;
        }
    }

    private bool TryHandleSnippetTab(KeyEventArgs eventArgs)
    {
        var decision = _inputBehavior.ProcessTab(
            Text,
            SelectionStart,
            SelectionLength,
            eventArgs.Control,
            eventArgs.Alt,
            eventArgs.Shift);
        if (decision.Kind == ScriptSnippetDecisionKind.None) return false;
        eventArgs.SuppressKeyPress = true;
        eventArgs.Handled = true;
        _completionPopup.Dismiss();
        if (decision.Kind == ScriptSnippetDecisionKind.Expand)
        {
            _completionToolTip.Hide(_editor);
            return TryInsertSnippet(decision.Shortcut, decision.Start, decision.End);
        }

        var point = GetPositionFromCharIndex(SelectionStart);
        _completionToolTip.Show(
            $"再次按 Tab 插入 {decision.Shortcut} 代码片段",
            _editor,
            Math.Max(0, point.X + 16),
            Math.Max(0, point.Y + Math.Max(18, _editor.Font.Height) + 4),
            4000);
        return true;
    }

    private void ResetPendingSnippet() => _inputBehavior.Reset();

    private bool HandlePairedKey(KeyEventArgs eventArgs)
    {
        if (eventArgs.Control || eventArgs.Alt) return false;
        var caret = _editor.CurrentPosition;
        if (eventArgs.KeyCode == Keys.Back
            && ScriptEditorInputBehavior.ShouldDeletePair(Text, caret, SelectionLength))
        {
            _editor.SetSelection(caret + 1, caret - 1);
            _editor.ReplaceSelection(string.Empty);
            eventArgs.SuppressKeyPress = true;
            eventArgs.Handled = true;
            return true;
        }
        var typed = eventArgs.KeyCode switch
        {
            Keys.D0 when eventArgs.Shift => ')',
            Keys.OemCloseBrackets when eventArgs.Shift => '}',
            Keys.OemCloseBrackets => ']',
            Keys.OemQuotes when eventArgs.Shift => '"',
            Keys.OemQuotes => '\'',
            _ => '\0'
        };
        if (typed != '\0' && ScriptEditorInputBehavior.ShouldSkipClosingCharacter(Text, caret, SelectionLength, typed))
        {
            _editor.SetEmptySelection(caret + 1);
            eventArgs.SuppressKeyPress = true;
            eventArgs.Handled = true;
            return true;
        }
        return false;
    }

    private bool TryWrapSelection(char opening)
    {
        if (SelectionLength == 0) return false;
        var closing = opening switch
        {
            '(' => ')',
            '[' => ']',
            '{' => '}',
            '"' => '"',
            '\'' => '\'',
            _ => '\0'
        };
        if (closing == '\0') return false;
        var start = Math.Min(_editor.SelectionStart, _editor.SelectionEnd);
        var selected = SelectedText;
        _editor.ReplaceSelection(opening + selected + closing);
        Select(start + 1, selected.Length);
        return true;
    }

    private void UpdateBraceHighlighting()
    {
        if (_editor.TextLength == 0) return;
        var caret = _editor.CurrentPosition;
        var brace = FindAdjacentBrace(caret);
        if (brace >= 0)
        {
            var match = _editor.BraceMatch(brace);
            if (match >= 0)
            {
                _editor.BraceHighlight(brace, match);
                _editor.HighlightGuide = _editor.GetColumn(Math.Min(brace, match));
                return;
            }
            _editor.BraceBadLight(brace);
            _editor.HighlightGuide = 0;
            return;
        }

        var enclosing = FindEnclosingOpenBrace(caret);
        if (enclosing >= 0)
        {
            var match = _editor.BraceMatch(enclosing);
            _editor.BraceHighlight(enclosing, match);
            _editor.HighlightGuide = _editor.GetColumn(enclosing);
        }
        else
        {
            _editor.BraceHighlight(Scintilla.InvalidPosition, Scintilla.InvalidPosition);
            _editor.HighlightGuide = 0;
        }
    }

    private int FindAdjacentBrace(int caret)
    {
        if (caret > 0 && IsBrace(Text[caret - 1])) return caret - 1;
        return caret < TextLength && IsBrace(Text[caret]) ? caret : -1;
    }

    private int FindEnclosingOpenBrace(int caret)
    {
        var depth = 0;
        for (var position = Math.Min(caret - 1, TextLength - 1); position >= 0; position--)
        {
            var character = Text[position];
            if (character is not ('{' or '}')) continue;
            var style = _editor.GetStyleAt(position);
            if (style == StyleIndex(RoslynScriptHighlightKind.String)
                || style == StyleIndex(RoslynScriptHighlightKind.Comment))
                continue;
            if (character == '}')
            {
                depth++;
            }
            else if (depth > 0)
            {
                depth--;
            }
            else
            {
                var match = _editor.BraceMatch(position);
                if (match >= caret) return position;
            }
        }
        return -1;
    }

    private static bool IsBrace(char character) => character is '(' or ')' or '[' or ']' or '{' or '}';
    internal static bool ShouldIncludeSnippetCompletions(string source, int caret)
    {
        var position = Clamp(caret, 0, source.Length);
        var start = RoslynScriptService.GetCompletionStart(source, position);
        if (start <= 0) return true;
        var previous = source[start - 1];
        return previous is not ('.' or ':');
    }

    private IReadOnlyList<RoslynScriptCompletionItem> GetSnippetCompletions()
    {
        var prefixStart = RoslynScriptService.GetCompletionStart(Text, SelectionStart);
        var prefix = Text.Substring(prefixStart, SelectionStart - prefixStart);
        return _inputBehavior.GetSnippetCompletions(prefix).ToArray();
    }

    private bool TryInsertSnippet(string shortcut) => TryInsertSnippet(
        shortcut,
        RoslynScriptService.GetCompletionStart(Text, SelectionStart),
        SelectionStart);

    private bool TryInsertSnippet(string shortcut, int replaceStart, int replaceEnd)
    {
        if (!_inputBehavior.TryCreateSnippetEdit(
                shortcut,
                Text,
                replaceStart,
                replaceEnd,
                SelectionLength > 0 ? SelectedText : string.Empty,
                out var edit))
            return false;
        _insertingPair = true;
        _editor.BeginUndoAction();
        try
        {
            _editor.SetSelection(edit.Start + edit.Length, edit.Start);
            _editor.ReplaceSelection(edit.NewText);
            _editor.SetEmptySelection(edit.NewCaretPosition);
        }
        finally
        {
            _editor.EndUndoAction();
            _insertingPair = false;
        }
        return true;
    }

    private static int FindText(
        string source,
        string value,
        int start,
        StringComparison comparison,
        bool wholeWord)
    {
        var position = Math.Max(0, start);
        while (position <= source.Length - value.Length)
        {
            var found = source.IndexOf(value, position, comparison);
            if (found < 0) return -1;
            if (!wholeWord || IsWordBoundary(source, found - 1)
                           && IsWordBoundary(source, found + value.Length))
                return found;
            position = found + 1;
        }
        return -1;
    }

    private static bool IsWordBoundary(string source, int position) =>
        position < 0 || position >= source.Length || !SyntaxFacts.IsIdentifierPartCharacter(source[position]);

    private void ClearDiagnosticIndicators()
    {
        _editor.IndicatorCurrent = ErrorIndicator;
        _editor.IndicatorClearRange(0, _editor.TextLength);
        _editor.IndicatorCurrent = WarningIndicator;
        _editor.IndicatorClearRange(0, _editor.TextLength);
        _diagnosticOverview.SetDiagnostics(Array.Empty<RoslynScriptDiagnostic>(), _editor.Lines.Count);
    }

    private static int StyleIndex(RoslynScriptHighlightKind kind) => SemanticStyleBase + (int)kind;
    private string GetCurrentCompletionFilter()
    {
        var caret = SelectionStart;
        var start = RoslynScriptService.GetCompletionStart(Text, caret);
        return Text.Substring(start, Math.Max(0, caret - start));
    }

    private static string NormalizeCompletionDisplayText(string item)
    {
        var normalized = item.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');
        return string.Join(" ", normalized.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
    }
    private Color MutedColor(Color foreground) => Blend(BackColor, foreground, 0.6);

    private static Color Blend(Color background, Color foreground, double amount)
    {
        var ratio = Math.Max(0d, Math.Min(1d, amount));
        return Color.FromArgb(
            (int)Math.Round(background.R + (foreground.R - background.R) * ratio),
            (int)Math.Round(background.G + (foreground.G - background.G) * ratio),
            (int)Math.Round(background.B + (foreground.B - background.B) * ratio));
    }

    private static int Clamp(int value, int minimum, int maximum) =>
        value < minimum ? minimum : value > maximum ? maximum : value;

    private enum CallTipPurpose
    {
        None,
        Signature,
        Hover
    }
}
