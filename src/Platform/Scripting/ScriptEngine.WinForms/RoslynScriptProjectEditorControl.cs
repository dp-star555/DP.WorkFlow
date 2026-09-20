using System.Diagnostics;
using ScriptEngine.Workspaces;

namespace ScriptEngine.WinForms;

/// <summary>
/// 以文件标签组织多个 <see cref="RoslynScriptEditorControl"/>，统一提供项目级编译、诊断、
/// 跨文件补全和定义跳转。每个文件仍复用同一套 Scintilla 编辑体验。
/// </summary>
public sealed class RoslynScriptProjectEditorControl : UserControl
{
    private const int ProjectHistoryCapacity = 32;
    private const int NavigationHistoryCapacity = 64;
    private readonly TabControl _tabs = new() { Dock = DockStyle.Fill };
    private readonly ContextMenuStrip _tabMenu = new();
    private readonly SplitContainer _layout = new()
    {
        Dock = DockStyle.Fill,
        Orientation = Orientation.Horizontal,
        Panel2Collapsed = true,
        FixedPanel = FixedPanel.Panel2
    };
    private readonly ListView _referencesList = new()
    {
        Dock = DockStyle.Fill,
        View = View.Details,
        FullRowSelect = true,
        HideSelection = false,
        MultiSelect = false,
        VirtualMode = true,
        BorderStyle = BorderStyle.None
    };
    private readonly Stack<ProjectHistoryEntry> _projectUndo = new();
    private readonly Stack<ProjectHistoryEntry> _projectRedo = new();
    private readonly Stack<NavigationLocation> _navigationBackward = new();
    private readonly Stack<NavigationLocation> _navigationForward = new();
    private IReadOnlyList<RoslynScriptReferenceLocation> _visibleReferences = Array.Empty<RoslynScriptReferenceLocation>();
    private readonly System.Windows.Forms.Timer _completionTimer = new() { Interval = 300 };
    private readonly System.Windows.Forms.Timer _diagnosticTimer = new() { Interval = 600 };
    private CancellationTokenSource? _completionCancellation;
    private CancellationTokenSource? _diagnosticCancellation;
    private CancellationTokenSource? _workspaceCancellation;
    private IReadOnlyList<RoslynScriptDiagnostic> _projectDiagnostics = Array.Empty<RoslynScriptDiagnostic>();
    private RoslynScriptService _scriptService = new();
    private IRoslynScriptProjectEditingService? _projectEditingService;
    private IRoslynScriptCompletionProvider? _completionProvider;
    private IDisposable? _ownedOptionalWorkspaceService;
    private RoslynScriptEnvironment _scriptEnvironment = new();
    private bool _ownsScriptService = true;
    private bool _loading;
    private bool _resourcesDisposed;
    private int _autoCompletionMinimumPrefixLength = 2;
    private bool _autoCompletionEnabled = true;
    private bool _liveDiagnosticsEnabled = true;
    private bool _navigatingHistory;

    /// <summary>初始化包含一个空 Program.cs 的项目编辑器。</summary>
    public RoslynScriptProjectEditorControl()
    {
        ConfigureProjectLayout();
        Controls.Add(_layout);
        _tabs.SelectedIndexChanged += (_, _) =>
        {
            _completionTimer.Stop();
            _completionCancellation?.Cancel();
            ActiveFileChanged?.Invoke(this, EventArgs.Empty);
        };
        _completionTimer.Tick += (_, _) =>
        {
            _completionTimer.Stop();
            _ = ShowProjectCompletionsAsync();
        };
        _diagnosticTimer.Tick += (_, _) =>
        {
            _diagnosticTimer.Stop();
            _ = AnalyzeProjectDiagnosticsAsync();
        };
        BackColor = Color.FromArgb(30, 30, 30);
        ForeColor = Color.FromArgb(212, 212, 212);
        var optionalWorkspaceService = TryCreateOptionalWorkspaceService();
        _projectEditingService = optionalWorkspaceService as IRoslynScriptProjectEditingService;
        _completionProvider = optionalWorkspaceService as IRoslynScriptCompletionProvider;
        _ownedOptionalWorkspaceService = optionalWorkspaceService as IDisposable;
        LoadProject(new RoslynScriptProject
        {
            SourceFiles = new[] { new RoslynScriptSourceFile("Program.cs", string.Empty) }
        });
    }

    /// <summary>项目任一文件内容、名称或集合发生变化时触发。</summary>
    public event EventHandler? ProjectChanged;

    /// <summary>活动文件变化时触发。</summary>
    public event EventHandler? ActiveFileChanged;

    /// <summary>项目级实时诊断刷新完成时触发。</summary>
    public event EventHandler<ScriptDiagnosticsUpdatedEventArgs>? DiagnosticsUpdated;

    /// <summary>查找引用完成时触发；订阅后由宿主展示结果，否则使用内置结果窗口。</summary>
    public event EventHandler<ScriptReferencesFoundEventArgs>? ReferencesFound;

    /// <summary>项目中当前所有源码文件的不可变快照。</summary>
    public RoslynScriptProject Project
    {
        get => CreateProjectSnapshot();
        set
        {
            _projectUndo.Clear();
            _projectRedo.Clear();
            _navigationBackward.Clear();
            _navigationForward.Clear();
            LoadProject(value ?? throw new ArgumentNullException(nameof(value)));
        }
    }

    /// <summary>当前活动文件名称。</summary>
    public string ActiveFileName => ActivePage is null ? string.Empty : GetFileName(ActivePage);

    /// <summary>当前活动的单文件编辑器。</summary>
    public RoslynScriptEditorControl ActiveEditor => ActivePage?.Controls.OfType<RoslynScriptEditorControl>().Single()
        ?? throw new InvalidOperationException("项目编辑器没有活动文件。");

