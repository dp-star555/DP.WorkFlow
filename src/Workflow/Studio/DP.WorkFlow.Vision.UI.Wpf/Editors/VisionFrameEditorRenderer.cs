using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using DP.Vision;
using DP.Vision.UI;
using DP.WorkFlow.UI;
using DP.WorkFlow.UI.Wpf;

namespace DP.WorkFlow.Vision.UI.Wpf;

/// <summary>独立DP.Vision原生WPF画布Renderer，不使用WindowsFormsHost。</summary>
public sealed class VisionFrameEditorRenderer : IWorkflowWpfNodeEditorPageRenderer, IWorkflowWpfNodeEditorSidePanelRenderer
{
    /// <inheritdoc/>
    public string RendererKey => VisionFrameEditorPageProvider.RendererKey;
    /// <inheritdoc/>
    public Type ModelType => typeof(VisionFrameEditorPageModel);
    /// <inheritdoc/>
    public FrameworkElement CreateElement(WorkflowNodeEditorPageDescriptor page) => page.Model is VisionFrameEditorPageModel model
        ? new VisionFrameEditorControl(model) : throw new ArgumentException("Invalid frame editor model.", nameof(page));
    /// <inheritdoc/>
    public IEnumerable<WorkflowWpfNodeEditorSidePanel> CreateSidePanels(WorkflowNodeEditorPageDescriptor page)
    {
        if (page.Model is VisionFrameEditorPageModel model && new VisionRoiListModel(model) is { IsAvailable: true } roi)
            yield return new WorkflowWpfNodeEditorSidePanel("Roi", "ROI列表", new VisionRoiListControl(roi));
        if (page.Model is VisionFrameEditorPageModel { MeasureGroup: not null } group)
            yield return new WorkflowWpfNodeEditorSidePanel("MeasureItems", "测量项", new VisionMeasureItemListControl(group));
    }
}

