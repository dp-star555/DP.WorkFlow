using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using DP.LabelInspection.Contracts;
using DP.LabelInspection.Runtime;
using DP.WorkFlow.LabelInspection.UI;
using DP.WorkFlow.UI;
using DP.WorkFlow.UI.Wpf;
using Microsoft.Win32;

namespace DP.WorkFlow.LabelInspection.UI.Wpf;

/// <summary>
/// 标签节点的WPF页面：配方导入/导出与摘要、只读报告。WPF没有原生标签工作台，
/// ROI图上编辑与试检测请在WinForms配置页完成，或导入在其它工作台导出的配方。
/// </summary>
public sealed class LabelInspectionWpfExtension : IWorkflowWpfStudioExtension
{
    /// <summary>已经提交的图像预览来源。</summary>
    public IWorkflowVisionPreviewSource? FrameSource { get; init; }
    /// <inheritdoc/>
    public string ExtensionId => "workflow.label-inspection.wpf";
    /// <inheritdoc/>
    public void Register(WorkflowWpfStudioExtensionCatalog extensions)
    {
        ArgumentNullException.ThrowIfNull(extensions);
        extensions.RegisterPageProvider(new LabelInspectionEditorPageProvider(new OpenCvImageCodec(), FrameSource))
            .RegisterRenderer(new LabelRecipeWpfRenderer())
            .RegisterRenderer(new LabelReportWpfRenderer());
    }

    internal static Brush Background => Frozen(Color.FromRgb(30, 30, 30));
    internal static Brush Surface => Frozen(Color.FromRgb(37, 37, 38));
    internal static Brush Text => Brushes.Gainsboro;
    internal static Brush Muted => Frozen(Color.FromRgb(160, 160, 160));
    internal static Button Button(string text) => new()
    {
        Content = text, Margin = new Thickness(0, 0, 6, 0), Padding = new Thickness(10, 4, 10, 4),
        Background = Frozen(Color.FromRgb(45, 45, 48)), Foreground = Text, BorderBrush = Brushes.DimGray
    };
    private static Brush Frozen(Color color) { var brush = new SolidColorBrush(color); brush.Freeze(); return brush; }
}

/// <summary>WPF配方页：导入、导出配方JSON并显示摘要；确认前只改隔离节点。</summary>
public sealed class LabelRecipeWpfRenderer : IWorkflowWpfNodeEditorPageRenderer
{
    /// <inheritdoc/>
    public string RendererKey => LabelInspectionEditorPageProvider.RendererKey;
    /// <inheritdoc/>
    public Type ModelType => typeof(LabelInspectionEditorPageModel);
    /// <inheritdoc/>
    public FrameworkElement CreateElement(WorkflowNodeEditorPageDescriptor page) => new RecipeView((LabelInspectionEditorPageModel)page.Model);

    private sealed class RecipeView : DockPanel
    {
        private readonly LabelInspectionEditorPageModel _model;
        private readonly TextBlock _summary = new() { Margin = new Thickness(10), TextWrapping = TextWrapping.Wrap, Foreground = LabelInspectionWpfExtension.Text };
        private readonly TextBlock _status = new() { Margin = new Thickness(10, 4, 10, 6), TextWrapping = TextWrapping.Wrap, Foreground = LabelInspectionWpfExtension.Muted };

        internal RecipeView(LabelInspectionEditorPageModel model)
        {
            _model = model; Background = LabelInspectionWpfExtension.Background;
            var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 8, 10, 4) };
            var import = LabelInspectionWpfExtension.Button("导入配方");
            var export = LabelInspectionWpfExtension.Button("导出配方");
            import.Click += (_, _) => Guard(Import);
            export.Click += (_, _) => Guard(Export);
            toolbar.Children.Add(import); toolbar.Children.Add(export);
            SetDock(toolbar, Dock.Top); Children.Add(toolbar);
            SetDock(_status, Dock.Bottom); Children.Add(_status);
            Children.Add(new ScrollViewer { Content = _summary, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            _status.Text = "WPF 下支持导入/导出配方；ROI 图上编辑与试检测请使用 WinForms 配置页。导入在“确定/应用”时提交，取消不回写。";
            Refresh();
        }

        private void Guard(Action action)
        {
            try { action(); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException
                or ArgumentException or System.Text.Json.JsonException or FormatException)
            { _status.Text = error.Message; }
        }

        private void Import()
        {
            var dialog = new OpenFileDialog { Filter = "标签配方|*.json|所有文件|*.*" };
            if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
            if (new FileInfo(dialog.FileName).Length > 4 * 1024 * 1024) throw new InvalidDataException("配方文件超过预算。");
            _model.ImportRecipe(File.ReadAllText(dialog.FileName));
            Refresh();
            _status.Text = "配方已导入：" + dialog.FileName;
        }

        private void Export()
        {
            if (string.IsNullOrWhiteSpace(_model.Node.RecipeJson)) throw new InvalidOperationException("当前节点还没有配方。");
            var dialog = new SaveFileDialog { Filter = "标签配方|*.json", FileName = "recipe.json" };
            if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
            File.WriteAllText(dialog.FileName, _model.Node.RecipeJson);
            _status.Text = "配方已导出：" + dialog.FileName;
        }

        private void Refresh()
        {
            if (string.IsNullOrWhiteSpace(_model.Node.RecipeJson)) { _summary.Text = "尚无配方。请导入在标签工作台导出的 recipe.json。"; return; }
            try
            {
                var recipe = _model.Serializer.Deserialize(_model.Node.RecipeJson);
                var lines = new List<string>
                {
                    $"配方：{recipe.Name}", $"尺寸：{recipe.Width} × {recipe.Height}", $"模式：{recipe.Mode}；对齐：{recipe.Alignment}", $"ROI：{recipe.Regions.Count} 个"
                };
                lines.AddRange(recipe.Regions.Select(r => $"  · {r.Name}（{r.Kind}）  {r.Bounds.X},{r.Bounds.Y}  {r.Bounds.Width}×{r.Bounds.Height}"));
                _summary.Text = string.Join(Environment.NewLine, lines);
            }
            catch (Exception error) when (error is InvalidDataException or ArgumentException or System.Text.Json.JsonException or FormatException)
            { _summary.Text = "配方无法解析：" + error.Message; }
        }
    }
}

