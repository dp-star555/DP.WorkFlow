using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using DP.WorkFlow.UI;

namespace DP.WorkFlow.UI.Wpf;

/// <summary>WPF Block 输入/输出映射集合编辑窗口。</summary>
public sealed class WorkflowBlockMappingEditorWindow : Window
{
    private readonly WorkflowBlockMappingEditorModel _model;
    private readonly ObservableCollection<InputItem> _inputs;
    private readonly ObservableCollection<OutputItem> _outputs;

    /// <summary>初始化复合块输入输出映射编辑窗口。</summary>
    /// <param name="model">“model”参数。</param>
    /// <param name="startNodeId">“startNodeId”参数。</param>
    public WorkflowBlockMappingEditorWindow(WorkflowBlockMappingEditorModel model, string startNodeId)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
        _inputs = new ObservableCollection<InputItem>(model.Inputs.Select(InputItem.From));
        _outputs = new ObservableCollection<OutputItem>(model.Outputs.Select(OutputItem.From));
        Title = $"Block 映射 - {model.Block.Title}";
        Width = 980;
        Height = 620;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush(15, 23, 42);
        Foreground = Brush(226, 232, 240);

        var inputGrid = CreateInputGrid(TryCandidates(() => model.GetParentCandidates(startNodeId)));
        inputGrid.ItemsSource = _inputs;
        var outputGrid = CreateOutputGrid(TryCandidates(model.GetChildCandidates));
        outputGrid.ItemsSource = _outputs;
        var tabs = new TabControl();
        tabs.Items.Add(new TabItem { Header = "输入映射", Content = WithCommands(inputGrid, () => _inputs.Add(new InputItem()), () => RemoveSelected(inputGrid, _inputs)) });
        tabs.Items.Add(new TabItem { Header = "输出映射", Content = WithCommands(outputGrid, () => _outputs.Add(new OutputItem()), () => RemoveSelected(outputGrid, _outputs)) });

