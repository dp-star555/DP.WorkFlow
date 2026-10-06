using DP.Vision;
using DP.Vision.UI;
using DP.WorkFlow.UI;
using DP.WorkFlow.UI.WinForms;

namespace DP.WorkFlow.Vision.UI.WinForms;

/// <summary>独立DP.Vision原生WinForms画布Renderer。</summary>
public sealed class VisionFrameEditorRenderer : IWorkflowWinFormsNodeEditorPageRenderer, IWorkflowWinFormsNodeEditorSidePanelRenderer
{
    /// <inheritdoc/>
    public string RendererKey => VisionFrameEditorPageProvider.RendererKey;
    /// <inheritdoc/>
    public Type ModelType => typeof(VisionFrameEditorPageModel);
    /// <inheritdoc/>
    public Control CreateControl(WorkflowNodeEditorPageDescriptor page) => page.Model is VisionFrameEditorPageModel model
        ? new VisionFrameEditorControl(model) : throw new ArgumentException("Invalid frame editor model.", nameof(page));
    /// <inheritdoc/>
    public IEnumerable<WorkflowWinFormsNodeEditorSidePanel> CreateSidePanels(WorkflowNodeEditorPageDescriptor page)
    {
        if (page.Model is VisionFrameEditorPageModel model && new VisionRoiListModel(model) is { IsAvailable: true } roi)
            yield return new WorkflowWinFormsNodeEditorSidePanel("Roi", "ROI列表", new VisionRoiListControl(roi));
    }
}

