using DP.WorkFlow.UI;

namespace DP.WorkFlow.UI.WinForms;

/// <summary>
/// WinForms 工作流综合工作台，组合工具箱、画布、参数、诊断、运行监视和文档命令。
/// 固定分栏及工具栏位于同名 Designer.cs，Session/Runtime 绑定行为保留在本文件。
/// </summary>
public sealed partial class WorkflowStudioControl : UserControl
{
    private WorkflowDesignerSession? _session;
    private WorkflowDesignerNavigator? _navigator;
    private WorkflowStudioRuntimeBinding? _runtimeBinding;
    private WorkflowDocumentWorkspace? _workspace;
    private string? _startNodeId;

    /// <summary>初始化集设计器、工具箱、诊断和运行控制于一体的工作室控件。</summary>
    public WorkflowStudioControl()
    {
        InitializeComponent();

        // 菜单行为依赖当前 Session/Workspace，固定 ToolStripItem 已在 Designer 中声明。
        fileMenu.DropDownItems.Add("新建", null, (_, _) => NewDocument());
        fileMenu.DropDownItems.Add("打开…", null, (_, _) => OpenDocument());
        fileMenu.DropDownItems.Add("保存", null, (_, _) => SaveDocument(false));
        fileMenu.DropDownItems.Add("另存为…", null, (_, _) => SaveDocument(true));
        layoutMenu.DropDownItems.Add("自动布局", null, (_, _) => Session?.AutoLayout());
        layoutMenu.DropDownItems.Add("左对齐", null, (_, _) => Session?.AlignSelectedNodes(WorkflowNodeAlignment.Left));
        layoutMenu.DropDownItems.Add("右对齐", null, (_, _) => Session?.AlignSelectedNodes(WorkflowNodeAlignment.Right));
        layoutMenu.DropDownItems.Add("顶端对齐", null, (_, _) => Session?.AlignSelectedNodes(WorkflowNodeAlignment.Top));
        layoutMenu.DropDownItems.Add("底端对齐", null, (_, _) => Session?.AlignSelectedNodes(WorkflowNodeAlignment.Bottom));
        layoutMenu.DropDownItems.Add("水平分布", null, (_, _) => Session?.DistributeSelectedNodes(WorkflowNodeDistribution.Horizontal));
        layoutMenu.DropDownItems.Add("垂直分布", null, (_, _) => Session?.DistributeSelectedNodes(WorkflowNodeDistribution.Vertical));

        _undoButton.Click += (_, _) => Session?.Undo();
        _redoButton.Click += (_, _) => Session?.Redo();
        _upButton.Click += (_, _) => Navigator?.NavigateUp();
        _runButton.Click += async (_, _) => await RunWorkflowAsync();
        _pauseButton.Click += (_, _) => RuntimeBinding?.Pause();
        _resumeButton.Click += (_, _) => RuntimeBinding?.Resume();
        _stopButton.Click += (_, _) => RuntimeBinding?.Cancel();
        fitButton.Click += (_, _) => Session?.FitToView(Designer.ClientSize.Width, Designer.ClientSize.Height);
        Toolbox.NodeTypeActivated += OnNodeTypeActivated;
        Designer.InteractionError += (_, message) => InteractionError?.Invoke(this, message);
        Designer.NodeEditRequested += OnDesignerNodeEditRequested;
        Properties.EditError += (_, message) => InteractionError?.Invoke(this, message);
        Properties.BlockMappingEditRequested += OnBlockMappingEditRequested;
        Properties.PropertyActionRequested += OnPropertyActionRequested;
        Load += (_, _) =>
        {
            if (toolboxAndEditor.Width > 500)
                toolboxAndEditor.SplitterDistance = Math.Min(210, toolboxAndEditor.Width - 300);
        };
        WorkflowWinFormsStyle.Apply(this);
        var palette = WorkflowWinFormsStyle.Get();
        _toolbar.BackColor = palette.Surface;
        _toolbar.ForeColor = palette.Text;
    }

    /// <summary>获取画布控件。</summary>
    public WorkflowDesignerControl Designer => designerControl;

    /// <summary>获取工具箱控件。</summary>
    public WorkflowToolboxControl Toolbox => toolboxControl;

    /// <summary>获取属性面板。</summary>
    public WorkflowPropertyPanel Properties => propertyPanel;

    /// <summary>获取编译诊断面板。</summary>
    public WorkflowDiagnosticsControl Diagnostics => diagnosticsControl;
    /// <summary>为领域工具添加独立工作台页面。</summary>
    public void AddToolPage(string title, Control page)
    { var tab = new TabPage(title); page.Dock = DockStyle.Fill; tab.Controls.Add(page); bottomTabs.TabPages.Add(tab); }

    /// <summary>获取运行监视器。</summary>
    public WorkflowRuntimeMonitorControl RuntimeMonitor => runtimeMonitorControl;