    /// <summary>项目内全部文件名称。</summary>
    public IReadOnlyList<string> FileNames => _tabs.TabPages.Cast<TabPage>().Select(GetFileName).ToArray();

    /// <summary>包含未标记为已保存内容的文件名。</summary>
    public IReadOnlyList<string> ModifiedFileNames => _tabs.TabPages.Cast<TabPage>()
        .Where(page => GetEditor(page).IsModified || GetTabState(page).MetadataModified)
        .Select(GetFileName)
        .ToArray();

    /// <summary>是否可以撤销最近一次项目级重命名或代码修复。</summary>
    public bool CanUndoProjectEdit => CanRestoreProjectHistory(_projectUndo);

    /// <summary>是否可以重做最近一次项目级重命名或代码修复。</summary>
    public bool CanRedoProjectEdit => CanRestoreProjectHistory(_projectRedo);

    /// <summary>查找引用停靠面板当前是否可见。</summary>
    public bool ReferencesPanelVisible => !_layout.Panel2Collapsed;

    /// <summary>项目共享的脚本服务。</summary>
    public RoslynScriptService ScriptService
    {
        get => _scriptService;
        set
        {
            if (value is null) throw new ArgumentNullException(nameof(value));
            if (ReferenceEquals(value, _scriptService)) return;
            if (_ownsScriptService) _scriptService.Dispose();
            _scriptService = value;
            _ownsScriptService = false;
            foreach (var editor in Editors) editor.ScriptService = value;
            ScheduleDiagnostics();
        }
    }

    /// <summary>可选的项目级语义编辑实现；引用 ScriptEngine.Workspaces 时会自动发现默认实现。</summary>
    public IRoslynScriptProjectEditingService? ProjectEditingService
    {
        get => _projectEditingService;
        set
        {
            var previous = _projectEditingService;
            _projectEditingService = value;
            if (_completionProvider is null || ReferenceEquals(_completionProvider, previous))
                _completionProvider = value as IRoslynScriptCompletionProvider;
        }
    }

    /// <summary>上下文感知项目补全提供器；默认复用自动发现的 Workspaces 模块。</summary>
    public IRoslynScriptCompletionProvider? CompletionProvider
    {
        get => _completionProvider;
        set
        {
            _completionCancellation?.Cancel();
            _completionProvider = value;
        }
    }

    /// <summary>项目全部文件共享的引用、导入、上下文和安全策略。</summary>
    public RoslynScriptEnvironment ScriptEnvironment
    {
        get => _scriptEnvironment;
        set
        {
            _scriptEnvironment = value ?? throw new ArgumentNullException(nameof(value));
            foreach (var editor in Editors) editor.ScriptEnvironment = value;
            ScheduleDiagnostics();
        }
    }

    /// <summary>是否在输入后显示跨文件补全。</summary>
    public bool AutoCompletionEnabled
    {
        get => _autoCompletionEnabled;
        set
        {
            _autoCompletionEnabled = value;
            if (value) return;
            _completionTimer.Stop();
            _completionCancellation?.Cancel();
        }
    }

    /// <summary>自动补全最少前缀字符数。</summary>
    public int AutoCompletionMinimumPrefixLength
    {
        get => _autoCompletionMinimumPrefixLength;
        set => _autoCompletionMinimumPrefixLength = value is >= 1 and <= 10
            ? value
            : throw new ArgumentOutOfRangeException(nameof(value));
    }

    /// <summary>跨文件自动补全防抖时间。</summary>
    public int AutoCompletionDelay
    {
        get => _completionTimer.Interval;
        set => _completionTimer.Interval = value is >= 50 and <= 2000
            ? value
            : throw new ArgumentOutOfRangeException(nameof(value));
    }

    /// <summary>是否启用项目级实时诊断。</summary>
    public bool LiveDiagnosticsEnabled
    {
        get => _liveDiagnosticsEnabled;
        set
        {
            _liveDiagnosticsEnabled = value;
            if (value) ScheduleDiagnostics();
            else
            {
                _diagnosticTimer.Stop();
                _diagnosticCancellation?.Cancel();
            }
        }
    }

    /// <summary>项目级实时诊断防抖时间。</summary>
    public int LiveDiagnosticsDelay
    {
        get => _diagnosticTimer.Interval;
        set => _diagnosticTimer.Interval = value is >= 200 and <= 5000
            ? value
            : throw new ArgumentOutOfRangeException(nameof(value));
    }

    /// <summary>添加源码文件并将其设为活动文件。</summary>
    public void AddFile(string fileName, string source = "")
    {
        var normalized = ValidateFileName(fileName);
        if (FindPage(normalized) is not null) throw new ArgumentException($"脚本文件已经存在：{normalized}", nameof(fileName));
        var page = CreatePage(normalized, source ?? string.Empty, markSaved: false);
        _tabs.TabPages.Add(page);
        _tabs.SelectedTab = page;
        OnProjectChanged();
    }

    /// <summary>删除源码文件；项目至少保留一个文件。</summary>
    public bool RemoveFile(string fileName)
    {
        if (_tabs.TabPages.Count <= 1) return false;
        var page = FindPage(fileName);
        if (page is null) return false;
        _tabs.TabPages.Remove(page);
        page.Dispose();
        OnProjectChanged();
        return true;
    }

    /// <summary>重命名源码文件。</summary>
    public bool RenameFile(string oldFileName, string newFileName)
    {
        var page = FindPage(oldFileName);
        if (page is null) return false;
        var normalized = ValidateFileName(newFileName);
        if (FindPage(normalized) is { } existing && !ReferenceEquals(existing, page))
            throw new ArgumentException($"脚本文件已经存在：{normalized}", nameof(newFileName));
        GetTabState(page).FileName = normalized;
        GetTabState(page).MetadataModified = true;
        UpdatePageTitle(page);
        OnProjectChanged();
        return true;
    }

