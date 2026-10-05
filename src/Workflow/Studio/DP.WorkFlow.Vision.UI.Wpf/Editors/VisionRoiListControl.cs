using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using DP.Vision.UI;

namespace DP.WorkFlow.Vision.UI.Wpf;

/// <summary>节点窗口左侧“ROI列表”页：列出右侧画布正在编辑的 ROI，并提供选择、删除和用途切换。</summary>
internal sealed class VisionRoiListControl : DockPanel
{
    private readonly VisionRoiListModel _model;
    private readonly DataGrid _grid = new()
    {
        IsReadOnly = true,
        AutoGenerateColumns = false,
        CanUserAddRows = false,
        CanUserDeleteRows = false,
        SelectionMode = DataGridSelectionMode.Single,
        SelectionUnit = DataGridSelectionUnit.FullRow,
        HeadersVisibility = DataGridHeadersVisibility.Column
    };
    private readonly TextBlock _empty = new()
    {
        Text = "暂无 ROI\n在右侧图像上使用“绘制范围”等工具添加。",
        TextAlignment = TextAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center
    };
    private readonly Button _delete;
    private readonly Button? _include, _exclude, _toggle;
    private bool _syncing;

    public VisionRoiListControl(VisionRoiListModel model)
    {
        _model = model;
        Column("标识", nameof(VisionRoiListItem.Id), 0.8);
        Column("形状", nameof(VisionRoiListItem.Shape), 0.6);
        Column("用途", nameof(VisionRoiListItem.Purpose), 0.45);
        Column("启用", nameof(VisionRoiListItem.Enabled), 0.4);
        Column("位置", nameof(VisionRoiListItem.Summary), 1.6);

        var actions = new WrapPanel { Margin = new Thickness(2, 2, 2, 6) };
        _delete = Action(actions, "删除", _model.DeleteSelected);
        if (_model.SupportsRegions)
        {
            _include = Action(actions, "设为包含", () => _model.SetSelectedPurpose(ERoiPurpose.Include));
            _exclude = Action(actions, "设为排除", () => _model.SetSelectedPurpose(ERoiPurpose.Exclude));
            _toggle = Action(actions, "停用", () =>
            {
                if (Selected() is { } selected) _model.SetSelectedEnabled(!selected.Enabled);
            });
        }
        SetDock(actions, Dock.Top);
        Children.Add(actions);
        var body = new Grid();
        body.Children.Add(_grid);
        body.Children.Add(_empty);
        Children.Add(body);

        _grid.SelectionChanged += (_, _) =>
        {
            if (_syncing) return;
            if (_grid.SelectedItem is VisionRoiListItem item) _model.Select(item.Id);
            UpdateActions();
        };
        Loaded += (_, _) => { _model.Changed -= OnModelChanged; _model.Changed += OnModelChanged; Reload(); };
        Unloaded += (_, _) => _model.Changed -= OnModelChanged;
        Reload();
    }

    private void Column(string header, string path, double weight) => _grid.Columns.Add(new DataGridTextColumn
    {
        Header = header,
        Binding = new Binding(path),
        Width = new DataGridLength(weight, DataGridLengthUnitType.Star)
    });

    private static Button Action(Panel host, string text, Action action)
    {
        var button = new Button { Content = text, Margin = new Thickness(0, 0, 6, 0), Padding = new Thickness(9, 4, 9, 4) };
        button.Click += (_, _) => action();
        host.Children.Add(button);
        return button;
    }

    private VisionRoiListItem? Selected() => _model.Items.FirstOrDefault(item => item.Id == _model.SelectedId);

    private void OnModelChanged(object? sender, EventArgs e)
    {
        if (Dispatcher.CheckAccess()) Reload();
        else Dispatcher.BeginInvoke(Reload);
    }

    /// <summary>按 ROI 文档重建行并恢复画布中的当前选择。</summary>
    private void Reload()
    {
        _syncing = true;
        try
        {
            var items = _model.Items;
            _grid.ItemsSource = items;
            _grid.SelectedItem = items.FirstOrDefault(item => item.Id == _model.SelectedId);
            _grid.Visibility = items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            _empty.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        finally { _syncing = false; }
        UpdateActions();
    }

    private void UpdateActions()
    {
        var selected = Selected();
        _delete.IsEnabled = selected is not null;
        if (_include is null || _exclude is null || _toggle is null) return;
        _include.IsEnabled = selected is { Purpose: not "包含" };
        _exclude.IsEnabled = selected is { Purpose: not "排除" };
        _toggle.IsEnabled = selected is not null;
        _toggle.Content = selected is { Enabled: false } ? "启用" : "停用";
    }
}
