using DP.WorkFlow.UI;
using ModernUI.WinForms;

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
    private WorkflowStudioRunCommands? _runCommands;
    private readonly ToolStripButton _saveButton = new() { Name = "_saveButton", Text = "保存", DisplayStyle = ToolStripItemDisplayStyle.Image, ToolTipText = "保存 (Ctrl+S)" };
    private readonly WorkflowNodeInspectorControl _inspector;

    /// <summary>初始化集设计器、工具箱、诊断和运行控制于一体的工作室控件。</summary>
    public WorkflowStudioControl()
    {
        InitializeComponent();

        // 画布工具栏、工具箱、诊断与运行监视来自共用的画布工作区；这里只加工作台自己的文档命令和右侧检查器。
        AddMenuItem(fileMenu, "新建", ModernIconKind.FileNew, NewDocument);
        AddMenuItem(fileMenu, "打开…", ModernIconKind.FolderOpen, OpenDocument);
        AddMenuItem(fileMenu, "保存", ModernIconKind.Save, () => SaveDocument(false));
        AddMenuItem(fileMenu, "另存为…", ModernIconKind.SaveAs, () => SaveDocument(true));
        canvasWorkspace.Toolbar.Items.Insert(1, _saveButton);
        canvasWorkspace.RegisterThemedIcon(_saveButton, ModernIconKind.Save);
        _saveButton.Click += (_, _) => SaveDocument(false);
        _inspector = new WorkflowNodeInspectorControl(propertyPanel);
        _inspector.EditRequested += (_, node) => OpenNodeEditor(node);
        canvasWorkspace.Inspector = _inspector;
        canvasWorkspace.InteractionError += (_, message) => InteractionError?.Invoke(this, message);
        canvasWorkspace.RunFaulted += (_, message) => RunFaulted?.Invoke(this, message);
        Designer.NodeEditRequested += OnDesignerNodeEditRequested;
        Properties.EditError += (_, message) => InteractionError?.Invoke(this, message);
        Properties.PropertyActionRequested += OnPropertyActionRequested;
        UpdateNavigation();
        WorkflowWinFormsTheme.Follow(this);
    }

    /// <summary>向下拉菜单添加带图标的命令项。</summary>
    private void AddMenuItem(ToolStripDropDownItem menu, string text, ModernIconKind icon, Action action)
    {
        var item = new ToolStripMenuItem(text, null, (_, _) => action());
        canvasWorkspace.RegisterThemedIcon(item, icon);
        menu.DropDownItems.Add(item);
    }

    /// <summary>获取共用的画布工作区（工具栏、工具箱、画布、诊断、运行监视）。</summary>
    public WorkflowCanvasWorkspaceControl CanvasWorkspace => canvasWorkspace;

    /// <summary>获取画布控件。</summary>
    public WorkflowDesignerControl Designer => canvasWorkspace.Designer;

    /// <summary>获取工具箱控件。</summary>
    public WorkflowToolboxControl Toolbox => canvasWorkspace.Toolbox;

    /// <summary>获取属性面板（右侧检查器的“参数”页）。</summary>
    public WorkflowPropertyPanel Properties => propertyPanel;

    /// <summary>获取右侧节点检查器。</summary>
    public WorkflowNodeInspectorControl Inspector => _inspector;

    /// <summary>
    /// “文件”菜单。宿主有自己的流程库时可以清空后放自己的条目（新建、打开库里的流程、放弃草稿等），保存用 <see cref="Save"/>。
    /// </summary>
    public ToolStripDropDownButton FileMenu => fileMenu;

    /// <summary>向“文件”菜单加一个带图标的条目。</summary>
    /// <param name="text">文字。</param>
    /// <param name="icon">图标。</param>
    /// <param name="action">点击时执行。</param>
    /// <returns>加入的条目。</returns>
    public ToolStripMenuItem AddFileMenuItem(string text, ModernIconKind icon, Action action)
    {
        var item = new ToolStripMenuItem(text, null, (_, _) => action());
        canvasWorkspace.RegisterThemedIcon(item, icon);
        fileMenu.DropDownItems.Add(item);
        return item;
    }

    /// <summary>保存当前文件（还没有路径时询问保存位置）。</summary>
    public void Save() => SaveDocument(false);

    /// <summary>获取编译诊断面板。</summary>
    public WorkflowDiagnosticsControl Diagnostics => canvasWorkspace.Diagnostics;
    /// <summary>
    /// 为领域工具添加弹出窗口入口：在“文件”菜单末尾出现同名菜单项，点击打开窗口。
    /// 关闭窗口只隐藏，面板状态与正在进行的检查都会保留。
    /// </summary>
    /// <param name="title">菜单项与窗口标题。</param>
    /// <param name="content">窗口内容。</param>
    /// <param name="icon">菜单项图标。</param>
    public void AddToolWindow(string title, Control content, ModernIconKind icon = ModernIconKind.Settings)
    {
        Guard.NotNullOrWhiteSpace(title);
        Guard.NotNull(content);
        Form? window = null;
        if (!_hasToolWindows)
        {
            fileMenu.DropDownItems.Add(new ToolStripSeparator());
            _hasToolWindows = true;
        }
        AddMenuItem(fileMenu, title + "…", icon, () =>
        {
            if (window is null || window.IsDisposed)
            {
                window = new Form
                {
                    Text = title,
                    StartPosition = FormStartPosition.CenterParent,
                    Size = new Size(1100, 640),
                    MinimumSize = new Size(640, 360),
                    ShowInTaskbar = false,
                    ShowIcon = false
                };
                content.Dock = DockStyle.Fill;
                window.Controls.Add(content);
                WorkflowWinFormsTheme.Follow(window);
                window.FormClosing += (_, e) =>
                {
                    if (e.CloseReason != CloseReason.UserClosing) return;
                    e.Cancel = true;
                    window.Hide();
                };
                Disposed += (_, _) => window.Dispose();
            }
            if (window.Visible) window.Activate();
            else window.Show(FindForm());
        });
    }

    private bool _hasToolWindows;

    /// <summary>获取运行监视器。</summary>
    public WorkflowRuntimeMonitorControl RuntimeMonitor => canvasWorkspace.RuntimeMonitor;

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
            _runCommands?.Dispose();
            _runCommands = null;
            if (_runtimeBinding is not null) _runtimeBinding.OperationFailed -= OnBindingOperationFailed;
            _runtimeBinding = value;
            if (_runtimeBinding is not null) _runtimeBinding.OperationFailed += OnBindingOperationFailed;
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
                _runCommands = new WorkflowStudioRunCommands(_runtimeBinding,
                    () => Navigator?.Depth > 0 ? "运行当前子流程" : "调试运行");
            }
            canvasWorkspace.RunCommands = _runCommands;
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
            _session = value;
            canvasWorkspace.Session = value;
            UpdateNavigation();
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
            propertyPanel.EntryNodeId = value;
        }
    }

    /// <summary>工作台交互错误时发生。</summary>
    public event EventHandler<string>? InteractionError;

    /// <summary>流程运行以故障结束时发生（只用于记录；故障详情已写入运行监控）。</summary>
    public event EventHandler<string>? RunFaulted;

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
            Designer.NodeEditRequested -= OnDesignerNodeEditRequested;
            if (_navigator is not null)
                _navigator.CurrentChanged -= OnNavigatorChanged;
            _runCommands?.Dispose();
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

    private async void OnPropertyActionRequested(object? sender, WorkflowPropertyActionRequest request)
    {
        if (Session == null || string.IsNullOrWhiteSpace(EntryNodeId)) return;
        WorkflowNodeEditorModel? model = null;
        try
        {
            model = new WorkflowNodeEditorModel(Session, EntryNodeId, request.NodeId, NodeEditorExtensions.GetPageProviders(),
                Properties.ChoiceProvider, Properties.AdditionalProperties, request.EditorKey, Properties.Services);
            using var dialog = new WorkflowNodeEditorDialog(model, NodeEditorExtensions.GetRenderers());
            dialog.ShowDialog(this);
        }
        catch (Exception ex) { InteractionError?.Invoke(this, $"无法打开属性编辑窗口：{ex.Message}"); }
        finally { if (model != null) await model.DisposeAsync(); }
    }

    /// <summary>双击画布节点：打开完整的节点编辑窗口。</summary>
    private void OnDesignerNodeEditRequested(object? sender, IWorkflowNodeModel node) => OpenNodeEditor(node);

    /// <summary>打开节点编辑窗口（专用页面、子画布、脚本等）；关闭后通知宿主。</summary>
    /// <param name="node">节点。</param>
    public void OpenNodeEditor(IWorkflowNodeModel node)
    {
        Guard.NotNull(node);
        if (Session is null || string.IsNullOrWhiteSpace(EntryNodeId)) return;
        try
        {
            var model = new WorkflowNodeEditorModel(
                Session, EntryNodeId, node.Id, NodeEditorExtensions.GetPageProviders(),
                Properties.ChoiceProvider, Properties.AdditionalProperties, services: Properties.Services);
            using var dialog = new WorkflowNodeEditorDialog(model, NodeEditorExtensions.GetRenderers());
            _ = dialog.ShowDialog(this);
            NodeEditRequested?.Invoke(this, node);
        }
        catch (InvalidOperationException exception)
        {
            InteractionError?.Invoke(this, $"无法打开节点编辑：{exception.Message}");
        }
    }

    private void OnBindingOperationFailed(object? sender, string message)
    {
        if (IsDisposed) return;
        if (InvokeRequired) BeginInvoke(new Action(() => InteractionError?.Invoke(this, message)));
        else InteractionError?.Invoke(this, message);
    }

    /// <summary>重新读取运行按钮的可用性和提示（例如整线状态、权限变化后）。</summary>
    public void RefreshRunCommands()
    {
        _runCommands?.Refresh();
        canvasWorkspace.RefreshRunCommands();
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

    /// <summary>更新路径文字和运行按钮提示（导航到子画布时运行当前子流程）。</summary>
    private void UpdateNavigation()
    {
        var path = Navigator is null
            ? null
            : string.Join("  /  ", Navigator.Breadcrumbs.Select(item => item.Title));
        canvasWorkspace.BreadcrumbText = path;
        canvasWorkspace.DocumentState = Workspace?.IsDirty == true ? "已修改" : null;
        _saveButton.Enabled = Workspace is not null;
        _runCommands?.Refresh();
    }
}
