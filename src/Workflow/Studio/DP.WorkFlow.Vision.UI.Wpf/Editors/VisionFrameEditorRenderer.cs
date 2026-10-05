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
    private readonly ComboBox _tool = new() { Width = 120, Margin = new Thickness(3) };
    private bool _syncingTool;
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, MinHeight = 42, Margin = new Thickness(5) };
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private bool _disposed;
    private readonly VisionTemplateEditorControl? _template;

    private readonly Action<PointD>? _pick;
    private readonly Func<bool>? _picking;
    private string? _fittedFrame;
    internal VisionFrameEditorControl(VisionFrameEditorPageModel model, bool templatePane = true, Action<PointD>? pick = null, Func<bool>? picking = null)
    {
        _model = model; _pick = pick; _picking = picking;
        var tools = new WrapPanel();
        _source.ItemsSource = model.Template == null ? new[] { "输入图像", "结果图像", "模板图像", "手动预览" }
            : new[] { "输入图像", "结果图像", "模板图像", "手动预览", "模板制作样图", "模板试匹配" }; _source.SelectedIndex = model.IsTemplateEditor ? 4 : 1;
        tools.Children.Add(_source);
        DP.Vision.UI.RoiEditor Editing() => _source.SelectedIndex == 4 && model.Template != null ? model.Template.Editor : model.IsTemplateEditor ? throw new InvalidOperationException("请切换到模板制作样图后编辑制作区域。") : model.Editor;
        void Button(string text, Action action, bool enabled = true)
        {
            var button = new Button { Content = text, Margin = new Thickness(3), Padding = new Thickness(5), IsEnabled = enabled };
            button.Click += (_, _) => { try { action(); RefreshPreview(); _status.Text = model.Status; } catch (Exception ex) { _status.Text = ex.Message; } }; tools.Children.Add(button);
        }
        if (model.CanBindCoordinates)
        {
            var coordinates = new ComboBox { Width = 160, Margin = new Thickness(3), ItemsSource = model.CoordinateSources, SelectedIndex = 0 };
            tools.Children.Add(coordinates);
            Button(model.SupportsRegions ? "绑定/更换坐标系" : "绑定/更换坐标系", () => model.BindCoordinates((coordinates.SelectedItem as VisionCoordinateSource)?.NodeId
                ?? throw new InvalidOperationException("请选择定位节点。")));
            Button("解除坐标系转原图", model.UnbindCoordinates);
        }
        Button("适应窗口", _canvas.FitToWindow);
        if (model.SupportsMaskPreview)
        {
            var showMask = new CheckBox { Content = "显示有效掩膜", IsChecked = model.ShowMask, Margin = new Thickness(3), VerticalAlignment = VerticalAlignment.Center };
            showMask.Checked += (_, _) => { model.ShowMask = true; RefreshPreview(); };
            showMask.Unchecked += (_, _) => { model.ShowMask = false; RefreshPreview(); };
            tools.Children.Add(showMask);
        }
        _tool.ItemsSource = model.RegionTools;
        _tool.SelectedIndex = 0;
        _tool.IsEnabled = model.CanEdit;
        _tool.SelectionChanged += (_, _) =>
        {
            if (_syncingTool || _tool.SelectedItem is not RoiToolChoice choice) return;
            try { Editing().Tool = choice.Tool; _canvas.Focus(); }
            catch (Exception ex) { _status.Text = ex.Message; }
        };
        tools.Children.Add(_tool);
        Button("完成轮廓", () => Editing().Finish(), model.SupportsRegions);
        Button("设为排除", () => Editing().SetSelectedMetadata(ERoiPurpose.Exclude, true), model.SupportsRegions);
        Button("设为包含", () => Editing().SetSelectedMetadata(ERoiPurpose.Include, true), model.SupportsRegions);
        Button("删除ROI", () => Editing().DeleteSelected(), model.CanEdit);
        Button("全图", () => { if (_source.SelectedIndex == 4) Editing().Load(new RoiDocument(Array.Empty<RoiDefinition>())); else if (!model.IsTemplateEditor) model.UseFullImage(); else throw new InvalidOperationException("请切换到模板制作样图。"); }, model.CanEdit);
        Button("撤销ROI", () => Editing().Undo(), model.CanEdit);
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
        if (model.Template != null && templatePane)
        {
            _template = new VisionTemplateEditorControl(model.Template, view => { _source.SelectedIndex = view; RefreshPreview(); },
                () => model.TryTemplateAsync(), () => model.TryTemplateAsync(true), () => model.TemplateReference, model.IsTemplateEditor);
            SetDock(_template, Dock.Top); Children.Add(_template);
        }
        SetDock(_status, Dock.Bottom); Children.Add(_status); Children.Add(_canvas);
        _canvas.Editor = model.CanEdit ? model.Editor : null;
        _canvas.MouseLeftButtonUp += (_, e) =>
        {
            var p = e.GetPosition(_canvas);
            if (_source.SelectedIndex == 4) { var point = _canvas.Viewport.ToImage(new PointD(p.X, p.Y)); if (_pick != null) _pick(point); else _template?.Pick(point); RefreshPreview(); return; }
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
    internal void SetView(int view) { _source.SelectedIndex = view; RefreshPreview(); }

    internal void InitializeTemplate(Func<Task> open)
    {
        bool started = false;
        Loaded += async (_, _) =>
        {
            if (started) return; started = true;
            try { await open(); if (!_disposed) { _template?.RefreshFields(); RefreshPreview(); } }
            catch (Exception ex) { if (!_disposed) { _template?.RefreshFields(); RefreshPreview(); _status.Text = ex.Message; } }
        };
    }

    private void OnTick(object? sender, EventArgs e) => RefreshPreview();
    internal void RefreshPreview()
    {
        if (_disposed) return;
        try
        {
            using var frame = _model.Capture(_source.SelectedIndex);
            _canvas.Editor = _source.SelectedIndex == 4 ? (_picking?.Invoke() == true || _template is { PickOrigin: true } or { PickDirection: true }) ? null : _model.Template?.Editor
                : _source.SelectedIndex == 5 ? null : !_model.IsTemplateEditor && _model.CanEdit && _model.CoordinateEditingReady ? _model.Editor : null;
            SyncTool();
            if (frame is not null) { _canvas.Present(frame); if (_model.IsTemplateEditor && _fittedFrame != frame.FrameId) { _fittedFrame = frame.FrameId; _canvas.FitToWindow(); } _status.Text = _source.SelectedIndex is 4 or 5 ? _model.Template?.Status : _model.Status; }
            else
            {
                if (_source.SelectedIndex is 4 or 5 && _canvas.DisplayedFrameId != null) _canvas.ClearImage();
                if (_status.Text.Length == 0) _status.Text = _source.SelectedIndex is 4 or 5 ? _model.Template?.Status : _model.Status;
            }
        }
        catch (Exception ex) { _canvas.Editor = null; _status.Text = ex.Message; }
    }
    // 编辑器创建形状后会自动回到“选择”，下拉框跟随当前编辑器的工具。
    private void SyncTool()
    {
        if (_canvas.Editor is not { } editor || _tool.ItemsSource is not IReadOnlyList<RoiToolChoice> tools
            || RoiToolChoice.Find(tools, editor.Tool) is not { } choice || ReferenceEquals(_tool.SelectedItem, choice)) return;
        _syncingTool = true;
        try { _tool.SelectedItem = choice; } finally { _syncingTool = false; }
    }

    public void Dispose()
    {
        if (_disposed) return; _disposed = true;
        _timer.Stop(); _timer.Tick -= OnTick; _canvas.Dispose();
    }
}