    /// <summary>选择指定项目文件。</summary>
    public bool SelectFile(string fileName)
    {
        var page = FindPage(fileName);
        if (page is null) return false;
        _tabs.SelectedTab = page;
        return true;
    }

    /// <summary>将所有文件的当前内容标记为已保存并移除标签星号。</summary>
    public void MarkProjectSaved()
    {
        foreach (var page in _tabs.TabPages.Cast<TabPage>())
        {
            GetEditor(page).MarkSaved();
            GetTabState(page).MetadataModified = false;
            UpdatePageTitle(page);
        }
    }

    /// <summary>关闭文件；修改未保存时由确认回调决定是否放弃。项目始终至少保留一个文件。</summary>
    public bool TryCloseFile(string fileName, Func<string, bool>? confirmDiscardChanges = null)
    {
        if (_tabs.TabPages.Count <= 1) return false;
        var page = FindPage(fileName);
        if (page is null) return false;
        if (GetEditor(page).IsModified)
        {
            var discard = confirmDiscardChanges is not null
                ? confirmDiscardChanges(GetFileName(page))
                : MessageBox.Show(
                    FindForm(),
                    $"{GetFileName(page)} 包含尚未保存的修改。是否放弃修改并关闭？",
                    "关闭脚本文件",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning) == DialogResult.Yes;
            if (!discard) return false;
        }
        _tabs.TabPages.Remove(page);
        page.Dispose();
        OnProjectChanged();
        return true;
    }

    /// <summary>撤销最近一次原子项目编辑。</summary>
    public bool UndoProjectEdit() => RestoreProjectHistory(_projectUndo, _projectRedo);

    /// <summary>重做最近一次撤销的原子项目编辑。</summary>
    public bool RedoProjectEdit() => RestoreProjectHistory(_projectRedo, _projectUndo);

    /// <summary>导航到上一个定义或引用位置。</summary>
    public bool NavigateBackward() => RestoreNavigation(_navigationBackward, _navigationForward);

    /// <summary>导航到后一个定义或引用位置。</summary>
    public bool NavigateForward() => RestoreNavigation(_navigationForward, _navigationBackward);

    /// <summary>隐藏查找引用停靠面板。</summary>
    public void HideReferencesPanel() => _layout.Panel2Collapsed = true;

    /// <summary>使用可选语义编辑模块查找指定符号的项目引用。</summary>
    public Task<IReadOnlyList<RoslynScriptReferenceLocation>> FindReferencesAsync(
        string fileName,
        int position,
        CancellationToken cancellationToken = default) =>
        RequireProjectEditingService().FindReferencesAsync(
            CreateProjectSnapshot(),
            fileName,
            position,
            ScriptEnvironment,
            cancellationToken);

    /// <summary>使用可选语义编辑模块创建跨文件重命名编辑。</summary>
    public Task<RoslynScriptWorkspaceEdit?> CreateRenameEditAsync(
        string fileName,
        int position,
        string newName,
        CancellationToken cancellationToken = default) =>
        RequireProjectEditingService().RenameSymbolAsync(
            CreateProjectSnapshot(),
            fileName,
            position,
            newName,
            ScriptEnvironment,
            cancellationToken);

    /// <summary>获取指定诊断的项目级语义代码修复。</summary>
    public Task<IReadOnlyList<RoslynScriptWorkspaceEdit>> GetProjectCodeFixesAsync(
        string fileName,
        RoslynScriptDiagnostic diagnostic,
        CancellationToken cancellationToken = default) =>
        RequireProjectEditingService().GetCodeFixesAsync(
            CreateProjectSnapshot(),
            fileName,
            diagnostic,
            ScriptEnvironment,
            cancellationToken);

    /// <summary>将项目级编辑原子应用到对应标签；默认拒绝已检测到冲突的编辑。</summary>
    public void ApplyWorkspaceEdit(RoslynScriptWorkspaceEdit edit, bool allowConflicts = false)
    {
        if (edit is null) throw new ArgumentNullException(nameof(edit));
        if (edit.OriginalProjectRevision is not null
            && !string.Equals(edit.OriginalProjectRevision, CreateProjectSnapshot().ComputeSourceRevision(), StringComparison.Ordinal))
            throw new InvalidOperationException("项目内容已变化，不能应用基于旧源码生成的编辑。");
        if (edit.HasConflicts && !allowConflicts)
            throw new InvalidOperationException("项目编辑包含名称或编译冲突，必须由调用方明确允许后才能应用。");
        PushProjectHistory(_projectUndo, new ProjectHistoryEntry(
            CreateProjectSnapshot(),
            ActiveFileName,
            ActiveEditor.SelectionStart,
            edit.Title,
            edit.Project.ComputeSourceRevision()));
        _projectRedo.Clear();
        var activeName = ActiveFileName;
        var activePosition = ActiveEditor.SelectionStart;
        var output = edit.Project.SourceFiles.ToDictionary(item => item.FileName, StringComparer.OrdinalIgnoreCase);
        var currentNames = FileNames.OrderBy(item => item, StringComparer.OrdinalIgnoreCase).ToArray();
        var outputNames = output.Keys.OrderBy(item => item, StringComparer.OrdinalIgnoreCase).ToArray();
        if (!currentNames.SequenceEqual(outputNames, StringComparer.OrdinalIgnoreCase))
        {
            LoadProject(edit.Project, markSaved: false);
            SelectFile(activeName);
            ActiveEditor.SelectionStart = Math.Min(activePosition, ActiveEditor.TextLength);
            return;
        }

        _loading = true;
        try
        {
            foreach (var page in _tabs.TabPages.Cast<TabPage>())
                GetEditor(page).Text = output[GetFileName(page)].Source;
        }
        finally
        {
            _loading = false;
        }
        SelectFile(activeName);
        ActiveEditor.SelectionStart = Math.Min(activePosition, ActiveEditor.TextLength);
        OnProjectChanged();
    }

