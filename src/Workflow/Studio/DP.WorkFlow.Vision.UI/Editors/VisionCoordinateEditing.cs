using DP.Vision;
using DP.Vision.Algorithms;
using DP.Vision.UI;

namespace DP.WorkFlow.Vision.UI;

/// <summary>可供显式绑定的定位节点。</summary>
/// <param name="NodeId">节点身份。</param><param name="Label">显示文本。</param>
public sealed record VisionCoordinateSource(string NodeId, string Label)
{
    /// <inheritdoc/>
    public override string ToString() => Label;
}

public sealed partial class VisionFrameEditorPageModel
{
    private LocatedCoordinateSystem? _displayCoordinates;
    private string? _displayedFrameId;
    private string? _displayBindingKey;
    private bool HasDisplayedCoordinateBinding(WorkflowVisionCoordinateBinding binding) => CoordinateEditingReady
        && _displayCoordinates is not null && binding.CoordinateSystemId == _displayCoordinates.CoordinateSystemId
        && binding.TemplateSignature == _displayCoordinates.TemplateSignature && binding.System?.Binding?.ToString() == _displayBindingKey;
    /// <summary>文档内定位节点候选；选择不等于自动应用。</summary>
    public IReadOnlyList<VisionCoordinateSource> CoordinateSources { get; }
    /// <summary>面积ROI页面支持绘制时绑定/解除；卡尺可通过强类型配置指定局部端点和绑定。</summary>
    public bool CanBindCoordinates => SupportsRegions;
    /// <summary>当前显示图像是否允许编辑；手动图像不能借用另一帧的定位。</summary>
    public bool CoordinateEditingReady { get; private set; }

    /// <summary>显式将当前图上ROI转为指定模板局部配置；仅修改隔离编辑副本。</summary>
    /// <param name="sourceNodeId">已成功定位的节点。</param>
    public void BindCoordinates(string sourceNodeId)
    {
        if (!CanBindCoordinates || _node is not AnalyzeVisionFrameNodeModel node) throw new InvalidOperationException("此页面不支持面积ROI定位绑定。");
        if (node.Coordinates is not null) throw new InvalidOperationException("已绑定定位；更换坐标系前请先显式解除并转换为当前图像ROI。");
        using var input = CaptureInput(node);
        using var preview = _frames?.Capture(sourceNodeId);
        var system = (preview?.Facts as TemplatePoseResult)?.CoordinateSystem ?? throw new InvalidOperationException("请先运行所选定位节点，必须成功检出。");
        system.Validate(input.Frame, system.CoordinateSystemId, system.TemplateSignature);
        if (_displayedFrameId != input.Frame.FrameId) throw new InvalidOperationException("请先显示与定位同帧的输入图像，再确认ROI绑定。");
        // 无绘制范围时显式以模板矩形作为初始局部范围，而不是整张场景图。
        var imageRois = Editor.Document.Rois.ToArray();
        var local = imageRois.Length == 0
            ? new List<WorkflowVisionRoi> { new() { Id = "template-domain", CenterX = system.Pose.TemplateWidth / 2d,
                CenterY = system.Pose.TemplateHeight / 2d, Width = system.Pose.TemplateWidth, Height = system.Pose.TemplateHeight } }
            : imageRois.Select(r => MapRegion(new RoiDefinition(r.Id, system.ToLocalGeometry(r.Shape), r.Purpose, r.Enabled))).ToList();
        if (!local.Any(r => r.Enabled && !r.Exclude)) throw new InvalidOperationException("随动ROI必须包含至少一个启用的包含范围。");
        // 先完成所有转换，再一次修改隔离配置；不缓存运行矩阵到文档。
        node.Coordinates = new WorkflowVisionCoordinateBinding
        {
            System = WorkflowInput<LocatedCoordinateSystem>.FromBinding(new WorkflowBindingKey(sourceNodeId, "CoordinateSystem")),
            CoordinateSystemId = system.CoordinateSystemId, TemplateSignature = system.TemplateSignature
        };
        node.Regions = local; node.FullImage = true; _displayCoordinates = system; CoordinateEditingReady = true;
        _displayBindingKey = node.Coordinates.System.Binding?.ToString();
        LoadCoordinateRois(node, system); _lastKey = null;
        Status = "ROI已转换为模板局部坐标；确认节点后才提交，后续运行按本帧定位随动。";
    }

