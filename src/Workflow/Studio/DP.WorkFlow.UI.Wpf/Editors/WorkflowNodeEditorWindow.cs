using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DP.WorkFlow.UI;

namespace DP.WorkFlow.UI.Wpf;

/// <summary>WPF 节点工作台自定义页面渲染器。</summary>
public interface IWorkflowWpfNodeEditorPageRenderer
{
    /// <summary>获取与 UI 无关页面描述匹配的稳定渲染器键。</summary>
    string RendererKey { get; }

    /// <summary>获取 Renderer 接受的强类型页面模型。</summary>
    Type ModelType { get; }

    /// <summary>为页面创建 WPF 元素。</summary>
    /// <param name="page">UI 无关页面描述。</param>
    /// <returns>平台元素。</returns>
    FrameworkElement CreateElement(WorkflowNodeEditorPageDescriptor page);
}

/// <summary>可自动容纳参数、子流程、脚本、图像和宿主扩展页面的统一 WPF 节点工作台。</summary>
public sealed class WorkflowNodeEditorWindow : Window
{
    private readonly WorkflowNodeEditorModel _model;
    private readonly ContentControl _host;
    private readonly ListBox _navigation;
    private readonly IReadOnlyDictionary<string, IWorkflowWpfNodeEditorPageRenderer> _renderers;
    private readonly Action<IWorkflowBlockMappingNode>? _editMappings;