    /// <summary>编译当前多文件项目。</summary>
    public Task<RoslynScriptCompilationResult> CompileProjectAsync(CancellationToken cancellationToken = default) =>
        ScriptService.CompileProjectAsync(CreateProjectSnapshot(), ScriptEnvironment, cancellationToken);

    /// <summary>立即运行一次项目级诊断并把诊断路由到对应文件编辑器。</summary>
    public async Task AnalyzeProjectDiagnosticsAsync()
    {
        var project = CreateProjectSnapshot();
        var signature = ProjectSignature(project);
        _diagnosticCancellation?.Cancel();
        _diagnosticCancellation?.Dispose();
        var cancellation = _diagnosticCancellation = new CancellationTokenSource();
        try
        {
            var diagnostics = await ScriptService.GetProjectDiagnosticsAsync(
                project,
                ScriptEnvironment,
                cancellation.Token);
            if (IsDisposed || cancellation.IsCancellationRequested
                || !string.Equals(signature, ProjectSignature(CreateProjectSnapshot()), StringComparison.Ordinal))
                return;
            ShowDiagnostics(diagnostics);
            DiagnosticsUpdated?.Invoke(this, new ScriptDiagnosticsUpdatedEventArgs(diagnostics));
        }
        catch (OperationCanceledException)
        {
            // 新项目快照替代旧诊断。
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"多文件脚本诊断失败：{exception}");
        }
    }

    /// <summary>按 SourceName 将项目诊断显示到对应文件。</summary>
    public void ShowDiagnostics(IEnumerable<RoslynScriptDiagnostic> diagnostics)
    {
        _projectDiagnostics = diagnostics.ToArray();
        var grouped = _projectDiagnostics
            .Where(item => item.Origin == RoslynScriptDiagnosticOrigin.UserSource)
            .GroupBy(item => item.SourceName ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => (IEnumerable<RoslynScriptDiagnostic>)group.ToArray(), StringComparer.OrdinalIgnoreCase);
        foreach (var page in _tabs.TabPages.Cast<TabPage>())
        {
            var editor = GetEditor(page);
            editor.ShowDiagnostics(grouped.TryGetValue(GetFileName(page), out var fileDiagnostics)
                ? fileDiagnostics
                : Array.Empty<RoslynScriptDiagnostic>());
        }
    }

    /// <summary>安排项目级实时诊断。</summary>
    public void ScheduleDiagnostics()
    {
        _diagnosticTimer.Stop();
        if (LiveDiagnosticsEnabled && !_loading) _diagnosticTimer.Start();
    }

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        foreach (var editor in Editors) editor.Font = Font;
    }

    protected override void OnBackColorChanged(EventArgs e)
    {
        base.OnBackColorChanged(e);
        _tabs.BackColor = BackColor;
        _referencesList.BackColor = BackColor;
        _layout.Panel2.BackColor = BackColor;
        foreach (var editor in Editors) editor.BackColor = BackColor;
    }

    protected override void OnForeColorChanged(EventArgs e)
    {
        base.OnForeColorChanged(e);
        _tabs.ForeColor = ForeColor;
        _referencesList.ForeColor = ForeColor;
        _layout.Panel2.ForeColor = ForeColor;
        foreach (var editor in Editors) editor.ForeColor = ForeColor;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_resourcesDisposed)
        {
            _resourcesDisposed = true;
            _completionTimer.Dispose();
            _diagnosticTimer.Dispose();
            _completionCancellation?.Cancel();
            _completionCancellation?.Dispose();
            _diagnosticCancellation?.Cancel();
            _diagnosticCancellation?.Dispose();
            _workspaceCancellation?.Cancel();
            _workspaceCancellation?.Dispose();
            foreach (var page in _tabs.TabPages.Cast<TabPage>().ToArray()) page.Dispose();
            _ownedOptionalWorkspaceService?.Dispose();
            _ownedOptionalWorkspaceService = null;
            _tabMenu.Dispose();
            if (_ownsScriptService) _scriptService.Dispose();
        }
        base.Dispose(disposing);
    }

    private void ConfigureProjectLayout()
    {
        _layout.Panel1.Controls.Add(_tabs);
        var closeFile = _tabMenu.Items.Add("关闭文件");
        _tabMenu.Items.Add(new ToolStripSeparator());
        var undoProject = _tabMenu.Items.Add("撤销项目修改");
        var redoProject = _tabMenu.Items.Add("重做项目修改");
        _tabMenu.Items.Add(new ToolStripSeparator());
        var markSaved = _tabMenu.Items.Add("标记全部已保存");
        _tabMenu.Opening += (_, _) =>
        {
            closeFile.Enabled = _tabs.TabPages.Count > 1;
            undoProject.Enabled = CanUndoProjectEdit;
            redoProject.Enabled = CanRedoProjectEdit;
            markSaved.Enabled = ModifiedFileNames.Count > 0;
        };
        closeFile.Click += (_, _) =>
        {
            if (_tabs.SelectedTab is not null) TryCloseFile(GetFileName(_tabs.SelectedTab));
        };
        undoProject.Click += (_, _) => UndoProjectEdit();
        redoProject.Click += (_, _) => RedoProjectEdit();
        markSaved.Click += (_, _) => MarkProjectSaved();
        _tabs.ContextMenuStrip = _tabMenu;
        _tabs.MouseDown += (_, eventArgs) =>
        {
            if (eventArgs.Button != MouseButtons.Right) return;
            for (var index = 0; index < _tabs.TabPages.Count; index++)
            {
                if (!_tabs.GetTabRect(index).Contains(eventArgs.Location)) continue;
                _tabs.SelectedIndex = index;
                break;
            }
        };
        _referencesList.Columns.Add("类型", 72);
        _referencesList.Columns.Add("文件", 360);
        _referencesList.Columns.Add("行", 64);
        _referencesList.Columns.Add("列", 64);
        _referencesList.RetrieveVirtualItem += (_, eventArgs) =>
        {
            var location = _visibleReferences[eventArgs.ItemIndex];
            var row = new ListViewItem(location.IsDefinition ? "定义" : "引用") { Tag = location };
            row.SubItems.Add(location.FileName);
            row.SubItems.Add(location.Line.ToString(System.Globalization.CultureInfo.InvariantCulture));
            row.SubItems.Add(location.Column.ToString(System.Globalization.CultureInfo.InvariantCulture));
            eventArgs.Item = row;
        };
        _referencesList.DoubleClick += (_, _) =>
        {
            if (_referencesList.SelectedIndices.Count > 0)
                NavigateToReference(_visibleReferences[_referencesList.SelectedIndices[0]]);
        };
        var header = new Panel { Dock = DockStyle.Top, Height = 30 };
        var title = new Label
        {
            Text = "查找所有引用",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(8, 0, 0, 0)
        };
        var close = new Button
        {
            Text = "×",
            Dock = DockStyle.Right,
            Width = 34,
            FlatStyle = FlatStyle.Flat,
            TabStop = false
        };
        close.FlatAppearance.BorderSize = 0;
        close.Click += (_, _) => HideReferencesPanel();
        header.Controls.Add(title);
        header.Controls.Add(close);
        _layout.Panel2.Controls.Add(_referencesList);
        _layout.Panel2.Controls.Add(header);
    }

    private void ShowReferencesPanel(IReadOnlyList<RoslynScriptReferenceLocation> locations)
    {
        _visibleReferences = locations;
        _referencesList.VirtualListSize = locations.Count;
        _layout.Panel2Collapsed = false;
        var preferredHeight = Math.Min(Math.Max(150, Height / 3), Math.Max(150, Height - 120));
        if (Height > preferredHeight + 80) _layout.SplitterDistance = Height - preferredHeight;
        if (locations.Count > 0)
        {
            _referencesList.SelectedIndices.Clear();
            _referencesList.SelectedIndices.Add(0);
            _referencesList.EnsureVisible(0);
        }
    }

    private bool RestoreProjectHistory(Stack<ProjectHistoryEntry> source, Stack<ProjectHistoryEntry> destination)
    {
        if (!CanRestoreProjectHistory(source)) return false;
        var entry = source.Pop();
        PushProjectHistory(destination, new ProjectHistoryEntry(
            CreateProjectSnapshot(),
            ActiveFileName,
            ActiveEditor.SelectionStart,
            entry.Title,
            entry.Project.ComputeSourceRevision()));
        LoadProject(entry.Project, markSaved: false);
        SelectFile(entry.ActiveFileName);
        ActiveEditor.SelectionStart = Math.Min(entry.CaretPosition, ActiveEditor.TextLength);
        return true;
    }

    private bool CanRestoreProjectHistory(Stack<ProjectHistoryEntry> stack) =>
        stack.Count > 0
        && string.Equals(
            stack.Peek().ExpectedCurrentRevision,
            CreateProjectSnapshot().ComputeSourceRevision(),
            StringComparison.Ordinal);

    private static void PushProjectHistory(Stack<ProjectHistoryEntry> stack, ProjectHistoryEntry entry)
    {
        if (stack.Count >= ProjectHistoryCapacity)
        {
            var keep = stack.Take(ProjectHistoryCapacity - 1).Reverse().ToArray();
            stack.Clear();
            foreach (var item in keep) stack.Push(item);
        }
        stack.Push(entry);
    }

    private bool RestoreNavigation(Stack<NavigationLocation> source, Stack<NavigationLocation> destination)
    {
        if (source.Count == 0) return false;
        var current = CaptureNavigation();
        var target = source.Pop();
        if (current is not null) PushNavigation(destination, current);
        _navigatingHistory = true;
        try
        {
            if (!SelectFile(target.FileName)) return false;
            ActiveEditor.Select(Math.Min(target.Start, ActiveEditor.TextLength), Math.Min(target.Length, Math.Max(0, ActiveEditor.TextLength - target.Start)));
            ActiveEditor.Focus();
            return true;
        }
        finally
        {
            _navigatingHistory = false;
        }
    }

    private void RecordNavigationOrigin()
    {
        if (_navigatingHistory) return;
        var location = CaptureNavigation();
        if (location is null) return;
        PushNavigation(_navigationBackward, location);
        _navigationForward.Clear();
    }

    private NavigationLocation? CaptureNavigation() => _tabs.SelectedTab is null
        ? null
        : new NavigationLocation(ActiveFileName, ActiveEditor.SelectionStart, Math.Max(1, ActiveEditor.SelectionLength));

    private static void PushNavigation(Stack<NavigationLocation> stack, NavigationLocation location)
    {
        if (stack.Count > 0 && stack.Peek() == location) return;
        if (stack.Count >= NavigationHistoryCapacity)
        {
            var keep = stack.Take(NavigationHistoryCapacity - 1).Reverse().ToArray();
            stack.Clear();
            foreach (var item in keep) stack.Push(item);
        }
        stack.Push(location);
    }

    private void HandleProjectNavigationKey(KeyEventArgs eventArgs)
    {
        var handled = eventArgs switch
        {
            { Control: true, Alt: true, Shift: false, KeyCode: Keys.Z } => UndoProjectEdit(),
            { Control: true, Alt: true, Shift: false, KeyCode: Keys.Y } => RedoProjectEdit(),
            { Control: false, Alt: true, Shift: false, KeyCode: Keys.Left } => NavigateBackward(),
            { Control: false, Alt: true, Shift: false, KeyCode: Keys.Right } => NavigateForward(),
            _ => false
        };
        if (!handled) return;
        eventArgs.SuppressKeyPress = true;
        eventArgs.Handled = true;
    }

    private static RoslynScriptEditorControl GetEditor(TabPage page) =>
        page.Controls.OfType<RoslynScriptEditorControl>().Single();

    private static ScriptFileTabState GetTabState(TabPage page) =>
        page.Tag as ScriptFileTabState
        ?? throw new InvalidOperationException("脚本文件标签缺少内部状态。");

    private static string GetFileName(TabPage page) => GetTabState(page).FileName;

    private static void UpdatePageTitle(TabPage page)
    {
        var fileName = GetFileName(page);
        page.Text = GetEditor(page).IsModified || GetTabState(page).MetadataModified
            ? fileName + " *"
            : fileName;
    }

    private TabPage? ActivePage => _tabs.SelectedTab;
    private IEnumerable<RoslynScriptEditorControl> Editors => _tabs.TabPages.Cast<TabPage>().Select(GetEditor);

    private void LoadProject(RoslynScriptProject project, bool markSaved = true)
    {
        var files = project.SourceFiles;
        if (files is null || files.Count == 0) throw new ArgumentException("项目至少需要一个源码文件。", nameof(project));
        _loading = true;
        try
        {
            foreach (var page in _tabs.TabPages.Cast<TabPage>().ToArray())
            {
                _tabs.TabPages.Remove(page);
                page.Dispose();
            }
            foreach (var file in files)
            {
                var name = ValidateFileName(file.FileName);
                if (FindPage(name) is not null) throw new ArgumentException($"脚本文件名重复：{name}", nameof(project));
                _tabs.TabPages.Add(CreatePage(name, file.Source ?? string.Empty, markSaved));
            }
            _tabs.SelectedIndex = 0;
        }
        finally
        {
            _loading = false;
        }
        OnProjectChanged();
    }

    private TabPage CreatePage(string fileName, string source, bool markSaved = true)
    {
        var editor = new RoslynScriptEditorControl
        {
            Dock = DockStyle.Fill,
            BackColor = BackColor,
            ForeColor = ForeColor,
            Font = Font,
            ScriptService = ScriptService,
            ScriptEnvironment = ScriptEnvironment,
            AutoCompletionEnabled = false,
            LiveDiagnosticsEnabled = false,
            Text = source
        };
        editor.TextChanged += (_, _) => OnProjectChanged();
        editor.ModifiedChanged += (_, _) =>
        {
            if (editor.Parent is TabPage parent) UpdatePageTitle(parent);
        };
        editor.CharacterEntered += (_, character) => ScheduleCompletion(character);
        editor.CompletionRequested += (_, _) => _ = ShowProjectCompletionsAsync();
        editor.DefinitionRequested += (_, eventArgs) =>
            _ = NavigateProjectDefinitionAsync(editor.Parent is TabPage parent ? GetFileName(parent) : fileName, eventArgs.Position);
        editor.RenameRequested += (_, eventArgs) =>
            _ = PromptRenameSymbolAsync(editor.Parent is TabPage renamePage ? GetFileName(renamePage) : fileName, eventArgs.Position);
        editor.FindReferencesRequested += (_, eventArgs) =>
            _ = ShowProjectReferencesAsync(editor.Parent is TabPage referencesPage ? GetFileName(referencesPage) : fileName, eventArgs.Position);
        editor.CodeFixRequested += (_, eventArgs) =>
            _ = ShowProjectCodeFixesAsync(editor.Parent is TabPage fixPage ? GetFileName(fixPage) : fileName, eventArgs.Position);
        editor.KeyDown += (_, eventArgs) => HandleProjectNavigationKey(eventArgs);
        var page = new TabPage(fileName)
        {
            BackColor = BackColor,
            ForeColor = ForeColor,
            Padding = Padding.Empty,
            Tag = new ScriptFileTabState(fileName, metadataModified: !markSaved)
        };
        page.Controls.Add(editor);
        if (markSaved) editor.MarkSaved();
        UpdatePageTitle(page);
        return page;
    }

    private void OnProjectChanged()
    {
        if (_loading) return;
        _completionCancellation?.Cancel();
        _diagnosticCancellation?.Cancel();
        _workspaceCancellation?.Cancel();
        _projectDiagnostics = Array.Empty<RoslynScriptDiagnostic>();
        ScheduleDiagnostics();
        ProjectChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ScheduleCompletion(char character)
    {
        if (!AutoCompletionEnabled) return;
        var editor = ActiveEditor;
        var prefixLength = editor.SelectionStart - RoslynScriptService.GetCompletionStart(editor.Text, editor.SelectionStart);
        if (character == '.')
        {
            _completionTimer.Stop();
            _ = ShowProjectCompletionsAsync();
        }
        else if ((character == '_' || char.IsLetterOrDigit(character))
                 && prefixLength >= AutoCompletionMinimumPrefixLength)
        {
            _completionTimer.Stop();
            _completionTimer.Start();
        }
        else
        {
            _completionTimer.Stop();
            _completionCancellation?.Cancel();
        }
    }

    private async Task ShowProjectCompletionsAsync()
    {
        _completionTimer.Stop();
        if (_tabs.SelectedTab is null) return;
        var editor = ActiveEditor;
        var fileName = ActiveFileName;
        var project = CreateProjectSnapshot();
        var source = editor.Text;
        var caret = editor.SelectionStart;
        _completionCancellation?.Cancel();
        _completionCancellation?.Dispose();
        var cancellation = _completionCancellation = new CancellationTokenSource();
        try
        {
            var provider = CompletionProvider;
            var items = provider is null
                ? await ScriptService.GetProjectCompletionsAsync(
                    project,
                    fileName,
                    caret,
                    ScriptEnvironment,
                    cancellation.Token)
                : await provider.GetCompletionsAsync(
                    project,
                    fileName,
                    caret,
                    ScriptEnvironment,
                    cancellation.Token);
            if (IsDisposed || cancellation.IsCancellationRequested
                || !ReferenceEquals(editor, ActiveEditor)
                || caret != editor.SelectionStart
                || !string.Equals(source, editor.Text, StringComparison.Ordinal))
                return;
            editor.ShowCompletionItems(items);
        }
        catch (OperationCanceledException)
        {
            // 后续输入替代旧补全。
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"项目补全失败：{exception}");
        }
    }

    private async Task NavigateProjectDefinitionAsync(string sourceFileName, int position)
    {
        var project = CreateProjectSnapshot();
        var definition = await ScriptService.GetProjectDefinitionAsync(
            project,
            sourceFileName,
            position,
            ScriptEnvironment);
        if (definition is null || IsDisposed) return;
        if (definition.IsSource && definition.SourceName is not null)
        {
            RecordNavigationOrigin();
            if (!SelectFile(definition.SourceName)) return;
            ActiveEditor.Select(definition.Start, Math.Max(1, definition.Length));
            ActiveEditor.Focus();
        }
    }

    private async Task PromptRenameSymbolAsync(string fileName, int position)
    {
        if (!EnsureProjectEditingService()) return;
        var identifier = GetIdentifierAtPosition(
            FindPage(fileName)?.Controls.OfType<RoslynScriptEditorControl>().Single().Text ?? string.Empty,
            position);
        if (identifier.Name.Length == 0) return;
        var newName = ScriptWorkspaceDialogs.RequestNewName(FindForm(), identifier.Name.TrimStart('@'));
        if (string.IsNullOrWhiteSpace(newName) || string.Equals(newName, identifier.Name, StringComparison.Ordinal)) return;

        var project = CreateProjectSnapshot();
        var signature = ProjectSignature(project);
        var cancellation = BeginWorkspaceOperation();
        try
        {
            var edit = await _projectEditingService!.RenameSymbolAsync(
                project,
                fileName,
                position,
                newName!,
                ScriptEnvironment,
                cancellation.Token);
            if (!CanApplyWorkspaceResult(signature, cancellation)) return;
            if (edit is null)
            {
                MessageBox.Show(FindForm(), "当前位置没有可重命名的源码符号。", "重命名符号", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (edit.HasConflicts && MessageBox.Show(
                    FindForm(),
                    "新名称会产生名称或编译冲突。仍要应用重命名吗？",
                    "重命名冲突",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning) != DialogResult.Yes)
                return;
            if (!ScriptWorkspaceDialogs.ConfirmEdit(FindForm(), edit)) return;
            ApplyWorkspaceEdit(edit, allowConflicts: edit.HasConflicts);
            if (SelectFile(fileName)) ActiveEditor.Select(identifier.Start, newName!.Length);
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            ShowWorkspaceError("重命名符号失败", exception);
        }
    }

    private async Task ShowProjectReferencesAsync(string fileName, int position)
    {
        if (!EnsureProjectEditingService()) return;
        var project = CreateProjectSnapshot();
        var signature = ProjectSignature(project);
        var cancellation = BeginWorkspaceOperation();
        try
        {
            var locations = await _projectEditingService!.FindReferencesAsync(
                project,
                fileName,
                position,
                ScriptEnvironment,
                cancellation.Token);
            if (!CanApplyWorkspaceResult(signature, cancellation)) return;
            var eventArgs = new ScriptReferencesFoundEventArgs(locations);
            ReferencesFound?.Invoke(this, eventArgs);
            if (eventArgs.Handled) return;
            if (locations.Count == 0)
            {
                MessageBox.Show(FindForm(), "没有找到源码声明或引用。", "查找所有引用", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            ShowReferencesPanel(locations);
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            ShowWorkspaceError("查找引用失败", exception);
        }
    }

    private async Task ShowProjectCodeFixesAsync(string fileName, int position)
    {
        if (!EnsureProjectEditingService()) return;
        var project = CreateProjectSnapshot();
        var signature = ProjectSignature(project);
        var cancellation = BeginWorkspaceOperation();
        try
        {
            var diagnostics = _projectDiagnostics;
            var diagnostic = FindDiagnostic(diagnostics, fileName, position);
            if (diagnostic is null)
            {
                diagnostics = await ScriptService.GetProjectDiagnosticsAsync(project, ScriptEnvironment, cancellation.Token);
                diagnostic = FindDiagnostic(diagnostics, fileName, position);
            }
            if (diagnostic is null || !CanApplyWorkspaceResult(signature, cancellation))
            {
                MessageBox.Show(FindForm(), "光标位置没有可修复的项目诊断。", "代码修复", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            var edits = await _projectEditingService!.GetCodeFixesAsync(
                project,
                fileName,
                diagnostic,
                ScriptEnvironment,
                cancellation.Token);
            if (!CanApplyWorkspaceResult(signature, cancellation)) return;
            var selected = ScriptWorkspaceDialogs.SelectEdit(FindForm(), edits);
            if (selected is null)
            {
                if (edits.Count == 0)
                    MessageBox.Show(FindForm(), "该诊断没有可用的项目级语义修复。", "代码修复", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (selected.HasConflicts && MessageBox.Show(
                    FindForm(),
                    "该修复会产生新的编译冲突。仍要应用吗？",
                    "代码修复冲突",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning) != DialogResult.Yes)
                return;
            if (!ScriptWorkspaceDialogs.ConfirmEdit(FindForm(), selected)) return;
            ApplyWorkspaceEdit(selected, allowConflicts: selected.HasConflicts);
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            ShowWorkspaceError("项目代码修复失败", exception);
        }
    }

    private void NavigateToReference(RoslynScriptReferenceLocation location)
    {
        RecordNavigationOrigin();
        if (!SelectFile(location.FileName)) return;
        ActiveEditor.Select(location.Start, Math.Max(1, location.Length));
        ActiveEditor.Focus();
    }

    private CancellationTokenSource BeginWorkspaceOperation()
    {
        _workspaceCancellation?.Cancel();
        _workspaceCancellation?.Dispose();
        return _workspaceCancellation = new CancellationTokenSource();
    }

    private bool CanApplyWorkspaceResult(string signature, CancellationTokenSource cancellation) =>
        !IsDisposed
        && !cancellation.IsCancellationRequested
        && string.Equals(signature, ProjectSignature(CreateProjectSnapshot()), StringComparison.Ordinal);

    private bool EnsureProjectEditingService()
    {
        if (_projectEditingService is not null) return true;
        MessageBox.Show(
            FindForm(),
            "高级项目编辑需要引用 ScriptEngine.Workspaces，并设置 ProjectEditingService。",
            "高级编辑不可用",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
        return false;
    }

    private IRoslynScriptProjectEditingService RequireProjectEditingService() =>
        _projectEditingService
        ?? throw new InvalidOperationException("高级项目编辑需要引用 ScriptEngine.Workspaces，并设置 ProjectEditingService。");

    private static object? TryCreateOptionalWorkspaceService()
    {
        try
        {
            var type = Type.GetType(
                "ScriptEngine.Workspaces.RoslynScriptWorkspaceService, ScriptEngine.Workspaces",
                throwOnError: false);
            return type is null ? null : Activator.CreateInstance(type);
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"加载可选 ScriptEngine.Workspaces 失败：{exception}");
            return null;
        }
    }

    private static RoslynScriptDiagnostic? FindDiagnostic(
        IEnumerable<RoslynScriptDiagnostic> diagnostics,
        string fileName,
        int position) => diagnostics
        .Where(item => item.Origin == RoslynScriptDiagnosticOrigin.UserSource
                       && string.Equals(item.SourceName, fileName, StringComparison.OrdinalIgnoreCase)
                       && position >= item.Start
                       && position <= item.Start + Math.Max(1, item.Length))
        .OrderByDescending(item => item.Severity)
        .ThenBy(item => item.Length)
        .FirstOrDefault();

    private static (string Name, int Start) GetIdentifierAtPosition(string source, int position)
    {
        if (source.Length == 0) return (string.Empty, 0);
        var index = Math.Min(Math.Max(position, 0), source.Length - 1);
        if (!IsIdentifierCharacter(source[index]) && index > 0 && IsIdentifierCharacter(source[index - 1])) index--;
        if (!IsIdentifierCharacter(source[index])) return (string.Empty, index);
        var start = index;
        while (start > 0 && IsIdentifierCharacter(source[start - 1])) start--;
        if (start > 0 && source[start - 1] == '@') start--;
        var end = index + 1;
        while (end < source.Length && IsIdentifierCharacter(source[end])) end++;
        return (source.Substring(start, end - start), start);
    }

    private static bool IsIdentifierCharacter(char value) => value == '_' || char.IsLetterOrDigit(value);

    private void ShowWorkspaceError(string title, Exception exception)
    {
        Debug.WriteLine($"{title}：{exception}");
        if (!IsDisposed)
            MessageBox.Show(FindForm(), exception.Message, title, MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    private RoslynScriptProject CreateProjectSnapshot() => new()
    {
        SourceFiles = _tabs.TabPages.Cast<TabPage>()
            .Select(page => new RoslynScriptSourceFile(
                GetFileName(page),
                GetEditor(page).Text))
            .ToArray()
    };

    private TabPage? FindPage(string fileName) => _tabs.TabPages.Cast<TabPage>().FirstOrDefault(page =>
        string.Equals(GetFileName(page), fileName, StringComparison.OrdinalIgnoreCase));

    private static string ValidateFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) throw new ArgumentException("脚本文件名不能为空。", nameof(fileName));
        var normalized = fileName.Trim().Replace('\\', '/');
        if (Path.IsPathRooted(normalized) || normalized.Split('/').Any(part => part == "..")
            || !normalized.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
            || normalized.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"无效的脚本文件名：{fileName}", nameof(fileName));
        return normalized;
    }

    private static string ProjectSignature(RoslynScriptProject project) => string.Join("\n", project.SourceFiles.Select(file =>
        file.FileName + "\0" + file.Source.Length + "\0" + file.Source));

    private sealed class ScriptFileTabState
    {
        public ScriptFileTabState(string fileName, bool metadataModified)
        {
            FileName = fileName;
            MetadataModified = metadataModified;
        }

        public string FileName { get; set; }
        public bool MetadataModified { get; set; }
    }

    private sealed record ProjectHistoryEntry(
        RoslynScriptProject Project,
        string ActiveFileName,
        int CaretPosition,
        string Title,
        string ExpectedCurrentRevision);

    private sealed record NavigationLocation(string FileName, int Start, int Length);
}

/// <summary>项目查找引用完成事件参数。</summary>
public sealed class ScriptReferencesFoundEventArgs : EventArgs
{
    /// <summary>创建查找引用完成事件。</summary>
    public ScriptReferencesFoundEventArgs(IReadOnlyList<RoslynScriptReferenceLocation> locations) => Locations = locations;

    /// <summary>项目中的声明和引用位置。</summary>
    public IReadOnlyList<RoslynScriptReferenceLocation> Locations { get; }

    /// <summary>宿主是否已经展示结果；设为 true 可阻止内置结果窗口。</summary>
    public bool Handled { get; set; }
}

/// <summary>项目诊断更新事件参数。</summary>
public sealed class ScriptDiagnosticsUpdatedEventArgs : EventArgs
{
    /// <summary>创建诊断更新事件。</summary>
    public ScriptDiagnosticsUpdatedEventArgs(IReadOnlyList<RoslynScriptDiagnostic> diagnostics) => Diagnostics = diagnostics;

    /// <summary>当前项目全部诊断。</summary>
    public IReadOnlyList<RoslynScriptDiagnostic> Diagnostics { get; }
}