    /// <summary>显式解除定位，将局部ROI转换为当前显示帧上的固定ROI。</summary>
    public void UnbindCoordinates()
    {
        if (_node is not AnalyzeVisionFrameNodeModel { Coordinates: { } binding } node) return;
        using var input = CaptureInput(node);
        var system = CaptureCoordinates(binding, input.Frame);
        if (!CoordinateEditingReady || _displayedFrameId != input.Frame.FrameId || !ReferenceEquals(_displayCoordinates, system))
            throw new InvalidOperationException("请先显示同帧有效定位后再解除。");
        var mapped = node.Regions.Select(r => MapRegion(new RoiDefinition(r.Id, system.ToImageGeometry(r.ToGeometry()),
            r.Exclude ? ERoiPurpose.Exclude : ERoiPurpose.Include, r.Enabled))).ToList();
        node.Regions = mapped; node.Coordinates = null; node.FullImage = true; _displayCoordinates = null;
        _loading = true;
        try { Editor.Load(new RoiDocument(mapped.Select(r => new RoiDefinition(r.Id, r.ToGeometry(), r.Exclude ? ERoiPurpose.Exclude : ERoiPurpose.Include, r.Enabled)))); }
        finally { _loading = false; }
        _lastKey = null; Status = "已显式转为当前原图固定ROI；确认节点后提交。";
    }

    private WorkflowVisionPreview CaptureInput(AnalyzeVisionFrameNodeModel node)
    {
        if (node.Frame.Binding is not { IsPublicData: false } binding)
            throw new InvalidOperationException("交互制作需要可预览的节点图像绑定；公共数据绑定可执行，但此页面不猜测其图像来源。");
        return _frames?.Capture(binding.NodeId) ?? throw new InvalidOperationException("尚无输入图像预览，请先运行采集和定位。");
    }

    private LocatedCoordinateSystem CaptureCoordinates(WorkflowVisionCoordinateBinding binding, ImageFrame frame)
    {
        if (binding.System.Binding is not { IsPublicData: false } source || source.MemberPath != "CoordinateSystem")
            throw new InvalidOperationException("交互制作需要直接绑定定位节点的CoordinateSystem成员。");
        using var preview = _frames?.Capture(source.NodeId);
        var system = (preview?.Facts as TemplatePoseResult)?.CoordinateSystem ?? throw new InvalidOperationException("本帧没有成功定位；局部ROI不可编辑。");
        system.Validate(frame, binding.CoordinateSystemId, binding.TemplateSignature); return system;
    }

    private void UpdateCoordinatePreview(ImageFrame frame, int view)
    {
        if (_node is not AnalyzeVisionFrameNodeModel { Coordinates: { } binding } node)
        { CoordinateEditingReady = CanEdit; return; }
        bool wasReady = CoordinateEditingReady;
        CoordinateEditingReady = false;
        if (view is 2 or 3) return; // 模板/手动预览只读，不把局部数值画到无关图像。
        var system = CaptureCoordinates(binding, frame);
        if (!wasReady || !ReferenceEquals(_displayCoordinates, system))
        {
            LoadCoordinateRois(node, system); _displayCoordinates = system;
        }
        _displayBindingKey = binding.System.Binding?.ToString();
        CoordinateEditingReady = CanEdit;
    }

    private void LoadCoordinateRois(AnalyzeVisionFrameNodeModel node, LocatedCoordinateSystem system)
    {
        // 图像切换时取消未完成手势；完成的修改已经写入隔离副本的局部配置。
        var rois = node.Regions.Select(r => new RoiDefinition(r.Id, system.ToImageGeometry(r.ToGeometry()),
            r.Exclude ? ERoiPurpose.Exclude : ERoiPurpose.Include, r.Enabled)).ToArray();
        _loading = true;
        try { Editor.Cancel(); Editor.Load(new RoiDocument(rois)); }
        finally { _loading = false; }
    }
}
