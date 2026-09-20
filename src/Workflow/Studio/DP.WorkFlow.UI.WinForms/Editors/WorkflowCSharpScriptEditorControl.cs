using System.ComponentModel;
using ModernUI.WinForms;
using ScriptEngine;
using ScriptEngine.WinForms;
using DP.WorkFlow.UI;

namespace DP.WorkFlow.UI.WinForms;

/// <summary>
/// 工作流专用 C# 脚本编辑控件。统一封装现代命令栏、Roslyn 编辑器、编译状态和持久诊断。
/// 节点窗口只需绑定 <see cref="Page"/>，不再自行拼装脚本页面。
/// </summary>
[Description("工作流 C# 脚本编辑器")]
[DisplayName("工作流 C# 脚本编辑器")]
[ToolboxItem(true)]
public sealed class WorkflowCSharpScriptEditorControl : UserControl
{
    private readonly TableLayoutPanel _layout = new();
    private readonly ModernCommandBar _commands = new();
    private readonly RoslynScriptEditorControl _editor = new();
    private readonly ModernListBox _diagnostics = new() { Name = "ScriptDiagnostics" };
    private readonly ModernStatusBar _status = new();
    private readonly ModernPanel _feedbackSurface = new() { Name = "ScriptFeedback" };
    private readonly TableLayoutPanel _feedbackLayout = new();
    private readonly ToolStripStatusLabel _statusText = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly ModernCommand _compileCommand;
    private readonly ModernCommand _referenceCommand;
    private WorkflowScriptEditorPageModel? _page;
    private WorkflowCSharpScriptEditorSession? _session;
    private IReadOnlyList<RoslynScriptDiagnostic>? _renderedDiagnostics;
    private bool _renderedDiagnosticsAreStale;
    private WorkflowScriptCompilationState _renderedCompilationState = WorkflowScriptCompilationState.NotCompiled;
    private bool _binding;
    private bool _focusPending;

