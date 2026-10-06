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
    }
}

internal sealed class VisionFrameEditorControl : DockPanel, IDisposable
{
    private readonly VisionFrameEditorPageModel _model;
    private readonly DP.Vision.WPF.VisionCanvasControl _canvas = new();
    private readonly ToolBar _toolbar = new() { Padding = new Thickness(4, 3, 4, 3) };
    private readonly ComboBox _source = new() { MinWidth = 150, Height = 30, Margin = new Thickness(2), VerticalContentAlignment = VerticalAlignment.Center, ToolTip = "显示的图像" };
    private readonly ComboBox _tool = new() { MinWidth = 170, Height = 30, Margin = new Thickness(2), VerticalContentAlignment = VerticalAlignment.Center, ToolTip = "区域类型" };
    private readonly ComboBox _purpose = new() { MinWidth = 80, Height = 30, Margin = new Thickness(2), VerticalContentAlignment = VerticalAlignment.Center, ToolTip = "包含/排除：作用于选中的区域；画笔写入所选用途" };
    private readonly Slider _radius = new() { Width = 110, Minimum = 1, Maximum = 200, Value = 10, IsSnapToTickEnabled = true, TickFrequency = 1, VerticalAlignment = VerticalAlignment.Center, ToolTip = "笔刷半径（像素）" };
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
        SetViewCore(model.IsTemplateEditor ? 4 : 1);
        _toolbar.Items.Add(_source);
        _toolbar.Items.Add(new Separator());

        // 区域类型下拉项带图标；用途同时作用于选中的ROI和画笔；半径只在画笔/橡皮下出现。
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
        _toolbar.Items.Add(new Separator());

        if (model.SupportsMaskPreview || model.Template != null)
        {
            var showMask = new System.Windows.Controls.Primitives.ToggleButton { IsChecked = model.ShowMask, ToolTip = "显示有效掩膜", Content = Icon(VisionToolIcons.Eye), Width = 32, Height = 30 };
            showMask.Checked += (_, _) => { model.ShowMask = true; showMask.Content = Icon(VisionToolIcons.Eye); RefreshPreview(); };
            showMask.Unchecked += (_, _) => { model.ShowMask = false; showMask.Content = Icon(VisionToolIcons.EyeOff); RefreshPreview(); };
            if (!model.ShowMask) showMask.Content = Icon(VisionToolIcons.EyeOff);
            _toolbar.Items.Add(showMask);
        }
        var fit = new Button { ToolTip = "适应窗口", Content = Icon(VisionToolIcons.FitWindow), Width = 32, Height = 30 };
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
        _source.SelectionChanged += (_, _) => RefreshPreview();
        _timer.Tick += OnTick;
        Loaded += (_, _) => { if (!_disposed) { _timer.Start(); RefreshPreview(); } };
        // Tab导航只暂停刷新，不释放可重用画布；真正关闭页面由Model的生命周期释放。
        Unloaded += (_, _) => _timer.Stop();
        model.RegisterViewLifetime(Dispose);
        SyncToolbar();

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
            int view = View;
            using var frame = _model.Capture(view);
            _canvas.Editor = view == 4 ? (_picking?.Invoke() == true || _template is { PickOrigin: true } or { PickDirection: true }) ? null : _model.Template?.Editor
                : view == 5 ? null : !_model.IsTemplateEditor && _model.CanEdit && _model.CoordinateEditingReady ? _model.Editor : null;
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
            _tool.IsEnabled = editor != null;
            _purpose.Visibility = regions ? Visibility.Visible : Visibility.Collapsed; _purpose.IsEnabled = editor != null;
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
