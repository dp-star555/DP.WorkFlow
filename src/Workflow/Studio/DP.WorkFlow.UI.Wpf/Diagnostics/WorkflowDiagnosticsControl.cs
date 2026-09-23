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
        _list.MouseDoubleClick += OnDoubleClick;
        Content = _list;
        Loaded += (_, _) => RecreateModel();
        Unloaded += (_, _) => DisposeModel();
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

    private static void OnConfigurationChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e) =>
        ((WorkflowDiagnosticsControl)dependencyObject).RecreateModel();

    /// <summary>执行 Recreate Model 相关处理。</summary>
    private void RecreateModel()
    {
        DisposeModel();
        if (Session is not null && !string.IsNullOrWhiteSpace(EntryNodeId))
        {
            _model = new WorkflowDiagnosticsModel(Session, EntryNodeId);
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

    private void OnModelChanged(object? sender, EventArgs e)
    {
        if (Dispatcher.CheckAccess())
            RefreshItems();
        else
            _ = Dispatcher.BeginInvoke(RefreshItems);
    }

    /// <summary>刷新Items。</summary>
    private void RefreshItems() => _list.ItemsSource = _model?.Items;

    private void OnDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_list.SelectedItem is WorkflowDiagnosticItem item)
            _model?.NavigateTo(item);
    }

    /// <summary>创建并冻结指定 RGB 颜色的 WPF 画刷。</summary>
    private static SolidColorBrush Brush(byte red, byte green, byte blue)
    {
        var brush = new SolidColorBrush(Color.FromRgb(red, green, blue));
        brush.Freeze();
        return brush;
    }
}
