using DP.LabelInspection.Adapter.Vision;
using DP.LabelInspection.Contracts;
using DP.Vision;
using DP.WorkFlow.LabelInspection.UI;
using DP.WorkFlow.UI;
using DP.WorkFlow.UI.WinForms;
using ModernUI.WinForms;

namespace DP.WorkFlow.LabelInspection.UI.WinForms;

/// <summary>把标签坐标下的叠加图层按放置换算到原图。</summary>
public static class LabelOverlayMapping
{
    /// <summary>换算图层；相似变换保持为带角度矩形，含剪切时转为不填充的闭合轮廓。</summary>
    /// <param name="layers">标签坐标下的图层。</param><param name="placement">配方坐标到原图的放置。</param>
    public static IReadOnlyList<CanvasLayer> ToImage(IReadOnlyList<CanvasLayer> layers, InspectionPlacement placement)
    {
        ArgumentNullException.ThrowIfNull(layers); ArgumentNullException.ThrowIfNull(placement);
        return layers.Select(layer => new CanvasLayer(layer.Id, layer.Kind,
            layer.Visuals.Select(v => new Visual(v.Id, Map(v.Geometry, placement), v.Argb, v.Caption)), layer.Order, layer.Visible, layer.Name)).ToArray();
    }

    /// <summary>换算单个几何。</summary>
    public static Geometry Map(Geometry geometry, InspectionPlacement placement)
    {
        PointD Point(PointD p) { var (x, y) = placement.Map(p.X, p.Y); return new PointD(x, y); }
        bool similarity = Math.Abs(placement.M11 - placement.M22) < 1e-9 && Math.Abs(placement.M12 + placement.M21) < 1e-9;
        return geometry switch
        {
            RectangleGeometry r when similarity => new RectangleGeometry(Point(r.Center), r.Width * placement.Scale, r.Height * placement.Scale,
                r.Angle + Math.Atan2(placement.M21, placement.M11)),
            RectangleGeometry r => new ContourGeometry(r.Corners.Select(Point), closed: true),
            ContourGeometry c => new ContourGeometry(c.Points.Select(Point), c.Closed, c.Filled),
            _ => geometry
        };
    }
}

/// <summary>只读显示本节点正式提交报告及同帧证据，不重跑算法。</summary>
public sealed class LabelInspectionReportRenderer : IWorkflowWinFormsNodeEditorPageRenderer
{
    /// <inheritdoc/>
    public string RendererKey => LabelInspectionResultPageModel.RendererKey;
    /// <inheritdoc/>
    public Type ModelType => typeof(LabelInspectionResultPageModel);
    /// <inheritdoc/>
    public Control CreateControl(WorkflowNodeEditorPageDescriptor page) => new ReportControl((LabelInspectionResultPageModel)page.Model);

    private sealed class ReportControl : UserControl
    {
        private readonly DP.Vision.Winform.VisionCanvasControl _canvas = new() { Dock = DockStyle.Fill };
        private static readonly ModernTheme Theme = ModernTheme.Dark;
        private readonly ModernTreeView _evidence = new() { Dock = DockStyle.Fill, Theme = Theme };
        private readonly Label _summary = new()
        {
            Dock = DockStyle.Bottom, Height = 42, AutoEllipsis = true, Padding = new Padding(8, 4, 8, 4),
            BackColor = Theme.Container, ForeColor = Theme.TextSecondary, TextAlign = ContentAlignment.MiddleLeft
        };
        private readonly System.Windows.Forms.Timer _timer = new() { Interval = 200 };
        private long _sequence = -1;
        internal ReportControl(LabelInspectionResultPageModel model)
        {
            BackColor = Theme.Background; ForeColor = Theme.Text;
            var split = new ModernSplitter { Dock = DockStyle.Fill, Theme = Theme, Width = 900, SplitterDistance = 500 };
            split.Panel1.Controls.Add(_canvas); split.Panel2.Controls.Add(_evidence);
            Controls.Add(split); Controls.Add(_summary);
            _summary.Text = "尚无已提交的标签报告；试检测不进入此页。";
            _timer.Tick += (_, _) => Refresh(model);
            VisibleChanged += (_, _) => { if (Visible) { Refresh(model); _timer.Start(); } else _timer.Stop(); };
            model.RegisterViewLifetime(Dispose);
            Refresh(model); _timer.Start();
        }
        private void Refresh(LabelInspectionResultPageModel model)
        {
            using var preview = model.Capture();
            if (preview?.Facts is not WorkflowLabelInspectionResult result || result.FrameId != preview.Frame.FrameId)
            {
                if (_sequence != -1) { _canvas.ClearImage(); _evidence.Nodes.Clear(); _sequence = -1; }
                _summary.Text = "尚无已提交的标签报告，或该输出已失效。";
                return;
            }
            if (_sequence == preview.Sequence) return;
            _sequence = preview.Sequence;
            var report = result.Report;
            var characters = report.Analysis.Regions.Where(r => r.Segmentation != null).SelectMany(r => r.Segmentation!.Characters);
            // 叠加配方ROI框作为检测范围参照，再叠加证据与字块。
            var layers = VisionAdapter.LabelLayers(result.RecipeRegions, report.EvidenceGroups.Select(g => g.Summary), characters);
            // 绑定了标签坐标系时报告坐标是标签坐标：按本次放置换算到原图（随标签旋转）。
            if (result.Placement is { } placement) layers = LabelOverlayMapping.ToImage(layers, placement);
            using var frame = new CanvasFrame(result.FrameId, preview.Sequence, preview.Frame.Image, new GeometryOverlay(result.FrameId, layers));
            _canvas.Present(frame); _canvas.FitToWindow();
            _summary.Text = result.Summary + "；帧 " + result.FrameId + "；检查覆盖/阻断见详细报告，Success不代表产品合格。";
            _evidence.BeginUpdate();
            try
            {
                _evidence.Nodes.Clear();
                _evidence.Nodes.Add("配方摘要：" + result.RecipeSha256);
                _evidence.Nodes.Add("资源快照：" + result.ResourceIdentity);
                foreach (var finding in report.Findings) _evidence.Nodes.Add($"全局 {finding.Verdict} [{finding.Code}] {finding.Message}");
                foreach (var region in report.Analysis.Regions)
                {
                    var row = _evidence.Nodes.Add(region.RegionName);
                    if (region.Execution is { } execution)
                        row.Nodes.Add($"前检 {execution.Prerequisites}；读取 {execution.Data}；内容 {execution.Comparison}；质量 {execution.Quality}");
                    if (region.Recognition is { } reading) row.Nodes.Add("实际OCR：" + reading.Text);
                    foreach (var code in region.Barcodes) row.Nodes.Add("实际读码：" + code.Text);
                    foreach (var finding in region.Findings) row.Nodes.Add($"{finding.Verdict} [{finding.Code}] {finding.Message}");
                }
                foreach (var group in report.EvidenceGroups)
                {
                    var row = _evidence.Nodes.Add($"{group.Id} {group.RegionName} {group.Status}；定位候选{group.LocalizedCandidateCount}；阻断{group.BlockingItemCount}");
                    foreach (var detail in group.Children) row.Nodes.Add($"{detail.Id} {detail.Status} [{detail.Finding.Code}] {detail.Finding.Message}");
                }
                _evidence.ExpandAll();
            }
            finally { _evidence.EndUpdate(); }
        }
        protected override void Dispose(bool disposing)
        { if (disposing) { _timer.Stop(); _timer.Dispose(); } base.Dispose(disposing); }
    }
}
