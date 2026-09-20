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
        Loaded += (_, _) => Subscribe(_runtimeBinding);
    }

    public WorkflowStudioRuntimeBinding? RuntimeBinding
    {
        get => _runtimeBinding;
        set
        {
            if (ReferenceEquals(_runtimeBinding, value))
                return;
            Subscribe(null);
            _runtimeBinding = value;
            Subscribe(value);
            RefreshRuntimeData();
        }
    }

    /// <summary>执行 Subscribe 相关处理。</summary>
    /// <param name="binding">“binding”参数。</param>
    private void Subscribe(WorkflowStudioRuntimeBinding? binding)
    {
        if (_runtimeBinding is not null)
            _runtimeBinding.StateChanged -= OnRuntimeChanged;
        if (binding is not null)
            binding.StateChanged += OnRuntimeChanged;
    }

    /// <summary>处理“Runtime Changed”事件。</summary>
    /// <param name="sender">事件发送者。</param>
    /// <param name="e">事件参数。</param>
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
        _model.SetSnapshot(_runtimeBinding?.Host.GetSnapshot());
        _model.SetTraceBatch(_runtimeBinding?.Host.Engine?.GetTraceBatch());
    }

    /// <summary>创建Trace Panel。</summary>
    /// <returns>返回处理结果。</returns>
    private FrameworkElement CreateTracePanel()
    {
        var filter = new TextBox
        {
            Width = 300,
            Margin = new Thickness(4),
            ToolTip = "筛选节点、Token、Scope、步骤或消息"
        };
        var pause = new CheckBox { Content = "暂停滚动", Margin = new Thickness(8, 7, 4, 4), Foreground = Foreground };
        var export = new Button { Content = "导出 CSV", Margin = new Thickness(4), Padding = new Thickness(10, 3, 10, 3) };
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
        var toolbar = new StackPanel { Orientation = Orientation.Horizontal };
        toolbar.Children.Add(filter);
        toolbar.Children.Add(pause);
        toolbar.Children.Add(export);
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
    /// <param name="red">“red”参数。</param>
    /// <param name="green">“green”参数。</param>
    /// <param name="blue">“blue”参数。</param>
    /// <returns>返回处理结果。</returns>
    private static SolidColorBrush Brush(byte red, byte green, byte blue)
    {
        var brush = new SolidColorBrush(Color.FromRgb(red, green, blue));
        brush.Freeze();
        return brush;
    }
}
