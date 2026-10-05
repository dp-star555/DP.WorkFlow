namespace DP.WorkFlow.Vision.UI;

public sealed partial class VisionFrameEditorPageModel
{
    private bool _showMask = true;
    /// <summary>仅影响画布显示，不修改节点配置或模型。</summary>
    public bool ShowMask
    {
        get => _showMask;
        set { _showMask = value; if (Template is not null) Template.ShowMask = value; _lastKey = null; }
    }
    /// <summary>面积范围与模板制作页面展示掩膜预览开关。</summary>
    public bool SupportsMaskPreview => IsTemplateEditor || _node is AnalyzeVisionFrameNodeModel { RangeCapability: EWorkflowVisionRange.Region };
}