    public WorkflowCSharpScriptEditorControl()
    {
        Name = nameof(WorkflowCSharpScriptEditorControl);
        BackColor = ModernTheme.Dark.Background;
        ForeColor = ModernTheme.Dark.Text;
        Padding = new Padding(2);

        _compileCommand = Command("编译", ModernIconKind.Play, () => _ = CompileAsync());
        _referenceCommand = Command("DLL 引用", ModernIconKind.Key, ManageReferences);
        _referenceCommand.Visible = false;
        _commands.Name = "ScriptCommandBar";
        _commands.Theme = ModernTheme.Dark;
        _commands.DisplayMode = ModernCommandBarDisplayMode.Adaptive;
        _commands.Dock = DockStyle.Fill;
        _commands.Commands.Add(_compileCommand);
        _commands.Commands.Add(new ModernCommand { Kind = ModernCommandKind.Separator });
        _commands.Commands.Add(Command("using 管理", ModernIconKind.Settings, ManageUsings));
        _commands.Commands.Add(_referenceCommand);
        _commands.Commands.Add(Command("代码补全", ModernIconKind.Search, () => _ = ShowCompletionsAsync(), Keys.Control | Keys.Space));
        _commands.Commands.Add(Command("脚本上下文", ModernIconKind.Info, ShowContext));

        _editor.Name = "ScriptCodeEditor";
        _editor.Dock = DockStyle.Fill;
        _editor.BackColor = Color.FromArgb(30, 30, 30);
        _editor.ForeColor = ModernTheme.Dark.Text;
        _editor.TextChanged += OnEditorTextChanged;
        _editor.CompletionRequested += (_, _) => _ = ShowCompletionsAsync();

        _diagnostics.Theme = ModernTheme.Dark;
        _diagnostics.Dock = DockStyle.Fill;
        _diagnostics.RowHeight = 26;
        _diagnostics.Visible = false;

        _status.Name = "ScriptStatusBar";
        _status.Theme = ModernTheme.Dark;
        _status.AutoSize = false;
        _status.Dock = DockStyle.Fill;
        _status.Items.Add(_statusText);

        _feedbackSurface.Theme = ModernTheme.Dark;
        _feedbackSurface.Radius = 5;
        _feedbackSurface.Padding = new Padding(1);
        _feedbackSurface.Margin = Padding.Empty;
        _feedbackSurface.Dock = DockStyle.Fill;
        _feedbackLayout.Dock = DockStyle.Fill;
        _feedbackLayout.Margin = Padding.Empty;
        _feedbackLayout.Padding = Padding.Empty;
        _feedbackLayout.ColumnCount = 1;
        _feedbackLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _feedbackLayout.RowCount = 2;
        _feedbackLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _feedbackLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        _feedbackLayout.Controls.Add(_diagnostics, 0, 0);
        _feedbackLayout.Controls.Add(_status, 0, 1);
        _feedbackSurface.Controls.Add(_feedbackLayout);

        _layout.Name = "ScriptWorkspace";
        _layout.Dock = DockStyle.Fill;
        _layout.Margin = Padding.Empty;
        _layout.Padding = Padding.Empty;
        _layout.ColumnCount = 1;
        _layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _layout.RowCount = 4;
        _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        _layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));
        _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        _layout.Controls.Add(_commands, 0, 0);
        _layout.Controls.Add(_editor, 0, 1);
        _layout.Controls.Add(_feedbackSurface, 0, 2);
        _layout.SetRowSpan(_feedbackSurface, 2);
        Controls.Add(_layout);
        UpdateView();
    }

    /// <summary>绑定脚本页面模型；重新绑定时会解除旧状态会话。</summary>
    public WorkflowScriptEditorPageModel? Page
    {
        get => _page;
        set
        {
            if (ReferenceEquals(_page, value)) return;
            if (_session is not null) _session.Changed -= OnSessionChanged;
            _page = value;
            _session = value is null ? null : new WorkflowCSharpScriptEditorSession(value);
            if (_session is not null) _session.Changed += OnSessionChanged;
            _binding = true;
            try
            {
                _editor.Text = value?.Script ?? string.Empty;
                _editor.ScriptEnvironment = WorkflowCSharpScriptEditorModel.CreateEnvironment(value?.ReferencePaths);
            }
            finally
            {
                _binding = false;
            }
            _editor.ShowDiagnostics(Array.Empty<RoslynScriptDiagnostic>());
            _editor.ScheduleHighlight();
            SetReferenceCommandVisible(value?.SupportsReferenceManagement == true);
            _focusPending = value is not null;
            _renderedDiagnostics = null;
            UpdateView();
            QueueInitialFocus();
        }
    }

    /// <summary>已经完成UI反馈刷新的编译状态；后台完成但诊断仍在派发队列时继续显示Compiling。</summary>
    public WorkflowScriptCompilationState CompilationState => _renderedCompilationState;

    public IReadOnlyList<RoslynScriptDiagnostic> Diagnostics =>
        _session?.Diagnostics ?? Array.Empty<RoslynScriptDiagnostic>();

    /// <summary>将输入焦点放入代码区，供节点窗口和独立脚本弹窗统一调用。</summary>
    public bool FocusEditor()
    {
        var focused = _editor.Focus();
        if (focused) _focusPending = false;
        return focused;
    }

    /// <summary>编译当前编辑快照并刷新错误标记；编辑期间返回的旧结果只作为过期诊断展示。</summary>
    public async Task<RoslynScriptCompilationResult?> CompileAsync(CancellationToken cancellationToken = default)
    {
        var session = _session;
        if (session is null) return null;
        _compileCommand.Enabled = false;
        _compileCommand.RaiseCanExecuteChanged();
        try
        {
            var result = await session.CompileAsync(_editor.ScriptService, cancellationToken);
            _editor.ShowDiagnostics(session.DiagnosticsAreStale
                ? Array.Empty<RoslynScriptDiagnostic>()
                : result.Diagnostics);
            return result;
        }
        finally
        {
            _compileCommand.Enabled = true;
            _compileCommand.RaiseCanExecuteChanged();
            UpdateView();
        }
    }

    private void OnEditorTextChanged(object? sender, EventArgs e)
    {
        if (_binding || _session is null) return;
        _session.SetSource(_editor.Text);
        // 上次诊断的位置已经不再可靠，列表保留，但编辑器中的波浪线必须清除。
        _editor.ShowDiagnostics(Array.Empty<RoslynScriptDiagnostic>());
    }

    private void OnSessionChanged(object? sender, EventArgs e)
    {
        if (IsDisposed) return;
        if (InvokeRequired) BeginInvoke(UpdateView);
        else UpdateView();
    }

    private void UpdateView()
    {
        if (IsDisposed) return;
        var session = _session;
        // 先快照状态，最后发布，避免后台状态先完成而列表/行高尚未应用的观察窗口。
        var viewState = session?.State ?? WorkflowScriptCompilationState.NotCompiled;
        _statusText.Text = session?.GetStatusText() ?? "尚未绑定脚本";
        _compileCommand.Enabled = session is not null && session.State != WorkflowScriptCompilationState.Compiling;
        _compileCommand.RaiseCanExecuteChanged();

        var currentDiagnostics = session?.Diagnostics ?? Array.Empty<RoslynScriptDiagnostic>();
        var diagnosticsAreStale = session?.DiagnosticsAreStale == true;
        if (!ReferenceEquals(_renderedDiagnostics, currentDiagnostics)
            || _renderedDiagnosticsAreStale != diagnosticsAreStale)
        {
            _diagnostics.BeginUpdate();
            _diagnostics.Items.Clear();
            if (currentDiagnostics.Count > 0)
            {
                if (diagnosticsAreStale)
                    _diagnostics.Items.Add("⚠ 代码已修改，以下为上次编译诊断（位置可能已失效）");
                _diagnostics.Items.AddRange(currentDiagnostics
                    .Select(WorkflowCSharpScriptEditorModel.FormatDiagnostic)
                    .Cast<object>()
                    .ToArray());
            }
            _diagnostics.EndUpdate();
            _renderedDiagnostics = currentDiagnostics;
            _renderedDiagnosticsAreStale = diagnosticsAreStale;
            var showDiagnostics = _diagnostics.Items.Count > 0;
            _diagnostics.Visible = showDiagnostics;
            _layout.RowStyles[2].Height = showDiagnostics
                ? Math.Min(112, Math.Max(36, _diagnostics.Items.Count * 26 + 8))
                : 0;
            _layout.PerformLayout();
        }
        _renderedCompilationState = viewState;
    }

    private async Task ShowCompletionsAsync()
    {
        var source = _editor.Text;
        var candidates = (await _editor.GetCompletionsAsync())
            .Select(item => item.InsertionText)
            .ToArray();
        if (!IsDisposed && string.Equals(source, _editor.Text, StringComparison.Ordinal))
        {
            _editor.Focus();
            _editor.ShowCompletionList(candidates);
        }
    }

    private void ManageUsings()
    {
        if (_session is null) return;
        using var dialog = new CSharpUsingManagerDialog(WorkflowCSharpScriptEditorModel.GetUsings(_editor.Text));
        if (dialog.ShowDialog(FindForm()) == DialogResult.OK)
            _editor.Text = WorkflowCSharpScriptEditorModel.SetUsings(_editor.Text, dialog.Namespaces);
    }

    private void ManageReferences()
    {
        var session = _session;
        if (session is null) return;
        using var dialog = new RoslynScriptReferenceManagerDialog(session.ReferencePaths);
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;
        session.SetReferencePaths(dialog.ReferencePaths);
        _editor.ScriptEnvironment = WorkflowCSharpScriptEditorModel.CreateEnvironment(session.ReferencePaths);
        _editor.ShowDiagnostics(Array.Empty<RoslynScriptDiagnostic>());
        _editor.ScheduleHighlight();
    }

    private void ShowContext() => MessageBox.Show(
        FindForm(),
        string.Join(Environment.NewLine, WorkflowCSharpScriptEditorModel.ContextApi),
        "脚本上下文",
        MessageBoxButtons.OK,
        MessageBoxIcon.Information);

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        QueueInitialFocus();
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        QueueInitialFocus();
    }

    private void QueueInitialFocus()
    {
        if (!_focusPending || !Visible || !IsHandleCreated || IsDisposed) return;
        BeginInvoke(new Action(() =>
        {
            if (_focusPending && Visible && !IsDisposed) FocusEditor();
        }));
    }

    private void SetReferenceCommandVisible(bool visible)
    {
        if (_referenceCommand.Visible == visible) return;
        _referenceCommand.Visible = visible;
        _commands.Commands.ResetItem(_commands.Commands.IndexOf(_referenceCommand));
    }

    private static ModernCommand Command(
        string text,
        ModernIconKind icon,
        Action execute,
        Keys shortcutKeys = Keys.None) => new(execute)
    {
        Text = text,
        Icon = icon,
        ShortcutKeys = shortcutKeys
    };

    protected override void Dispose(bool disposing)
    {
        if (disposing && _session is not null) _session.Changed -= OnSessionChanged;
        base.Dispose(disposing);
    }
}
