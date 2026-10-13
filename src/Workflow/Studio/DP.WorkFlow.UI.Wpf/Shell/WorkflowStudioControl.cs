using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DP.WorkFlow.UI;

namespace DP.WorkFlow.UI.Wpf;

/// <summary>组合工具箱、画布、属性面板和常用命令的 WPF 工作流工作台。</summary>
public sealed class WorkflowStudioControl : UserControl
{
    public static readonly DependencyProperty SessionProperty = DependencyProperty.Register(
        nameof(Session), typeof(WorkflowDesignerSession), typeof(WorkflowStudioControl),
        new PropertyMetadata(null, OnSessionChanged));

    public static readonly DependencyProperty EntryNodeIdProperty = DependencyProperty.Register(
        nameof(EntryNodeId), typeof(string), typeof(WorkflowStudioControl),
        new PropertyMetadata(null, OnEntryNodeIdChanged));

    private readonly WorkflowCanvasWorkspaceControl _canvasWorkspace = new();
    private WorkflowStudioRuntimeBinding? _runtimeBinding;
    private WorkflowStudioRunCommands? _runCommands;
    private WorkflowDocumentWorkspace? _workspace;
    private WorkflowDesignerNavigator? _navigator;
    private int _toolWindowCount;

    /// <summary>初始化集设计器、工具箱、诊断和运行控制于一体的工作室控件。</summary>
    public WorkflowStudioControl()
    {
        Background = Brush(30, 30, 30);
        Properties = new WorkflowPropertyPanel();

        // 画布工具栏、工具箱、诊断与运行监视来自共用的画布工作区；这里只加工作台自己的文档命令。
        var newButton = WorkflowCanvasWorkspaceControl.CommandButton("新建");
        var openButton = WorkflowCanvasWorkspaceControl.CommandButton("打开");
        var saveButton = WorkflowCanvasWorkspaceControl.CommandButton("保存");
        _canvasWorkspace.Toolbar.Children.Insert(0, saveButton);
        _canvasWorkspace.Toolbar.Children.Insert(0, openButton);
        _canvasWorkspace.Toolbar.Children.Insert(0, newButton);
        Content = _canvasWorkspace;

        newButton.Click += (_, _) => NewDocument();
        openButton.Click += (_, _) => OpenDocument();
        saveButton.Click += (_, _) => SaveDocument();
        _canvasWorkspace.InteractionError += (_, message) => InteractionError?.Invoke(this, message);
        _canvasWorkspace.RunFaulted += (_, message) => RunFaulted?.Invoke(this, message);
        Designer.NodeEditRequested += OnDesignerNodeEditRequested;
        Properties.EditError += (_, message) => InteractionError?.Invoke(this, message);
        Properties.PropertyActionRequested += OnPropertyActionRequested;
        Unloaded += (_, _) =>
        {
            if (_navigator is not null)
                _navigator.CurrentChanged -= OnNavigatorChanged;
            if (_workspace is not null)
            {
                _workspace.DocumentChanged -= OnWorkspaceChanged;
                _workspace.DirtyStateChanged -= OnWorkspaceChanged;
            }
        };
        Loaded += (_, _) =>
        {
            if (_navigator is not null)
            {
                _navigator.CurrentChanged -= OnNavigatorChanged;
                _navigator.CurrentChanged += OnNavigatorChanged;
            }
            if (_workspace is not null)
            {
                _workspace.DocumentChanged -= OnWorkspaceChanged;
                _workspace.DirtyStateChanged -= OnWorkspaceChanged;
                _workspace.DocumentChanged += OnWorkspaceChanged;
                _workspace.DirtyStateChanged += OnWorkspaceChanged;
            }
            UpdateNavigation();
        };
        UpdateNavigation();
    }

    /// <summary>获取共用的画布工作区（工具栏、工具箱、画布、诊断、运行监视）。</summary>
    public WorkflowCanvasWorkspaceControl CanvasWorkspace => _canvasWorkspace;

    /// <summary>获取或设置 Designer 成员。</summary>
    public WorkflowDesignerControl Designer => _canvasWorkspace.Designer;

    /// <summary>获取或设置 Toolbox 成员。</summary>
    public WorkflowToolboxControl Toolbox => _canvasWorkspace.Toolbox;

    /// <summary>获取或设置 Properties 成员。</summary>
    public WorkflowPropertyPanel Properties { get; }

    /// <summary>获取或设置 Diagnostics 成员。</summary>
    public WorkflowDiagnosticsControl Diagnostics => _canvasWorkspace.Diagnostics;
    /// <summary>为领域工具添加弹出窗口入口：工具栏“保存”之后出现同名按钮，点击打开；关闭窗口不会丢失面板状态。</summary>
    /// <param name="title">按钮与窗口标题。</param>
    /// <param name="content">窗口内容。</param>
    public void AddToolWindow(string title, UIElement content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(content);
        Window? window = null;
        var button = WorkflowCanvasWorkspaceControl.CommandButton(title + "…");
        button.Click += (_, _) =>
        {
            if (window is null)
            {
                window = new Window
                {
                    Title = title,
                    Content = content,
                    Owner = Window.GetWindow(this),
                    Width = 1100,
                    Height = 640,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    ShowInTaskbar = false,
                    Background = Brush(30, 30, 30)
                };
                // 关闭时释放内容，下次打开重新承载同一实例，面板状态得以保留。
                window.Closed += (_, _) => { window.Content = null; window = null; };
            }
            window.Show();
            window.Activate();
        };
        _canvasWorkspace.Toolbar.Children.Insert(3 + _toolWindowCount++, button);
    }

