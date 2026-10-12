using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DP.WorkFlow.UI;

namespace DP.WorkFlow.UI.Wpf;

/// <summary>
/// 节点窗口“输入输出”页：编辑复合节点子流程的输入和输出。
/// 输入的来源选父画布中本节点之前的节点输出（类型跟随来源），或使用手动值（整数、小数、布尔、文本）；
/// 子画布中通过“启动节点.名称”读取。输出的来源选子画布中的节点输出，父画布中通过“本节点.名称”读取。
/// </summary>
internal sealed class WorkflowSubflowInterfaceControl : UserControl
{
    private static readonly E_WorkflowSubflowValueType[] ValueTypes = Enum.GetValues<E_WorkflowSubflowValueType>();
    private readonly WorkflowSubflowInterfacePageModel _model;
    private readonly StackPanel _inputs = new();
    private readonly StackPanel _outputs = new();
    private bool _refreshQueued;

    public WorkflowSubflowInterfaceControl(WorkflowSubflowInterfacePageModel model)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
        Background = Brush(15, 23, 42);
        Foreground = Brush(226, 232, 240);
        var content = new StackPanel { Margin = new Thickness(6) };
        content.Children.Add(Header("输入", "添加输入",
            "来源选父画布中本节点之前的节点输出，类型自动跟随来源；不选来源时使用手动值。子画布中绑定“启动节点.名称”读取。",
            () => _model.AddInput()));
        content.Children.Add(ColumnHeader("名称", "来源", "类型", "值"));
        content.Children.Add(_inputs);
        content.Children.Add(Header("输出", "添加输出",
            "来源选子画布中的节点输出，类型自动跟随来源。父画布中绑定“本节点.名称”读取。",
            () => _model.AddOutput()));
        content.Children.Add(ColumnHeader("名称", "来源", "类型", string.Empty));
        content.Children.Add(_outputs);
        Content = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        _model.Changed += OnModelChanged;
        Loaded += (_, _) => { _model.Changed -= OnModelChanged; _model.Changed += OnModelChanged; RefreshRows(); };
        Unloaded += (_, _) => _model.Changed -= OnModelChanged;
        RefreshRows();
    }

    private void OnModelChanged(object? sender, EventArgs e)
    {
        // 修改常由本页控件的事件触发，推迟到事件结束后重建，避免在事件处理中替换控件。
        if (_refreshQueued) return;
        _refreshQueued = true;
        _ = Dispatcher.BeginInvoke(RefreshRows);
    }

    private void RefreshRows()
    {
        _refreshQueued = false;
        _inputs.Children.Clear();
        foreach (var input in _model.Inputs)
        {
            var index = input.Index;
            var name = EditBox(input.Name, text => _model.RenameInput(index, text));
            var source = new Button { Content = input.SourceText, Margin = new Thickness(2), Padding = new Thickness(6, 2, 6, 2) };
            source.Click += (_, _) => ShowInputSourceMenu(source, index);
            FrameworkElement type;
            FrameworkElement value;
            if (input.IsBound)
            {
                // 绑定来源时类型跟随来源，不能修改，也没有手动值。
                type = new TextBlock { Text = input.TypeText, Margin = new Thickness(4), VerticalAlignment = VerticalAlignment.Center };
                value = new TextBlock();
            }
            else
            {
                var combo = new ComboBox
                {
                    ItemsSource = ValueTypes.Select(WorkflowSubflowInput.DisplayName).ToArray(),
                    SelectedItem = WorkflowSubflowInput.DisplayName(input.ValueType),
                    Margin = new Thickness(2)
                };
                combo.SelectionChanged += (_, _) =>
                {
                    var selected = ValueTypes.FirstOrDefault(item => WorkflowSubflowInput.DisplayName(item) == (string?)combo.SelectedItem);
                    _model.SetInputValueType(index, selected);
                };
                type = combo;
                value = EditBox(input.ValueText, text => _model.SetInputValue(index, text));
            }
            _inputs.Children.Add(Row(name, source, type, value, RemoveButton(() => _model.RemoveInput(index))));
        }
        _outputs.Children.Clear();
        foreach (var output in _model.Outputs)
        {
            var index = output.Index;
            var name = EditBox(output.Name, text => _model.RenameOutput(index, text));
            var source = new Button { Content = output.SourceText, Margin = new Thickness(2), Padding = new Thickness(6, 2, 6, 2) };
            source.Click += (_, _) =>
            {
                var dialog = new WorkflowBindingSelectorWindow(_model.GetOutputCandidates(), _model.Outputs[index].Source)
                {
                    Owner = Window.GetWindow(this)
                };
                if (dialog.ShowDialog() == true && dialog.SelectedCandidate is { } candidate)
                    _model.SetOutputSource(index, candidate.ToBindingKey());
            };
            var type = new TextBlock { Text = output.TypeText, Margin = new Thickness(4), VerticalAlignment = VerticalAlignment.Center };
            _outputs.Children.Add(Row(name, source, type, new TextBlock(), RemoveButton(() => _model.RemoveOutput(index))));
        }
    }

    private void ShowInputSourceMenu(Button anchor, int index)
    {
        var choose = new MenuItem { Header = "选择父画布中的来源…" };
        choose.Click += (_, _) =>
        {
            var dialog = new WorkflowBindingSelectorWindow(_model.GetInputCandidates(), _model.Inputs[index].Source)
            {
                Owner = Window.GetWindow(this)
            };
            if (dialog.ShowDialog() == true && dialog.SelectedCandidate is { } candidate)
                _model.SetInputSource(index, candidate.ToBindingKey());
        };
        var manual = new MenuItem { Header = "使用手动值" };
        manual.Click += (_, _) => _model.SetInputSource(index, null);
        var menu = new ContextMenu { PlacementTarget = anchor };
        menu.Items.Add(choose);
        menu.Items.Add(manual);
        menu.IsOpen = true;
    }

    /// <summary>失去焦点或按回车时提交文本。</summary>
    private static TextBox EditBox(string text, Action<string> commit)
    {
        var box = new TextBox { Text = text, Margin = new Thickness(2), VerticalContentAlignment = VerticalAlignment.Center };
        var committed = text;
        void Commit()
        {
            if (box.Text == committed) return;
            committed = box.Text;
            commit(box.Text);
        }
        box.LostKeyboardFocus += (_, _) => Commit();
        box.KeyDown += (_, e) => { if (e.Key == Key.Enter) Commit(); };
        return box;
    }

    private static Button RemoveButton(Action remove)
    {
        var button = new Button { Content = "删除", Margin = new Thickness(2), Padding = new Thickness(6, 2, 6, 2) };
        button.Click += (_, _) => remove();
        return button;
    }

    private static Grid Row(params FrameworkElement[] cells)
    {
        var grid = Columns();
        for (var index = 0; index < cells.Length; index++)
        {
            Grid.SetColumn(cells[index], index);
            grid.Children.Add(cells[index]);
        }
        return grid;
    }

    private static Grid ColumnHeader(params string[] titles)
    {
        var grid = Columns();
        for (var index = 0; index < titles.Length; index++)
        {
            var title = new TextBlock { Text = titles[index], Margin = new Thickness(4, 2, 4, 2), Foreground = Brush(148, 163, 184) };
            Grid.SetColumn(title, index);
            grid.Children.Add(title);
        }
        return grid;
    }

    private static Grid Columns()
    {
        var grid = new Grid();
        foreach (var width in new[] { 3d, 5d, 2d, 3d })
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(width, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        return grid;
    }

    private static FrameworkElement Header(string title, string addText, string hint, Action add)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 8, 0, 2) };
        var line = new StackPanel { Orientation = Orientation.Horizontal };
        line.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center });
        var button = new Button { Content = addText, Padding = new Thickness(8, 2, 8, 2) };
        button.Click += (_, _) => add();
        line.Children.Add(button);
        panel.Children.Add(line);
        panel.Children.Add(new TextBlock { Text = hint, TextWrapping = TextWrapping.Wrap, Foreground = Brush(148, 163, 184), Margin = new Thickness(0, 4, 0, 0) });
        return panel;
    }

    private static SolidColorBrush Brush(byte red, byte green, byte blue)
    {
        var brush = new SolidColorBrush(Color.FromRgb(red, green, blue));
        brush.Freeze();
        return brush;
    }
}
