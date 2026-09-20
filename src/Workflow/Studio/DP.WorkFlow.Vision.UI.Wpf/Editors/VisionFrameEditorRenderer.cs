using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using DP.Vision;
using DP.Vision.UI;
using DP.WorkFlow.UI;
using DP.WorkFlow.UI.Wpf;

namespace DP.WorkFlow.Vision.UI.Wpf;

/// <summary>独立DP.Vision原生WPF画布Renderer，不使用WindowsFormsHost。</summary>
public sealed class VisionFrameEditorRenderer : IWorkflowWpfNodeEditorPageRenderer
{
    /// <inheritdoc/>
    public string RendererKey => VisionFrameEditorPageProvider.RendererKey;
    /// <inheritdoc/>
    public Type ModelType => typeof(VisionFrameEditorPageModel);
    /// <inheritdoc/>
    public FrameworkElement CreateElement(WorkflowNodeEditorPageDescriptor page) => page.Model is VisionFrameEditorPageModel model
        ? new VisionFrameEditorControl(model) : throw new ArgumentException("Invalid frame editor model.", nameof(page));
}

internal sealed class VisionFrameEditorControl : DockPanel, IDisposable
{
    private readonly VisionFrameEditorPageModel _model;
    private readonly DP.Vision.WPF.VisionCanvasControl _canvas = new();
    private readonly ComboBox _source = new() { Width = 120, Margin = new Thickness(3) };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, MinHeight = 42, Margin = new Thickness(5) };
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private bool _disposed;

    internal VisionFrameEditorControl(VisionFrameEditorPageModel model)
    {
        _model = model;
        var tools = new WrapPanel();
        _source.ItemsSource = new[] { "输入图像", "结果图像", "模板图像", "手动预览" }; _source.SelectedIndex = 1;
        tools.Children.Add(_source);
        void Button(string text, Action action, bool enabled = true)
        {
            var button = new Button { Content = text, Margin = new Thickness(3), Padding = new Thickness(5), IsEnabled = enabled };
            button.Click += (_, _) => { try { action(); RefreshPreview(); _status.Text = model.Status; } catch (Exception ex) { _status.Text = ex.Message; } }; tools.Children.Add(button);
        }
        if (model.CanBindCoordinates)
        {
            var coordinates = new ComboBox { Width = 160, Margin = new Thickness(3), ItemsSource = model.CoordinateSources, SelectedIndex = 0 };
            tools.Children.Add(coordinates);
            Button("绑定定位并转换ROI", () => model.BindCoordinates((coordinates.SelectedItem as VisionCoordinateSource)?.NodeId
                ?? throw new InvalidOperationException("请选择定位节点。")));
            Button("解除定位转原图", model.UnbindCoordinates);
        }
        Button("适应窗口", _canvas.FitToWindow);
        Button("选择/移动", () => model.Editor.Tool = ERoiTool.Select, model.CanEdit);
        Button("绘制范围", () => model.Editor.Tool = ERoiTool.Rectangle, model.CanEdit);
        Button("旋转矩形", () => model.Editor.Tool = ERoiTool.RotatedRectangle, model.SupportsRegions);
        Button("椭圆", () => model.Editor.Tool = ERoiTool.Ellipse, model.SupportsRegions);
        Button("多边形", () => model.Editor.Tool = ERoiTool.Polygon, model.SupportsRegions);
        Button("完成轮廓", () => model.Editor.Finish(), model.SupportsRegions);
        Button("设为排除", () => model.Editor.SetSelectedMetadata(ERoiPurpose.Exclude, true), model.SupportsRegions);
        Button("设为包含", () => model.Editor.SetSelectedMetadata(ERoiPurpose.Include, true), model.SupportsRegions);
        Button("删除ROI", model.Editor.DeleteSelected, model.CanEdit);
        Button("全图", model.UseFullImage, model.CanEdit);
        Button("撤销ROI", () => model.Editor.Undo(), model.CanEdit);
        var read = new Button { Content = "预览文件…", Margin = new Thickness(3) };
        read.Click += async (_, _) =>
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "图像|*.png;*.bmp;*.jpg;*.jpeg;*.tif;*.tiff|所有文件|*.*" };
            if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
            read.IsEnabled = false;
            try { await model.ReadPreviewAsync(dialog.FileName); if (!_disposed) { _source.SelectedIndex = 3; RefreshPreview(); } }
            catch (Exception ex) { if (!_disposed) _status.Text = ex.Message; }
            finally { if (!_disposed) read.IsEnabled = true; }
        };
        tools.Children.Add(read);
        SetDock(tools, Dock.Top); Children.Add(tools);
        SetDock(_status, Dock.Bottom); Children.Add(_status); Children.Add(_canvas);
        _canvas.Editor = model.CanEdit ? model.Editor : null;
        _canvas.MouseLeftButtonUp += (_, e) =>
        {
            var p = e.GetPosition(_canvas);
            var caption = model.Pick(_canvas.Viewport.ToImage(new PointD(p.X, p.Y)), 5 / _canvas.Viewport.Scale);
            if (caption is not null) _status.Text = caption;
        };
        _source.SelectionChanged += (_, _) => RefreshPreview();
        _timer.Tick += OnTick;
        Loaded += (_, _) => { if (!_disposed) { _timer.Start(); RefreshPreview(); } };
        // Tab导航只暂停刷新，不释放可重用画布；真正关闭页面由Model的生命周期释放。
        Unloaded += (_, _) => _timer.Stop();
        model.RegisterViewLifetime(Dispose);
    }
    private void OnTick(object? sender, EventArgs e) => RefreshPreview();
    private void RefreshPreview()
    {
        if (_disposed) return;
        try
        {
            using var frame = _model.Capture(_source.SelectedIndex);
            _canvas.Editor = _model.CoordinateEditingReady ? _model.Editor : null;
            if (frame is not null) { _canvas.Present(frame); _status.Text = _model.Status; }
            else if (_status.Text.Length == 0) _status.Text = _model.Status;
        }
        catch (Exception ex) { _canvas.Editor = null; _status.Text = ex.Message; }
    }
    public void Dispose()
    {
        if (_disposed) return; _disposed = true;
        _timer.Stop(); _timer.Tick -= OnTick; _canvas.Dispose();
    }
}