internal sealed class VisionFrameEditorControl : UserControl
{
    private static readonly ModernUI.WinForms.ModernTheme Theme = ModernUI.WinForms.ModernTheme.Dark;
    private readonly VisionFrameEditorPageModel _model;
    private readonly DP.Vision.Winform.VisionCanvasControl _canvas = new() { Dock = DockStyle.Fill };
    private readonly ModernUI.WinForms.ModernToolStrip _toolbar = new() { Dock = DockStyle.Top, Theme = Theme };
    private readonly ModernUI.WinForms.ModernSelect _source = ToolSelect(130);
    private readonly ModernUI.WinForms.ModernSelect _tool = ToolSelect(140);
    private readonly ModernUI.WinForms.ModernSelect _purpose = ToolSelect(80);
    private readonly ModernUI.WinForms.ModernInputNumber _radius = new() { Size = new Size(90, 30), Minimum = 1, Maximum = 500, Value = 10, Theme = Theme };
    private readonly ToolStripControlHost _toolHost, _purposeHost, _radiusHost;
    private readonly ImageList _toolIcons = new() { ImageSize = new Size(IconPixels, IconPixels), ColorDepth = ColorDepth.Depth32Bit };
    // 工具栏宿主控件及其宽度依据：宽度=最长文字+额外留白（逻辑像素），高度统一，随DPI和字体重新计算。
    private readonly List<(ToolStripControlHost Host, Func<IEnumerable<string>> Texts, int Extra)> _fitted = new();
    private const int ControlHeight = 32, IconPixels = 32;
    private readonly IReadOnlyList<VisionFrameView> _views;
    private IReadOnlyList<RoiToolChoice> _tools = Array.Empty<RoiToolChoice>();
    private bool? _toolsForTemplate;
    private bool _syncing;
    private readonly Label _status = new() { Dock = DockStyle.Bottom, Height = 52, AutoEllipsis = true };
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 100 };
    private readonly VisionTemplateEditorControl? _template;

    private readonly Action<PointD>? _pick;
    private readonly Func<bool>? _picking;
    private string? _fittedFrame;
    internal VisionFrameEditorControl(VisionFrameEditorPageModel model, bool templatePane = true, Action<PointD>? pick = null, Func<bool>? picking = null)
    {
        _model = model; _pick = pick; _picking = picking;
        _views = model.Views;
        _source.Items.AddRange(_views.Cast<object>().ToArray());
        SetViewCore(model.IsTemplateEditor ? 4 : 1);
        _toolbar.Items.Add(Fit(Host(_source, "显示的图像"), () => _views.Select(v => v.Text), 44));
        _toolbar.Items.Add(new ToolStripSeparator());

        // 区域类型下拉框带图标；用途同时作用于选中的ROI和画笔；半径只在画笔/橡皮下出现。
        foreach (var (tool, icon) in ToolIcons)
            _toolIcons.Images.Add(tool.ToString(), ModernUI.WinForms.ModernIcons.CreateBitmap(icon, Theme.Text, IconPixels));
        _tool.ImageList = _toolIcons; _tool.ImageKeyMember = nameof(RoiToolChoice.Tool);
        _tool.SelectedIndexChanged += (_, _) =>
        {
            if (_syncing || _tool.SelectedItem is not RoiToolChoice choice || _canvas.Editor is not { } editor) return;
            Guard(() => editor.Tool = choice.Tool); _canvas.Focus();
        };
        _toolbar.Items.Add(_toolHost = Fit(Host(_tool, "区域类型"), () => _tools.Select(t => t.Text), 66));
        _purpose.Items.AddRange(new object[] { "包含", "排除" });
        _purpose.SelectedIndexChanged += (_, _) =>
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
        _toolbar.Items.Add(_purposeHost = Fit(Host(_purpose, "包含/排除：作用于选中的区域；画笔写入所选用途"), () => new[] { "包含", "排除" }, 44));
        _radius.ValueChanged += (_, _) => { if (!_syncing && _canvas.Editor is { } editor) Guard(() => editor.BrushRadius = (double)_radius.Value); };
        _toolbar.Items.Add(_radiusHost = Fit(Host(_radius, "笔刷半径（像素）"), () => new[] { "500" }, 64));
        _toolbar.Items.Add(new ToolStripSeparator());

        if (model.SupportsMaskPreview || model.Template != null)
        {
            var showMask = IconButton("显示有效掩膜", model.ShowMask ? ModernUI.WinForms.ModernIconKind.Eye : ModernUI.WinForms.ModernIconKind.EyeOff, () => { });
            showMask.CheckOnClick = true; showMask.Checked = model.ShowMask;
            showMask.CheckedChanged += (_, _) =>
            {
                model.ShowMask = showMask.Checked;
                var old = showMask.Image;
                showMask.Image = ModernUI.WinForms.ModernIcons.CreateBitmap(showMask.Checked ? ModernUI.WinForms.ModernIconKind.Eye : ModernUI.WinForms.ModernIconKind.EyeOff, Theme.Text, IconPixels);
                old?.Dispose(); RefreshPreview();
            };
        }
        IconButton("适应窗口", ModernUI.WinForms.ModernIconKind.FitWindow, _canvas.FitToWindow);

        Controls.Add(_canvas); Controls.Add(_status);
        if (model.Template != null && templatePane)
        {
            _template = new VisionTemplateEditorControl(model.Template, SetView,
                () => model.TryTemplateAsync(), async path => { await model.ReadPreviewAsync(path); await model.TryTemplateAsync(true); },
                () => model.TemplateReference, model.IsTemplateEditor);
            Controls.Add(_template);
        }
        Controls.Add(_toolbar);
        _canvas.Editor = model.CanEdit ? model.Editor : null;
        _canvas.MouseClick += (_, e) =>
        {
            if (View == 4) { var point = _canvas.Viewport.ToImage(new PointD(e.X, e.Y)); if (_pick != null) _pick(point); else _template?.Pick(point); RefreshPreview(); return; }
            var caption = model.Pick(_canvas.Viewport.ToImage(new PointD(e.X, e.Y)), 5 / _canvas.Viewport.Scale);
            if (caption is not null) _status.Text = caption;
        };
        if (model.Caliper is { } caliper) AttachCaliper(caliper);
        _source.SelectedIndexChanged += (_, _) => RefreshPreview();
        _timer.Tick += (_, _) => RefreshPreview();
        VisibleChanged += (_, _) => { if (Visible) _timer.Start(); else _timer.Stop(); };
        model.RegisterViewLifetime(Dispose);
        SyncToolbar();
        HandleCreated += (_, _) => FitToolbar();
        FontChanged += (_, _) => FitToolbar();
        _timer.Start();

        void Guard(Action action)
        {
            try { action(); _status.Text = _canvas.Editor?.ValidationError ?? _status.Text; }
            catch (Exception ex) { _status.Text = ex.Message; }
        }
        ToolStripButton IconButton(string text, ModernUI.WinForms.ModernIconKind icon, Action action)
        {
            var button = new ToolStripButton(text, ModernUI.WinForms.ModernIcons.CreateBitmap(icon, Theme.Text, IconPixels))
            { DisplayStyle = ToolStripItemDisplayStyle.Image, ToolTipText = text, AutoToolTip = false, Padding = new Padding(4) };
            button.Click += (_, _) => Guard(action);
            _toolbar.Items.Add(button);
            return button;
        }
    }

    // 区域工具与ModernUI矢量图标的对应关系，图标键为工具名。
    private static readonly (ERoiTool Tool, ModernUI.WinForms.ModernIconKind Icon)[] ToolIcons =
    {
        (ERoiTool.Select, ModernUI.WinForms.ModernIconKind.Pointer),
        (ERoiTool.Rectangle, ModernUI.WinForms.ModernIconKind.Rectangle),
        (ERoiTool.RotatedRectangle, ModernUI.WinForms.ModernIconKind.RotatedRectangle),
        (ERoiTool.Circle, ModernUI.WinForms.ModernIconKind.Ellipse),
        (ERoiTool.Ellipse, ModernUI.WinForms.ModernIconKind.Ellipse),
        (ERoiTool.Polygon, ModernUI.WinForms.ModernIconKind.Polygon),
        (ERoiTool.Brush, ModernUI.WinForms.ModernIconKind.Brush),
        (ERoiTool.Eraser, ModernUI.WinForms.ModernIconKind.Eraser),
    };

    /// <summary>当前视图编号（见<see cref="VisionFrameEditorPageModel.Views"/>）。</summary>
    private int View => _source.SelectedItem is VisionFrameView view ? view.Code : -1;

    /// <summary>按视图编号切换；页面没有该视图时保持不变。</summary>
    internal void SetView(int view) { SetViewCore(view); RefreshPreview(); }

    private void SetViewCore(int view)
    {
        int index = _views.ToList().FindIndex(v => v.Code == view);
        if (index >= 0) _source.SelectedIndex = index;
    }

    private ToolStripControlHost Fit(ToolStripControlHost host, Func<IEnumerable<string>> texts, int extra)
    {
        _fitted.Add((host, texts, extra));
        return host;
    }

    /// <summary>按当前DPI和字体重新计算工具栏尺寸，避免缩放后下拉框文字被截断、工具栏显得单薄。</summary>
    private void FitToolbar()
    {
        if (IsDisposed) return;
        int Scale(int logical) => LogicalToDeviceUnits(logical);
        _toolbar.SuspendLayout();
        try
        {
            _toolbar.Padding = new Padding(Scale(6), Scale(5), Scale(6), Scale(5));
            _toolbar.ImageScalingSize = new Size(Scale(20), Scale(20));
            foreach (var (host, texts, extra) in _fitted)
            {
                var control = host.Control;
                int text = texts().DefaultIfEmpty("").Max(t => TextRenderer.MeasureText(t, control.Font).Width);
                var size = new Size(Math.Min(text + Scale(extra), Scale(360)), Scale(ControlHeight)); // 文字可能很长，限制最大宽度。
                control.Size = size; host.Size = size;
                host.Margin = new Padding(Scale(2), 0, Scale(2), 0);
            }
        }
        finally { _toolbar.ResumeLayout(true); }
    }

    /// <inheritdoc/>
    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        FitToolbar();
    }

    private static ToolStripControlHost Host(Control control, string tip) => new(control)
    {
        AutoSize = false, Size = control.Size, Margin = new Padding(2, 1, 2, 1), ToolTipText = tip
    };

    private static ModernUI.WinForms.ModernSelect ToolSelect(int width) => new()
    {
        Size = new Size(width, 30),
        Theme = Theme,
        DropDownAnimationDuration = 0
    };

    internal void InitializeTemplate(Func<Task> open)
    {
        bool started = false;
        Load += async (_, _) =>
        {
            if (started) return; started = true;
            try { await open(); if (!IsDisposed) { _template?.RefreshFields(); RefreshPreview(); } }
            catch (Exception ex) { if (!IsDisposed) { _template?.RefreshFields(); RefreshPreview(); _status.Text = ex.Message; } }
        };
    }

    /// <summary>卡尺图上编辑：拖动起点/终点/带宽方块/采样带内部，松开后通知参数页刷新。</summary>
    private void AttachCaliper(VisionCaliperGizmo caliper)
    {
        double Unit() => 1 / Math.Max(1e-9, _canvas.Viewport.Scale);
        PointD ToImage(MouseEventArgs e) => _canvas.Viewport.ToImage(new PointD(e.X, e.Y));
        _canvas.MouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Left || View is 4 or 5) return;
            var point = ToImage(e);
            if (caliper.Hit(point, Unit()) is { } handle) { caliper.BeginDrag(handle, point); _canvas.Capture = true; }
        };
        _canvas.MouseMove += (_, e) =>
        {
            var point = ToImage(e);
            if (caliper.IsDragging)
            {
                if (caliper.Drag(point)) { _model.InvalidatePreview(); RefreshPreview(); }
                return;
            }
            _canvas.Cursor = View is 4 or 5 ? Cursors.Default : caliper.Hit(point, Unit()) switch
            {
                EVisionCaliperHandle.Body => Cursors.SizeAll,
                EVisionCaliperHandle.Width => Cursors.SizeNS,
                EVisionCaliperHandle.Start or EVisionCaliperHandle.End => Cursors.Cross,
                _ => Cursors.Default
            };
        };
        _canvas.MouseUp += (_, _) =>
        {
            if (caliper.EndDrag()) { _model.NotifyConfigurationChanged(); _model.InvalidatePreview(); RefreshPreview(); }
        };
    }

    internal void RefreshPreview()
    {
        if (IsDisposed) return;
        try
        {
            int view = View;
            _model.ImagePixelsPerScreenPixel = 1 / Math.Max(1e-9, _canvas.Viewport.Scale);
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
                _tool.Items.Clear(); _tool.Items.AddRange(_tools.Cast<object>().ToArray());
                if (IsHandleCreated) FitToolbar();
            }
            bool regions = templateMaking || _model.SupportsRegions;
            _toolHost.Enabled = editor != null;
            _purposeHost.Visible = regions; _purposeHost.Enabled = editor != null;
            _radiusHost.Visible = editor?.Tool is ERoiTool.Brush or ERoiTool.Eraser;
            if (editor == null) return;
            if (RoiToolChoice.Find(_tools, editor.Tool) is { } choice && !ReferenceEquals(_tool.SelectedItem, choice)) _tool.SelectedItem = choice;
            var purpose = editor.Document.Rois.FirstOrDefault(r => r.Id == editor.SelectedId)?.Purpose ?? editor.PaintPurpose;
            int purposeIndex = purpose == ERoiPurpose.Exclude ? 1 : 0;
            if (_purpose.SelectedIndex != purposeIndex) _purpose.SelectedIndex = purposeIndex;
            var radius = Math.Min(_radius.Maximum, Math.Max(_radius.Minimum, (decimal)editor.BrushRadius));
            if (_radius.Value != radius) _radius.Value = radius;
        }
        finally { _syncing = false; }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _timer.Stop(); _timer.Dispose(); _toolIcons.Dispose(); }
        base.Dispose(disposing);
    }
}
