using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DP.WorkFlow.UI;

namespace DP.WorkFlow.UI.Wpf;

/// <summary>
/// 画布工作区：工具栏（撤销、重做、适合画布、布局、运行/暂停/继续/停止）、工具箱、画布，以及底部“诊断 | 运行监视”。
/// 工作台主画布和节点窗口的子画布共用这一个控件；两者的差别只在运行命令（<see cref="RunCommands"/>）、
/// 监视来源（<see cref="WorkflowRuntimeMonitorControl.Source"/>）和宿主在工具栏前端加入的按钮（例如新建、打开、保存）。
/// </summary>
public sealed class WorkflowCanvasWorkspaceControl : UserControl
{
    private readonly StackPanel _toolbar;
    private readonly Button _undoButton;
    private readonly Button _redoButton;
    private readonly TextBlock _breadcrumb;
    private readonly Button _runButton;
    private readonly Button _pauseButton;
    private readonly Button _resumeButton;
    private readonly Button _stopButton;
    private readonly TextBlock _runtimeLabel;
    private readonly TabControl _bottomTabs = new();
    private WorkflowDesignerSession? _session;
    private WorkflowDesignerSession? _subscribedSession;
    private IWorkflowCanvasRunCommands? _runCommands;
    private bool _autoFitPending;
    private bool _autoFitQueued;