internal sealed class VisionFrameEditorControl : DockPanel, IDisposable
{
    private readonly VisionFrameEditorPageModel _model;
    private readonly DP.Vision.WPF.VisionCanvasControl _canvas = new();
    private readonly ToolBar _toolbar = new() { Padding = new Thickness(4, 3, 4, 3) };
    private readonly ComboBox _source = new() { MinWidth = 150, Height = 30, Margin = new Thickness(2), VerticalContentAlignment = VerticalAlignment.Center, ToolTip = "图展示：输入、结果及节点的分步图像" };
    private readonly ComboBox _tool = new() { MinWidth = 170, Height = 30, Margin = new Thickness(2), VerticalContentAlignment = VerticalAlignment.Center, ToolTip = "ROI：区域类型" };
    private readonly ComboBox _shape = new() { MinWidth = 150, Height = 30, Margin = new Thickness(2), VerticalContentAlignment = VerticalAlignment.Center, ToolTip = "卡尺：布局" };
    private readonly ComboBox _purpose = new() { MinWidth = 80, Height = 30, Margin = new Thickness(2), VerticalContentAlignment = VerticalAlignment.Center, ToolTip = "包含/排除：作用于选中的区域；画笔写入所选用途" };
    private readonly Slider _radius = new() { Width = 110, Minimum = 1, Maximum = 200, Value = 10, IsSnapToTickEnabled = true, TickFrequency = 1, VerticalAlignment = VerticalAlignment.Center, ToolTip = "笔刷半径（像素）" };
    private readonly Button? _run, _load, _previous, _next;
    private readonly MenuItem? _upstream;
    private readonly TextBlock _imageName = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 4, 0) };
    private readonly IReadOnlyList<VisionFrameView> _views;
    private IReadOnlyList<RoiToolChoice> _tools = Array.Empty<RoiToolChoice>();
    private bool? _toolsForTemplate;
    private bool _syncing;
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
        _views = model.Views;
        _source.ItemsSource = _views;
        SetViewCore(model.DefaultView);

        // 统一工具栏：运行 | 加载 | 图展示 | ROI | 卡尺 | 掩膜、自适应；节点不支持的组隐藏。
        if (model.CanRun)
        {
            _run = IconButton("运行本节点一次（用当前参数和输入图像，结果只显示在本窗口，不写入流程）", VisionToolIcons.Run);
            _run.Click += (_, _) => Command(async () => { await model.RunOnceAsync(); SetView(1); });
            _toolbar.Items.Add(_run);
        }
        if (model.CanLoadFiles)
        {
            _load = IconButton("加载输入图像", VisionToolIcons.Folder);
            _upstream = new MenuItem { Header = "上游图像", IsCheckable = false };
            _upstream.Click += (_, _) => { model.UseUpstreamImage(); SetView(0); };
            var file = new MenuItem { Header = "打开图像文件…" };
            file.Click += (_, _) =>
            {
                var dialog = new Microsoft.Win32.OpenFileDialog { Title = "加载图像", Filter = "图像|*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff|全部文件|*.*" };
                if (dialog.ShowDialog(Window.GetWindow(this)) == true) Command(async () => { await model.LoadImageFileAsync(dialog.FileName); SetView(0); });
            };
            var folder = new MenuItem { Header = "打开文件夹…" };
            folder.Click += (_, _) =>
            {
                if (DP.WorkFlow.UI.Wpf.WorkflowFolderPicker.TrySelect(Window.GetWindow(this), "加载文件夹中的图像（按文件名排序）", null, out var selected))
                    Command(async () => { await model.LoadFolderAsync(selected); SetView(0); });
            };
            var menu = new ContextMenu { Items = { _upstream, file, folder } };
            _load.ContextMenu = menu;
            _load.Click += (_, _) => { menu.PlacementTarget = _load; menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom; menu.IsOpen = true; };
            _toolbar.Items.Add(_load);
            _previous = IconButton("上一张", VisionToolIcons.Previous);
            _previous.Click += (_, _) => Command(async () => { await model.StepFolderAsync(-1); SetView(0); });
            _next = IconButton("下一张", VisionToolIcons.Next);
            _next.Click += (_, _) => Command(async () => { await model.StepFolderAsync(1); SetView(0); });
            _toolbar.Items.Add(_previous); _toolbar.Items.Add(_next); _toolbar.Items.Add(_imageName);
        }
        if (_toolbar.Items.Count > 0) _toolbar.Items.Add(new Separator());
        _toolbar.Items.Add(_source);
        _toolbar.Items.Add(new Separator());

        // ROI：区域类型下拉项带图标；用途同时作用于选中的ROI和画笔；半径只在画笔/橡皮下出现。
        _tool.SelectionChanged += (_, _) =>
        {
            if (_syncing || (_tool.SelectedItem as ComboBoxItem)?.Tag is not RoiToolChoice choice || _canvas.Editor is not { } editor) return;
            Guard(() => editor.Tool = choice.Tool); _canvas.Focus();
        };
        _toolbar.Items.Add(_tool);
        _purpose.ItemsSource = new[] { "包含", "排除" };
        _purpose.SelectionChanged += (_, _) =>
        {
            if (_syncing || _purpose.SelectedIndex < 0 || _canvas.Editor is not { } editor) return;
            var purpose = _purpose.SelectedIndex == 1 ? ERoiPurpose.Exclude : ERoiPurpose.Include;
            Guard(() =>
            {
                editor.PaintPurpose = purpose;
                if (editor.Document.Rois.FirstOrDefault(r => r.Id == editor.SelectedId) is { } selected && !RoiEditor.IsPaintId(selected.Id))
                    editor.SetSelectedMetadata(purpose, selected.Enabled);
            });
        };
        _toolbar.Items.Add(_purpose);
        _radius.ValueChanged += (_, _) => { if (!_syncing && _canvas.Editor is { } editor) Guard(() => editor.BrushRadius = _radius.Value); };
        _toolbar.Items.Add(_radius);

        // 卡尺：卡尺测量可在线/扇环间切换并保持位置；找线、找圆、找矩形显示各自的布局。
        var layouts = model.CaliperLayouts;
        if (layouts.Count > 0)
        {
            _shape.ItemsSource = layouts.Select(ShapeItem).ToArray();
            _shape.IsEnabled = model.CanSwitchCaliperLayout;
            _shape.SelectionChanged += (_, _) =>
            {
                if (_syncing || (_shape.SelectedItem as ComboBoxItem)?.Tag is not VisionCaliperLayoutChoice choice) return;
                if (_model.SetCaliperLayout(choice.Layout)) RefreshPreview();
                _canvas.Focus();
            };
            _toolbar.Items.Add(new Separator());
            _toolbar.Items.Add(_shape);
        }
        _toolbar.Items.Add(new Separator());

        if (model.SupportsMaskPreview || model.Template != null)
        {
            var showMask = new System.Windows.Controls.Primitives.ToggleButton { IsChecked = model.ShowMask, ToolTip = "显示有效掩膜", Content = Icon(VisionToolIcons.Eye), Width = 32, Height = 30 };
            showMask.Checked += (_, _) => { model.ShowMask = true; showMask.Content = Icon(VisionToolIcons.Eye); RefreshPreview(); };
            showMask.Unchecked += (_, _) => { model.ShowMask = false; showMask.Content = Icon(VisionToolIcons.EyeOff); RefreshPreview(); };
            if (!model.ShowMask) showMask.Content = Icon(VisionToolIcons.EyeOff);
            _toolbar.Items.Add(showMask);
        }
        var fit = new Button { ToolTip = "自适应：适应窗口", Content = Icon(VisionToolIcons.FitWindow), Width = 32, Height = 30 };
        fit.Click += (_, _) => _canvas.FitToWindow();
        _toolbar.Items.Add(fit);
        var tray = new ToolBarTray { IsLocked = true };
        tray.ToolBars.Add(_toolbar);
        SetDock(tray, Dock.Top); Children.Add(tray);
        if (model.Template != null && templatePane)
        {
            _template = new VisionTemplateEditorControl(model.Template, SetView,
                () => model.TryTemplateAsync(), async path => { await model.ReadPreviewAsync(path); await model.TryTemplateAsync(true); },
                () => model.TemplateReference, model.IsTemplateEditor);
            SetDock(_template, Dock.Top); Children.Add(_template);
        }
        SetDock(_status, Dock.Bottom); Children.Add(_status); Children.Add(_canvas);
        _canvas.Editor = model.CanEdit ? model.Editor : null;
        _canvas.MouseLeftButtonUp += (_, e) =>
        {
            var p = e.GetPosition(_canvas);
            if (View == 4) { var point = _canvas.Viewport.ToImage(new PointD(p.X, p.Y)); if (_pick != null) _pick(point); else _template?.Pick(point); RefreshPreview(); return; }
            var caption = model.Pick(_canvas.Viewport.ToImage(new PointD(p.X, p.Y)), 5 / _canvas.Viewport.Scale);
            if (caption is not null) _status.Text = caption;
        };
        if (model.Caliper is not null || model.MeasureGroup is not null) AttachCaliper();
        _source.SelectionChanged += (_, _) => RefreshPreview();
        _timer.Tick += OnTick;
        Loaded += (_, _) => { if (!_disposed) { _timer.Start(); RefreshPreview(); } };
        // Tab导航只暂停刷新，不释放可重用画布；真正关闭页面由Model的生命周期释放。
        Unloaded += (_, _) => _timer.Stop();
        model.RegisterViewLifetime(Dispose);
        SyncToolbar();

        // 异步命令：期间禁用运行按钮，失败写状态栏，结束后刷新画面和工具栏。
        async void Command(Func<Task> action)
        {
            if (_run is not null) _run.IsEnabled = false;
            try { await action(); }
            catch (Exception ex) { _status.Text = ex.Message; }
            finally { if (!_disposed) { RefreshPreview(); SyncToolbar(); } }
        }
        void Guard(Action action)
        {
            try { action(); _status.Text = _canvas.Editor?.ValidationError ?? _status.Text; }
            catch (Exception ex) { _status.Text = ex.Message; }
        }
    }

    /// <summary>当前视图编号（见<see cref="VisionFrameEditorPageModel.Views"/>）。</summary>
    private int View => _source.SelectedItem is VisionFrameView view ? view.Code : -1;

    /// <summary>按视图编号切换；页面没有该视图时保持不变。</summary>
    internal void SetView(int view) { SetViewCore(view); RefreshPreview(); }

    private void SetViewCore(int view)
    {
        int index = _views.ToList().FindIndex(v => v.Code == view);
        if (index >= 0) _source.SelectedIndex = index;
    }

    private static System.Windows.Shapes.Path Icon(string data) => new()
    {
        Data = System.Windows.Media.Geometry.Parse(data), Width = 18, Height = 18, Stretch = System.Windows.Media.Stretch.Uniform,
        StrokeThickness = 1.5, StrokeLineJoin = System.Windows.Media.PenLineJoin.Round,
        StrokeStartLineCap = System.Windows.Media.PenLineCap.Round, StrokeEndLineCap = System.Windows.Media.PenLineCap.Round,
        Stroke = SystemColors.ControlTextBrush, VerticalAlignment = VerticalAlignment.Center
    };

    private static object ToolItem(RoiToolChoice choice)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        if (VisionToolIcons.For(choice.Tool) is { } data) panel.Children.Add(Icon(data));
        panel.Children.Add(new TextBlock { Text = choice.Text, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        return new ComboBoxItem { Content = panel, Tag = choice };
    }

    private static Button IconButton(string tip, string icon) => new() { ToolTip = tip, Content = Icon(icon), Width = 32, Height = 30 };

    private static object ShapeItem(VisionCaliperLayoutChoice choice)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(Icon(choice.Layout switch
        {
            EVisionCaliperLayout.Annulus => VisionToolIcons.ArcCaliper,
            EVisionCaliperLayout.Rectangle => VisionToolIcons.RectangleCaliper,
            _ => VisionToolIcons.LineCaliper
        }));
        panel.Children.Add(new TextBlock { Text = choice.Text, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        return new ComboBoxItem { Content = panel, Tag = choice };
    }

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
    /// <summary>卡尺图上编辑：拖动起点/终点/带宽方块/采样带内部，松开后通知参数页刷新。</summary>
    // 图上编辑器随测量组的选中项变化，事件里总是取当前的编辑器。
    private void AttachCaliper()
    {
        double Unit() => 1 / Math.Max(1e-9, _canvas.Viewport.Scale);
        PointD ToImage(System.Windows.Input.MouseEventArgs e)
        {
            var p = e.GetPosition(_canvas);
            return _canvas.Viewport.ToImage(new PointD(p.X, p.Y));
        }
        _canvas.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (_model.Caliper is not { } caliper) return;
            if (!VisionFrameEditorPageModel.ShowsEditableOverlays(View)) return;
            var point = ToImage(e);
            if (caliper.Hit(point, Unit()) is { } handle) { caliper.BeginDrag(handle, point, Unit()); _canvas.CaptureMouse(); }
        };
        _canvas.PreviewMouseMove += (_, e) =>
        {
            if (_model.Caliper is not { } caliper) return;
            var point = ToImage(e);
            if (caliper.IsDragging)
            {
                if (caliper.Drag(point)) { _model.InvalidatePreview(); RefreshPreview(); }
                return;
            }
            _canvas.Cursor = !VisionFrameEditorPageModel.ShowsEditableOverlays(View) ? null : caliper.Hit(point, Unit()) switch
            {
                EVisionCaliperHandle.Body or EVisionCaliperHandle.Center => System.Windows.Input.Cursors.SizeAll,
                EVisionCaliperHandle.Radius or EVisionCaliperHandle.Step => System.Windows.Input.Cursors.Hand,
                EVisionCaliperHandle.Width or EVisionCaliperHandle.Length => System.Windows.Input.Cursors.SizeNS,
                EVisionCaliperHandle.Start or EVisionCaliperHandle.End => System.Windows.Input.Cursors.Cross,
                _ => null
            };
        };
        _canvas.PreviewMouseLeftButtonUp += (_, _) =>
        {
            if (_model.Caliper is not { } caliper) return;
            if (!caliper.IsDragging) return;
            var changed = caliper.EndDrag();
            _canvas.ReleaseMouseCapture();
            if (changed) { _model.NotifyConfigurationChanged(); _model.InvalidatePreview(); RefreshPreview(); }
        };
    }

    internal void RefreshPreview()
    {
        if (_disposed) return;
        try
        {
            int view = View;
            _model.ImagePixelsPerScreenPixel = 1 / Math.Max(1e-9, _canvas.Viewport.Scale);
            using var frame = _model.Capture(view);
            _canvas.Editor = view == 4 ? (_picking?.Invoke() == true || _template is { PickOrigin: true } or { PickDirection: true }) ? null : _model.Template?.Editor
                : VisionFrameEditorPageModel.ShowsEditableOverlays(view) && !_model.IsTemplateEditor && _model.CanEdit && _model.CoordinateEditingReady ? _model.Editor : null;
            SyncToolbar();
            if (frame is not null) { _canvas.Present(frame); if (_model.IsTemplateEditor && _fittedFrame != frame.FrameId) { _fittedFrame = frame.FrameId; _canvas.FitToWindow(); } _status.Text = view is 4 or 5 ? _model.Template?.Status : _model.Status; }
            else
            {
                if (view is 4 or 5 && _canvas.DisplayedFrameId != null) _canvas.ClearImage();
                if (_status.Text.Length == 0) _status.Text = view is 4 or 5 ? _model.Template?.Status : _model.Status;
            }
        }
        catch (Exception ex) { _canvas.Editor = null; _status.Text = ex.Message; }
    }

    // 工具栏跟随当前视图和编辑器：编辑模板制作区域时才有画笔/橡皮；画完形状后编辑器自动回到“选择”，下拉框随之同步。
    private void SyncToolbar()
    {
        bool templateMaking = View == 4 && _model.Template != null;
        var editor = _canvas.Editor;
        _syncing = true;
        try
        {
            if (_toolsForTemplate != templateMaking)
            {
                _toolsForTemplate = templateMaking;
                _tools = _model.RegionTools(templateMaking);
                _tool.ItemsSource = _tools.Select(ToolItem).ToArray();
            }
            bool regions = templateMaking || _model.SupportsRegions;
            // 不支持ROI的节点隐藏ROI组；卡尺不是面积ROI，扫描线与采样带直接在图上拖动。
            _tool.Visibility = _model.ShowsRegionTools(templateMaking) ? Visibility.Visible : Visibility.Collapsed; _tool.IsEnabled = editor != null;
            _purpose.Visibility = regions ? Visibility.Visible : Visibility.Collapsed; _purpose.IsEnabled = editor != null;
            if (_model.CaliperLayout is { } layout && _shape.Items.Cast<ComboBoxItem>().FirstOrDefault(i => Equals(i.Tag, layout)) is { } shapeItem
                && !ReferenceEquals(_shape.SelectedItem, shapeItem)) _shape.SelectedItem = shapeItem;
            if (_run is not null) _run.IsEnabled = !_model.IsRunning;
            if (_upstream is not null) _upstream.IsChecked = _model.ImageSource == EVisionEditorImageSource.Upstream;
            if (_previous is not null && _next is not null)
                _previous.Visibility = _next.Visibility = _model.CanStepFolder ? Visibility.Visible : Visibility.Collapsed;
            _imageName.Text = _model.ImageSource == EVisionEditorImageSource.File ? _model.ImageSourceText : "";
            _radius.Visibility = editor?.Tool is ERoiTool.Brush or ERoiTool.Eraser ? Visibility.Visible : Visibility.Collapsed;
            if (editor == null) return;
            if (_tool.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (i.Tag as RoiToolChoice)?.Tool == editor.Tool) is { } item && !ReferenceEquals(_tool.SelectedItem, item)) _tool.SelectedItem = item;
            var purpose = editor.Document.Rois.FirstOrDefault(r => r.Id == editor.SelectedId)?.Purpose ?? editor.PaintPurpose;
            int purposeIndex = purpose == ERoiPurpose.Exclude ? 1 : 0;
            if (_purpose.SelectedIndex != purposeIndex) _purpose.SelectedIndex = purposeIndex;
            var radius = Math.Min(_radius.Maximum, Math.Max(_radius.Minimum, editor.BrushRadius));
            if (_radius.Value != radius) _radius.Value = radius;
        }
        finally { _syncing = false; }
    }

    public void Dispose()
    {
        if (_disposed) return; _disposed = true;
        _timer.Stop(); _timer.Tick -= OnTick; _canvas.Dispose();
    }
}

