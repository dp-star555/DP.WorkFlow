using DP.LabelInspection.Adapter.Vision;
using DP.LabelInspection.Contracts;
using DP.Vision;
using DP.WorkFlow.LabelInspection.UI;
using DP.WorkFlow.UI;
using DP.WorkFlow.UI.WinForms;

namespace DP.WorkFlow.LabelInspection.UI.WinForms;

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
        private readonly TreeView _evidence = new() { Dock = DockStyle.Fill };
        private readonly Label _summary = new() { Dock = DockStyle.Bottom, Height = 42, AutoEllipsis = true };
        private readonly System.Windows.Forms.Timer _timer = new() { Interval = 200 };
        private long _sequence = -1;
        internal ReportControl(LabelInspectionResultPageModel model)
        {
            var split = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 500, Width = 900 };
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
            var layers = VisionAdapter.LabelLayers(Array.Empty<InspectionRegion>(), report.EvidenceGroups.Select(g => g.Summary), characters);
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
