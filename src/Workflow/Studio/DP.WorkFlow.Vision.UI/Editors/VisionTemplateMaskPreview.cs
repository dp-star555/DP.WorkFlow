using DP.Vision;
using DP.Vision.Algorithms;
using DP.Vision.UI;

namespace DP.WorkFlow.Vision.UI;

public sealed partial class VisionTemplateEditorModel
{
    private long _maskGeneration = -1;
    private RegionGeometry? _previewMask;
    private RegionGeometry? _trialMask;
    private string _maskIssue = "";
    /// <summary>制作画布显示精确有效掩膜；仅影响显示，不使模型失效。</summary>
    public bool ShowMask { get; set; } = true;
    /// <summary>当前制作区域的有效像素数或明确错误，未用包围框代替掩膜。</summary>
    public string MaskSummary => _source is null ? "请读取样图后绘制包含／排除ROI。"
        : PreviewMask(_source) is { } mask ? $"有效掩膜 {mask.AreaPixels}像素；包含并集减排除，孔洞保留。" : _maskIssue;

    private RegionGeometry? PreviewMask(ImageFrame frame)
    {
        if (_maskGeneration == _generation) return _previewMask;
        _maskGeneration = _generation; _previewMask = null; _maskIssue = "";
        try
        {
            _previewMask = MakingRegion(frame);
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException)
        { _maskIssue = "掩膜预览失败：" + error.Message; }
        return _previewMask;
    }

    private RegionGeometry MakingRegion(ImageFrame frame)
    {
        var active = Editor.Document.Rois.Where(r => r.Enabled).ToArray();
        if (Editor.Document.Rois.Count > 0 && active.Length == 0)
            throw new InvalidOperationException("全部ROI已禁用，掩膜不会回退全图。");
        return InspectionMask.Compose(frame.Image, active.Where(r => r.Purpose == ERoiPurpose.Include).Select(r => r.Shape),
            active.Where(r => r.Purpose == ERoiPurpose.Exclude).Select(r => r.Shape));
    }
}