    /// <summary>初始化节点综合编辑窗口并创建对应页面。</summary>
    public WorkflowNodeEditorWindow(
        WorkflowNodeEditorModel model,
        IEnumerable<IWorkflowWpfNodeEditorPageRenderer>? renderers = null,
        Action<IWorkflowBlockMappingNode>? editMappings = null)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
        _renderers = CreateRendererIndex(renderers);
        ValidatePageRenderers(model.Pages, _renderers);
        _editMappings = editMappings;
        Title = model.PropertyEditorKey == null ? $"节点信息 - {model.Node.Title}"
            : $"{model.Pages.Single(p => p.PropertyEditorKey == model.PropertyEditorKey).Title} - {model.Node.Title}";
        var hasSpecialContent = model.Pages.Any(page => page.Kind is not (WorkflowNodeEditorPageKind.Properties or WorkflowNodeEditorPageKind.Diagnostics));
        // 脚本窗口要适配常见的 1366×768 工作区，不能默认超出屏幕高度。
        Width = hasSpecialContent ? 1060 : 740;
        Height = hasSpecialContent ? 720 : 680;
        MinWidth = hasSpecialContent ? 760 : 620;
        MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        WorkflowWpfStyle.Apply(this);

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(model.PropertyEditorKey == null ? 112 : 0) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(48) });
        if (model.PropertyEditorKey == null) root.Children.Add(CreateHeader());
        _navigation = new ListBox { Visibility = Visibility.Collapsed };
        _host = new ContentControl { Content = CreateWorkspace() };
        Grid.SetRow(_host, 1);
        root.Children.Add(_host);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var apply = Button("应用");
        var ok = Button("确定");
        var cancel = Button("取消");
        ok.IsDefault = true;
        cancel.IsCancel = true;
        apply.Click += (_, _) => ApplyChanges();
        ok.Click += (_, _) => { if (ApplyChanges()) { DialogResult = true; Close(); } };
        cancel.Click += (_, _) => { DialogResult = false; Close(); };
        buttons.Children.Add(apply);
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        if (model.Pages.Any(p => p.Model is IWorkflowNodeEditorCommitReadiness))
        {
            var readiness = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
            void UpdateReadiness() { apply.IsEnabled = ok.IsEnabled = model.CanApplyChanges; }
            readiness.Tick += (_, _) => UpdateReadiness(); UpdateReadiness(); readiness.Start(); Closed += (_, _) => readiness.Stop();
        }
        Grid.SetRow(buttons, 2);
        root.Children.Add(buttons);
        Content = root;
        Loaded += (_, _) => FitToWorkingArea();
        Closed += async (_, _) => await _model.DisposeAsync();
    }

    /// <summary>按系统工作区限制窗口大小，避免高 DPI 或低分辨率下底部命令被裁剪。</summary>
    private void FitToWorkingArea()
    {
        var workingArea = SystemParameters.WorkArea;
        Width = Math.Min(Width, Math.Max(MinWidth, workingArea.Width - 24));
        Height = Math.Min(Height, Math.Max(MinHeight, workingArea.Height - 24));
    }

    /// <summary>创建Header。</summary>
    private FrameworkElement CreateHeader()
    {
        var grid = new Grid { Margin = new Thickness(12, 8, 12, 8) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(92) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var index = 0; index < 3; index++) grid.RowDefinitions.Add(new RowDefinition());
        AddHeaderRow(grid, 0, "节点名称", _model.EditingNode.Id, true);
        AddHeaderRow(grid, 1, "节点类型", _model.EditingNode.NodeType, true);
        AddHeaderRow(grid, 2, "标题", _model.EditingNode.Title, false);
        return grid;
    }

    /// <summary>添加Header Row。</summary>
    /// <param name="grid">“grid”参数。</param>
    /// <param name="row">“row”参数。</param>
    /// <param name="title">“title”参数。</param>
    /// <param name="value">要转换或设置的值。</param>
    /// <param name="readOnly">“readOnly”参数。</param>
    private void AddHeaderRow(Grid grid, int row, string title, string value, bool readOnly)
    {
        var label = new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(4) };
        Grid.SetRow(label, row);
        grid.Children.Add(label);
        var text = new TextBox { Text = value, IsReadOnly = readOnly, Margin = new Thickness(4), VerticalContentAlignment = VerticalAlignment.Center };
        if (!readOnly) text.LostKeyboardFocus += (_, _) =>
        {
            if (_model.EditingNode.Title == text.Text) return;
            _model.EditingSession.ExecuteNodeConfigurationChange(
                _model.EditingNode.Id,
                node => node.Title = text.Text);
            Title = $"节点信息 - {_model.EditingNode.Title}";
        };
        Grid.SetRow(text, row);
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
    }

    /// <summary>创建Workspace。</summary>
    private FrameworkElement CreateWorkspace()
    {
        if (_model.PropertyEditorKey is { } key)
            return CreatePageElement(_model.Pages.Single(p => p.PropertyEditorKey == key));
        var propertiesPage = _model.Pages.Single(page => page.Kind == WorkflowNodeEditorPageKind.Properties);
        var properties = CreatePageElement(propertiesPage);
        var specialPages = _model.Pages
            .Where(page => page.PropertyEditorKey == null && page.Kind is not (WorkflowNodeEditorPageKind.Properties or WorkflowNodeEditorPageKind.Diagnostics))
            .ToArray();
        if (specialPages.Length == 0) return properties;

        var specialHost = new Grid();
        for (var index = 0; index < specialPages.Length; index++)
        {
            specialHost.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            var special = CreatePageElement(specialPages[index]);
            var group = new GroupBox { Header = specialPages[index].Title, Content = special, Margin = new Thickness(3) };
            Grid.SetRow(group, index);
            specialHost.Children.Add(group);
        }
        var layout = new Grid();
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(340), MinWidth = 280 });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(5) });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 360 });
        layout.Children.Add(properties);
        var splitter = new GridSplitter { Width = 5, HorizontalAlignment = HorizontalAlignment.Stretch, Background = Brush(51, 51, 55) };
        Grid.SetColumn(splitter, 1);
        layout.Children.Add(splitter);
        Grid.SetColumn(specialHost, 2);
        layout.Children.Add(specialHost);
        return layout;
    }

    /// <summary>应用Changes。</summary>
    private bool ApplyChanges()
    {
        try
        {
            _model.ApplyChanges();
            return true;
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException or FormatException or OverflowException)
        {
            MessageBox.Show(this, exception.Message, "参数校验失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
    }

    /// <summary>创建Page Element。</summary>
    private FrameworkElement CreatePageElement(WorkflowNodeEditorPageDescriptor page)
    {
        var rendererKey = ResolveRendererKey(page);
        if (rendererKey is not null && _renderers.TryGetValue(rendererKey, out var renderer))
        {
            if (!renderer.ModelType.IsInstanceOfType(page.Model))
                throw new InvalidOperationException(
                    $"WPF Renderer“{rendererKey}”要求 {renderer.ModelType.FullName}，页面“{page.PageId}”提供了 {page.Model.GetType().FullName}。");
            return renderer.CreateElement(page);
        }
        return page.Kind switch
        {
            WorkflowNodeEditorPageKind.Properties => CreateProperties((WorkflowPropertyEditorPageModel)page.Model),
            WorkflowNodeEditorPageKind.SubWorkflow => CreateSubWorkflow((WorkflowSubWorkflowEditorPageModel)page.Model),
            WorkflowNodeEditorPageKind.Script => CreateScript((WorkflowScriptEditorPageModel)page.Model),
            WorkflowNodeEditorPageKind.Diagnostics => CreateDiagnostics((WorkflowScriptEditorPageModel)page.Model),
            _ => throw new InvalidOperationException($"WPF 不支持节点详情页类型 {page.Kind}：{page.PageId}。")
        };
    }

    /// <summary>创建Properties。</summary>
    private FrameworkElement CreateProperties(WorkflowPropertyEditorPageModel page)
    {
        var panel = new WorkflowPropertyPanel
        {
            ChoiceProvider = page.ChoiceProvider,
            AdditionalProperties = page.AdditionalProperties,
            Session = page.Session,
            EntryNodeId = page.EntryNodeId,
            HideScriptProperty = _model.EditingNode is IWorkflowScriptNode,
            HideSpecialActions = _model.Pages.Any(item => item.Kind != WorkflowNodeEditorPageKind.Properties)
        };
        panel.PropertyActionRequested += async (_, request) =>
        {
            WorkflowNodeEditorModel? child = null;
            try
            {
                child = _model.CreatePropertyEditor(request.EditorKey);
                var dialog = new WorkflowNodeEditorWindow(child, _renderers.Values, _editMappings) { Owner = this };
                dialog.ShowDialog();
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "属性编辑失败", MessageBoxButton.OK, MessageBoxImage.Warning); }
            finally { if (child != null) await child.DisposeAsync(); }
        };
        panel.EditError += (_, message) => MessageBox.Show(this, message, "参数错误", MessageBoxButton.OK, MessageBoxImage.Warning);
        if (_editMappings is not null) panel.BlockMappingEditRequested += (_, block) => _editMappings(block);
        return panel;
    }

    /// <summary>创建Sub DP.WorkFlow。</summary>
    private FrameworkElement CreateSubWorkflow(WorkflowSubWorkflowEditorPageModel page)
    {
        var designer = new WorkflowDesignerControl { Session = page.Session };
        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Height = 42 };
        var fit = Button("适合画布");
        var layout = Button("自动布局");
        fit.Click += (_, _) => page.Session.FitToView(designer.ActualWidth, designer.ActualHeight);
        layout.Click += (_, _) => page.Session.AutoLayout();
        toolbar.Children.Add(fit);
        toolbar.Children.Add(layout);
        if (_model.Node is IWorkflowBlockMappingNode block && _editMappings is not null)
        {
            var mappings = Button("输入/输出映射");
            mappings.Click += (_, _) => _editMappings(block);
            toolbar.Children.Add(mappings);
        }
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(toolbar);
        Grid.SetRow(designer, 1);
        grid.Children.Add(designer);
        return grid;
    }

    /// <summary>创建Script。</summary>
    private FrameworkElement CreateScript(WorkflowScriptEditorPageModel page) => new WorkflowCSharpScriptEditorControl
    {
        Page = page
    };

    /// <summary>创建Diagnostics。</summary>
    private static FrameworkElement CreateDiagnostics(WorkflowScriptEditorPageModel page)
    {
        var list = new ListBox();
        var diagnostics = page.GetDiagnostics();
        if (diagnostics.Count == 0) list.Items.Add("✓ 未发现脚本诊断。");
        else foreach (var item in diagnostics) list.Items.Add(item);
        return list;
    }

    private static IReadOnlyDictionary<string, IWorkflowWpfNodeEditorPageRenderer> CreateRendererIndex(
        IEnumerable<IWorkflowWpfNodeEditorPageRenderer>? renderers)
    {
        var index = new Dictionary<string, IWorkflowWpfNodeEditorPageRenderer>(StringComparer.Ordinal);
        foreach (var renderer in renderers ?? Array.Empty<IWorkflowWpfNodeEditorPageRenderer>())
        {
            if (string.IsNullOrWhiteSpace(renderer.RendererKey))
                throw new InvalidOperationException("WPF 节点详情页 RendererKey 不能为空。");
            if (!index.TryAdd(renderer.RendererKey.Trim(), renderer))
                throw new InvalidOperationException($"WPF 节点详情页渲染器“{renderer.RendererKey}”重复注册。");
        }
        return index;
    }

    private static string? ResolveRendererKey(WorkflowNodeEditorPageDescriptor page) =>
        !string.IsNullOrWhiteSpace(page.RendererKey) ? page.RendererKey.Trim() : null;

    private static void ValidatePageRenderers(
        IEnumerable<WorkflowNodeEditorPageDescriptor> pages,
        IReadOnlyDictionary<string, IWorkflowWpfNodeEditorPageRenderer> renderers)
    {
        foreach (var page in pages)
        {
            var rendererKey = ResolveRendererKey(page);
            if (rendererKey is null)
                continue;
            if (!renderers.TryGetValue(rendererKey, out var renderer))
                throw new InvalidOperationException(
                    $"WPF 节点详情页“{page.PageId}”缺少 Renderer“{rendererKey}”。");
            if (!renderer.ModelType.IsInstanceOfType(page.Model))
            {
                throw new InvalidOperationException(
                    $"WPF Renderer“{rendererKey}”要求 {renderer.ModelType.FullName}，页面“{page.PageId}”提供了 {page.Model.GetType().FullName}。");
            }
        }
    }

    /// <summary>执行 Thickness 相关处理。</summary>
    private static Button Button(string text) => new() { Content = text, Margin = new Thickness(5), Padding = new Thickness(9, 4, 9, 4) };
    /// <summary>执行 From Rgb 相关处理。</summary>
    private static SolidColorBrush Brush(byte r, byte g, byte b) => new(Color.FromRgb(r, g, b));
    /// <summary>定义 EditorPageItem 类型。</summary>
    private sealed record EditorPageItem(WorkflowNodeEditorPageDescriptor Page) { public string Title => Page.Title; }
}
