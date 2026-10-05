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
    private VisionCoordinateSystem? _displayCoordinates;
    private string? _displayedFrameId;
    private string? _displayBindingKey;
    private bool HasDisplayedCoordinateBinding(WorkflowVisionCoordinateBinding binding) => CoordinateEditingReady
        && _displayCoordinates is not null && binding.CoordinateSystemId == _displayCoordinates.CoordinateSystemId
        && (string.IsNullOrEmpty(binding.DefinitionSignature) || binding.DefinitionSignature == _displayCoordinates.Definition.Signature && binding.DefinitionVersion == _displayCoordinates.Definition.Version)
        && (string.IsNullOrEmpty(binding.TemplateSignature) || _displayCoordinates is LocatedCoordinateSystem legacy && binding.TemplateSignature == legacy.TemplateSignature)
        && binding.System?.Binding?.ToString() == _displayBindingKey;
    /// <summary>文档内定位节点候选；选择不等于自动应用。</summary>
    public IReadOnlyList<VisionCoordinateSource> CoordinateSources { get; }
    /// <summary>范围能力允许显式定位绑定；面积ROI、卡尺和几何事实使用各自配置语义。</summary>
    public bool CanBindCoordinates => !IsTemplateEditor && _node is AnalyzeVisionFrameNodeModel node && node.SupportsCoordinates;
    /// <summary>当前显示图像是否允许编辑；手动图像不能借用另一帧的定位。</summary>
    public bool CoordinateEditingReady { get; private set; }

    /// <summary>显式绑定定位，转换ROI/卡尺配置；几何点保持其显式输入空间，仅修改隔离副本。</summary>
    /// <param name="sourceNodeId">已成功定位的节点。</param>
    public void BindCoordinates(string sourceNodeId)
    {
        if (!CanBindCoordinates || _node is not AnalyzeVisionFrameNodeModel node) throw new InvalidOperationException("此页面不支持定位绑定。");
        using var input = CaptureInput(node);
        using var preview = _frames?.Capture(sourceNodeId);
        var system = (preview?.Facts as IVisionCoordinateResult)?.CoordinateSystem ?? throw new InvalidOperationException("请先运行所选定位节点，必须成功检出。");
        system.ValidateFrame(input.Frame);
        if (_displayedFrameId != input.Frame.FrameId) throw new InvalidOperationException("请先显示与定位同帧的输入图像，再确认ROI绑定。");
        if (node.Coordinates is { } existing)
        {
            // 同定义切换来源只改绑定。ROI仍表达业务局部位置，不重复转换到新姿态。
            existing.Validate(system, input.Frame);
            if (node is MeasureVisionCaliperNodeModel or FitVisionRobustLineNodeModel) _ = system.SimilarityScale;
            node.Coordinates = WorkflowVisionCoordinateBinding.Capture(sourceNodeId, system);
            _displayCoordinates = system; CoordinateEditingReady = true; _displayBindingKey = node.Coordinates.System.Binding?.ToString();
            if (SupportsRegions) LoadCoordinateRois(node, system);
            _lastKey = null; Status = "已更换同定义坐标来源，局部ROI及参数保持；确认节点后提交。";
            return;
        }
        // 无绘制范围时显式以模板矩形作为初始局部范围，而不是整张场景图。
        var imageRois = Editor.Document.Rois.ToArray();
        if (SupportsRegions && imageRois.Length == 0 && system is not LocatedCoordinateSystem) throw new InvalidOperationException("请先在样图绘制包含ROI，再绑定业务坐标系。");
        var local = !SupportsRegions ? node.Regions : imageRois.Length == 0 && system is LocatedCoordinateSystem template
            ? new List<WorkflowVisionRoi> { new() { Id = "template-domain", CenterX = template.Pose.TemplateWidth / 2d,
                CenterY = template.Pose.TemplateHeight / 2d, Width = template.Pose.TemplateWidth, Height = template.Pose.TemplateHeight } }
            : imageRois.Select(r => MapRegion(new RoiDefinition(r.Id, system.ToLocalGeometry(r.Shape), r.Purpose, r.Enabled))).ToList();
        if (SupportsRegions && !local.Any(r => r.Enabled && !r.Exclude)) throw new InvalidOperationException("随动ROI必须包含至少一个启用的包含范围。");
        Coordinate2D? caliperStart = null, caliperEnd = null;
        if (node is MeasureVisionCaliperNodeModel or FitVisionRobustLineNodeModel) _ = system.SimilarityScale;
        if (node is MeasureVisionCaliperNodeModel caliper)
        {
            _ = new CaliperOptions(new PointD(caliper.StartX, caliper.StartY), new PointD(caliper.EndX, caliper.EndY),
                caliper.HalfWidth, caliper.MinimumGradient, caliper.Polarity, caliper.MinimumSeparation, caliper.BandSampleStep);
            caliperStart = system.ImageToLocal.Map(new Coordinate2D(caliper.StartX, caliper.StartY));
            caliperEnd = system.ImageToLocal.Map(new Coordinate2D(caliper.EndX, caliper.EndY));
        }
        // 先完成所有转换，再一次修改隔离配置；不缓存运行矩阵到文档。
        node.Coordinates = WorkflowVisionCoordinateBinding.Capture(sourceNodeId, system);
        node.Regions = local; node.FullImage = true; _displayCoordinates = system; CoordinateEditingReady = true;
        if (node is MeasureVisionCaliperNodeModel band)
        {
            band.StartX = caliperStart!.Value.X; band.StartY = caliperStart.Value.Y;
            band.EndX = caliperEnd!.Value.X; band.EndY = caliperEnd.Value.Y;
            band.MinimumSeparation /= system.SimilarityScale;
            band.BandSampleStep /= system.SimilarityScale;
        }
        if (node is FitVisionRobustLineNodeModel fit) fit.DistanceThreshold /= system.SimilarityScale;
        _displayBindingKey = node.Coordinates.System.Binding?.ToString();
        if (SupportsRegions) LoadCoordinateRois(node, system);
        _lastKey = null;
        Status = SupportsRegions ? "ROI已转换为所选业务局部坐标；确认节点后才提交，后续运行按本帧变换随动。"
            : "已绑定本帧定位；几何点由显式空间解释，卡尺端点已转换，确认节点后提交。";
    }

    /// <summary>显式解除定位并转换ROI/卡尺；几何点需确认显式空间，不自动改写绑定数值。</summary>
    public void UnbindCoordinates()
    {
        if (_node is not AnalyzeVisionFrameNodeModel { Coordinates: { } binding } node) return;
        using var input = CaptureInput(node);
        var system = CaptureCoordinates(binding, input.Frame);
        if (node is MeasureVisionCaliperNodeModel or FitVisionRobustLineNodeModel) _ = system.SimilarityScale;
        if (!CoordinateEditingReady || _displayedFrameId != input.Frame.FrameId || !ReferenceEquals(_displayCoordinates, system))
            throw new InvalidOperationException("请先显示同帧有效定位后再解除。");
        var mapped = node.Regions.Select(r => MapRegion(new RoiDefinition(r.Id, system.ToImageGeometry(r.ToGeometry()),
            r.Exclude ? ERoiPurpose.Exclude : ERoiPurpose.Include, r.Enabled))).ToList();
        Coordinate2D? caliperStart = null, caliperEnd = null;
        if (node is MeasureVisionCaliperNodeModel caliper)
        {
            caliperStart = system.LocalToImage.Map(new Coordinate2D(caliper.StartX, caliper.StartY));
            caliperEnd = system.LocalToImage.Map(new Coordinate2D(caliper.EndX, caliper.EndY));
            _ = new CaliperOptions(new PointD(caliperStart.Value.X, caliperStart.Value.Y), new PointD(caliperEnd.Value.X, caliperEnd.Value.Y),
                caliper.HalfWidth, caliper.MinimumGradient, caliper.Polarity, caliper.MinimumSeparation * system.SimilarityScale, caliper.BandSampleStep * system.SimilarityScale);
        }
        node.Regions = mapped; node.Coordinates = null; node.FullImage = true; _displayCoordinates = null;
        if (node is MeasureVisionCaliperNodeModel band)
        {
            band.StartX = caliperStart!.Value.X; band.StartY = caliperStart.Value.Y;
            band.EndX = caliperEnd!.Value.X; band.EndY = caliperEnd.Value.Y;
            band.MinimumSeparation *= system.SimilarityScale;
            band.BandSampleStep *= system.SimilarityScale;
        }
        if (node is FitVisionRobustLineNodeModel fit) fit.DistanceThreshold *= system.SimilarityScale;
        _loading = true;
        try { Editor.Load(new RoiDocument(mapped.Select(r => new RoiDefinition(r.Id, r.ToGeometry(), r.Exclude ? ERoiPurpose.Exclude : ERoiPurpose.Include, r.Enabled)))); }
        finally { _loading = false; }
        _lastKey = null; Status = SupportsRegions ? "已显式转为当前原图固定ROI；确认节点后提交。"
            : "已解除定位；卡尺端点及间隔已转为原图表达。几何点请确认显式输入空间，确认节点后提交。";
    }

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
        { CoordinateEditingReady = CanEdit || CanBindCoordinates; return; }
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
