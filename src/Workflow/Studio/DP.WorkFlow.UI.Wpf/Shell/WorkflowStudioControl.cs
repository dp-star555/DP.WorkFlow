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

    private readonly Button _undoButton;
    private readonly Button _redoButton;
    private readonly TextBlock _breadcrumb;
    private readonly Button _runButton;
    private readonly Button _pauseButton;
    private readonly Button _resumeButton;
    private readonly Button _stopButton;
    private readonly TextBlock _runtimeLabel;
    private WorkflowDesignerSession? _subscribedSession;
    private WorkflowStudioRuntimeBinding? _runtimeBinding;
    private WorkflowDocumentWorkspace? _workspace;
    private WorkflowDesignerNavigator? _navigator;
    private StackPanel? _toolbar;
    private int _toolWindowCount;
    private bool _autoFitPending;
    private bool _autoFitQueued;

    /// <summary>初始化集设计器、工具箱、诊断和运行控制于一体的工作室控件。</summary>
    public WorkflowStudioControl()
    {
        Background = Brush(30, 30, 30);
        Designer = new WorkflowDesignerControl();
        Toolbox = new WorkflowToolboxControl();
        Properties = new WorkflowPropertyPanel();
        Diagnostics = new WorkflowDiagnosticsControl();
        RuntimeMonitor = new WorkflowRuntimeMonitorControl();

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(42) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(150) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(5) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var toolbar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Background = Brush(37, 37, 38)
        };
        _toolbar = toolbar;
        var newButton = CommandButton("新建");
        var openButton = CommandButton("打开");
        var saveButton = CommandButton("保存");
        _undoButton = CommandButton("撤销");
        _redoButton = CommandButton("重做");
        _breadcrumb = new TextBlock
        {
            Text = "Root",
            Foreground = Brush(148, 163, 184),
            Margin = new Thickness(10, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        var fitButton = CommandButton("适合画布");
        var autoLayoutButton = CommandButton("自动布局");
        var alignLeftButton = CommandButton("左对齐");
        var alignTopButton = CommandButton("顶对齐");
        _runButton = CommandButton("运行");
        _pauseButton = CommandButton("暂停");
        _resumeButton = CommandButton("继续");
        _stopButton = CommandButton("停止");
        _runtimeLabel = new TextBlock
        {
            Text = "Idle",
            Foreground = Brush(148, 163, 184),
            Margin = new Thickness(8, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        toolbar.Children.Add(newButton);
        toolbar.Children.Add(openButton);
        toolbar.Children.Add(saveButton);
        toolbar.Children.Add(_undoButton);
        toolbar.Children.Add(_redoButton);
        toolbar.Children.Add(_breadcrumb);
        toolbar.Children.Add(fitButton);
        toolbar.Children.Add(autoLayoutButton);
        toolbar.Children.Add(alignLeftButton);
        toolbar.Children.Add(alignTopButton);
        toolbar.Children.Add(_runButton);
        toolbar.Children.Add(_pauseButton);
        toolbar.Children.Add(_resumeButton);
        toolbar.Children.Add(_stopButton);
        toolbar.Children.Add(_runtimeLabel);
        Grid.SetColumnSpan(toolbar, 3);
        root.Children.Add(toolbar);

        AddToGrid(root, Toolbox, 0, 2);
        AddToGrid(root, new Border { Background = Brush(30, 41, 59) }, 1, 2);
        AddToGrid(root, Designer, 2);
        _bottomTabs.Items.Add(new TabItem { Header = "诊断", Content = Diagnostics });
        _bottomTabs.Items.Add(new TabItem { Header = "运行监视", Content = RuntimeMonitor });
        AddToGrid(root, _bottomTabs, 2, row: 2);
        Content = root;

        newButton.Click += (_, _) => NewDocument();
        openButton.Click += (_, _) => OpenDocument();
        saveButton.Click += (_, _) => SaveDocument();
        _undoButton.Click += (_, _) => Session?.Undo();
        _redoButton.Click += (_, _) => Session?.Redo();
        _runButton.Click += async (_, _) => await RunWorkflowAsync();
        _pauseButton.Click += (_, _) => RuntimeBinding?.Pause();
        _resumeButton.Click += (_, _) => RuntimeBinding?.Resume();
        _stopButton.Click += (_, _) => RuntimeBinding?.Cancel();
        autoLayoutButton.Click += (_, _) => Session?.AutoLayout();
        alignLeftButton.Click += (_, _) => Session?.AlignSelectedNodes(WorkflowNodeAlignment.Left);
        alignTopButton.Click += (_, _) => Session?.AlignSelectedNodes(WorkflowNodeAlignment.Top);
        fitButton.Click += (_, _) => Session?.FitToView(Designer.ActualWidth, Designer.ActualHeight);
        Toolbox.NodeTypeActivated += OnNodeTypeActivated;
        Designer.InteractionError += (_, message) => InteractionError?.Invoke(this, message);
        Designer.NodeEditRequested += OnDesignerNodeEditRequested;
        Properties.EditError += (_, message) => InteractionError?.Invoke(this, message);
        Properties.BlockMappingEditRequested += OnBlockMappingEditRequested;
        Properties.PropertyActionRequested += OnPropertyActionRequested;
        Designer.SizeChanged += (_, _) => QueueAutoFit();
        Unloaded += (_, _) =>
        {
            SubscribeSession(null);
            if (_navigator is not null)
                _navigator.CurrentChanged -= OnNavigatorChanged;
            if (_runtimeBinding is not null)
                _runtimeBinding.StateChanged -= OnRuntimeStateChanged;
            if (_workspace is not null)
            {
                _workspace.DocumentChanged -= OnWorkspaceChanged;
                _workspace.DirtyStateChanged -= OnWorkspaceChanged;
            }
        };
        Loaded += (_, _) =>
        {
            QueueAutoFit();
            SubscribeSession(Session);
            if (_navigator is not null)
            {
                _navigator.CurrentChanged -= OnNavigatorChanged;
                _navigator.CurrentChanged += OnNavigatorChanged;
            }
            if (_runtimeBinding is not null)
            {
                _runtimeBinding.StateChanged -= OnRuntimeStateChanged;
                _runtimeBinding.StateChanged += OnRuntimeStateChanged;
            }
            if (_workspace is not null)
            {
                _workspace.DocumentChanged -= OnWorkspaceChanged;
                _workspace.DirtyStateChanged -= OnWorkspaceChanged;
                _workspace.DocumentChanged += OnWorkspaceChanged;
                _workspace.DirtyStateChanged += OnWorkspaceChanged;
            }
        };
    }

    /// <summary>获取或设置 Designer 成员。</summary>
    public WorkflowDesignerControl Designer { get; }

    /// <summary>获取或设置 Toolbox 成员。</summary>
    public WorkflowToolboxControl Toolbox { get; }

    /// <summary>获取或设置 Properties 成员。</summary>
    public WorkflowPropertyPanel Properties { get; }

    /// <summary>获取或设置 Diagnostics 成员。</summary>
    public WorkflowDiagnosticsControl Diagnostics { get; }
    /// <summary>为领域工具添加弹出窗口入口：工具栏“保存”之后出现同名按钮，点击打开；关闭窗口不会丢失面板状态。</summary>
    /// <param name="title">按钮与窗口标题。</param>
    /// <param name="content">窗口内容。</param>
    public void AddToolWindow(string title, UIElement content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(content);
        Window? window = null;
        var button = CommandButton(title + "…");
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
        _toolbar!.Children.Insert(3 + _toolWindowCount++, button);
    }

    /// <summary>获取或设置 Runtime Monitor 成员。</summary>
    public WorkflowRuntimeMonitorControl RuntimeMonitor { get; }

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

    private readonly TabControl _bottomTabs = new();

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
        var session = (WorkflowDesignerSession?)e.NewValue;
        control.Designer.Session = session;
        control.Toolbox.Session = session;
        control.Diagnostics.Session = session;
        control.SubscribeSession(session);
        control._autoFitPending = session is not null;
        control.QueueAutoFit();
        control.UpdateCommands();
    }

    private static void OnEntryNodeIdChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        var control = (WorkflowStudioControl)dependencyObject;
        control.Diagnostics.EntryNodeId = (string?)e.NewValue;
    }

    // 在 Loaded 和布局完成后消费一次请求，避免用初始化时的零尺寸计算视口。
    private void QueueAutoFit()
    {
        if (!_autoFitPending || _autoFitQueued || !IsLoaded)
            return;
        _autoFitQueued = true;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, (Action)(() =>
        {
            _autoFitQueued = false;
            if (!IsLoaded || !_autoFitPending || Session is null
                || Designer.ActualWidth <= 0 || Designer.ActualHeight <= 0)
                return;
            _autoFitPending = false;
            Session.FitToView(Designer.ActualWidth, Designer.ActualHeight);
        }));
    }

    /// <summary>执行 Subscribe Session 相关处理。</summary>
    /// <param name="session">设计器会话。</param>
    private void SubscribeSession(WorkflowDesignerSession? session)
    {
        if (ReferenceEquals(_subscribedSession, session))
            return;
        if (_subscribedSession is not null)
            _subscribedSession.Changed -= OnDesignerSessionChanged;
        _subscribedSession = session;
        if (_subscribedSession is not null)
            _subscribedSession.Changed += OnDesignerSessionChanged;
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

    private void OnBlockMappingEditRequested(object? sender, IWorkflowBlockMappingNode block)
    {
        if (Session is null || string.IsNullOrWhiteSpace(EntryNodeId))
            return;
        using var model = new WorkflowBlockMappingEditorModel(Session, block.Id);
        var window = new WorkflowBlockMappingEditorWindow(model, EntryNodeId)
        {
            Owner = Window.GetWindow(this)
        };
        _ = window.ShowDialog();
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
            var dialog = new WorkflowNodeEditorWindow(
                model, NodeEditorExtensions.GetRenderers(),
                block => OnBlockMappingEditRequested(this, block))
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
            {
                // 运行失败只记录：失败节点标红、故障写入运行监视轨迹，切到运行监视页查看，不弹窗。
                _bottomTabs.SelectedIndex = 1;
                RunFaulted?.Invoke(this, $"流程运行失败：{result.Message}");
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or ObjectDisposedException)
        {
            InteractionError?.Invoke(this, exception.Message);
        }
    }

    private void OnRuntimeStateChanged(object? sender, EventArgs e)
    {
        if (Dispatcher.CheckAccess())
            UpdateRuntimeCommands();
        else
            _ = Dispatcher.BeginInvoke(UpdateRuntimeCommands);
    }

    /// <summary>处理设计会话变化并请求 WPF 画布重绘。</summary>
    private void OnDesignerSessionChanged(object? sender, WorkflowDesignerChangedEventArgs e)
    {
        if (Dispatcher.CheckAccess())
            UpdateCommands();
        else
            _ = Dispatcher.BeginInvoke(UpdateCommands);
    }

    /// <summary>更新Commands。</summary>
    private void UpdateCommands()
    {
        _undoButton.IsEnabled = Session?.CanUndo == true;
        _redoButton.IsEnabled = Session?.CanRedo == true;
        UpdateNavigation();
        UpdateRuntimeCommands();
    }

    /// <summary>更新Runtime Commands。</summary>
    private void UpdateRuntimeCommands()
    {
        var state = RuntimeBinding?.State ?? E_WorkflowExecutionState.Idle;
        _runButton.IsEnabled = RuntimeBinding is not null && state is not (E_WorkflowExecutionState.Running or E_WorkflowExecutionState.Paused);
        _pauseButton.IsEnabled = state == E_WorkflowExecutionState.Running;
        _resumeButton.IsEnabled = state == E_WorkflowExecutionState.Paused;
        _stopButton.IsEnabled = state is E_WorkflowExecutionState.Running or E_WorkflowExecutionState.Paused;
        _runtimeLabel.Text = state.ToString();
    }

    /// <summary>更新Navigation。</summary>
    private void UpdateNavigation()
    {
        var path = Navigator is null
            ? "Root"
            : string.Join("  /  ", Navigator.Breadcrumbs.Select(item => item.Title));
        _breadcrumb.Text = Workspace?.IsDirty == true ? path + " *" : path;
        _runButton.Content = Navigator?.Depth > 0 ? "运行当前子流程" : "运行";
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
            Designer.ActualWidth / 2,
            Designer.ActualHeight / 2);
        Session.AddNode(item.NodeType, center.X - 90, center.Y - 30);
    }

    /// <summary>添加To Grid。</summary>
    private static void AddToGrid(
        Grid grid,
        UIElement element,
        int column,
        int rowSpan = 1,
        int row = 1)
    {
        Grid.SetRow(element, row);
        Grid.SetRowSpan(element, rowSpan);
        Grid.SetColumn(element, column);
        grid.Children.Add(element);
    }

    private static Button CommandButton(string text) => new()
    {
        Content = text,
        Foreground = Brush(241, 241, 241),
        Background = Brush(51, 51, 55),
        BorderBrush = Brush(63, 63, 70),
        Padding = new Thickness(14, 5, 14, 5),
        Margin = new Thickness(4)
    };

    /// <summary>创建并冻结指定 RGB 颜色的 WPF 画刷。</summary>
    private static SolidColorBrush Brush(byte red, byte green, byte blue)
    {
        var brush = new SolidColorBrush(Color.FromRgb(red, green, blue));
        brush.Freeze();
        return brush;
    }
}