    /// <summary>获取或设置 Runtime Monitor 成员。</summary>
    public WorkflowRuntimeMonitorControl RuntimeMonitor => _canvasWorkspace.RuntimeMonitor;

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

    /// <summary>存在未保存修改时由宿主确认是否放弃。</summary>
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
                _runCommands = new WorkflowStudioRunCommands(_runtimeBinding,
                    () => Navigator?.Depth > 0 ? "运行当前子流程" : "运行");
            }
            _canvasWorkspace.RunCommands = _runCommands;
        }
    }

    /// <summary>获取或设置子画布导航器。</summary>
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

    public WorkflowDesignerSession? Session
    {
        get => (WorkflowDesignerSession?)GetValue(SessionProperty);
        set => SetValue(SessionProperty, value);
    }

    public string? EntryNodeId
    {
        get => (string?)GetValue(EntryNodeIdProperty);
        set => SetValue(EntryNodeIdProperty, value);
    }

    /// <summary>获取或设置 Interaction Error 成员。</summary>
    public event EventHandler<string>? InteractionError;

    /// <summary>流程运行以故障结束时发生（只用于记录；故障详情已写入运行监视）。</summary>
    public event EventHandler<string>? RunFaulted;

    /// <summary>双击节点并关闭统一节点工作台后发生。</summary>
    public event EventHandler<IWorkflowNodeModel>? NodeEditRequested;

    /// <summary>获取节点详情插件组合目录；插件应在首次打开节点编辑器前完成注册。</summary>
    public WorkflowWpfStudioExtensionCatalog NodeEditorExtensions { get; } = new();

    /// <summary>从插件目录自动发现并注册 WPF Studio Module。</summary>
    /// <param name="pluginRoot">插件包根目录。</param>
    /// <param name="loader">可复用的插件加载器；为空时创建新实例。</param>
    /// <returns>实际注册的插件 Module 数量。</returns>
    public int LoadNodeEditorPlugins(string pluginRoot, WorkflowPluginLoader? loader = null)
    {
        var effectiveLoader = loader ?? new WorkflowPluginLoader();
        var count = NodeEditorExtensions.LoadSharedPlugins(pluginRoot, effectiveLoader);
        var modules = effectiveLoader.LoadModules<IWorkflowWpfStudioExtension>(
            pluginRoot,
            WorkflowPluginModuleGroups.Wpf);
        foreach (var module in modules)
            NodeEditorExtensions.Register(module);
        return count + modules.Count;
    }

    /// <summary>处理工作室 Session 依赖属性变化并同步各子控件。</summary>
    /// <param name="dependencyObject">发生属性变化的工作室控件。</param>
    /// <param name="e">依赖属性变化参数。</param>
    private static void OnSessionChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        var control = (WorkflowStudioControl)dependencyObject;
        control._canvasWorkspace.Session = (WorkflowDesignerSession?)e.NewValue;
        control.UpdateNavigation();
    }

    private static void OnEntryNodeIdChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        var control = (WorkflowStudioControl)dependencyObject;
        control.Diagnostics.EntryNodeId = (string?)e.NewValue;
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
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "DP.WorkFlow JSON (*.json)|*.json|All files (*.*)|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog() != true)
            return;
        try { Navigator = Workspace.Open(dialog.FileName); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                          or InvalidOperationException or System.Text.Json.JsonException)
        { InteractionError?.Invoke(this, exception.Message); }
    }

    /// <summary>执行 Save Document 相关处理。</summary>
    private void SaveDocument()
    {
        if (Workspace is null)
            return;
        try
        {
            if (string.IsNullOrWhiteSpace(Workspace.CurrentFilePath))
            {
                var dialog = new Microsoft.Win32.SaveFileDialog
                {
                    Filter = "DP.WorkFlow JSON (*.json)|*.json|All files (*.*)|*.*",
                    DefaultExt = ".json",
                    AddExtension = true
                };
                if (dialog.ShowDialog() != true)
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
                Properties.ChoiceProvider, Properties.AdditionalProperties, request.EditorKey);
            var dialog = new WorkflowNodeEditorWindow(model, NodeEditorExtensions.GetRenderers()) { Owner = Window.GetWindow(this) };
            dialog.ShowDialog();
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
            var dialog = new WorkflowNodeEditorWindow(model, NodeEditorExtensions.GetRenderers())
            {
                Owner = Window.GetWindow(this)
            };
            _ = dialog.ShowDialog();
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

    /// <summary>更新路径文字和运行按钮（导航到子画布时运行当前子流程）。</summary>
    private void UpdateNavigation()
    {
        var path = Navigator is null
            ? "Root"
            : string.Join("  /  ", Navigator.Breadcrumbs.Select(item => item.Title));
        _canvasWorkspace.BreadcrumbText = Workspace?.IsDirty == true ? path + " *" : path;
        _runCommands?.Refresh();
    }

    /// <summary>创建并冻结指定 RGB 颜色的 WPF 画刷。</summary>
    private static SolidColorBrush Brush(byte red, byte green, byte blue)
    {
        var brush = new SolidColorBrush(Color.FromRgb(red, green, blue));
        brush.Freeze();
        return brush;
    }
}
