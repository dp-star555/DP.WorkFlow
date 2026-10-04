using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DP.WorkFlow.UI;

namespace DP.WorkFlow.UI.Wpf;

/// <summary>WPF 编译诊断列表；双击定位问题节点。</summary>
public sealed class WorkflowDiagnosticsControl : UserControl
{
    public static readonly DependencyProperty SessionProperty = DependencyProperty.Register(
        nameof(Session), typeof(WorkflowDesignerSession), typeof(WorkflowDiagnosticsControl),
        new PropertyMetadata(null, OnConfigurationChanged));

    public static readonly DependencyProperty EntryNodeIdProperty = DependencyProperty.Register(
        nameof(EntryNodeId), typeof(string), typeof(WorkflowDiagnosticsControl),
        new PropertyMetadata(null, OnConfigurationChanged));

    private readonly ListView _list;
    private WorkflowDiagnosticsModel? _model;

    /// <summary>初始化工作流诊断列表控件。</summary>
    public WorkflowDiagnosticsControl()
    {
        _list = new ListView
        {
            Background = Brush(15, 23, 42),
            Foreground = Brush(226, 232, 240),
            BorderThickness = new Thickness(0)
        };
        var gridView = new GridView();
        gridView.Columns.Add(new GridViewColumn { Header = "级别", Width = 70, DisplayMemberBinding = new System.Windows.Data.Binding(nameof(WorkflowDiagnosticItem.Severity)) });
        gridView.Columns.Add(new GridViewColumn { Header = "代码", Width = 80, DisplayMemberBinding = new System.Windows.Data.Binding(nameof(WorkflowDiagnosticItem.Code)) });
        gridView.Columns.Add(new GridViewColumn { Header = "节点", Width = 120, DisplayMemberBinding = new System.Windows.Data.Binding(nameof(WorkflowDiagnosticItem.NodeId)) });
        gridView.Columns.Add(new GridViewColumn { Header = "消息", Width = 600, DisplayMemberBinding = new System.Windows.Data.Binding(nameof(WorkflowDiagnosticItem.Message)) });
        _list.View = gridView;
        var itemStyle = new Style(typeof(ListViewItem));
        itemStyle.Setters.Add(new Setter(ToolTipProperty, new System.Windows.Data.Binding(nameof(WorkflowDiagnosticItem.Detail))));
        _list.ItemContainerStyle = itemStyle;
        _list.MouseDoubleClick += OnDoubleClick;
        Content = _list;
        Loaded += (_, _) => { if (_provider != null) { _provider.Changed -= ProviderChanged; _provider.Changed += ProviderChanged; } RecreateModel(); };
        Unloaded += (_, _) => { if (_provider != null) _provider.Changed -= ProviderChanged; DisposeModel(); };
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

    public bool CanRun => _model?.CanRun == true;
    private IWorkflowDiagnosticProvider? _provider;
    private WorkflowDesignerNavigator? _navigator;
    /// <summary>领域检查及后台准备报告。</summary>
    public IWorkflowDiagnosticProvider? Provider
    {
        get => _provider;
        set { if (_provider != null) _provider.Changed -= ProviderChanged; _provider = value; if (_provider != null && IsLoaded) _provider.Changed += ProviderChanged; RecreateModel(); }
    }
    /// <summary>诊断定位所用导航器。</summary>
    public WorkflowDesignerNavigator? Navigator { get => _navigator; set { _navigator = value; RecreateModel(); } }
    /// <summary>运行前重新检查外部文件及选择。</summary>
    public void RefreshDiagnostics() => _model?.Refresh();
    private void ProviderChanged(object? sender, EventArgs e)
    { if (Dispatcher.CheckAccess()) RefreshDiagnostics(); else _ = Dispatcher.BeginInvoke(RefreshDiagnostics); }

    /// <summary>处理“Configuration Changed”事件。</summary>
    /// <param name="dependencyObject">“dependencyObject”参数。</param>
    /// <param name="e">事件参数。</param>
    private static void OnConfigurationChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e) =>
        ((WorkflowDiagnosticsControl)dependencyObject).RecreateModel();

    /// <summary>执行 Recreate Model 相关处理。</summary>
    private void RecreateModel()
    {
        DisposeModel();
        if (Session is not null && !string.IsNullOrWhiteSpace(EntryNodeId))
        {
            _model = new WorkflowDiagnosticsModel(Session, EntryNodeId, _provider, _navigator);
            _model.Changed += OnModelChanged;
        }
        RefreshItems();
    }

    /// <summary>执行 Dispose Model 相关处理。</summary>
    private void DisposeModel()
    {
        if (_model is null)
            return;
        _model.Changed -= OnModelChanged;
        _model.Dispose();
        _model = null;
    }

    /// <summary>处理“Model Changed”事件。</summary>
    /// <param name="sender">事件发送者。</param>
    /// <param name="e">事件参数。</param>
    private void OnModelChanged(object? sender, EventArgs e)
    {
        if (Dispatcher.CheckAccess())
            RefreshItems();
        else
            _ = Dispatcher.BeginInvoke(RefreshItems);
    }

    /// <summary>刷新Items。</summary>
    private void RefreshItems() => _list.ItemsSource = _model?.Items;

    /// <summary>处理“Double Click”事件。</summary>
    /// <param name="sender">事件发送者。</param>
    /// <param name="e">事件参数。</param>
    private void OnDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_list.SelectedItem is WorkflowDiagnosticItem item)
            _model?.NavigateTo(item);
    }

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
