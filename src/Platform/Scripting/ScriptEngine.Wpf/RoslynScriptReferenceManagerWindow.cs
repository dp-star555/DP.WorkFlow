using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using MediaColor = System.Windows.Media.Color;
using WpfButton = System.Windows.Controls.Button;
using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfControl = System.Windows.Controls.Control;
using WpfDataGrid = System.Windows.Controls.DataGrid;
using WpfDataGridColumnHeader = System.Windows.Controls.Primitives.DataGridColumnHeader;
using WpfGroupBox = System.Windows.Controls.GroupBox;
using WpfListBox = System.Windows.Controls.ListBox;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace ScriptEngine.Wpf;

/// <summary>使用 PE 元数据管理脚本 DLL 引用的 WPF 窗口；检查过程不会加载目标程序集。</summary>
public sealed class RoslynScriptReferenceManagerWindow : Window
{
    private readonly ObservableCollection<RoslynScriptReferenceInfo> _references = new();
    private readonly System.Windows.Controls.DataGrid _grid = new();

    /// <summary>创建脚本引用管理窗口。</summary>
    /// <param name="owner">窗口所有者。</param>
    /// <param name="paths">当前脚本已有的 DLL 路径。</param>
    public RoslynScriptReferenceManagerWindow(Window owner, IEnumerable<string> paths)
    {
        if (owner is null) throw new ArgumentNullException(nameof(owner));
        // 测试/设计器阶段 Owner 可能尚未创建 HWND；仅在真实已显示窗口上建立模态所有权。
        if (new System.Windows.Interop.WindowInteropHelper(owner).Handle != IntPtr.Zero) Owner = owner;
        Title = "C# 脚本 DLL 引用";
        Width = 920;
        Height = 520;
        MinWidth = 720;
        MinHeight = 360;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        ApplyOwnerAppearance(owner);

        _grid.AutoGenerateColumns = false;
        _grid.IsReadOnly = true;
        _grid.SelectionMode = DataGridSelectionMode.Extended;
        _grid.SelectionUnit = DataGridSelectionUnit.FullRow;
        _grid.Columns.Add(new DataGridTextColumn { Header = "程序集", Binding = new System.Windows.Data.Binding(nameof(RoslynScriptReferenceInfo.AssemblyName)), Width = new DataGridLength(2, DataGridLengthUnitType.Star) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "版本", Binding = new System.Windows.Data.Binding(nameof(RoslynScriptReferenceInfo.Version)), Width = new DataGridLength(1.4, DataGridLengthUnitType.Star) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "架构", Binding = new System.Windows.Data.Binding(nameof(RoslynScriptReferenceInfo.Architecture)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "文件", Binding = new System.Windows.Data.Binding(nameof(RoslynScriptReferenceInfo.Path)), Width = new DataGridLength(5, DataGridLengthUnitType.Star) });
        _grid.ItemsSource = _references;

        var add = Button("添加 DLL…");
        var remove = Button("删除选中");
        var details = Button("查看信息");
        var ok = Button("确定");
        var cancel = Button("取消");
        ok.IsDefault = true;
        cancel.IsCancel = true;
        add.Click += (_, _) => AddReferences();
        remove.Click += (_, _) => RemoveSelected();
        details.Click += (_, _) => ShowSelectedDetails();
        ok.Click += (_, _) => { DialogResult = true; Close(); };
        cancel.Click += (_, _) => { DialogResult = false; Close(); };

        var toolbar = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new Thickness(6) };
        toolbar.Children.Add(add);
        toolbar.Children.Add(remove);
        toolbar.Children.Add(details);
        var buttons = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right, Margin = new Thickness(6) };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);

        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.Children.Add(toolbar);
        Grid.SetRow(_grid, 1);
        layout.Children.Add(_grid);
        Grid.SetRow(buttons, 2);
        layout.Children.Add(buttons);
        Content = layout;
        Loaded += (_, _) => FitToWorkingArea();

        foreach (var path in paths.Where(item => !string.IsNullOrWhiteSpace(item)))
        {
            try { AddReference(path, showDuplicateMessage: false); }
            catch (Exception exception) { System.Windows.MessageBox.Show(this, exception.Message, "引用读取失败", MessageBoxButton.OK, MessageBoxImage.Warning); }
        }
    }

    /// <summary>继承宿主窗口的主题资源；独立使用时回退到脚本编辑器默认深色调色板。</summary>
    private void ApplyOwnerAppearance(Window owner)
    {
        Background = owner.Background ?? new SolidColorBrush(MediaColor.FromRgb(30, 30, 30));
        Foreground = owner.Foreground ?? new SolidColorBrush(MediaColor.FromRgb(241, 241, 241));
        FontFamily = owner.FontFamily;
        FontSize = owner.FontSize;

        foreach (var key in new object[] { typeof(WpfButton), typeof(WpfTextBox), typeof(WpfComboBox), typeof(WpfListBox),
                     typeof(WpfDataGrid), typeof(WpfDataGridColumnHeader), typeof(WpfGroupBox),
                     System.Windows.SystemColors.ControlBrushKey, System.Windows.SystemColors.ControlTextBrushKey,
                     System.Windows.SystemColors.WindowBrushKey, System.Windows.SystemColors.WindowTextBrushKey,
                     System.Windows.SystemColors.HighlightBrushKey, System.Windows.SystemColors.HighlightTextBrushKey })
        {
            var resource = owner.TryFindResource(key);
            if (resource is not null) Resources[key] = resource;
        }

        if (!Resources.Contains(typeof(WpfButton))) Resources[typeof(WpfButton)] = CreateFallbackButtonStyle();
        if (!Resources.Contains(typeof(WpfDataGrid))) Resources[typeof(WpfDataGrid)] = CreateFallbackGridStyle();
    }

    private static Style CreateFallbackButtonStyle()
    {
        var style = new Style(typeof(WpfButton));
        style.Setters.Add(new Setter(WpfControl.BackgroundProperty, new SolidColorBrush(MediaColor.FromRgb(51, 51, 55))));
        style.Setters.Add(new Setter(WpfControl.ForegroundProperty, new SolidColorBrush(MediaColor.FromRgb(241, 241, 241))));
        style.Setters.Add(new Setter(WpfControl.BorderBrushProperty, new SolidColorBrush(MediaColor.FromRgb(63, 63, 70))));
        style.Setters.Add(new Setter(FrameworkElement.MinHeightProperty, 30d));
        style.Setters.Add(new Setter(WpfControl.PaddingProperty, new Thickness(10, 4, 10, 4)));
        return style;
    }

    private static Style CreateFallbackGridStyle()
    {
        var style = new Style(typeof(WpfDataGrid));
        style.Setters.Add(new Setter(WpfControl.BackgroundProperty, new SolidColorBrush(MediaColor.FromRgb(37, 37, 38))));
        style.Setters.Add(new Setter(WpfControl.ForegroundProperty, new SolidColorBrush(MediaColor.FromRgb(241, 241, 241))));
        style.Setters.Add(new Setter(WpfDataGrid.RowBackgroundProperty, new SolidColorBrush(MediaColor.FromRgb(51, 51, 55))));
        return style;
    }

    /// <summary>确保引用窗口不会超出当前系统工作区。</summary>
    private void FitToWorkingArea()
    {
        var workingArea = SystemParameters.WorkArea;
        Width = Math.Min(Width, Math.Max(MinWidth, workingArea.Width - 24));
        Height = Math.Min(Height, Math.Max(MinHeight, workingArea.Height - 24));
    }

    /// <summary>用户确认后返回的 DLL 绝对路径。</summary>
    public IReadOnlyList<string> ReferencePaths => _references.Select(item => item.Path).ToArray();

    private static System.Windows.Controls.Button Button(string text) => new() { Content = text, MinWidth = 88, Margin = new Thickness(3) };

    private void AddReferences()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择脚本引用 DLL",
            Filter = "托管程序集 (*.dll)|*.dll|所有文件 (*.*)|*.*",
            Multiselect = true,
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != true) return;
        foreach (var path in dialog.FileNames)
        {
            try { AddReference(path, showDuplicateMessage: true); }
            catch (Exception exception) { System.Windows.MessageBox.Show(this, exception.Message, "无法添加引用", MessageBoxButton.OK, MessageBoxImage.Error); }
        }
    }

    private void AddReference(string path, bool showDuplicateMessage)
    {
        var info = RoslynScriptReferenceInspector.Inspect(path);
        var duplicate = _references.FirstOrDefault(item => string.Equals(item.AssemblyName, info.AssemblyName, StringComparison.OrdinalIgnoreCase));
        if (duplicate is not null)
        {
            if (string.Equals(duplicate.Path, info.Path, StringComparison.OrdinalIgnoreCase)) return;
            if (showDuplicateMessage)
                System.Windows.MessageBox.Show(this, $"已经存在同名程序集 {info.AssemblyName}：\n{duplicate.Path}", "程序集名称冲突", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        _references.Add(info);
    }

    private void RemoveSelected()
    {
        var selected = _grid.SelectedItems.Cast<RoslynScriptReferenceInfo>().ToArray();
        foreach (var item in selected) _references.Remove(item);
    }

    private void ShowSelectedDetails()
    {
        if (_grid.SelectedItem is not RoslynScriptReferenceInfo item) return;
        var dependencies = item.Dependencies.Count == 0 ? "（无）" : string.Join(Environment.NewLine, item.Dependencies);
        System.Windows.MessageBox.Show(
            this,
            $"程序集：{item.AssemblyName}\n版本：{item.Version}\n架构：{item.Architecture}\nSHA256：{item.Sha256}\n路径：{item.Path}\n\n直接依赖：\n{dependencies}",
            "DLL 引用信息",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }
}
