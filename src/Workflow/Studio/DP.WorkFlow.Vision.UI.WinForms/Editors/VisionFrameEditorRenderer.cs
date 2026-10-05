using DP.Vision;
using DP.Vision.UI;
using DP.WorkFlow.UI;
using DP.WorkFlow.UI.WinForms;

namespace DP.WorkFlow.Vision.UI.WinForms;

/// <summary>独立DP.Vision原生WinForms画布Renderer。</summary>
public sealed class VisionFrameEditorRenderer : IWorkflowWinFormsNodeEditorPageRenderer
{
    /// <inheritdoc/>
    public string RendererKey => VisionFrameEditorPageProvider.RendererKey;
    /// <inheritdoc/>
    public Type ModelType => typeof(VisionFrameEditorPageModel);
    /// <inheritdoc/>
    public Control CreateControl(WorkflowNodeEditorPageDescriptor page) => page.Model is VisionFrameEditorPageModel model
        ? new VisionFrameEditorControl(model) : throw new ArgumentException("Invalid frame editor model.", nameof(page));
}

internal sealed class VisionFrameEditorControl : UserControl
{
    private readonly VisionFrameEditorPageModel _model;
    private readonly DP.Vision.Winform.VisionCanvasControl _canvas = new() { Dock = DockStyle.Fill };
    private readonly ModernUI.WinForms.ModernSelect _source = ToolSelect(120);
    private readonly ModernUI.WinForms.ModernSelect _tool = ToolSelect(120);
    private IReadOnlyList<RoiToolChoice> _tools = Array.Empty<RoiToolChoice>();
    private bool _syncingTool;
    private readonly Label _status = new() { Dock = DockStyle.Bottom, Height = 52, AutoEllipsis = true };
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 100 };
    private readonly VisionTemplateEditorControl? _template;

    private readonly Action<PointD>? _pick;
    private readonly Func<bool>? _picking;
    private string? _fittedFrame;
    internal VisionFrameEditorControl(VisionFrameEditorPageModel model, bool templatePane = true, Action<PointD>? pick = null, Func<bool>? picking = null)
    {
        _model = model; _pick = pick; _picking = picking;
        var tools = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 40, AutoSize = true };
        _source.Items.AddRange(model.Template == null ? new object[] { "输入图像", "结果图像", "模板图像", "手动预览" }
            : new object[] { "输入图像", "结果图像", "模板图像", "手动预览", "模板制作样图", "模板试匹配" });
        _source.SelectedIndex = model.IsTemplateEditor ? 4 : 1;
        tools.Controls.Add(_source);
        DP.Vision.UI.RoiEditor Editing() => _source.SelectedIndex == 4 && model.Template != null ? model.Template.Editor : model.IsTemplateEditor ? throw new InvalidOperationException("请切换到模板制作样图后编辑制作区域。") : model.Editor;
        void Button(string text, Action action, bool enabled = true)
        {
            var button = ToolButton(text);
            button.Enabled = enabled;
            button.Click += (_, _) => { try { action(); RefreshPreview(); _status.Text = model.Status; } catch (Exception ex) { _status.Text = ex.Message; } };
            tools.Controls.Add(button);
        }
        if (model.CanBindCoordinates)
        {
            var coordinates = ToolSelect(160);
            coordinates.Items.AddRange(model.CoordinateSources.Cast<object>().ToArray());
            if (coordinates.Items.Count > 0) coordinates.SelectedIndex = 0;
            tools.Controls.Add(coordinates);
            Button(model.SupportsRegions ? "绑定/更换坐标系" : "绑定/更换坐标系", () => model.BindCoordinates((coordinates.SelectedItem as VisionCoordinateSource)?.NodeId
                ?? throw new InvalidOperationException("请选择定位节点。")));
            Button("解除坐标系转原图", model.UnbindCoordinates);
        }
        Button("适应窗口", _canvas.FitToWindow);
        if (model.SupportsMaskPreview)
        {
            var showMask = new ModernUI.WinForms.ModernCheckbox
            {
                Text = "显示有效掩膜",
                Checked = model.ShowMask,
                Theme = ModernUI.WinForms.ModernTheme.Dark,
                Size = new Size(TextRenderer.MeasureText("显示有效掩膜", Font).Width + 32, 30)
            };
            showMask.CheckedChanged += (_, _) => { model.ShowMask = showMask.Checked; RefreshPreview(); };
            tools.Controls.Add(showMask);
        }
        _tools = model.RegionTools;
        _tool.Items.AddRange(_tools.Cast<object>().ToArray());
        _tool.SelectedIndex = 0;
        _tool.Enabled = model.CanEdit;
        _tool.SelectedIndexChanged += (_, _) =>
        {
            if (_syncingTool || _tool.SelectedIndex < 0) return;
            try { Editing().Tool = _tools[_tool.SelectedIndex].Tool; _canvas.Focus(); }
            catch (Exception ex) { _status.Text = ex.Message; }
        };
        tools.Controls.Add(_tool);
        Button("完成轮廓", () => Editing().Finish(), model.SupportsRegions);
        Button("设为排除", () => Editing().SetSelectedMetadata(ERoiPurpose.Exclude, true), model.SupportsRegions);
        Button("设为包含", () => Editing().SetSelectedMetadata(ERoiPurpose.Include, true), model.SupportsRegions);
        Button("删除ROI", () => Editing().DeleteSelected(), model.CanEdit);
        Button("全图", () => { if (_source.SelectedIndex == 4) Editing().Load(new RoiDocument(Array.Empty<RoiDefinition>())); else if (!model.IsTemplateEditor) model.UseFullImage(); else throw new InvalidOperationException("请切换到模板制作样图。"); }, model.CanEdit);
        Button("撤销ROI", () => Editing().Undo(), model.CanEdit);
        var read = ToolButton("预览文件…");
        read.Click += async (_, _) =>
        {
            using var dialog = new OpenFileDialog { Filter = "图像|*.png;*.bmp;*.jpg;*.jpeg;*.tif;*.tiff|所有文件|*.*" };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            read.Enabled = false;
            try { await model.ReadPreviewAsync(dialog.FileName); if (!IsDisposed) { _source.SelectedIndex = 3; RefreshPreview(); } }
            catch (Exception ex) { if (!IsDisposed) _status.Text = ex.Message; }
            finally { if (!IsDisposed) read.Enabled = true; }
        };
        tools.Controls.Add(read);
        Controls.Add(_canvas); Controls.Add(_status);
        if (model.Template != null && templatePane)
        {
            _template = new VisionTemplateEditorControl(model.Template, view => { _source.SelectedIndex = view; RefreshPreview(); },
                () => model.TryTemplateAsync(), () => model.TryTemplateAsync(true), () => model.TemplateReference, model.IsTemplateEditor);
            Controls.Add(_template);
        }
        Controls.Add(tools);
        _canvas.Editor = model.CanEdit ? model.Editor : null;
        _canvas.MouseClick += (_, e) =>
        {
            if (_source.SelectedIndex == 4) { var point = _canvas.Viewport.ToImage(new PointD(e.X, e.Y)); if (_pick != null) _pick(point); else _template?.Pick(point); RefreshPreview(); return; }
            var caption = model.Pick(_canvas.Viewport.ToImage(new PointD(e.X, e.Y)), 5 / _canvas.Viewport.Scale);
            if (caption is not null) _status.Text = caption;
        };
        _source.SelectedIndexChanged += (_, _) => RefreshPreview();
        _timer.Tick += (_, _) => RefreshPreview();
        VisibleChanged += (_, _) => { if (Visible) _timer.Start(); else _timer.Stop(); };
        model.RegisterViewLifetime(Dispose);
        _timer.Start();
    }

    internal void SetView(int view) { _source.SelectedIndex = view; RefreshPreview(); }

    private static ModernUI.WinForms.ModernSelect ToolSelect(int width) => new()
    {
        Size = new Size(width, 30),
        Theme = ModernUI.WinForms.ModernTheme.Dark,
        DropDownAnimationDuration = 0
    };

    private ModernUI.WinForms.ModernButton ToolButton(string text) => new()
    {
        Text = text,
        Theme = ModernUI.WinForms.ModernTheme.Dark,
        Size = new Size(TextRenderer.MeasureText(text, Font).Width + 28, 30)
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

    internal void RefreshPreview()
    {
        if (IsDisposed) return;
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
        if (_canvas.Editor is not { } editor || RoiToolChoice.Find(_tools, editor.Tool) is not { } choice) return;
        int index = _tools.ToList().IndexOf(choice);
        if (_tool.SelectedIndex == index) return;
        _syncingTool = true;
        try { _tool.SelectedIndex = index; } finally { _syncingTool = false; }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _timer.Stop(); _timer.Dispose(); }
        base.Dispose(disposing);
    }
}