/// <summary>工具栏矢量图标（16×16路径数据），与WinForms侧ModernUI图标同形。</summary>
internal static class VisionToolIcons
{
    public const string Eye = "M1.3,8 C4.5,2.9 11.5,2.9 14.7,8 C11.5,13.1 4.5,13.1 1.3,8 Z M6.2,8 A1.8,1.8 0 1 0 9.8,8 A1.8,1.8 0 1 0 6.2,8 Z";
    public const string EyeOff = Eye + " M2.6,2.6 L13.4,13.4";
    public const string LineCaliper = "M1.9,8 L14.1,8 M10.6,4.6 L14.1,8 L10.6,11.4 M4.5,5.2 L4.5,10.8 M7.3,5.2 L7.3,10.8";
    public const string ArcCaliper = "M2.6,11.4 A6.2,6.2 0 0 1 13.4,7.2 M10.6,4.4 L13.4,7.2 L9.9,8.4 M4.2,6.8 L2.2,5.2 M6.8,4.8 L5.9,2.5";
    public const string Run = "M4.5,2.5 L13,8 L4.5,13.5 Z";
    public const string Folder = "M1.8,4 L1.8,12.8 L14.2,12.8 L14.2,5.4 L7.6,5.4 L6.2,3.4 L2.4,3.4 Z";
    public const string Previous = "M10,2.8 L4.8,8 L10,13.2";
    public const string Next = "M6,2.8 L11.2,8 L6,13.2";
    public const string RectangleCaliper = "M4,4.5 L12,4.5 L12,11.5 L4,11.5 Z M8,1.5 L8,6.5 M8,9.5 L8,14.5 M1.5,8 L6,8 M10,8 L14.5,8";
    public const string FitWindow = "M2.2,5.8 L2.2,2.2 L5.8,2.2 M10.2,2.2 L13.8,2.2 L13.8,5.8 M13.8,10.2 L13.8,13.8 L10.2,13.8 M5.8,13.8 L2.2,13.8 L2.2,10.2";