    /// <summary>创建画布工作区。</summary>
    public WorkflowCanvasWorkspaceControl()
    {
        Background = Brush(30, 30, 30);
        Designer = new WorkflowDesignerControl();
        Toolbox = new WorkflowToolboxControl();
        Diagnostics = new WorkflowDiagnosticsControl();
        RuntimeMonitor = new WorkflowRuntimeMonitorControl();

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(42) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(150) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(5) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        _toolbar = new StackPanel { Orientation = Orientation.Horizontal, Background = Brush(37, 37, 38) };
        _undoButton = CommandButton("撤销");
        _redoButton = CommandButton("重做");
        _breadcrumb = new TextBlock
        {
            Foreground = Brush(148, 163, 184),
            Margin = new Thickness(10, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed
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
        foreach (var item in new UIElement[]
                 {
                     _undoButton, _redoButton, _breadcrumb, fitButton, autoLayoutButton, alignLeftButton, alignTopButton,
                     _runButton, _pauseButton, _resumeButton, _stopButton, _runtimeLabel
                 })
            _toolbar.Children.Add(item);
        Grid.SetColumnSpan(_toolbar, 3);
        root.Children.Add(_toolbar);

        AddToGrid(root, Toolbox, 0, 2);
        AddToGrid(root, new Border { Background = Brush(30, 41, 59) }, 1, 2);
        AddToGrid(root, Designer, 2);
        _bottomTabs.Items.Add(new TabItem { Header = "诊断", Content = Diagnostics });
        _bottomTabs.Items.Add(new TabItem { Header = "运行监视", Content = RuntimeMonitor });
        AddToGrid(root, _bottomTabs, 2, row: 2);
        Content = root;

        _undoButton.Click += (_, _) => Session?.Undo();
        _redoButton.Click += (_, _) => Session?.Redo();
        _runButton.Click += async (_, _) => await RunAsync();
        _pauseButton.Click += (_, _) => RunCommands?.Pause();
        _resumeButton.Click += (_, _) => RunCommands?.Resume();
        _stopButton.Click += (_, _) => RunCommands?.Cancel();
        autoLayoutButton.Click += (_, _) => Session?.AutoLayout();
        alignLeftButton.Click += (_, _) => Session?.AlignSelectedNodes(WorkflowNodeAlignment.Left);
        alignTopButton.Click += (_, _) => Session?.AlignSelectedNodes(WorkflowNodeAlignment.Top);
        fitButton.Click += (_, _) => Session?.FitToView(Designer.ActualWidth, Designer.ActualHeight);
        Toolbox.NodeTypeActivated += OnNodeTypeActivated;
        Designer.InteractionError += (_, message) => InteractionError?.Invoke(this, message);
        Designer.SizeChanged += (_, _) => QueueAutoFit();
        Unloaded += (_, _) =>
        {
            SubscribeSession(null);
            if (_runCommands is not null) _runCommands.Changed -= OnRunCommandsChanged;
        };
        Loaded += (_, _) =>
        {
            QueueAutoFit();
            SubscribeSession(_session);
            if (_runCommands is not null)
            {
                _runCommands.Changed -= OnRunCommandsChanged;
                _runCommands.Changed += OnRunCommandsChanged;
            }
            UpdateCommands();
        };
        UpdateCommands();
    }

    /// <summary>画布。</summary>
    public WorkflowDesignerControl Designer { get; }

    /// <summary>工具箱。</summary>
    public WorkflowToolboxControl Toolbox { get; }

    /// <summary>诊断面板。</summary>
    public WorkflowDiagnosticsControl Diagnostics { get; }

    /// <summary>运行监视。</summary>
    public WorkflowRuntimeMonitorControl RuntimeMonitor { get; }

    /// <summary>工具栏；宿主可在最前面插入自己的按钮（例如新建、打开、保存）。</summary>
    public StackPanel Toolbar => _toolbar;

    /// <summary>工具栏上的路径文字；为空时不显示路径。</summary>
    public string? BreadcrumbText
    {
        get => _breadcrumb.Visibility == Visibility.Visible ? _breadcrumb.Text : null;
        set
        {
            _breadcrumb.Text = value ?? string.Empty;
            _breadcrumb.Visibility = string.IsNullOrEmpty(value) ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    /// <summary>画布会话；同时用于工具箱和诊断。</summary>
    public WorkflowDesignerSession? Session
    {
        get => _session;
        set
        {
            if (ReferenceEquals(_session, value)) return;
            _session = value;
            Designer.Session = value;
            Toolbox.Session = value;
            Diagnostics.Session = value;
            SubscribeSession(value);
            _autoFitPending = value is not null;
            QueueAutoFit();
            UpdateCommands();
        }
    }

    /// <summary>运行命令；为空时运行按钮不可用。</summary>
    public IWorkflowCanvasRunCommands? RunCommands
    {
        get => _runCommands;
        set
        {
            if (ReferenceEquals(_runCommands, value)) return;
            if (_runCommands is not null) _runCommands.Changed -= OnRunCommandsChanged;
            _runCommands = value;
            if (_runCommands is not null) _runCommands.Changed += OnRunCommandsChanged;
            UpdateRuntimeCommands();
        }
    }

    /// <summary>画布交互或运行入口报错时发生。</summary>
    public event EventHandler<string>? InteractionError;

    /// <summary>运行以故障结束时发生（故障详情已写入运行监视）。</summary>
    public event EventHandler<string>? RunFaulted;

    /// <summary>运行前检查诊断，再执行运行命令；故障时切到运行监视。</summary>
    public async Task RunAsync()
    {
        if (RunCommands is not { } commands) return;
        Diagnostics.RefreshDiagnostics();
        if (!Diagnostics.CanRun)
        {
            InteractionError?.Invoke(this, "当前流程存在配置或编译错误，不能运行。");
            return;
        }
        try
        {
            var result = await commands.RunAsync();
            if (!result.Success && result.State == E_WorkflowExecutionState.Faulted)
            {
                // 运行失败只记录：失败节点标红、故障写入运行监视轨迹，切到运行监视查看，不弹窗。
                _bottomTabs.SelectedIndex = 1;
                RunFaulted?.Invoke(this, $"流程运行失败：{result.Message}");
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or ObjectDisposedException
                                          or NotSupportedException or WorkflowCompilationException)
        {
            InteractionError?.Invoke(this, exception.Message);
        }
    }

    /// <summary>切到运行监视页。</summary>
    public void ShowRuntimeMonitor() => _bottomTabs.SelectedIndex = 1;

    /// <summary>重新读取运行命令的可用性和提示，例如导航层级变化后。</summary>
    public void RefreshRunCommands() => UpdateRuntimeCommands();

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

    private void SubscribeSession(WorkflowDesignerSession? session)
    {
        if (ReferenceEquals(_subscribedSession, session)) return;
        if (_subscribedSession is not null) _subscribedSession.Changed -= OnSessionChanged;
        _subscribedSession = session;
        if (_subscribedSession is not null) _subscribedSession.Changed += OnSessionChanged;
    }

    private void OnSessionChanged(object? sender, WorkflowDesignerChangedEventArgs e) => Dispatch(UpdateCommands);

    private void OnRunCommandsChanged(object? sender, EventArgs e) => Dispatch(UpdateRuntimeCommands);

    private void Dispatch(Action action)
    {
        if (Dispatcher.CheckAccess()) action();
        else _ = Dispatcher.BeginInvoke(action);
    }

    private void UpdateCommands()
    {
        _undoButton.IsEnabled = Session?.CanUndo == true;
        _redoButton.IsEnabled = Session?.CanRedo == true;
        UpdateRuntimeCommands();
    }

    private void UpdateRuntimeCommands()
    {
        var commands = RunCommands;
        var state = commands?.State ?? E_WorkflowExecutionState.Idle;
        _runButton.IsEnabled = commands?.CanRun == true;
        _runButton.Content = commands?.RunText ?? "运行";
        // 不能运行时提示里说明原因。
        _runButton.ToolTip = commands?.RunToolTip ?? "运行";
        ToolTipService.SetShowOnDisabled(_runButton, true);
        _pauseButton.IsEnabled = state == E_WorkflowExecutionState.Running;
        _resumeButton.IsEnabled = state == E_WorkflowExecutionState.Paused;
        _stopButton.IsEnabled = state is E_WorkflowExecutionState.Running or E_WorkflowExecutionState.Paused;
        _runtimeLabel.Text = state.ToString();
    }

    private void OnNodeTypeActivated(object? sender, WorkflowToolboxItem item)
    {
        if (Session is null) return;
        var center = WorkflowDesignerGeometry.ScreenToCanvas(Session, Designer.ActualWidth / 2, Designer.ActualHeight / 2);
        Session.AddNode(item.NodeType, center.X - 90, center.Y - 30);
    }

    private static void AddToGrid(Grid grid, UIElement element, int column, int rowSpan = 1, int row = 1)
    {
        Grid.SetRow(element, row);
        Grid.SetRowSpan(element, rowSpan);
        Grid.SetColumn(element, column);
        grid.Children.Add(element);
    }

    /// <summary>与工作区工具栏一致的命令按钮。</summary>
    /// <param name="text">按钮文字。</param>
    public static Button CommandButton(string text) => new()
    {
        Content = text,
        Foreground = Brush(241, 241, 241),
        Background = Brush(51, 51, 55),
        BorderBrush = Brush(63, 63, 70),
        Padding = new Thickness(14, 5, 14, 5),
        Margin = new Thickness(4)
    };

    private static SolidColorBrush Brush(byte red, byte green, byte blue)
    {
        var brush = new SolidColorBrush(Color.FromRgb(red, green, blue));
        brush.Freeze();
        return brush;
    }
}