        var ok = Button("确定");
        var cancel = Button("取消");
        ok.Click += (_, _) => Commit();
        cancel.Click += (_, _) => { DialogResult = false; Close(); };
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(8)
        };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        var root = new DockPanel();
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);
        root.Children.Add(tabs);
        Content = root;
    }

    /// <summary>创建Input Grid。</summary>
    /// <param name="candidates">“candidates”参数。</param>
    /// <returns>返回处理结果。</returns>
    private DataGrid CreateInputGrid(IReadOnlyList<WorkflowBindingCandidate> candidates)
    {
        var grid = Grid();
        grid.Columns.Add(TextColumn("子变量", nameof(InputItem.TargetVariableName)));
        grid.Columns.Add(ComboColumn("来源", nameof(InputItem.Source), Enum.GetValues<E_BlockInputSource>()));
        grid.Columns.Add(TextColumn("固定值", nameof(InputItem.LiteralValue)));
        grid.Columns.Add(TextColumn("父变量", nameof(InputItem.ParentVariableName)));
        grid.Columns.Add(ComboColumn("父节点绑定", nameof(InputItem.ParentBinding), BindingValues(candidates)));
        return grid;
    }

    /// <summary>创建Output Grid。</summary>
    /// <param name="candidates">“candidates”参数。</param>
    /// <returns>返回处理结果。</returns>
    private DataGrid CreateOutputGrid(IReadOnlyList<WorkflowBindingCandidate> candidates)
    {
        var grid = Grid();
        grid.Columns.Add(TextColumn("父变量", nameof(OutputItem.TargetVariableName)));
        grid.Columns.Add(ComboColumn("来源", nameof(OutputItem.Source), Enum.GetValues<E_BlockOutputSource>()));
        grid.Columns.Add(TextColumn("子变量", nameof(OutputItem.ChildVariableName)));
        grid.Columns.Add(ComboColumn("子节点绑定", nameof(OutputItem.ChildBinding), BindingValues(candidates)));
        return grid;
    }

    /// <summary>执行 Commit 相关处理。</summary>
    private void Commit()
    {
        try
        {
            _model.ReplaceAll(
                _inputs.Select((item, index) => new WorkflowBlockInputMappingRow(
                    index,
                    item.TargetVariableName,
                    item.Source,
                    item.LiteralValue,
                    item.ParentVariableName,
                    ParseBinding(item.ParentBinding))),
                _outputs.Select((item, index) => new WorkflowBlockOutputMappingRow(
                    index,
                    item.TargetVariableName,
                    item.Source,
                    item.ChildVariableName,
                    ParseBinding(item.ChildBinding))));
            DialogResult = true;
            Close();
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or FormatException)
        {
            MessageBox.Show(this, exception.Message, "映射配置无效", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>执行 With Commands 相关处理。</summary>
    /// <param name="grid">“grid”参数。</param>
    /// <param name="add">“add”参数。</param>
    /// <param name="remove">“remove”参数。</param>
    /// <returns>返回处理结果。</returns>
    private static FrameworkElement WithCommands(DataGrid grid, Action add, Action remove)
    {
        var addButton = Button("添加");
        var removeButton = Button("删除");
        addButton.Click += (_, _) => add();
        removeButton.Click += (_, _) => remove();
        var commands = new StackPanel { Orientation = Orientation.Horizontal };
        commands.Children.Add(addButton);
        commands.Children.Add(removeButton);
        var panel = new DockPanel();
        DockPanel.SetDock(commands, Dock.Top);
        panel.Children.Add(commands);
        panel.Children.Add(grid);
        return panel;
    }

    /// <summary>删除Selected。</summary>
    /// <param name="grid">“grid”参数。</param>
    /// <param name="collection">“collection”参数。</param>
    private static void RemoveSelected<T>(DataGrid grid, ICollection<T> collection)
    {
        foreach (var item in grid.SelectedItems.Cast<T>().ToArray())
            collection.Remove(item);
    }

    private static DataGrid Grid() => new()
    {
        AutoGenerateColumns = false,
        CanUserAddRows = false,
        CanUserDeleteRows = true,
        SelectionMode = DataGridSelectionMode.Extended,
        Background = Brush(15, 23, 42),
        Foreground = Brush(226, 232, 240),
        RowBackground = Brush(22, 32, 49),
        AlternatingRowBackground = Brush(30, 41, 59),
        BorderThickness = new Thickness(0)
    };

    private static DataGridTextColumn TextColumn(string title, string path) => new()
    {
        Header = title,
        Binding = new Binding(path) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged },
        Width = new DataGridLength(1, DataGridLengthUnitType.Star)
    };

    private static DataGridComboBoxColumn ComboColumn(string title, string path, System.Collections.IEnumerable values) => new()
    {
        Header = title,
        ItemsSource = values,
        SelectedItemBinding = new Binding(path) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged },
        Width = new DataGridLength(1, DataGridLengthUnitType.Star)
    };

    /// <summary>尝试执行“Candidates”。</summary>
    /// <param name="factory">“factory”参数。</param>
    /// <returns>返回处理结果。</returns>
    private static IReadOnlyList<WorkflowBindingCandidate> TryCandidates(Func<IReadOnlyList<WorkflowBindingCandidate>> factory)
    {
        try { return factory(); }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        { return Array.Empty<WorkflowBindingCandidate>(); }
    }

    /// <summary>执行 Binding Values 相关处理。</summary>
    /// <param name="candidates">“candidates”参数。</param>
    /// <returns>返回处理结果。</returns>
    private static string[] BindingValues(IEnumerable<WorkflowBindingCandidate> candidates) => new[] { string.Empty }
        .Concat(candidates.Select(item => item.ToBindingKey().ToString()))
        .Distinct(StringComparer.Ordinal)
        .ToArray();

    /// <summary>执行 Parse Binding 相关处理。</summary>
    /// <param name="text">要显示或处理的文本。</param>
    /// <returns>返回处理结果。</returns>
    private static WorkflowBindingKey? ParseBinding(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        return WorkflowBindingKey.TryParse(text, out var binding)
            ? binding
            : throw new FormatException($"绑定格式无效：{text}。");
    }

    private static Button Button(string text) => new()
    {
        Content = text,
        Margin = new Thickness(4),
        Padding = new Thickness(14, 5, 14, 5)
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

    /// <summary>定义 InputItem 类型。</summary>
    private sealed class InputItem
    {
        /// <summary>获取或设置 Target Variable Name 成员。</summary>
        public string TargetVariableName { get; set; } = "Input";
        /// <summary>获取或设置 Source 成员。</summary>
        public E_BlockInputSource Source { get; set; }
        /// <summary>获取或设置 Literal Value 成员。</summary>
        public string? LiteralValue { get; set; } = string.Empty;
        /// <summary>获取或设置 Parent Variable Name 成员。</summary>
        public string ParentVariableName { get; set; } = string.Empty;
        /// <summary>获取或设置 Parent Binding 成员。</summary>
        public string ParentBinding { get; set; } = string.Empty;

        public static InputItem From(WorkflowBlockInputMappingRow row) => new()
        {
            TargetVariableName = row.TargetVariableName,
            Source = row.Source,
            LiteralValue = Convert.ToString(row.LiteralValue),
            ParentVariableName = row.ParentVariableName,
            ParentBinding = row.ParentBinding?.ToString() ?? string.Empty
        };
    }

    /// <summary>定义 OutputItem 类型。</summary>
    private sealed class OutputItem
    {
        /// <summary>获取或设置 Target Variable Name 成员。</summary>
        public string TargetVariableName { get; set; } = "Output";
        /// <summary>获取或设置 Source 成员。</summary>
        public E_BlockOutputSource Source { get; set; }
        /// <summary>获取或设置 Child Variable Name 成员。</summary>
        public string ChildVariableName { get; set; } = "Result";
        /// <summary>获取或设置 Child Binding 成员。</summary>
        public string ChildBinding { get; set; } = string.Empty;

        public static OutputItem From(WorkflowBlockOutputMappingRow row) => new()
        {
            TargetVariableName = row.TargetVariableName,
            Source = row.Source,
            ChildVariableName = row.ChildVariableName,
            ChildBinding = row.ChildBinding?.ToString() ?? string.Empty
        };
    }
}
