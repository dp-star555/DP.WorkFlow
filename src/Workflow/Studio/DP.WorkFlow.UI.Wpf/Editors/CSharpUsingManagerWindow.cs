using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace DP.WorkFlow.UI.Wpf;

/// <summary>编辑 C# 脚本 using 命名空间的 WPF 窗口。</summary>
internal sealed class CSharpUsingManagerWindow : Window
{
    private static readonly string[] CommonNamespaces =
    {
        "System", "System.Collections.Generic", "System.Diagnostics", "System.Globalization", "System.IO",
        "System.Linq", "System.Net.Http", "System.Text", "System.Text.Json", "System.Threading",
        "System.Threading.Tasks", "System.Windows", "System.Windows.Forms", "ScriptEngine", "DP.WorkFlow"
    };

    private readonly ObservableCollection<string> _items;
    private readonly ListBox _list;
    private readonly ComboBox _input;

    /// <summary>初始化 C# using 命名空间管理窗口。</summary>
    public CSharpUsingManagerWindow(Window owner, IEnumerable<string> namespaces)
    {
        Owner = owner;
        Title = "using 管理";
        Width = 560;
        Height = 460;
        MinWidth = 460;
        MinHeight = 340;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        WorkflowWpfStyle.Apply(this);
        _items = new ObservableCollection<string>(namespaces.Select(Normalize).Where(IsValid).Distinct(StringComparer.Ordinal).OrderBy(item => item, StringComparer.Ordinal));
        _list = new ListBox { ItemsSource = _items, SelectionMode = SelectionMode.Extended };
        _input = new ComboBox { IsEditable = true, ItemsSource = CommonNamespaces, Margin = new Thickness(0, 5, 5, 5) };

        var add = Button("添加");
        var remove = Button("删除所选");
        var sort = Button("排序并去重");
        var defaults = Button("添加常用 using");
        add.Click += (_, _) => AddInput();
        remove.Click += (_, _) => RemoveSelected();
        sort.Click += (_, _) => ReplaceItems(_items);
        defaults.Click += (_, _) => ReplaceItems(_items.Concat(new[] { "System", "System.Collections.Generic", "System.Linq", "System.Threading", "System.Threading.Tasks" }));
        _input.PreviewKeyDown += (_, eventArgs) => { if (eventArgs.Key == Key.Enter) { AddInput(); eventArgs.Handled = true; } };
        _list.PreviewKeyDown += (_, eventArgs) => { if (eventArgs.Key == Key.Delete) { RemoveSelected(); eventArgs.Handled = true; } };
        _list.MouseDoubleClick += (_, _) => RemoveSelected();

        var inputGrid = new Grid();
        inputGrid.ColumnDefinitions.Add(new ColumnDefinition());
        inputGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        inputGrid.Children.Add(_input);
        Grid.SetColumn(add, 1);
        inputGrid.Children.Add(add);
        var commands = new StackPanel { Orientation = Orientation.Horizontal };
        commands.Children.Add(remove);
        commands.Children.Add(sort);
        commands.Children.Add(defaults);
        var ok = Button("应用");
        var cancel = Button("取消");
        ok.IsDefault = true;
        cancel.IsCancel = true;
        ok.Click += (_, _) => { DialogResult = true; Close(); };
        var bottom = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        bottom.Children.Add(ok);
        bottom.Children.Add(cancel);

        var layout = new Grid { Margin = new Thickness(10) };
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(38) });
        layout.RowDefinitions.Add(new RowDefinition());
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(40) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(42) });
        Add(layout, new TextBlock { Text = "输入命名空间后按 Enter；多选后按 Delete 或双击删除。" }, 0);
        Add(layout, inputGrid, 1);
        Add(layout, _list, 2);
        Add(layout, commands, 3);
        Add(layout, new TextBlock { Text = "只填写命名空间，例如 System.Text；不要填写 using 和分号。", Foreground = System.Windows.Media.Brushes.Gray }, 4);
        Add(layout, bottom, 5);
        Content = layout;
        Loaded += (_, _) => _input.Focus();
    }

    public IReadOnlyList<string> Namespaces => _items.ToArray();

    /// <summary>添加Input。</summary>
    private void AddInput()
    {
        var value = Normalize(_input.Text);
        if (!IsValid(value))
        {
            MessageBox.Show(this, "请输入有效的命名空间，例如 System.Text。", "using 管理", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        ReplaceItems(_items.Concat(new[] { value }));
        _input.Text = string.Empty;
        _input.Focus();
    }

    /// <summary>删除Selected。</summary>
    private void RemoveSelected()
    {
        foreach (var item in _list.SelectedItems.Cast<string>().ToArray()) _items.Remove(item);
    }

    /// <summary>执行 Replace Items 相关处理。</summary>
    private void ReplaceItems(IEnumerable<string> values)
    {
        var result = values.Select(Normalize).Where(IsValid).Distinct(StringComparer.Ordinal).OrderBy(item => item, StringComparer.Ordinal).ToArray();
        _items.Clear();
        foreach (var item in result) _items.Add(item);
    }

    /// <summary>执行 Thickness 相关处理。</summary>
    private static void Add(Grid grid, UIElement element, int row) { Grid.SetRow(element, row); grid.Children.Add(element); }
    private static Button Button(string text) => new() { Content = text, Margin = new Thickness(4), Padding = new Thickness(10, 4, 10, 4) };
    /// <summary>执行 Normalize 相关处理。</summary>
    /// <param name="value">要转换或设置的值。</param>
    private static string Normalize(string? value)
    {
        var result = value?.Trim() ?? string.Empty;
        if (result.StartsWith("global using ", StringComparison.Ordinal)) result = result[13..];
        else if (result.StartsWith("using ", StringComparison.Ordinal)) result = result[6..];
        return result.Trim().TrimEnd(';').Trim();
    }
    /// <summary>执行 All 相关处理。</summary>
    private static bool IsValid(string value) => value.Length > 0 && value.Split('.').All(segment => segment.Length > 0
        && (char.IsLetter(segment[0]) || segment[0] == '_')
        && segment.Skip(1).All(character => char.IsLetterOrDigit(character) || character == '_'));
}