    /// <summary>获取或设置文档工作区。</summary>
    public WorkflowDocumentWorkspace? Workspace
    {
        get => _workspace;
        set
        {
            if (ReferenceEquals(_workspace, value))
                return;
            if (_workspace is not null)
            {
                _workspace.DocumentChanged -= OnWorkspaceChanged;
                _workspace.DirtyStateChanged -= OnWorkspaceChanged;
            }
            _workspace = value;
            if (_workspace is not null)
            {
                _workspace.DocumentChanged += OnWorkspaceChanged;
                _workspace.DirtyStateChanged += OnWorkspaceChanged;
                Navigator = _workspace.Navigator;
            }
            UpdateNavigation();
        }
    }

    /// <summary>存在未保存修改时由宿主确认是否放弃；未设置时拒绝放弃。</summary>
    public Func<bool>? ConfirmDiscardChanges { get; set; }

    /// <summary>获取或设置运行时绑定。</summary>
    public WorkflowStudioRuntimeBinding? RuntimeBinding
    {
        get => _runtimeBinding;
        set
        {
            if (ReferenceEquals(_runtimeBinding, value))
                return;
            if (_runtimeBinding is not null)
                _runtimeBinding.StateChanged -= OnRuntimeStateChanged;
            _runtimeBinding = value;
            RuntimeMonitor.RuntimeBinding = value;
            if (_runtimeBinding is not null)
            {
                if (_navigator is not null)
                {
                    _runtimeBinding.SetNavigator(_navigator);
                    _runtimeBinding.RunTarget = _navigator.Depth > 0
                        ? WorkflowStudioRunTarget.CurrentCanvas
                        : WorkflowStudioRunTarget.RootWorkflow;
                }
                _runtimeBinding.StateChanged += OnRuntimeStateChanged;
            }
            UpdateRuntimeCommands();
        }
    }

    /// <summary>获取或设置子画布导航器；设置后自动切换 Session 和 EntryNodeId。</summary>
    public WorkflowDesignerNavigator? Navigator
    {
        get => _navigator;
        set
        {
            if (ReferenceEquals(_navigator, value))
                return;
            if (_navigator is not null)
                _navigator.CurrentChanged -= OnNavigatorChanged;
            _navigator = value;
            Diagnostics.Navigator = value;
            if (_navigator is not null)
            {
                _navigator.CurrentChanged += OnNavigatorChanged;
                _runtimeBinding?.SetNavigator(_navigator);
                ApplyNavigator();
            }
            else
                UpdateNavigation();
        }
    }

    /// <summary>获取或设置共享设计会话。</summary>
    public WorkflowDesignerSession? Session
    {
        get => _session;
        set
        {
            if (ReferenceEquals(_session, value))
                return;
            if (_session is not null)
                _session.Changed -= OnSessionChanged;
            _session = value;
            if (_session is not null)
                _session.Changed += OnSessionChanged;
            Designer.Session = value;
            Toolbox.Session = value;
            Diagnostics.Session = value;
            UpdateCommands();
        }
    }

    /// <summary>获取或设置流程入口，用于属性面板生成绑定候选。</summary>
    public string? EntryNodeId
    {
        get => _startNodeId;
        set
        {
            _startNodeId = value;
            Diagnostics.EntryNodeId = value;
        }
    }

    /// <summary>工作台交互错误时发生。</summary>
    public event EventHandler<string>? InteractionError;

    /// <summary>双击节点并关闭统一节点工作台后发生。</summary>
    public event EventHandler<IWorkflowNodeModel>? NodeEditRequested;

    /// <summary>获取节点详情插件组合目录；插件应在首次打开节点编辑器前完成注册。</summary>
    public WorkflowWinFormsStudioExtensionCatalog NodeEditorExtensions { get; } = new();

