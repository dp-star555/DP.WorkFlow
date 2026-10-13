using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DP.WorkFlow.UI;

namespace DP.WorkFlow.UI.Wpf;

/// <summary>WPF Token、ParallelScope 和 ChildWorkflow 运行监视器。</summary>
public sealed class WorkflowRuntimeMonitorControl : UserControl
{
    private readonly WorkflowRuntimeMonitorModel _model = new();
    private readonly DataGrid _tokens = CreateGrid();
    private readonly DataGrid _scopes = CreateGrid();
    private readonly DataGrid _children = CreateGrid();
    private readonly DataGrid _trace = CreateGrid();
    private readonly DataGrid _output = CreateGrid();
    private readonly DataGrid _timing = CreateGrid();
    private WorkflowStudioRuntimeBinding? _runtimeBinding;
    private IWorkflowRuntimeMonitorSource? _source;
    private IWorkflowRuntimeMonitorSource? _subscribed;

    /// <summary>初始化工作流运行时监视控件。</summary>
    public WorkflowRuntimeMonitorControl()
    {
        var tabs = new TabControl
        {
            Background = Brush(15, 23, 42),
            Foreground = Brush(226, 232, 240)
        };
        tabs.Items.Add(new TabItem { Header = "Tokens", Content = _tokens });
        tabs.Items.Add(new TabItem { Header = "Parallel", Content = _scopes });
        tabs.Items.Add(new TabItem { Header = "Children", Content = _children });
        tabs.Items.Add(new TabItem { Header = "Trace", Content = CreateTracePanel() });
        tabs.Items.Add(new TabItem { Header = "输出", Content = _output });
        tabs.Items.Add(new TabItem { Header = "Timing", Content = _timing });
        Content = tabs;
        _model.Changed += (_, _) => RefreshLists();
        Unloaded += (_, _) => Subscribe(null);
        Loaded += (_, _) =>
        {
            Subscribe(_source);
            RefreshRuntimeData();
        };
    }

    public WorkflowStudioRuntimeBinding? RuntimeBinding
    {
        get => _runtimeBinding;
        set
        {
            if (ReferenceEquals(_runtimeBinding, value))
                return;
            _runtimeBinding = value;
            Source = value;
        }
    }

    /// <summary>
    /// 监视的数据来源：主画布为整个运行（<see cref="RuntimeBinding"/>），
    /// 节点窗口的子画布为该复合节点的子运行（<see cref="WorkflowSessionRuntimeMonitorSource"/>）。
    /// </summary>
    public IWorkflowRuntimeMonitorSource? Source
    {
        get => _source;
        set
        {
            if (ReferenceEquals(_source, value))
                return;
            _source = value;
            Subscribe(value);
            RefreshRuntimeData();
        }
    }

    private void Subscribe(IWorkflowRuntimeMonitorSource? source)
    {
        if (ReferenceEquals(_subscribed, source))
            return;
        if (_subscribed is not null)
            _subscribed.Changed -= OnRuntimeChanged;
        _subscribed = source;
        if (_subscribed is not null)
            _subscribed.Changed += OnRuntimeChanged;
    }

    private void OnRuntimeChanged(object? sender, EventArgs e)
    {
        if (Dispatcher.CheckAccess())
            RefreshRuntimeData();
        else
            _ = Dispatcher.BeginInvoke(RefreshRuntimeData);
    }

    /// <summary>刷新Lists。</summary>
    private void RefreshLists()
    {
        _tokens.ItemsSource = _model.Tokens;
        _scopes.ItemsSource = _model.ParallelScopes;
        _children.ItemsSource = _model.ChildWorkflows;
        _trace.ItemsSource = _model.TraceEntries;
        _output.ItemsSource = _model.OutputEntries;
        _timing.ItemsSource = _model.TimingTrends;
    }

    /// <summary>刷新Runtime Data。</summary>
    private void RefreshRuntimeData()
    {
        _model.SetSnapshot(_source?.GetSnapshot());
        _model.SetTraceBatch(_source?.GetTraceBatch());
    }

    /// <summary>创建Trace Panel。</summary>
    private FrameworkElement CreateTracePanel()
    {
        var filter = new TextBox
        {
            Width = 300,
            Margin = new Thickness(4),
            ToolTip = "筛选节点、Token、Scope、步骤或消息"
        };
        var pause = new CheckBox { Content = "暂停滚动", Margin = new Thickness(8, 7, 4, 4), Foreground = Foreground };
        // 导出改为轨迹表格右键菜单。
        var export = new MenuItem { Header = "导出 CSV" };
        filter.TextChanged += (_, _) => _model.SetTraceFilter(filter.Text);
        pause.Checked += (_, _) => _model.SetTracePaused(true);
        pause.Unchecked += (_, _) => _model.SetTracePaused(false);
        export.Click += (_, _) =>
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "CSV 文件 (*.csv)|*.csv|所有文件 (*.*)|*.*",
                FileName = $"workflow-trace-{DateTime.Now:yyyyMMdd-HHmmss}.csv"
            };
            if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
            using var writer = new StreamWriter(dialog.FileName, false, new System.Text.UTF8Encoding(true));
            _model.ExportTraceCsv(writer);
        };
        _trace.ContextMenu = new ContextMenu { Items = { export } };
        var toolbar = new StackPanel { Orientation = Orientation.Horizontal };
        toolbar.Children.Add(filter);
        toolbar.Children.Add(pause);
        var panel = new DockPanel();
        DockPanel.SetDock(toolbar, Dock.Top);
        panel.Children.Add(toolbar);
        panel.Children.Add(_trace);
        return panel;
    }

    private static DataGrid CreateGrid() => new()
    {
        AutoGenerateColumns = true,
        IsReadOnly = true,
        CanUserAddRows = false,
        HeadersVisibility = DataGridHeadersVisibility.Column,
        GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
        Background = Brush(15, 23, 42),
        Foreground = Brush(226, 232, 240),
        RowBackground = Brush(22, 32, 49),
        AlternatingRowBackground = Brush(30, 41, 59),
        BorderThickness = new Thickness(0)
    };

    /// <summary>创建并冻结指定 RGB 颜色的 WPF 画刷。</summary>
    private static SolidColorBrush Brush(byte red, byte green, byte blue)
    {
        var brush = new SolidColorBrush(Color.FromRgb(red, green, blue));
        brush.Freeze();
        return brush;
    }
}
