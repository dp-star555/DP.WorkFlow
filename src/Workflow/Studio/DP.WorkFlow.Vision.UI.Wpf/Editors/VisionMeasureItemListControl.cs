using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace DP.WorkFlow.Vision.UI.Wpf;

/// <summary>测量组节点左侧“测量项”页：按类型新增、删除、启用/停用测量项；选中项在右侧画布上编辑，参数在参数页的“测量项”分组里。</summary>
internal sealed class VisionMeasureItemListControl : DockPanel
{
    private readonly VisionFrameEditorPageModel _model;
    private readonly DataGrid _grid = new()
    {
        IsReadOnly = true, AutoGenerateColumns = false, CanUserAddRows = false, CanUserDeleteRows = false,
        SelectionMode = DataGridSelectionMode.Single, SelectionUnit = DataGridSelectionUnit.FullRow, HeadersVisibility = DataGridHeadersVisibility.Column
    };
    private readonly TextBlock _empty = new()
    {
        Text = "暂无测量项\n用上方按钮添加找线、找圆或找矩形。", TextAlignment = TextAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
    };
    private readonly Button _delete, _toggle;
    private bool _syncing;

    public VisionMeasureItemListControl(VisionFrameEditorPageModel model)
    {
        _model = model;
        Column("名称", nameof(VisionMeasureItemRow.Name), .8);
        Column("类型", nameof(VisionMeasureItemRow.Kind), .5);
        Column("启用", nameof(VisionMeasureItemRow.Enabled), .36);
        Column("结果", nameof(VisionMeasureItemRow.Status), 1.1);
        Column("标识", nameof(VisionMeasureItemRow.Id), .7);
        var actions = new WrapPanel { Margin = new Thickness(2, 2, 2, 6) };
        foreach (var kind in Enum.GetValues<EVisionMeasureKind>())
            Action(actions, "+" + MeasureVisionGroupNodeModel.KindName(kind), () => _model.AddMeasureItem(kind));
        _delete = Action(actions, "删除", _model.RemoveSelectedMeasureItem);
        _toggle = Action(actions, "停用", () =>
        {
            if (Selected() is { } selected) _model.SetMeasureItemEnabled(selected.Id, !selected.Enabled);
        });
        SetDock(actions, Dock.Top);
        Children.Add(actions);
        var body = new Grid();
        body.Children.Add(_grid); body.Children.Add(_empty);
        Children.Add(body);
        _grid.SelectionChanged += (_, _) =>
        {
            if (_syncing) return;
            if (_grid.SelectedItem is VisionMeasureItemRow item) _model.SelectMeasureItem(item.Id);
            UpdateActions();
        };
        Loaded += (_, _) => { _model.MeasureItemsChanged -= OnModelChanged; _model.MeasureItemsChanged += OnModelChanged; Reload(); };
        Unloaded += (_, _) => _model.MeasureItemsChanged -= OnModelChanged;
        Reload();
    }

    private void Column(string header, string path, double weight) => _grid.Columns.Add(new DataGridTextColumn
    {
        Header = header, Binding = new Binding(path), Width = new DataGridLength(weight, DataGridLengthUnitType.Star)
    });

    private Button Action(Panel host, string text, Action action)
    {
        var button = new Button { Content = text, Margin = new Thickness(0, 0, 6, 6), Padding = new Thickness(9, 4, 9, 4) };
        button.Click += (_, _) =>
        {
            try { action(); }
            catch (Exception error) when (error is InvalidOperationException or ArgumentException) { MessageBox.Show(Window.GetWindow(this)!, error.Message, "测量项"); }
        };
        host.Children.Add(button);
        return button;
    }

    private VisionMeasureItemRow? Selected() => _model.MeasureItems.FirstOrDefault(item => item.Id == _model.SelectedMeasureItemId);

    private void OnModelChanged(object? sender, EventArgs e)
    {
        if (Dispatcher.CheckAccess()) Reload(); else Dispatcher.BeginInvoke(Reload);
    }

    private void Reload()
    {
        _syncing = true;
        try
        {
            var items = _model.MeasureItems;
            _grid.ItemsSource = items;
            _grid.SelectedItem = items.FirstOrDefault(item => item.Id == _model.SelectedMeasureItemId);
            _grid.Visibility = items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            _empty.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        finally { _syncing = false; }
        UpdateActions();
    }

    private void UpdateActions()
    {
        var selected = Selected();
        _delete.IsEnabled = _toggle.IsEnabled = selected is not null;
        _toggle.Content = selected is { Enabled: false } ? "启用" : "停用";
    }
}
