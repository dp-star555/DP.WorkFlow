using System.IO;
using System.Windows;
using System.Windows.Controls;
using DP.WorkFlow.UI;
using Microsoft.Win32;

namespace DP.WorkFlow.Vision.UI.Wpf;

/// <summary>插件清单与可取消的配方资源检查。</summary>
public sealed class WorkflowVisionAlgorithmPanel : UserControl
{
    private readonly WorkflowVisionAlgorithmDiagnostics _diagnostics;
    private readonly Func<WorkflowDocument?> _document;
    private readonly Func<WorkflowDesignerSession?> _session;
    private readonly TextBlock _status = new() { Margin = new Thickness(4) };
    private readonly Button _check = new() { Content = "检查配方资源", Margin = new Thickness(3) };
    private readonly Button _cancel = new() { Content = "取消检查", Margin = new Thickness(3), IsEnabled = false };
    private CancellationTokenSource? _cancellation;
    /// <summary>宿主提供根配方及当前选中节点所在会话。</summary>
    public WorkflowVisionAlgorithmPanel(WorkflowVisionAlgorithmDiagnostics diagnostics, Func<WorkflowDocument?> document,
        Func<WorkflowDesignerSession?> session)
    {
        _diagnostics = diagnostics; _document = document; _session = session;
        var root = new DockPanel();
        var toolbar = new StackPanel { Orientation = Orientation.Horizontal };
        var migrate = new Button { Content = "升级所选节点配置", Margin = new Thickness(3) };
        var export = new Button { Content = "导出复核报告", Margin = new Thickness(3) };
        toolbar.Children.Add(_check); toolbar.Children.Add(_cancel); toolbar.Children.Add(migrate); toolbar.Children.Add(export);
        DockPanel.SetDock(toolbar, Dock.Top); root.Children.Add(toolbar);
        DockPanel.SetDock(_status, Dock.Bottom); root.Children.Add(_status);
        var grid = new DataGrid { IsReadOnly = true, AutoGenerateColumns = false, CanUserAddRows = false, CanUserDeleteRows = false };
        foreach (var column in new[] { ("ImplementationId", "实现"), ("Engine", "引擎"), ("Capability", "能力"), ("Version", "版本"), ("Features", "特征"), ("Source", "来源") })
            grid.Columns.Add(new DataGridTextColumn { Header = column.Item2, Binding = new System.Windows.Data.Binding(column.Item1), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        grid.ItemsSource = diagnostics.Catalog.Implementations.Select(d => new
        {
            d.ImplementationId, d.Engine, Capability = d.DisplayName, d.Version, Features = string.Join(", ", d.Features),
            Source = diagnostics.Catalog.Origins.TryGetValue(d.ImplementationId, out var origin) ? origin.AssemblyPath : ""
        }).ToArray();
        root.Children.Add(grid); Content = root;
        _check.Click += async (_, _) => await CheckAsync();
        _cancel.Click += (_, _) => _cancellation?.Cancel();
        migrate.Click += (_, _) => Perform(() =>
        {
            var current = _session() ?? throw new InvalidOperationException("没有打开的配方。");
            _diagnostics.MigrateNode(current, current.SelectedNodeId ?? throw new InvalidOperationException("请先选择算法节点。"));
        });
        export.Click += (_, _) => Perform(() =>
        {
            var current = _document() ?? throw new InvalidOperationException("没有打开的配方。");
            var dialog = new SaveFileDialog { Filter = "JSON 报告|*.json", FileName = "algorithm-review.json" };
            if (dialog.ShowDialog() == true) File.WriteAllText(dialog.FileName, _diagnostics.ExportReport(current));
        });
        Loaded += (_, _) => { diagnostics.Changed -= OnChanged; diagnostics.Changed += OnChanged; _status.Text = diagnostics.Status; };
        Unloaded += (_, _) => { diagnostics.Changed -= OnChanged; _cancellation?.Cancel(); };
        _status.Text = diagnostics.Status;
    }
    private void Perform(Action action)
    { try { action(); } catch (Exception error) { MessageBox.Show(error.Message, "算法配置", MessageBoxButton.OK, MessageBoxImage.Error); } }
    private async Task CheckAsync()
    {
        var document = _document(); if (document == null || _cancellation != null) return;
        using var cancellation = new CancellationTokenSource(); _cancellation = cancellation;
        _check.IsEnabled = false; _cancel.IsEnabled = true;
        try { await _diagnostics.CheckAsync(document, cancellation.Token); }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (IsLoaded) MessageBox.Show(error.Message, "资源检查失败", MessageBoxButton.OK, MessageBoxImage.Error); }
        finally { _cancellation = null; _check.IsEnabled = true; _cancel.IsEnabled = false; }
    }
    private void OnChanged(object? sender, EventArgs e)
    { if (Dispatcher.CheckAccess()) _status.Text = _diagnostics.Status; else _ = Dispatcher.BeginInvoke(() => OnChanged(sender, e)); }
}