/// <summary>WPF只读报告页：本节点已提交报告的判定、ROI结果与证据，不重跑算法。</summary>
public sealed class LabelReportWpfRenderer : IWorkflowWpfNodeEditorPageRenderer
{
    /// <inheritdoc/>
    public string RendererKey => LabelInspectionResultPageModel.RendererKey;
    /// <inheritdoc/>
    public Type ModelType => typeof(LabelInspectionResultPageModel);
    /// <inheritdoc/>
    public FrameworkElement CreateElement(WorkflowNodeEditorPageDescriptor page) => new ReportView((LabelInspectionResultPageModel)page.Model);

    private sealed class ReportView : DockPanel
    {
        private readonly TreeView _evidence = new() { Background = LabelInspectionWpfExtension.Surface, Foreground = LabelInspectionWpfExtension.Text, BorderThickness = new Thickness(0) };
        private readonly TextBlock _summary = new() { Margin = new Thickness(10, 6, 10, 6), TextWrapping = TextWrapping.Wrap, Foreground = LabelInspectionWpfExtension.Muted };
        private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(500) };
        private long _sequence = -1;

        internal ReportView(LabelInspectionResultPageModel model)
        {
            Background = LabelInspectionWpfExtension.Background;
            SetDock(_summary, Dock.Bottom); Children.Add(_summary); Children.Add(_evidence);
            _summary.Text = "尚无已提交的标签报告；试检测不进入此页。";
            _timer.Tick += (_, _) => Refresh(model);
            Loaded += (_, _) => { Refresh(model); _timer.Start(); };
            Unloaded += (_, _) => _timer.Stop();
            model.RegisterViewLifetime(() => _timer.Stop());
        }

        private void Refresh(LabelInspectionResultPageModel model)
        {
            using var preview = model.Capture();
            if (preview?.Facts is not WorkflowLabelInspectionResult result || result.FrameId != preview.Frame.FrameId)
            {
                if (_sequence != -1) { _evidence.Items.Clear(); _sequence = -1; }
                _summary.Text = "尚无已提交的标签报告，或该输出已失效。";
                return;
            }
            if (_sequence == preview.Sequence) return;
            _sequence = preview.Sequence;
            var report = result.Report;
            _summary.Text = result.Summary + "；帧 " + result.FrameId;
            _evidence.Items.Clear();
            _evidence.Items.Add(Item("配方摘要：" + result.RecipeSha256));
            _evidence.Items.Add(Item("资源快照：" + result.ResourceIdentity));
            foreach (var finding in report.Findings) _evidence.Items.Add(Item($"全局 {finding.Verdict} [{finding.Code}] {finding.Message}"));
            foreach (var region in report.Analysis.Regions)
            {
                var row = Item(region.RegionName);
                if (region.Execution is { } execution)
                    row.Items.Add(Item($"前检 {execution.Prerequisites}；读取 {execution.Data}；内容 {execution.Comparison}；质量 {execution.Quality}"));
                if (region.Recognition is { } reading) row.Items.Add(Item("实际OCR：" + reading.Text));
                foreach (var code in region.Barcodes) row.Items.Add(Item("实际读码：" + code.Text));
                foreach (var finding in region.Findings) row.Items.Add(Item($"{finding.Verdict} [{finding.Code}] {finding.Message}"));
                _evidence.Items.Add(row);
            }
        }

        private static TreeViewItem Item(string text) => new() { Header = text, IsExpanded = true, Foreground = LabelInspectionWpfExtension.Text };
    }
}
