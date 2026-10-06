using DP.Vision;
using DP.Vision.Algorithms;
using DP.Vision.UI;

namespace DP.WorkFlow.Vision.UI;

public sealed partial class VisionFrameEditorPageModel
{
    private VisionCoordinateSystem? _displayCoordinates;
    private string? _displayBindingKey;
    private bool HasDisplayedCoordinateBinding(WorkflowVisionCoordinateBinding binding) => CoordinateEditingReady
        && _displayCoordinates is not null && binding.CoordinateSystemId == _displayCoordinates.CoordinateSystemId
        && binding.DefinitionSignature == _displayCoordinates.Definition.Signature && binding.DefinitionVersion == _displayCoordinates.Definition.Version
        && binding.System?.Binding?.ToString() == _displayBindingKey;
    /// <summary>范围能力允许坐标系绑定（在属性面板“坐标系”下拉中选择）；面积ROI、卡尺和几何事实使用各自配置语义。</summary>
    public bool CanBindCoordinates => !IsTemplateEditor && _node is AnalyzeVisionFrameNodeModel node && node.SupportsCoordinates;
    /// <summary>当前显示图像是否允许编辑；手动图像不能借用另一帧的定位。</summary>
    public bool CoordinateEditingReady { get; private set; }

    private WorkflowVisionPreview CaptureInput(AnalyzeVisionFrameNodeModel node)
    {
        if (node.Frame.Binding is not { IsPublicData: false } binding)
            throw new InvalidOperationException("交互制作需要可预览的节点图像绑定；公共数据绑定可执行，但此页面不猜测其图像来源。");
        return _frames?.Capture(binding.NodeId) ?? throw new InvalidOperationException("尚无输入图像预览，请先运行采集和定位。");
    }

    private VisionCoordinateSystem CaptureCoordinates(WorkflowVisionCoordinateBinding binding, ImageFrame frame)
    {
        if (binding.System.Binding is not { IsPublicData: false } source || source.MemberPath != "CoordinateSystem")
            throw new InvalidOperationException("交互制作需要直接绑定定位节点的CoordinateSystem成员。");
        using var preview = _frames?.Capture(source.NodeId);
        var system = (preview?.Facts as IVisionCoordinateResult)?.CoordinateSystem ?? throw new InvalidOperationException("本帧没有成功定位；局部ROI不可编辑。");
        binding.Validate(system, frame); return system;
    }

    private void UpdateCoordinatePreview(ImageFrame frame, int view)
    {
        if (_node is not AnalyzeVisionFrameNodeModel { Coordinates: { } binding } node)
        {
            // 属性面板解除了坐标系：范围已换算回原图，按原图表达重新显示。
            if (_displayCoordinates is not null && _node is AnalyzeVisionFrameNodeModel { RangeCapability: EWorkflowVisionRange.Region } unbound)
            {
                _displayCoordinates = null; _lastKey = null;
                _loading = true;
                try { Editor.Cancel(); Editor.Load(new RoiDocument(unbound.Regions.Select(r => new RoiDefinition(r.Id, r.ToGeometry(), r.Exclude ? ERoiPurpose.Exclude : ERoiPurpose.Include, r.Enabled)))); }
                finally { _loading = false; }
            }
            _displayCoordinates = null;
            CoordinateEditingReady = CanEdit || CanBindCoordinates; return;
        }
        bool wasReady = CoordinateEditingReady;
        CoordinateEditingReady = false;
        if (view is 2 or 3) return; // 模板/测试图像只读，不把局部数值画到无关图像。
        var system = CaptureCoordinates(binding, frame);
        if (!wasReady || !ReferenceEquals(_displayCoordinates, system))
        {
            if (SupportsRegions) LoadCoordinateRois(node, system);
            _displayCoordinates = system;
        }
        _displayBindingKey = binding.System.Binding?.ToString();
        CoordinateEditingReady = CanEdit || CanBindCoordinates;
    }

    private void LoadCoordinateRois(AnalyzeVisionFrameNodeModel node, VisionCoordinateSystem system)
    {
        _lastKey = null;
        // 图像切换时取消未完成手势；完成的修改已经写入隔离副本的局部配置。
        var rois = node.Regions.Select(r => new RoiDefinition(r.Id, system.ToImageGeometry(r.ToGeometry()),
            r.Exclude ? ERoiPurpose.Exclude : ERoiPurpose.Include, r.Enabled)).ToArray();
        _loading = true;
        try { Editor.Cancel(); Editor.Load(new RoiDocument(rois)); }
        finally { _loading = false; }
    }
}
