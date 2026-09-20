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
    private readonly ComboBox _source = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120 };
    private readonly Label _status = new() { Dock = DockStyle.Bottom, Height = 52, AutoEllipsis = true };
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 100 };

    internal VisionFrameEditorControl(VisionFrameEditorPageModel model)
    {
        _model = model;
        var tools = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 40, AutoSize = true };
        _source.Items.AddRange(new object[] { "输入图像", "结果图像", "模板图像", "手动预览" });
        _source.SelectedIndex = 1;
        tools.Controls.Add(_source);
        void Button(string text, Action action, bool enabled = true)
        {
            var button = new Button { Text = text, AutoSize = true, Enabled = enabled };
            button.Click += (_, _) => { try { action(); RefreshPreview(); _status.Text = model.Status; } catch (Exception ex) { _status.Text = ex.Message; } };
            tools.Controls.Add(button);
        }
        if (model.CanBindCoordinates)
        {
            var coordinates = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
            coordinates.Items.AddRange(model.CoordinateSources.Cast<object>().ToArray());
            if (coordinates.Items.Count > 0) coordinates.SelectedIndex = 0;
            tools.Controls.Add(coordinates);
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
        var read = new Button { Text = "预览文件…", AutoSize = true };
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
        Controls.Add(_canvas); Controls.Add(_status); Controls.Add(tools);
        _canvas.Editor = model.CanEdit ? model.Editor : null;
        _canvas.MouseClick += (_, e) =>
        {
            var caption = model.Pick(_canvas.Viewport.ToImage(new PointD(e.X, e.Y)), 5 / _canvas.Viewport.Scale);
            if (caption is not null) _status.Text = caption;
        };
        _source.SelectedIndexChanged += (_, _) => RefreshPreview();
        _timer.Tick += (_, _) => RefreshPreview();
        VisibleChanged += (_, _) => { if (Visible) _timer.Start(); else _timer.Stop(); };
        model.RegisterViewLifetime(Dispose);
        _timer.Start();
    }

    private void RefreshPreview()
    {
        if (IsDisposed) return;
        try
        {
            using var frame = _model.Capture(_source.SelectedIndex);
            _canvas.Editor = _model.CoordinateEditingReady ? _model.Editor : null;
            if (frame is not null) { _canvas.Present(frame); _status.Text = _model.Status; }
            else if (_status.Text.Length == 0) _status.Text = _model.Status;
        }
        catch (Exception ex) { _canvas.Editor = null; _status.Text = ex.Message; }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _timer.Stop(); _timer.Dispose(); }
        base.Dispose(disposing);
    }
}