    public static string? For(ERoiTool tool) => tool switch
    {
        ERoiTool.Select => "M4.2,2.2 L4.2,12.8 L6.7,10.2 L8.6,14.1 L10.2,13.3 L8.3,9.6 L11.8,9.3 Z",
        ERoiTool.Rectangle => "M2.6,4.2 L13.4,4.2 L13.4,11.8 L2.6,11.8 Z",
        ERoiTool.RotatedRectangle => "M5.4,1.9 L14.4,6.7 L10.6,14.1 L1.6,9.3 Z",
        ERoiTool.Circle or ERoiTool.Ellipse => "M1.9,8 A6.1,4.2 0 1 0 14.1,8 A6.1,4.2 0 1 0 1.9,8 Z",
        ERoiTool.Polygon => "M8,1.9 L14.1,6.4 L11.8,13.8 L4.2,13.8 L1.9,6.4 Z",
        ERoiTool.Brush => "M13.8,2.2 L7.4,8.6 M7.4,8.6 C4.8,8 3.5,10.6 3.2,13.8 C6.7,13.8 9,12.2 7.4,8.6 Z",
        ERoiTool.Eraser => "M1.9,9.9 L8,3.8 L13.8,9.6 L9.6,13.8 L5.8,13.8 Z M5,6.9 L10.7,12.6 M5.8,13.8 L14.1,13.8",
        _ => null,
    };
}
