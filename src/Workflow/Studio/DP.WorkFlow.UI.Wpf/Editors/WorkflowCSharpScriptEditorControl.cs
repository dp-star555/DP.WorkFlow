using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ScriptEngine;
using ScriptEngine.Wpf;
using DP.WorkFlow.UI;

namespace DP.WorkFlow.UI.Wpf;

/// <summary>WPF 脚本编辑适配器；编译状态由共享会话管理，窗口只负责承载本控件。</summary>
public sealed class WorkflowCSharpScriptEditorControl : UserControl
{
    private readonly Grid _layout = new();
    private readonly RoslynScriptEditorControl _editor = new();
    private readonly ListBox _diagnostics = new() { Name = "ScriptDiagnostics" };
    private readonly TextBlock _status = new();
    private readonly Button _compile = Button("编译");
    private readonly Button _manageReferences = Button("DLL 引用");
    private WorkflowScriptEditorPageModel? _page;
    private WorkflowCSharpScriptEditorSession? _session;
    private IReadOnlyList<RoslynScriptDiagnostic>? _renderedDiagnostics;
    private bool _renderedDiagnosticsAreStale;
    private bool _binding;

    public WorkflowCSharpScriptEditorControl()
    {
        var manageUsings = Button("using 管理");
        var showCompletions = Button("代码补全");
        showCompletions.ToolTip = "Ctrl+Space";
        var showContext = Button("脚本上下文");
        _manageReferences.Visibility = Visibility.Collapsed;
        var commands = new ToolBar { Background = Brush(37, 37, 38), Padding = new Thickness(2, 0, 2, 0) };
        commands.Items.Add(_compile);
        commands.Items.Add(new Separator());
        commands.Items.Add(manageUsings);
        commands.Items.Add(_manageReferences);
        commands.Items.Add(showCompletions);
        commands.Items.Add(showContext);
        var commandTray = new ToolBarTray
        {
            IsLocked = true,
            Background = Brush(37, 37, 38)
        };
        commandTray.ToolBars.Add(commands);
        var commandSurface = new Border
        {
            Background = Brush(37, 37, 38),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(2),
            Margin = new Thickness(2, 1, 2, 3),
            Child = commandTray
        };
        var statusSurface = new Border
        {
            Background = Brush(37, 37, 38),
            BorderBrush = Brush(63, 63, 70),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(8, 3, 8, 3),
            Child = _status
        };
        _status.Foreground = Brush(200, 200, 200);
        _status.VerticalAlignment = VerticalAlignment.Center;
        _diagnostics.Background = Brush(37, 37, 38);
        _diagnostics.Foreground = Brush(226, 232, 240);
        _diagnostics.BorderThickness = new Thickness(0);
        _diagnostics.Visibility = Visibility.Collapsed;
        var feedbackLayout = new Grid();
        feedbackLayout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        feedbackLayout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(32) });
        feedbackLayout.Children.Add(_diagnostics);
        Grid.SetRow(statusSurface, 1);
        feedbackLayout.Children.Add(statusSurface);
        var feedbackSurface = new Border
        {
            Name = "ScriptFeedback",
            Background = Brush(37, 37, 38),
            BorderBrush = Brush(63, 63, 70),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(5),
            Margin = new Thickness(2),
            Child = feedbackLayout
        };

        _layout.Name = "ScriptWorkspace";
        _layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(42) });
        _layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(0) });
        _layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(36) });
        _layout.Children.Add(commandSurface);
        Grid.SetRow(_editor, 1);
        _layout.Children.Add(_editor);
        Grid.SetRow(feedbackSurface, 2);
        Grid.SetRowSpan(feedbackSurface, 2);
        _layout.Children.Add(feedbackSurface);
        Content = _layout;

        _compile.Click += async (_, _) => await CompileAsync();
        manageUsings.Click += (_, _) => ManageUsings();
        _manageReferences.Click += (_, _) => ManageReferences();
        showCompletions.Click += (_, _) => _ = ShowCompletionsAsync();
        showContext.Click += (_, _) => ShowContext();
        _editor.TextChanged += (_, _) =>
        {
            if (_binding || _session is null) return;
            _session.SetSource(_editor.ScriptText);
            _editor.ShowDiagnostics(Array.Empty<RoslynScriptDiagnostic>());
        };
        _editor.CompletionRequested += (_, _) => _ = ShowCompletionsAsync();
        Loaded += (_, _) => { if (_page is not null) FocusEditor(); };
        UpdateView();
    }

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
                _editor.ScriptText = value?.Script ?? string.Empty;
                _editor.ScriptEnvironment = WorkflowCSharpScriptEditorModel.CreateEnvironment(value?.ReferencePaths);
            }
            finally { _binding = false; }
            _editor.ShowDiagnostics(Array.Empty<RoslynScriptDiagnostic>());
            _editor.ScheduleHighlight();
            _manageReferences.Visibility = value?.SupportsReferenceManagement == true
                ? Visibility.Visible
                : Visibility.Collapsed;
            _renderedDiagnostics = null;
            UpdateView();
            if (IsLoaded) _ = Dispatcher.BeginInvoke(new Action(() => FocusEditor()));
        }
    }

    public WorkflowScriptCompilationState CompilationState =>
        _session?.State ?? WorkflowScriptCompilationState.NotCompiled;

    public IReadOnlyList<RoslynScriptDiagnostic> Diagnostics =>
        _session?.Diagnostics ?? Array.Empty<RoslynScriptDiagnostic>();

    /// <summary>将键盘输入焦点放入共享 Scintilla 代码区。</summary>
    public bool FocusEditor() => _editor.Focus();

    public async Task<RoslynScriptCompilationResult?> CompileAsync(CancellationToken cancellationToken = default)
    {
        var session = _session;
        if (session is null) return null;
        _compile.IsEnabled = false;
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
            _compile.IsEnabled = true;
            UpdateView();
        }
    }

    private void OnSessionChanged(object? sender, EventArgs e)
    {
        if (!Dispatcher.CheckAccess()) _ = Dispatcher.BeginInvoke(UpdateView);
        else UpdateView();
    }

    private void UpdateView()
    {
        var session = _session;
        _status.Text = session?.GetStatusText() ?? "尚未绑定脚本";
        _compile.IsEnabled = session is not null && session.State != WorkflowScriptCompilationState.Compiling;
        var currentDiagnostics = session?.Diagnostics ?? Array.Empty<RoslynScriptDiagnostic>();
        var diagnosticsAreStale = session?.DiagnosticsAreStale == true;
        if (!ReferenceEquals(_renderedDiagnostics, currentDiagnostics)
            || _renderedDiagnosticsAreStale != diagnosticsAreStale)
        {
            _diagnostics.Items.Clear();
            if (currentDiagnostics.Count > 0)
            {
                if (diagnosticsAreStale)
                    _diagnostics.Items.Add("⚠ 代码已修改，以下为上次编译诊断（位置可能已失效）");
                foreach (var item in currentDiagnostics)
                    _diagnostics.Items.Add(WorkflowCSharpScriptEditorModel.FormatDiagnostic(item));
            }
            _renderedDiagnostics = currentDiagnostics;
            _renderedDiagnosticsAreStale = diagnosticsAreStale;
            var visible = _diagnostics.Items.Count > 0;
            _diagnostics.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            var diagnosticHeight = Math.Min(112, Math.Max(36, _diagnostics.Items.Count * 26 + 8));
            _layout.RowDefinitions[2].Height = new GridLength(visible ? diagnosticHeight : 0);
        }
    }

    private async Task ShowCompletionsAsync()
    {
        var source = _editor.ScriptText;
        var candidates = (await _editor.GetCompletionsAsync()).Select(item => item.InsertionText).ToArray();
        if (source == _editor.ScriptText)
        {
            _editor.Focus();
            _editor.ShowCompletionList(candidates);
        }
    }

    private void ManageUsings()
    {
        var owner = Window.GetWindow(this) ?? throw new InvalidOperationException("找不到 using 管理窗口的所有者。");
        var dialog = new CSharpUsingManagerWindow(owner, WorkflowCSharpScriptEditorModel.GetUsings(_editor.ScriptText));
        if (dialog.ShowDialog() == true)
            _editor.ScriptText = WorkflowCSharpScriptEditorModel.SetUsings(_editor.ScriptText, dialog.Namespaces);
    }

    private void ManageReferences()
    {
        var session = _session;
        var owner = Window.GetWindow(this);
        if (session is null || owner is null) return;
        var dialog = new RoslynScriptReferenceManagerWindow(owner, session.ReferencePaths);
        if (dialog.ShowDialog() != true) return;
        session.SetReferencePaths(dialog.ReferencePaths);
        _editor.ScriptEnvironment = WorkflowCSharpScriptEditorModel.CreateEnvironment(session.ReferencePaths);
        _editor.ShowDiagnostics(Array.Empty<RoslynScriptDiagnostic>());
        _editor.ScheduleHighlight();
    }

    private void ShowContext() => MessageBox.Show(
        Window.GetWindow(this),
        string.Join(Environment.NewLine, WorkflowCSharpScriptEditorModel.ContextApi),
        "脚本上下文",
        MessageBoxButton.OK,
        MessageBoxImage.Information);

    private static Button Button(string text) => new()
    {
        Content = text,
        MinWidth = 0,
        MinHeight = 30,
        Margin = new Thickness(1, 1, 1, 1),
        Padding = new Thickness(8, 3, 8, 3)
    };

    private static SolidColorBrush Brush(byte red, byte green, byte blue)
    {
        var brush = new SolidColorBrush(Color.FromRgb(red, green, blue));
        brush.Freeze();
        return brush;
    }
}
