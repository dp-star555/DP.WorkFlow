using System.Data;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using DP.WorkFlow.UI;

namespace DP.WorkFlow.UI.Wpf;

/// <summary>WPF 节点属性面板，支持标量、枚举和 WorkflowInput 绑定候选。</summary>
public sealed class WorkflowPropertyPanel : UserControl
{
    public static readonly DependencyProperty SessionProperty = DependencyProperty.Register(
        nameof(Session), typeof(WorkflowDesignerSession), typeof(WorkflowPropertyPanel),
        new PropertyMetadata(null, OnConfigurationChanged));

    public static readonly DependencyProperty EntryNodeIdProperty = DependencyProperty.Register(
        nameof(EntryNodeId), typeof(string), typeof(WorkflowPropertyPanel),
        new PropertyMetadata(null, OnConfigurationChanged));

    private readonly StackPanel _content;
    private readonly TreeView _propertyTree;
    private readonly TextBlock _details;
    private readonly TextBox _search;
    private readonly DispatcherTimer _searchTimer;
    private readonly HashSet<string> _collapsedCategories = new(StringComparer.Ordinal);
    private readonly Dictionary<FrameworkElement, string[]> _groupAncestors = new();
    private WorkflowPropertyInspectorModel? _model;
    private WorkflowPropertyChoiceProvider? _choiceProvider;
    private Func<IWorkflowNodeModel, IReadOnlyList<WorkflowPropertyEntry>>? _additionalProperties;

    /// <summary>领域描述生成的附加属性；与普通属性共用提交及撤销。</summary>
    public Func<IWorkflowNodeModel, IReadOnlyList<WorkflowPropertyEntry>>? AdditionalProperties
    {
        get => _additionalProperties;
        set { _additionalProperties = value; RecreateModel(); }
    }
    private bool _building;
    private int _inputEditorGeneration;
    private bool _hideScriptProperty;

