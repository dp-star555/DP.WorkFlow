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
    // 运行数据变化只置脏标记，由定时器合并刷新：快速循环时每秒可能有上千次变化，逐次重设表格会占满界面线程。
    private readonly System.Windows.Threading.DispatcherTimer _refreshTimer;
    private bool _runtimeDirty;
    private bool _applyingRuntimeData;
    private bool _traceReset = true;

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
        _refreshTimer = new System.Windows.Threading.DispatcherTimer(
            TimeSpan.FromMilliseconds(250), System.Windows.Threading.DispatcherPriority.Background, (_, _) => FlushRuntimeData(), Dispatcher);
        _refreshTimer.Stop();
        _model.Changed += (_, _) => { if (!_applyingRuntimeData) RefreshLists(); };
        IsVisibleChanged += (_, _) => { if (IsVisible) FlushRuntimeData(); };
        Unloaded += (_, _) =>
        {
            _refreshTimer.Stop();
            Subscribe(null);
        };
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
            _traceReset = true;
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
            MarkRuntimeDirty();
        else
            _ = Dispatcher.BeginInvoke(MarkRuntimeDirty);
    }

    /// <summary>记录运行数据已变化；定时器到期时合并为一次刷新。</summary>
    private void MarkRuntimeDirty()
    {
        _runtimeDirty = true;
        if (!_refreshTimer.IsEnabled) _refreshTimer.Start();
    }

    /// <summary>把累计的运行数据变化一次性应用到界面；控件不可见时推迟到重新显示。</summary>
    private void FlushRuntimeData()
    {
        _refreshTimer.Stop();
        if (!_runtimeDirty || !IsVisible) return;
        RefreshRuntimeData();
    }

    /// <summary>刷新Lists；数据未变的表格不重设数据源（重设会重建自动生成的列和全部行）。</summary>
    private void RefreshLists()
    {
        static void Set(DataGrid grid, System.Collections.IEnumerable items)
        {
            if (!ReferenceEquals(grid.ItemsSource, items)) grid.ItemsSource = items;
        }
        Set(_tokens, _model.Tokens);
        Set(_scopes, _model.ParallelScopes);
        Set(_children, _model.ChildWorkflows);
        Set(_trace, _model.TraceEntries);
        Set(_output, _model.OutputEntries);
        Set(_timing, _model.TimingTrends);
    }

    /// <summary>刷新Runtime Data。</summary>
    private void RefreshRuntimeData()
    {
        _runtimeDirty = false;
        // 快照和轨迹各自触发一次模型变化，合并成一次表格刷新。
        _applyingRuntimeData = true;
        try
        {
            _model.SetSnapshot(_source?.GetSnapshot());
            // 换了来源后整窗读取一次，此后只按游标读取新条目。
            if (_traceReset) _model.SetTraceBatch(_source?.GetTraceBatch());
            else _model.AppendTraceBatch(_source?.GetTraceBatch(_model.TraceCursor));
            _traceReset = false;
        }
        finally { _applyingRuntimeData = false; }
        RefreshLists();
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