    /// <summary>从插件目录自动发现并注册 WinForms Studio Module。</summary>
    /// <param name="pluginRoot">插件包根目录。</param>
    /// <param name="loader">可复用的插件加载器；为空时创建新实例。</param>
    /// <returns>实际注册的插件 Module 数量。</returns>
    public int LoadNodeEditorPlugins(string pluginRoot, WorkflowPluginLoader? loader = null)
    {
        var effectiveLoader = loader ?? new WorkflowPluginLoader();
        var count = NodeEditorExtensions.LoadSharedPlugins(pluginRoot, effectiveLoader);
        var modules = effectiveLoader.LoadModules<IWorkflowWinFormsStudioExtension>(
            pluginRoot,
            WorkflowPluginModuleGroups.WinForms);
        foreach (var module in modules)
            NodeEditorExtensions.Register(module);
        return count + modules.Count;
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Toolbox.NodeTypeActivated -= OnNodeTypeActivated;
            Designer.NodeEditRequested -= OnDesignerNodeEditRequested;
            Properties.BlockMappingEditRequested -= OnBlockMappingEditRequested;
            if (_session is not null)
                _session.Changed -= OnSessionChanged;
            if (_navigator is not null)
                _navigator.CurrentChanged -= OnNavigatorChanged;
            if (_runtimeBinding is not null)
                _runtimeBinding.StateChanged -= OnRuntimeStateChanged;
            if (_workspace is not null)
            {
                _workspace.DocumentChanged -= OnWorkspaceChanged;
                _workspace.DirtyStateChanged -= OnWorkspaceChanged;
            }
            components?.Dispose();
        }
        base.Dispose(disposing);
    }

    /// <summary>执行 New Document 相关处理。</summary>
    private void NewDocument()
    {
        if (Workspace is null || !CanDiscardCurrentDocument())
            return;
        Navigator = Workspace.New();
    }

    /// <summary>执行 Open Document 相关处理。</summary>
    private void OpenDocument()
    {
        if (Workspace is null || !CanDiscardCurrentDocument())
            return;
        using var dialog = new OpenFileDialog
        {
            Filter = "DP.WorkFlow JSON (*.json)|*.json|所有文件 (*.*)|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;
        try { Navigator = Workspace.Open(dialog.FileName); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                          or InvalidOperationException or System.Text.Json.JsonException)
        { InteractionError?.Invoke(this, exception.Message); }
    }

    /// <summary>执行 Save Document 相关处理。</summary>
    private void SaveDocument(bool saveAs)
    {
        if (Workspace is null)
            return;
        try
        {
            if (saveAs || string.IsNullOrWhiteSpace(Workspace.CurrentFilePath))
            {
                using var dialog = new SaveFileDialog
                {
                    Filter = "DP.WorkFlow JSON (*.json)|*.json|所有文件 (*.*)|*.*",
                    DefaultExt = "json",
                    AddExtension = true
                };
                if (dialog.ShowDialog(this) != DialogResult.OK)
                    return;
                Workspace.SaveAs(dialog.FileName);
            }
            else
                Workspace.Save();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        { InteractionError?.Invoke(this, exception.Message); }
    }

    /// <summary>执行 Invoke 相关处理。</summary>
    private bool CanDiscardCurrentDocument() => Workspace?.IsDirty != true || ConfirmDiscardChanges?.Invoke() == true;

    private void OnWorkspaceChanged(object? sender, EventArgs e)
    {
        if (Workspace?.Navigator is not null && !ReferenceEquals(Navigator, Workspace.Navigator))
            Navigator = Workspace.Navigator;
        UpdateNavigation();
    }

    /// <summary>处理“Node Type Activated”事件。</summary>
    /// <param name="sender">事件发送者。</param>
    /// <param name="item">目标数据项。</param>
    private void OnNodeTypeActivated(object? sender, WorkflowToolboxItem item)
    {
        if (Session is null)
            return;
        var center = WorkflowDesignerGeometry.ScreenToCanvas(
            Session,
            Designer.ClientSize.Width / 2d,
            Designer.ClientSize.Height / 2d);
        Session.AddNode(item.NodeType, center.X - 90, center.Y - 30);
    }

    private void OnBlockMappingEditRequested(object? sender, IWorkflowBlockMappingNode block)
    {
        if (Session is null || string.IsNullOrWhiteSpace(EntryNodeId))
            return;
        using var model = new WorkflowBlockMappingEditorModel(Session, block.Id);
        using var dialog = new WorkflowBlockMappingEditorDialog(model, EntryNodeId);
        _ = dialog.ShowDialog(this);
    }

    private async void OnPropertyActionRequested(object? sender, WorkflowPropertyActionRequest request)
    {
        if (Session == null || string.IsNullOrWhiteSpace(EntryNodeId)) return;
        WorkflowNodeEditorModel? model = null;
        try
        {
            model = new WorkflowNodeEditorModel(Session, EntryNodeId, request.NodeId, NodeEditorExtensions.GetPageProviders(),
                Properties.ChoiceProvider, Properties.AdditionalProperties, request.EditorKey);
            using var dialog = new WorkflowNodeEditorDialog(model, NodeEditorExtensions.GetRenderers());
            dialog.ShowDialog(this);
        }
        catch (Exception ex) { InteractionError?.Invoke(this, $"无法打开属性编辑窗口：{ex.Message}"); }
        finally { if (model != null) await model.DisposeAsync(); }
    }

    /// <summary>处理“Designer Node Edit Requested”事件。</summary>
    /// <param name="sender">事件发送者。</param>
    /// <param name="node">目标画布节点。</param>
    private void OnDesignerNodeEditRequested(object? sender, IWorkflowNodeModel node)
    {
        if (Session is null || string.IsNullOrWhiteSpace(EntryNodeId)) return;
        try
        {
            var model = new WorkflowNodeEditorModel(
                Session, EntryNodeId, node.Id, NodeEditorExtensions.GetPageProviders(),
                Properties.ChoiceProvider, Properties.AdditionalProperties);
            using var dialog = new WorkflowNodeEditorDialog(
                model, NodeEditorExtensions.GetRenderers(),
                block => OnBlockMappingEditRequested(this, block));
            _ = dialog.ShowDialog(this);
            NodeEditRequested?.Invoke(this, node);
        }
        catch (InvalidOperationException exception)
        {
            InteractionError?.Invoke(this, $"无法打开节点工作台：{exception.Message}");
        }
    }

    /// <summary>应用Navigator。</summary>
    private void OnNavigatorChanged(object? sender, EventArgs e) => ApplyNavigator();

    /// <summary>应用Navigator。</summary>
    private void ApplyNavigator()
    {
        if (Navigator is null)
            return;
        Session = Navigator.CurrentSession;
        EntryNodeId = Navigator.CurrentEntryNodeId;
        if (_runtimeBinding is not null
            && _runtimeBinding.State is not (E_WorkflowExecutionState.Running or E_WorkflowExecutionState.Paused))
        {
            _runtimeBinding.RunTarget = Navigator.Depth > 0
                ? WorkflowStudioRunTarget.CurrentCanvas
                : WorkflowStudioRunTarget.RootWorkflow;
        }
        UpdateNavigation();
    }

    /// <summary>执行 Run DP.WorkFlow 相关处理。</summary>
    private async Task RunWorkflowAsync()
    {
        Diagnostics.RefreshDiagnostics();
        if (RuntimeBinding is null)
            return;
        if (!Diagnostics.CanRun)
        {
            InteractionError?.Invoke(this, "当前流程存在配置或编译错误，不能运行。");
            return;
        }
        try
        {
            var result = await RuntimeBinding.RunAsync();
            if (!result.Success && result.State == E_WorkflowExecutionState.Faulted)
                InteractionError?.Invoke(this, $"流程运行失败：{result.Message}");
        }
        catch (Exception exception) when (exception is InvalidOperationException or ObjectDisposedException)
        {
            InteractionError?.Invoke(this, exception.Message);
        }
    }

    private void OnRuntimeStateChanged(object? sender, EventArgs e)
    {
        if (IsDisposed)
            return;
        if (InvokeRequired)
            BeginInvoke(UpdateRuntimeCommands);
        else
            UpdateRuntimeCommands();
    }

    /// <summary>更新Commands。</summary>
    private void OnSessionChanged(object? sender, WorkflowDesignerChangedEventArgs e) => UpdateCommands();

    /// <summary>更新Commands。</summary>
    private void UpdateCommands()
    {
        _undoButton.Enabled = Session?.CanUndo == true;
        _redoButton.Enabled = Session?.CanRedo == true;
        UpdateNavigation();
        UpdateRuntimeCommands();
    }

    /// <summary>更新Runtime Commands。</summary>
    private void UpdateRuntimeCommands()
    {
        var state = RuntimeBinding?.State ?? E_WorkflowExecutionState.Idle;
        _runButton.Enabled = RuntimeBinding is not null && state is not (E_WorkflowExecutionState.Running or E_WorkflowExecutionState.Paused);
        _pauseButton.Enabled = state == E_WorkflowExecutionState.Running;
        _resumeButton.Enabled = state == E_WorkflowExecutionState.Paused;
        _stopButton.Enabled = state is E_WorkflowExecutionState.Running or E_WorkflowExecutionState.Paused;
        _runtimeLabel.Text = state.ToString();
    }

    /// <summary>更新Navigation。</summary>
    private void UpdateNavigation()
    {
        _upButton.Enabled = Navigator?.Depth > 0;
        var path = Navigator is null
            ? "Root"
            : string.Join("  /  ", Navigator.Breadcrumbs.Select(item => item.Title));
        _breadcrumbLabel.Text = Workspace?.IsDirty == true ? path + " *" : path;
        _runButton.Text = Navigator?.Depth > 0 ? "运行当前子流程" : "运行";
    }

    /// <summary>定义 FixedColorTable 类型。</summary>
    private sealed class FixedColorTable : ProfessionalColorTable
    {
        public override Color ToolStripGradientBegin => Color.FromArgb(37, 37, 38);
        public override Color ToolStripGradientMiddle => Color.FromArgb(37, 37, 38);
        public override Color ToolStripGradientEnd => Color.FromArgb(37, 37, 38);
        public override Color ButtonSelectedHighlight => Color.FromArgb(62, 62, 66);
        public override Color ButtonSelectedGradientBegin => Color.FromArgb(62, 62, 66);
        public override Color ButtonSelectedGradientEnd => Color.FromArgb(62, 62, 66);
    }
}