    /// <summary>初始化节点属性编辑面板。</summary>
    public WorkflowPropertyPanel()
    {
        Background = Brush(15, 23, 42);
        Foreground = Brush(226, 232, 240);
        _content = new StackPanel { Margin = new Thickness(8) };
        _propertyTree = new TreeView { MinWidth = 170, Background = Brush(22, 32, 49), Foreground = Foreground };
        _details = new TextBlock
        {
            Text = "选择参数查看说明。",
            TextWrapping = TextWrapping.Wrap,
            Padding = new Thickness(8, 6, 8, 6),
            Foreground = Brush(148, 163, 184)
        };
        _search = new TextBox { Margin = new Thickness(6), ToolTip = "搜索参数、分类或说明" };
        _searchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
        _searchTimer.Tick += (_, _) => { _searchTimer.Stop(); Rebuild(); };
        _search.TextChanged += (_, _) => { _searchTimer.Stop(); _searchTimer.Start(); };
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _content };
        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(36) });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(76) });
        layout.Children.Add(_search);
        Grid.SetRow(scroll, 1);
        layout.Children.Add(scroll);
        Grid.SetRow(_details, 2);
        layout.Children.Add(_details);
        Content = layout;
        _propertyTree.SelectedItemChanged += (_, eventArgs) =>
        {
            if (eventArgs.NewValue is TreeViewItem { Tag: WorkflowPropertyEntry entry })
                _details.Text = $"{entry.DisplayName}\n{entry.Description}";
        };
        Unloaded += (_, _) => { _searchTimer.Stop(); DisposeModel(); };
        Loaded += (_, _) => RecreateModel();
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

    /// <summary>在专用脚本页面存在时隐藏参数表中的脚本正文。</summary>
    public bool HideScriptProperty
    {
        get => _hideScriptProperty;
        set
        {
            if (_hideScriptProperty == value) return;
            _hideScriptProperty = value;
            Rebuild();
        }
    }

    /// <summary>专用展示区域已承载 Block 等操作时隐藏重复快捷按钮。</summary>
    public bool HideSpecialActions { get; set; }

    /// <summary>
    /// 获取或设置候选值提供者。宿主用它把机器配置（例如已发布的逻辑图像源）注入参数面板；
    /// 未设置时候选编辑器退回文本输入，不会因为宿主未装配而无法编辑。
    /// </summary>
    public WorkflowPropertyChoiceProvider? ChoiceProvider
    {
        get => _choiceProvider;
        set
        {
            if (ReferenceEquals(_choiceProvider, value)) return;
            _choiceProvider = value;
            RecreateModel();
        }
    }

    /// <summary>获取或设置 Edit Error 成员。</summary>
    public event EventHandler<string>? EditError;

    /// <summary>获取或设置 Block Mapping Edit Requested 成员。</summary>
    public event EventHandler<IWorkflowBlockMappingNode>? BlockMappingEditRequested;

    private static void OnConfigurationChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e) =>
        ((WorkflowPropertyPanel)dependencyObject).RecreateModel();

    /// <summary>执行 Recreate Model 相关处理。</summary>
    private void RecreateModel()
    {
        DisposeModel();
        if (Session is not null && !string.IsNullOrWhiteSpace(EntryNodeId))
        {
            _model = new WorkflowPropertyInspectorModel(Session, EntryNodeId, _choiceProvider, _additionalProperties);
            _model.Changed += OnModelChanged;
        }
        Rebuild();
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
            Rebuild();
        else
            _ = Dispatcher.BeginInvoke(Rebuild);
    }

    /// <summary>执行 Rebuild 相关处理。</summary>
    private void Rebuild()
    {
        if (_building)
            return;
        _building = true;
        _inputEditorGeneration++;
        try
        {
            _content.Children.Clear();
            _propertyTree.Items.Clear();
            _groupAncestors.Clear();
            if (_model?.SelectedNode is null)
            {
                _content.Children.Add(new TextBlock
                {
                    Text = "选择节点以编辑属性",
                    Foreground = Brush(100, 116, 139),
                    Margin = new Thickness(8, 16, 8, 8),
                    HorizontalAlignment = HorizontalAlignment.Center
                });
                return;
            }
            var visibleEntries = _model.Entries
                .Where(entry => !HideScriptProperty || entry.Name != nameof(IWorkflowScriptNode.Script))
                .Where(MatchesSearch)
                .ToArray();
            _content.Children.Add(CreateColumnHeader());
            if (!HideSpecialActions && _model.SelectedNode is IWorkflowBlockMappingNode block)
            {
                var mappingButton = new Button
                {
                    Content = "编辑输入/输出映射…",
                    Margin = new Thickness(0, 0, 0, 6),
                    Padding = new Thickness(8),
                    Foreground = Foreground,
                    Background = Brush(30, 64, 94)
                };
                mappingButton.Click += (_, _) => BlockMappingEditRequested?.Invoke(this, block);
                _content.Children.Add(mappingButton);
            }
            AddOutputPortVisibilityEditors();
            foreach (var category in visibleEntries.GroupBy(entry => entry.Category, StringComparer.Ordinal))
            {
                _content.Children.Add(CreateCategory(category.Key));
                AddGroupedRows(category.Key, category.ToArray());
            }
        }
        finally
        {
            _building = false;
            ApplyFixedStyle(_content);
        }
    }

    /// <summary>创建Category。</summary>
    private FrameworkElement CreateCategory(string category)
    {
        var button = new Button
        {
            Content = $"{(_collapsedCategories.Contains(category) ? "▶" : "▼")}  {category}",
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Foreground = Brush(56, 189, 248),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Height = 27,
            Margin = new Thickness(0, 4, 0, 0),
            FontWeight = FontWeights.SemiBold
        };
        button.Click += (_, _) =>
        {
            var collapsed = _collapsedCategories.Add(category);
            if (!collapsed) _collapsedCategories.Remove(category);
            button.Content = $"{(collapsed ? "▶" : "▼")}  {category}";
            foreach (var child in _content.Children.OfType<FrameworkElement>())
                if (child.Tag is string rowCategory && rowCategory == category)
                    child.Visibility = collapsed || (_search.Text.Trim().Length == 0 && _groupAncestors.TryGetValue(child, out var ancestors)
                        && ancestors.Any(_collapsedCategories.Contains)) ? Visibility.Collapsed : Visibility.Visible;
        };
        return button;
    }

    private void AddGroupedRows(string category, IReadOnlyList<WorkflowPropertyEntry> entries)
    {
        AddLevel(entries, [], 0);
        void AddLevel(IReadOnlyList<WorkflowPropertyEntry> current, string[] ancestors, int depth)
        {
            foreach (var entry in current.Where(entry => entry.GroupPath.Count <= depth))
            {
                var row = CreateRow(entry);
                _content.Children.Add(row);
                _groupAncestors[row] = ancestors;
                UpdateVisibility(row);
            }
            foreach (var group in current.Where(entry => entry.GroupPath.Count > depth)
                         .GroupBy(entry => entry.GroupPath[depth], StringComparer.Ordinal))
            {
                var key = (ancestors.LastOrDefault() ?? "group:" + category.Length + ":" + category) + ":" + group.Key.Length + ":" + group.Key;
                var button = new Button
                {
                    Content = Header(key, group.Key),
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    Background = Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    Foreground = Foreground,
                    Padding = new Thickness((depth + 1) * 12, 4, 4, 4),
                    Tag = category
                };
                System.Windows.Automation.AutomationProperties.SetName(button, group.Key);
                button.Click += (_, _) =>
                {
                    if (_search.Text.Trim().Length != 0) return;
                    if (!_collapsedCategories.Add(key)) _collapsedCategories.Remove(key);
                    button.Content = Header(key, group.Key);
                    foreach (var row in _groupAncestors.Keys.Where(row => Equals(row.Tag, category))) UpdateVisibility(row);
                };
                _content.Children.Add(button);
                _groupAncestors[button] = ancestors;
                UpdateVisibility(button);
                AddLevel(group.ToArray(), [.. ancestors, key], depth + 1);
            }
        }
        string Header(string key, string text) => $"{(_collapsedCategories.Contains(key) && _search.Text.Trim().Length == 0 ? "▶" : "▼")}  {text}";
        void UpdateVisibility(FrameworkElement row) => row.Visibility = _collapsedCategories.Contains(category)
            || (_search.Text.Trim().Length == 0 && _groupAncestors[row].Any(_collapsedCategories.Contains)) ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>执行 Matches Search 相关处理。</summary>
    private bool MatchesSearch(WorkflowPropertyEntry entry)
    {
        var search = _search.Text.Trim();
        return search.Length == 0
            || entry.DisplayName.Contains(search, StringComparison.OrdinalIgnoreCase)
            || entry.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
            || entry.Category.Contains(search, StringComparison.OrdinalIgnoreCase)
            || entry.GroupPath.Any(group => group.Contains(search, StringComparison.OrdinalIgnoreCase))
            || entry.Description.Contains(search, StringComparison.OrdinalIgnoreCase)
            || (Convert.ToString(entry.Value)?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false);
    }

    /// <summary>显示Details。</summary>
    private void ShowDetails(WorkflowPropertyEntry entry) =>
        _details.Text = $"{entry.DisplayName}\n{entry.Description}\n属性：{entry.Name}    类型：{entry.ValueType.Name}";

    /// <summary>添加Output Port Visibility Editors。</summary>
    private void AddOutputPortVisibilityEditors()
    {
        if (Session is null || _model?.SelectedNode is null) return;
        var outputs = Session.GetDeclaredPorts(_model.SelectedNode.Id, WorkflowPortDirection.Output);
        if (outputs.Count <= 1) return;
        _content.Children.Add(new TextBlock
        {
            Text = "输出端口",
            Foreground = Brush(56, 189, 248),
            Margin = new Thickness(4, 10, 4, 5),
            FontWeight = FontWeights.SemiBold
        });
        var canvasNode = Session.Canvas.Nodes.First(item => item.Node.Id == _model.SelectedNode.Id);
        foreach (var port in outputs)
        {
            var check = new CheckBox
            {
                Content = $"显示并启用 {WorkflowPorts.GetDisplayName(port.Key)}",
                IsChecked = !canvasNode.HiddenOutputPorts.Contains(port.Key),
                Foreground = Foreground,
                Margin = new Thickness(7, 5, 7, 5)
            };
            check.Checked += (_, _) =>
            {
                if (!_building) Session.SetOutputPortVisible(canvasNode.Node.Id, port.Key, true);
            };
            check.Unchecked += (_, _) =>
            {
                if (!_building) Session.SetOutputPortVisible(canvasNode.Node.Id, port.Key, false);
            };
            _content.Children.Add(check);
        }
    }

    /// <summary>创建Column Header。</summary>
    private FrameworkElement CreateColumnHeader()
    {
        var grid = new Grid { Height = 27, Background = Brush(30, 41, 59) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.36, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.64, GridUnitType.Star) });
        grid.Children.Add(new TextBlock { Text = "属性", Margin = new Thickness(6), VerticalAlignment = VerticalAlignment.Center });
        var value = new TextBlock { Text = "值", Margin = new Thickness(6), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(value, 1);
        grid.Children.Add(value);
        return grid;
    }

    /// <summary>创建Row。</summary>
    private FrameworkElement CreateRow(WorkflowPropertyEntry entry)
    {
        var grid = new Grid
        {
            Background = Brush(22, 32, 49),
            Margin = new Thickness(0, 1, 0, 1),
            MinHeight = 30,
            Tag = entry.Category
        };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.36, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.64, GridUnitType.Star) });
        var label = new TextBlock
        {
            Text = entry.DisplayName,
            Foreground = Brush(148, 163, 184),
            Margin = new Thickness(6, 3, 6, 3),
            Padding = new Thickness(entry.GroupPath.Count * 12, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        grid.Children.Add(label);
        var editor = CreateEditor(entry);
        Grid.SetColumn(editor, 1);
        grid.Children.Add(editor);
        grid.AddHandler(
            System.Windows.Input.Mouse.PreviewMouseDownEvent,
            new System.Windows.Input.MouseButtonEventHandler((_, _) => ShowDetails(entry)),
            true);
        grid.AddHandler(
            System.Windows.Input.Keyboard.GotKeyboardFocusEvent,
            new System.Windows.Input.KeyboardFocusChangedEventHandler((_, _) => ShowDetails(entry)),
            true);
        return grid;
    }

    /// <summary>创建Editor。</summary>
    private FrameworkElement CreateEditor(WorkflowPropertyEntry entry)
    {
        if (entry.EditorKind == WorkflowPropertyEditorKind.Action)
        {
            var nodeId = _model?.SelectedNode?.Id;
            var generation = _inputEditorGeneration;
            var button = new Button { Content = Convert.ToString(entry.Value) ?? "打开编辑器…", Margin = new Thickness(4), IsEnabled = entry.ActionBlockReason.Length == 0 };
            button.Click += async (_, _) =>
            {
                if (generation != _inputEditorGeneration || nodeId != _model?.SelectedNode?.Id) return;
                if (entry.HasActionHandler)
                {
                    button.IsEnabled = false;
                    try { await entry.ExecuteActionAsync(); }
                    catch (Exception ex) { EditError?.Invoke(this, ex.Message); }
                    finally { if (generation == _inputEditorGeneration) button.IsEnabled = entry.ActionBlockReason.Length == 0; }
                    return;
                }
                if (PropertyActionRequested == null) { EditError?.Invoke(this, "宿主未注册此属性的编辑窗口。"); return; }
                PropertyActionRequested.Invoke(this, new WorkflowPropertyActionRequest(nodeId!, entry.EditorKey!));
            };
            return button;
        }
        if (entry.EditorKind == WorkflowPropertyEditorKind.ReadOnly)
            return Text(Convert.ToString(entry.Value) ?? string.Empty, false);
        if (entry.EditorKind == WorkflowPropertyEditorKind.Boolean)
        {
            var check = new CheckBox
            {
                IsChecked = entry.Value is true,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(6)
            };
            check.Checked += (_, _) => TryEdit(() => _model!.SetValue(entry, true));
            check.Unchecked += (_, _) => TryEdit(() => _model!.SetValue(entry, false));
            return check;
        }
        if (entry.EditorKind == WorkflowPropertyEditorKind.Enum)
        {
            var type = Nullable.GetUnderlyingType(entry.ValueType) ?? entry.ValueType;
            var combo = Combo(Enum.GetValues(type).Cast<object>().Distinct());
            combo.SelectedItem = entry.Value;
            combo.SelectionChanged += (_, _) =>
            {
                if (combo.SelectedItem is not null)
                    TryEdit(() => _model!.SetValue(entry, combo.SelectedItem));
            };
            return combo;
        }
        if (entry.EditorKind == WorkflowPropertyEditorKind.Choice)
        {
            var combo = Combo(entry.Choices);
            combo.SelectedItem = entry.Choices.FirstOrDefault(choice => Equals(choice.Value, entry.Value));
            combo.SelectionChanged += (_, _) =>
            {
                if (combo.SelectedItem is WorkflowPropertyChoice choice)
                    TryEdit(() => _model!.SetValue(entry, choice.Value));
            };
            return combo;
        }
        if (string.Equals(entry.EditorKey, WorkflowPropertyEditorKeys.FilePath, StringComparison.Ordinal)
            || string.Equals(entry.EditorKey, WorkflowPropertyEditorKeys.FolderPath, StringComparison.Ordinal))
            return CreatePathEditor(entry);
        if (entry.EditorKind == WorkflowPropertyEditorKind.WorkflowInput)
            return CreateInputEditor(entry);
        if (entry.EditorKind == WorkflowPropertyEditorKind.Script)
        {
            var button = new Button { Content = "打开智能脚本编辑器…", Margin = new Thickness(4) };
            button.Click += (_, _) => EditScript(entry);
            return button;
        }
        if (entry.EditorKind == WorkflowPropertyEditorKind.Structured)
        {
            var button = new Button { Content = "编辑集合/对象…", Margin = new Thickness(4) };
            button.Click += (_, _) => EditStructuredValue(entry);
            return button;
        }
        var text = Text(Convert.ToString(entry.Value, CultureInfo.InvariantCulture) ?? string.Empty, true);
        // 只提交用户实际改过的文字：失去焦点时，未编辑的旧显示值不能覆盖别处已更新的属性。
        var shown = text.Text;
        text.LostFocus += (_, _) =>
        {
            if (text.Text == shown) return;
            shown = text.Text;
            TryEdit(() => _model!.SetValue(entry, text.Text));
        };
        return text;
    }

    /// <summary>请求打开由插件页面提供的独立属性编辑窗口。</summary>
    public event EventHandler<WorkflowPropertyActionRequest>? PropertyActionRequested;

    /// <summary>创建Path Editor。</summary>
    private FrameworkElement CreatePathEditor(WorkflowPropertyEntry entry)
    {
        var text = Text(Convert.ToString(entry.Value) ?? string.Empty, true);
        var browse = new Button
        {
            Content = "…",
            Width = 32,
            Margin = new Thickness(2, 4, 4, 4),
            ToolTip = $"浏览{entry.DisplayName}"
        };
        // 只提交用户实际改过的文字：失去焦点时，未编辑的旧显示值不能覆盖别处已更新的属性。
        var shown = text.Text;
        text.LostFocus += (_, _) =>
        {
            if (text.Text == shown) return;
            shown = text.Text;
            TryEdit(() => _model!.SetValue(entry, text.Text));
        };
        browse.Click += (_, _) =>
        {
            if (string.Equals(entry.EditorKey, WorkflowPropertyEditorKeys.FilePath, StringComparison.Ordinal))
            {
                var dialog = new Microsoft.Win32.OpenFileDialog
                {
                    Title = string.IsNullOrWhiteSpace(entry.EditorDialogTitle) ? $"选择{entry.DisplayName}" : entry.EditorDialogTitle,
                    Filter = string.IsNullOrWhiteSpace(entry.EditorFilter) ? "所有文件|*.*" : entry.EditorFilter,
                    CheckFileExists = entry.EditorCheckExists,
                    Multiselect = false,
                    FileName = text.Text
                };
                if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
                text.Text = dialog.FileName;
            }
            else
            {
                var dialog = new Microsoft.Win32.OpenFolderDialog
                {
                    Title = string.IsNullOrWhiteSpace(entry.EditorDialogTitle) ? $"选择{entry.DisplayName}" : entry.EditorDialogTitle,
                    Multiselect = false,
                    InitialDirectory = Directory.Exists(text.Text) ? text.Text : string.Empty
                };
                if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
                text.Text = dialog.FolderName;
            }
            shown = text.Text;
            TryEdit(() => _model!.SetValue(entry, text.Text));
        };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(text);
        Grid.SetColumn(browse, 1);
        grid.Children.Add(browse);
        return grid;
    }

    /// <summary>执行 Edit Script 相关处理。</summary>
    private void EditScript(WorkflowPropertyEntry entry)
    {
        var page = WorkflowScriptEditorPageModel.CreateBuffer(Convert.ToString(entry.Value));
        var workspace = new WorkflowCSharpScriptEditorControl { Page = page };
        var ok = new Button { Content = "确定", Width = 76, Margin = new Thickness(4), IsDefault = true };
        var cancel = new Button { Content = "取消", Width = 76, Margin = new Thickness(4), IsCancel = true };
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(4)
        };
        actions.Children.Add(ok);
        actions.Children.Add(cancel);
        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.Children.Add(workspace);
        Grid.SetRow(actions, 1);
        layout.Children.Add(actions);
        var dialog = new Window
        {
            Title = $"C# 脚本 - {entry.DisplayName}",
            Owner = Window.GetWindow(this),
            Content = layout,
            Width = 960,
            Height = 640,
            MinWidth = 760,
            MinHeight = 480,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        WorkflowWpfStyle.Apply(dialog);
        dialog.Loaded += (_, _) => workspace.FocusEditor();
        ok.Click += (_, _) =>
        {
            if (workspace.CompilationState != WorkflowScriptCompilationState.Succeeded)
            {
                MessageBox.Show(dialog, "保存前必须点击“编译”，并确保当前 C# 代码编译通过。", "尚未编译", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            dialog.DialogResult = true;
            dialog.Close();
        };
        if (dialog.ShowDialog() == true)
            TryEdit(() => _model!.SetValue(entry, page.Script));
    }

    /// <summary>执行 Edit Structured Value 相关处理。</summary>
    private void EditStructuredValue(WorkflowPropertyEntry entry)
    {
        if (WorkflowCollectionTableModel.TryCreate(entry, out var table) && table is not null)
        {
            EditCollectionTable(table, entry.DisplayName);
            return;
        }
        var editor = new TextBox
        {
            Text = entry.GetStructuredJson(),
            AcceptsReturn = true,
            AcceptsTab = true,
            TextWrapping = TextWrapping.NoWrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            FontFamily = new FontFamily("Consolas")
        };
        var ok = new Button { Content = "确定", Width = 76, Margin = new Thickness(4), IsDefault = true };
        var cancel = new Button { Content = "取消", Width = 76, Margin = new Thickness(4), IsCancel = true };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.Children.Add(editor);
        Grid.SetRow(buttons, 1);
        layout.Children.Add(buttons);
        var dialog = new Window
        {
            Title = $"编辑 {entry.DisplayName}",
            Owner = Window.GetWindow(this),
            Content = layout,
            Width = 740,
            Height = 560,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        ok.Click += (_, _) => { dialog.DialogResult = true; dialog.Close(); };
        if (dialog.ShowDialog() == true)
            TryEdit(() => _model!.SetStructuredJson(entry, editor.Text));
    }

    /// <summary>执行 Edit Collection Table 相关处理。</summary>
    private void EditCollectionTable(WorkflowCollectionTableModel table, string displayName)
    {
        var data = new DataTable();
        foreach (var column in table.Columns) data.Columns.Add(column, typeof(string));
        foreach (var row in table.Rows) data.Rows.Add(row.Cast<object>().ToArray());
        var grid = new DataGrid
        {
            ItemsSource = data.DefaultView,
            AutoGenerateColumns = true,
            CanUserAddRows = true,
            CanUserDeleteRows = true,
            Background = Brush(15, 23, 42),
            Foreground = Brush(226, 232, 240)
        };
        var ok = new Button { Content = "确定", Width = 76, Margin = new Thickness(4), IsDefault = true };
        var cancel = new Button { Content = "取消", Width = 76, Margin = new Thickness(4), IsCancel = true };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.Children.Add(grid);
        Grid.SetRow(buttons, 1);
        layout.Children.Add(buttons);
        var dialog = new Window
        {
            Title = $"表格编辑 - {displayName}", Owner = Window.GetWindow(this), Content = layout,
            Width = 840, Height = 580, WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        ok.Click += (_, _) => { grid.CommitEdit(); dialog.DialogResult = true; dialog.Close(); };
        if (dialog.ShowDialog() != true) return;
        var rows = data.Rows.Cast<DataRow>()
            .Where(row => row.RowState != DataRowState.Deleted)
            .Select(row => (IReadOnlyList<string>)data.Columns.Cast<DataColumn>()
                .Select(column => Convert.ToString(row[column], CultureInfo.InvariantCulture) ?? string.Empty)
                .ToArray())
            .ToArray();
        TryEdit(() => _model!.ApplyCollectionTable(table, rows));
    }

    /// <summary>创建Input Editor。</summary>
    private FrameworkElement CreateInputEditor(WorkflowPropertyEntry entry)
    {
        var generation = _inputEditorGeneration;
        var model = _model!;
        var node = model.SelectedNode;
        var panel = new Grid { Margin = new Thickness(3) };
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.42, GridUnitType.Star) });
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.58, GridUnitType.Star) });
        var source = Combo(Enum.GetValues<WorkflowValueSource>().Cast<object>());
        source.SelectedItem = entry.GetInputSource();
        panel.Children.Add(source);

        bool IsCurrentEditor() => generation == _inputEditorGeneration
            && ReferenceEquals(model, _model) && ReferenceEquals(node, model.SelectedNode);

        FrameworkElement valueEditor;
        if (entry.GetInputSource() == WorkflowValueSource.Binding)
        {
            var candidates = _model!.GetBindingCandidates(entry);
            var binding = entry.GetInputBinding();
            var button = new Button
            {
                Content = "▼  " + (candidates.FirstOrDefault(item => item.ToBindingKey() == binding)?.DisplayPath
                    ?? binding?.ToString()
                    ?? "选择绑定…"),
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Background = Brush(15, 23, 42),
                Foreground = Foreground,
                BorderBrush = Brush(51, 65, 85)
            };
            button.Click += (_, _) =>
            {
                if (!IsCurrentEditor()) return;
                var window = new WorkflowBindingSelectorWindow(candidates, entry.GetInputBinding())
                {
                    Owner = Window.GetWindow(this)
                };
                if (window.ShowDialog() == true && window.SelectedCandidate is { } candidate && IsCurrentEditor())
                    TryEdit(() => _model.SetWorkflowInput(entry, WorkflowValueSource.Binding, null, candidate.ToBindingKey()));
            };
            valueEditor = button;
        }
        else
        {
            var editable = entry.CanEditInputLiteralAsText;
            var text = Text(editable
                ? Convert.ToString(entry.GetInputLiteral(), CultureInfo.InvariantCulture) ?? string.Empty
                : $"{entry.WorkflowInputType!.Name}：请使用绑定或专用编辑器", editable);
            if (editable)
                text.LostFocus += (_, _) =>
                {
                    if (!IsCurrentEditor() || entry.GetInputSource() != WorkflowValueSource.Literal
                        || source.SelectedItem is not WorkflowValueSource.Literal) return;
                    if (text.Text == Convert.ToString(entry.GetInputLiteral(), CultureInfo.InvariantCulture)) return;
                    TryEdit(() => _model!.SetWorkflowInput(entry, WorkflowValueSource.Literal, text.Text, null));
                };
            valueEditor = text;
        }
        Grid.SetColumn(valueEditor, 1);
        panel.Children.Add(valueEditor);
        source.SelectionChanged += (_, _) =>
        {
            if (!IsCurrentEditor()) return;
            if (source.SelectedItem is not WorkflowValueSource selected || selected == entry.GetInputSource())
                return;
            if (selected == WorkflowValueSource.Literal)
                TryEdit(() => _model!.SetWorkflowInput(entry, selected, entry.GetInputLiteral(), null));
            else
            {
                var candidates = _model!.GetBindingCandidates(entry);
                if (candidates.Count == 0)
                {
                    EditError?.Invoke(this, "当前节点没有可用的强类型绑定候选。");
                    Rebuild();
                    return;
                }
                var window = new WorkflowBindingSelectorWindow(candidates, entry.GetInputBinding())
                {
                    Owner = Window.GetWindow(this)
                };
                if (window.ShowDialog() == true && window.SelectedCandidate is { } candidate && IsCurrentEditor())
                    TryEdit(() => _model.SetWorkflowInput(entry, selected, null, candidate.ToBindingKey()));
                else
                    Rebuild();
            }
        };
        return panel;
    }

    private void TryEdit(Action action)
    {
        if (_building)
            return;
        try
        {
            action();
        }
        catch (Exception exception) when (exception is FormatException or InvalidOperationException
                                          or ArgumentException or OverflowException or InvalidCastException or System.Text.Json.JsonException)
        {
            EditError?.Invoke(this, exception.Message);
            Rebuild();
        }
    }

    /// <summary>应用Fixed Style。</summary>
    private void ApplyFixedStyle(DependencyObject root)
    {
        var background = Brush(15, 23, 42);
        var surface = Brush(22, 32, 49);
        var foreground = Brush(226, 232, 240);
        if (root is Control control)
        {
            control.Foreground = foreground;
            control.Background = root is TextBox or ComboBox ? surface : background;
        }
        else if (root is Panel panel)
            panel.Background = background;
        else if (root is TextBlock text)
            text.Foreground = foreground;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            ApplyFixedStyle(VisualTreeHelper.GetChild(root, index));
    }

    private TextBox Text(string value, bool editable) => new()
    {
        Text = value,
        IsReadOnly = !editable,
        Background = Brush(15, 23, 42),
        Foreground = editable ? Foreground : Brush(100, 116, 139),
        BorderBrush = Brush(51, 65, 85),
        Margin = new Thickness(4),
        Padding = new Thickness(4),
        VerticalContentAlignment = VerticalAlignment.Center
    };

    /// <summary>执行 Combo 相关处理。</summary>
    private ComboBox Combo(IEnumerable<object> values)
    {
        var itemStyle = new Style(typeof(ComboBoxItem));
        itemStyle.Setters.Add(new Setter(Control.ForegroundProperty, Foreground));
        itemStyle.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        var selected = new Trigger { Property = ComboBoxItem.IsSelectedProperty, Value = true };
        selected.Setters.Add(new Setter(Control.BackgroundProperty, Brush(37, 99, 235)));
        selected.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
        itemStyle.Triggers.Add(selected);
        return new ComboBox
        {
            ItemsSource = values,
            Background = Brush(15, 23, 42),
            Foreground = Foreground,
            ItemContainerStyle = itemStyle,
            Margin = new Thickness(4),
            Padding = new Thickness(2)
        };
    }

    /// <summary>创建并冻结指定 RGB 颜色的 WPF 画刷。</summary>
    private static SolidColorBrush Brush(byte red, byte green, byte blue)
    {
        var brush = new SolidColorBrush(Color.FromRgb(red, green, blue));
        brush.Freeze();
        return brush;
    }
}
