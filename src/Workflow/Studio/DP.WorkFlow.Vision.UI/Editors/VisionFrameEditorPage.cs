using DP.Vision;
using DP.Vision.Algorithms;
using DP.Vision.UI;
using DP.WorkFlow.UI;

namespace DP.WorkFlow.Vision.UI;

/// <summary>新版帧节点共用页面，不依赖厂商原生视口。</summary>
public sealed class VisionFrameEditorPageProvider(IWorkflowVisionPreviewSource? frames = null, IImageFileReader? reader = null,
    VisionTemplateEditingRuntime? templates = null) : IWorkflowNodeEditorPageProvider
{
    /// <summary>两个平台共享的精确Renderer键。</summary>
    public const string RendererKey = "DP.Vision.FrameEditor";
    /// <inheritdoc/>
    public string ExtensionId => RendererKey;
    /// <inheritdoc/>
    public bool CanProvide(WorkflowNodeEditorContext context) => context.Node is AnalyzeVisionFrameNodeModel
        or LoadVisionFileNodeModel or LoadVisionFolderNodeModel
        or CaptureAreaFrameNodeModel or CaptureLineScanFrameNodeModel or AcquireVisionImageNodeModel;
    /// <inheritdoc/>
    public IEnumerable<WorkflowNodeEditorPageDescriptor> CreatePages(WorkflowNodeEditorContext context)
    {
        if (!CanProvide(context)) yield break;
        var nodes = context.Session.Catalog.Snapshot();
        var scope = context.Session.Canvas.Nodes.Select(n => n.Node).ToArray();
        string SourceLabel(IWorkflowNodeModel node)
        {
            var label = string.IsNullOrWhiteSpace(node.Title) ? node.Id : $"{node.Title} [{node.Id}]";
            if (node is IWorkflowVisionCoordinateProducerNode producer)
                try { var definition = WorkflowVisionCoordinateCatalog.ResolveDefinition(scope, producer.Definition); return $"{definition.Name}（v{definition.Version}，{definition.UnitName}）— {label}"; }
                catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { }
            return label;
        }
        yield return new WorkflowNodeEditorPageDescriptor("Image", "图像与测量范围",
            WorkflowNodeEditorPageKind.Custom, 450, new VisionFrameEditorPageModel(context.Node, frames, reader,
                scope
                    .Where(n => n.Id != context.Node.Id && nodes.TryGetValue(n.NodeType, out var descriptor)
                        && descriptor.OutputType is { } type && typeof(IVisionCoordinateResult).IsAssignableFrom(type))
                    .Select(n => new VisionCoordinateSource(n.Id, SourceLabel(n))).ToArray(), templates, enableTemplateEditing: false),
            IconKey: "Image", RendererKey: RendererKey, Priority: 100);
        if (context.Node is IWorkflowVisionTemplateNode && context.RequestedPropertyEditor == WorkflowPropertyEditorKeys.VisionTemplateEditor)
            yield return new WorkflowNodeEditorPageDescriptor("Template", "模板制作/选择", WorkflowNodeEditorPageKind.Custom, 460,
                new VisionTemplateAuthoringPageModel(new VisionFrameEditorPageModel(context.Node, frames, reader,
                    templates: templates, templateEditorOnly: true)), IconKey: "Image",
                RendererKey: VisionTemplateAuthoringPageModel.RendererKey, PropertyEditorKey: WorkflowPropertyEditorKeys.VisionTemplateEditor);
    }
}

/// <summary>共享编辑模型：只改隔离EditingNode；读图/查看预览不隐式修改配置。</summary>
public sealed partial class VisionFrameEditorPageModel : IDisposable, IWorkflowNodeEditorCommitParticipant
{
    private readonly IWorkflowNodeModel _node;
    private readonly IWorkflowVisionPreviewSource? _frames;
    private readonly IImageFileReader? _reader;
    private readonly object _gate = new();
    private ImageFrame? _manual;
    private bool _disposed, _loading;
    private long _sequence;
    private string? _lastKey;
    private IReadOnlyList<Visual> _visuals = Array.Empty<Visual>();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly List<Action> _releaseViews = new();

    /// <summary>平台视图注册UI线程资源清理；由节点窗口关闭时的页面Dispose触发，不由Tab切换触发。</summary>
    /// <param name="release">幂等释放视图的动作。</param>
    public void RegisterViewLifetime(Action release)
    {
        ArgumentNullException.ThrowIfNull(release);
        if (_disposed) throw new ObjectDisposedException(nameof(VisionFrameEditorPageModel));
        _releaseViews.Add(release);
    }

    /// <summary>创建不触发IO和文档修复的页面。</summary>
    /// <param name="node">隔离编辑副本。</param>
    /// <param name="frames">运行预览源。</param>
    /// <param name="reader">显式预览读图能力，可为空。</param>
    /// <param name="coordinateSources">当前文档可显式选择的定位节点。</param>
    /// <param name="templates">宿主模板制作运行时，可为空。</param>
    /// <param name="enableTemplateEditing">是否创建模板制作草稿。</param>
    /// <param name="templateEditorOnly">是否只编辑模板，保留匹配节点搜索范围。</param>
    public VisionFrameEditorPageModel(IWorkflowNodeModel node, IWorkflowVisionPreviewSource? frames = null, IImageFileReader? reader = null,
        IReadOnlyList<VisionCoordinateSource>? coordinateSources = null, VisionTemplateEditingRuntime? templates = null,
        bool enableTemplateEditing = true, bool templateEditorOnly = false)
    {
        IsTemplateEditor = templateEditorOnly;
        _node = node ?? throw new ArgumentNullException(nameof(node)); _frames = frames; _reader = reader;
        CoordinateSources = coordinateSources ?? Array.Empty<VisionCoordinateSource>();
        Editor = new RoiEditor();
        if (node is AnalyzeVisionFrameNodeModel { Coordinates: not null }) { /* 等待同帧定位后显示局部ROI，不在原图上误画局部数值。 */ }
        else if (node is AnalyzeVisionFrameNodeModel { Regions.Count: > 0 } regionNode)
            Editor.Load(new RoiDocument(regionNode.Regions.Select(r => new RoiDefinition(r.Id, r.ToGeometry(),
                r.Exclude ? ERoiPurpose.Exclude : ERoiPurpose.Include, r.Enabled))));
        else if (node is AnalyzeVisionFrameNodeModel { FullImage: false } analysis && analysis.Width > 0 && analysis.Height > 0)
            Editor.Load(new RoiDocument(new[] { new RoiDefinition("bounds",
                new RectangleGeometry(new PointD(analysis.X + analysis.Width / 2d, analysis.Y + analysis.Height / 2d), analysis.Width, analysis.Height), ERoiConstraint.AxisAligned) }));
        Editor.DocumentChanged += OnDocumentChanged;
        if (enableTemplateEditing && node is IWorkflowVisionTemplateNode templateNode)
            Template = new VisionTemplateEditorModel(templateNode, templates, () =>
            { using var input = CaptureInput((AnalyzeVisionFrameNodeModel)node); return input.Frame.Retain(); }, reader);
    }
    /// <summary>模板节点的制作草稿，其他节点为空。</summary>
    public VisionTemplateEditorModel? Template { get; }
    /// <summary>独立模板编辑画布，不修改匹配节点的搜索ROI和坐标绑定。</summary>
    public bool IsTemplateEditor { get; }
    internal IWorkflowNodeModel EditingNode => _node;
    /// <summary>测试输入的可用性及同帧配置检查；不初始化算法。</summary>
    public string TemplateTestInputIssue(bool manual)
    {
        try
        {
            var node = (AnalyzeVisionFrameNodeModel)_node;
            using var preview = manual ? null : CaptureInput(node);
            var frame = manual ? _manual : preview?.Frame;
            if (frame == null) return manual ? "请先读取测试图像。" : "当前没有输入预览，请运行取图或选择样图/测试图像。";
            var parent = node.Coordinates == null ? null : CaptureCoordinates(node.Coordinates, frame);
            using var mask = node.Mask.Binding is { IsPublicData: false } binding ? _frames?.Capture(binding.NodeId) : null;
            var range = node.ResolvePreviewRange(frame, parent, mask?.Facts as RegionAnalysisResult);
            var options = _node is LocateVisionTemplatePoseNodeModel pose ? pose.OptionsForPreview(parent)
                : new TemplatePoseOptions(parent?.RotationRadians ?? 0, parent?.RotationRadians ?? 0, parent?.SimilarityScale ?? 1, parent?.SimilarityScale ?? 1, ((LocateVisionTemplateNodeModel)_node).MinimumScore);
            return Template?.SearchIssue(range.Bounds, options) ?? "";
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { return ex.Message; }
    }
    /// <summary>制作样图的整图测试或制作区域自检；不借用运行坐标和掩码。</summary>
    public async Task TryTemplateSampleAsync(bool roiSelfTest = false)
    {
        var draft = Template ?? throw new InvalidOperationException("缺少模板草稿。");
        using var frame = draft.RetainSample();
        var (bounds, options) = SampleTestSearch(frame, roiSelfTest);
        await draft.TryMatchAsync(frame, bounds, options);
    }
    /// <summary>制作区域自检或样图全图搜索的轻量验证。</summary>
    public string TemplateSampleTestIssue(bool roiSelfTest)
    {
        try
        {
            using var frame = Template!.RetainSample();
            var (bounds, options) = SampleTestSearch(frame, roiSelfTest);
            return Template.SearchIssue(bounds, options);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { return ex.Message; }
    }
    private (PixelBounds Bounds, TemplatePoseOptions Options) SampleTestSearch(ImageFrame frame, bool roiSelfTest)
    {
        var definition = Template!.BuiltDefinition ?? throw new InvalidOperationException("请先生成模型。");
        var bounds = roiSelfTest ? new PixelBounds(definition.X, definition.Y, definition.Width, definition.Height)
            : new PixelBounds(0, 0, frame.Image.Info.Width, frame.Image.Info.Height);
        var pose = _node as LocateVisionTemplatePoseNodeModel;
        var options = roiSelfTest ? new TemplatePoseOptions(0d, 0d, 1d, 1d, pose?.MinimumScore ?? ((LocateVisionTemplateNodeModel)_node).MinimumScore, pose?.MaximumWork ?? 200000000)
            : pose?.OptionsForPreview() ?? new TemplatePoseOptions(0d, 0d, 1d, 1d, ((LocateVisionTemplateNodeModel)_node).MinimumScore);
        return (bounds, options);
    }
    /// <summary>隔离节点当前保存的模板清单引用。</summary>
    public string TemplateReference => _node is IWorkflowVisionTemplateNode t ? t.ModelAlgorithm.Settings.GetValueOrDefault("templatePath", "") : "";
    /// <inheritdoc/>
    public void PrepareCommit() => Template?.PrepareCommit();
    /// <summary>用同帧搜索范围、父坐标和节点运行参数试匹配。</summary>
    public async Task TryTemplateAsync(bool manual = false)
    {
        var template = Template ?? throw new InvalidOperationException("此节点不支持模板制作。");
        var node = (AnalyzeVisionFrameNodeModel)_node;
        using var preview = manual ? null : CaptureInput(node);
        using var frame = (manual ? _manual : preview?.Frame)?.Retain() ?? throw new InvalidOperationException("没有试匹配图像，请先读取手动预览或运行输入。");
        var parent = node.Coordinates == null ? null : CaptureCoordinates(node.Coordinates, frame);
        using var maskPreview = node.Mask.Binding is { IsPublicData: false } maskBinding ? _frames?.Capture(maskBinding.NodeId) : null;
        var range = node.ResolvePreviewRange(frame, parent, maskPreview?.Facts as RegionAnalysisResult);
        var options = _node is LocateVisionTemplatePoseNodeModel pose ? pose.OptionsForPreview(parent)
            : new TemplatePoseOptions(parent?.RotationRadians ?? 0, parent?.RotationRadians ?? 0, parent?.SimilarityScale ?? 1, parent?.SimilarityScale ?? 1, ((LocateVisionTemplateNodeModel)_node).MinimumScore);
        await template.TryMatchAsync(frame, range.Bounds, options, range.Region, parent);
    }
    /// <summary>共享ROI编辑器，原图坐标。</summary>
    public RoiEditor Editor { get; }
    /// <summary>是否可以修改测量范围。</summary>
    public bool CanEdit => SupportsRegions || _node is AnalyzeVisionFrameNodeModel { RangeCapability: EWorkflowVisionRange.Rectangle };
    /// <summary>根据节点范围能力启用完整面积形状及包含/排除，不维护节点类型白名单。</summary>
    public bool SupportsRegions => IsTemplateEditor || _node is AnalyzeVisionFrameNodeModel { RangeCapability: EWorkflowVisionRange.Region };
    /// <summary>“区域类型”下拉框的工具：有面积范围能力时为选择、矩形、旋转矩形、椭圆、多边形；只支持矩形时为选择和矩形。</summary>
    public IReadOnlyList<RoiToolChoice> RegionTools => SupportsRegions
        ? RoiToolChoice.Areas.Where(c => c.Tool is ERoiTool.Select or ERoiTool.Rectangle or ERoiTool.RotatedRectangle or ERoiTool.Ellipse or ERoiTool.Polygon).ToArray()
        : RoiToolChoice.Areas.Where(c => c.Tool is ERoiTool.Select or ERoiTool.Rectangle).ToArray();
    /// <summary>最近的明确状态或错误。</summary>
    public string Status { get; private set; } = "选择运行输入/结果，或显式读取文件预览。范围使用原图整数半开矩形。";

    private void OnDocumentChanged(object? sender, RoiDocumentChangedEventArgs e)
    {
        if (IsTemplateEditor || _loading || !CanEdit || _node is not AnalyzeVisionFrameNodeModel node) return;
        _lastKey = null;
        if (node.Coordinates is not null && !HasDisplayedCoordinateBinding(node.Coordinates)) { Status = "没有同帧定位预览，不能修改局部ROI。"; return; }
        if (e.After.Rois.Count == 0) { node.FullImage = true; node.Regions.Clear(); return; }
        try
        {
            if (node.RangeCapability == EWorkflowVisionRange.Rectangle)
            {
                if (e.After.Rois.Count != 1 || e.After.Rois[0] is not { Enabled: true, Purpose: ERoiPurpose.Include, Shape: RectangleGeometry rectangle }
                    || Math.Abs(rectangle.Angle) > 1e-10) throw new ArgumentException("此节点只接受一个启用的轴对齐矩形。");
                var left = checked((int)Math.Floor(rectangle.Center.X - rectangle.Width / 2));
                var top = checked((int)Math.Floor(rectangle.Center.Y - rectangle.Height / 2));
                var right = checked((int)Math.Ceiling(rectangle.Center.X + rectangle.Width / 2));
                var bottom = checked((int)Math.Ceiling(rectangle.Center.Y + rectangle.Height / 2));
                if (left < 0 || top < 0 || right <= left || bottom <= top) throw new ArgumentException("矩形必须位于原图非负范围且宽高为正。");
                node.X = left; node.Y = top; node.Width = right - left; node.Height = bottom - top; node.FullImage = false; node.Regions.Clear();
                Status = "已编辑水平单行矩形；确认节点后提交。";
                return;
            }
            var mapped = e.After.Rois.Select(r => MapRegion(node.Coordinates is null ? r : new RoiDefinition(r.Id,
                _displayCoordinates!.ToLocalGeometry(r.Shape), r.Purpose, r.Enabled))).ToList();
            node.Regions = mapped; node.FullImage = true;
            Status = $"已编辑{mapped.Count}个ROI；包含并集减排除并集，确认节点后提交。";
        }
        catch (Exception ex) when (ex is ArgumentException or OverflowException) { Status = ex.Message; }
    }

    private static WorkflowVisionRoi MapRegion(RoiDefinition roi)
    {
        var value = new WorkflowVisionRoi { Id = roi.Id, Enabled = roi.Enabled, Exclude = roi.Purpose == ERoiPurpose.Exclude };
        switch (roi.Shape)
        {
            case RectangleGeometry r:
                value.Shape = EWorkflowVisionRoiShape.Rectangle; value.CenterX = r.Center.X; value.CenterY = r.Center.Y;
                value.Width = r.Width; value.Height = r.Height; value.Angle = r.Angle; break;
            case EllipseGeometry e:
                value.Shape = EWorkflowVisionRoiShape.Ellipse; value.CenterX = e.Center.X; value.CenterY = e.Center.Y;
                value.Width = e.RadiusX * 2; value.Height = e.RadiusY * 2; value.Angle = e.Angle; break;
            case ContourGeometry { Closed: true, Filled: true } c:
                value.Shape = EWorkflowVisionRoiShape.Polygon;
                value.Points = c.Points.Select(p => new WorkflowVisionRoiPoint { X = p.X, Y = p.Y }).ToList(); break;
            default: throw new ArgumentException("开放轮廓或点不能作为面积ROI，未修改节点参数。");
        }
        return value;
    }

    /// <summary>显式选择全图，不在打开页面时自动修复。</summary>
    public void UseFullImage()
    {
        if (_node is AnalyzeVisionFrameNodeModel { Coordinates: not null })
            throw new InvalidOperationException("定位随动必须保留显式局部ROI；需要固定全图时请先解除定位。");
        if (CanEdit) Editor.Load(new RoiDocument(Array.Empty<RoiDefinition>()));
    }

    /// <summary>只读文件预览，不执行算法、不更改节点文件路径。</summary>
    /// <param name="path">用户显式选择的预览文件。</param>
    public async Task ReadPreviewAsync(string path)
    {
        var token = _lifetime.Token;
        if (_reader is null) throw new InvalidOperationException("宿主未配置文件预览读取器。");
        using var image = await _reader.ReadAsync(path, token).ConfigureAwait(false);
        lock (_gate)
        {
            if (_disposed) return;
            var next = new ImageFrame(Guid.NewGuid().ToString("N"), image);
            _manual?.Dispose(); _manual = next; _lastKey = null;
        }
    }

    /// <summary>捕获要提交到平台画布的新快照，未变化时为空。0输入、1结果、2模板、3手动预览。</summary>
    /// <param name="view">图像来源。</param>
    /// <returns>调用者拥有的CanvasFrame。</returns>
    public CanvasFrame? Capture(int view)
    {
        lock (_gate)
        {
            if (_disposed) return null;
            if (view is 4 or 5 || view == 2 && _node is IWorkflowVisionTemplateNode { TemplateSource: EWorkflowVisionTemplateSource.Resource })
            {
                // 所有来源投递到同一画布，必须共用页面序号。离开普通预览后也不能沿用其去重键。
                _lastKey = null;
                return Template?.Capture(view == 5, checked(++_sequence));
            }
            string id = _node.Id;
            if (view == 0 && _node is AnalyzeVisionFrameNodeModel a && a.Frame.Binding is { IsPublicData: false } input) id = input.NodeId;
            if (view == 2 && _node is LocateVisionTemplateNodeModel t && t.Template.Binding is { IsPublicData: false } template) id = template.NodeId;
            if (view == 2 && _node is LocateVisionTemplatePoseNodeModel p && p.Template.Binding is { IsPublicData: false } poseTemplate) id = poseTemplate.NodeId;
            using var current = view == 3 ? null : _frames?.Capture(id);
            var frame = view == 3 ? _manual : current?.Frame;
            if (frame is null) { CoordinateEditingReady = false; return null; }
            UpdateCoordinatePreview(frame, view);
            _displayedFrameId = frame.FrameId;
            var analysis = _node as AnalyzeVisionFrameNodeModel;
            using var maskPreview = analysis?.Mask.Binding is { IsPublicData: false } maskBinding ? _frames?.Capture(maskBinding.NodeId) : null;
            string key = $"{view}:{frame.FrameId}:{current?.Sequence}:{maskPreview?.Sequence}:{analysis?.Mask.Source}:{analysis?.Mask.Binding}:{analysis?.FullImage}:{analysis?.X}:{analysis?.Y}:{analysis?.Width}:{analysis?.Height}:{ShowMask}";
            if (_lastKey == key) return null;
            var facts = view == 1 ? current?.Facts : null;
            _lastKey = key; // 失败的显示包不在定时器中反复分配；切换来源或新帧才重试。
            var visuals = Visuals(facts).Take(10001).ToArray();
            if (visuals.Length > 10000) throw new InvalidOperationException("结果超过画布显示预算；未截断运行事实，当前结果不显示叠加。");
            // 显示预算不是算法结果裁剪；大结果明确拒绝显示并保留完整运行事实。
            var layers = new List<CanvasLayer>();
            string maskStatus = "";
            if (ShowMask && analysis?.RangeCapability == EWorkflowVisionRange.Region && view is 0 or 1 or 3)
            {
                try
                {
                    // 手动图像有独立帧身份，绑定区域仍严格检查同帧；不把旧掩膜绘制到新图。
                    var range = analysis.ResolvePreviewRange(frame, analysis.Coordinates is null ? null : _displayCoordinates, maskPreview?.Facts as RegionAnalysisResult);
                    var effective = range.ToRegion();
                    if (!analysis.FullImage || analysis.Regions.Count > 0 || analysis.Mask.Binding is not null || analysis is CreateVisionRegionNodeModel)
                        layers.Add(new CanvasLayer("effective-mask", ELayerKind.Annotation,
                            new[] { new Visual("effective-mask", effective, 0x4022DD88, $"有效掩膜：{effective.AreaPixels}像素") }, -10, name: "有效掩膜"));
                    maskStatus = $" 有效掩膜 {effective.AreaPixels}像素；包含并集减排除，再与上游掩膜和矩形范围取交集。";
                }
                catch (Exception error) when (error is ArgumentException or InvalidOperationException)
                { maskStatus = " 无法预览掩膜：" + error.Message; }
            }
            layers.Add(new CanvasLayer("facts", ELayerKind.Annotation, visuals));
            var overlay = new GeometryOverlay(frame.FrameId, layers);
            var canvas = new CanvasFrame(frame.FrameId, ++_sequence, frame.Image, overlay);
            _visuals = visuals; _lastKey = key;
            Status = Describe(facts, frame) + maskStatus;
            if (_node is AnalyzeVisionFrameNodeModel { Coordinates: { } binding })
                Status += CoordinateEditingReady ? $" {(SupportsRegions ? "ROI" : "几何表达")}绑定模板局部系 {binding.CoordinateSystemId}，按本帧定位显示。" : " 当前视图只读，不使用其他帧的定位。";
            return canvas;
        }
    }

    /// <summary>按原图坐标拾取实际结果，不把ROI控制点当检测证据。</summary>
    /// <param name="point">原图坐标。</param>
    /// <param name="tolerance">原图像素容差。</param>
    /// <returns>结果说明。</returns>
    public string? Pick(PointD point, double tolerance) => _visuals.Reverse().FirstOrDefault(v => v.Geometry.Contains(point, tolerance))?.Caption;

    private static string Describe(object? facts, ImageFrame frame) => facts switch
    {
        IWorkflowVisionFrameFact result => result.Summary,
        IVisionGeometryFact result => result.Summary,
        RegionAnalysisResult r => $"精确Region面积 {r.Area}；孔洞保留，空区域正常完成。",
        CaliperResult c => $"卡尺边缘 {c.Count}；剖面采样 {c.Profile.Count}；梯度峰抛物线插值。",
        TemplatePoseResult p => $"模板姿态 Found={p.Found}；Score={p.Score:F5}；角度 {p.Transform?.AngleRadians * 180 / Math.PI:F2}°；尺度 {p.Transform?.Scale:F4}。",
        RobustLineResult r => $"鲁棒直线内点 {r.InlierCount}；RMS {r.RmsError:F4}px。",
        BlobAnalysisResult b => $"帧 {frame.FrameId}；连通域 {b.Count}；完成≠产品合格。点击Region查看面积/质心。",
        ColorAnalysisResult c => $"RGB=({c.Red:F3},{c.Green:F3},{c.Blue:F3})；像素 {c.PixelCount}；Alpha不加权。",
        EdgeMeasurementResult e => $"{e.Model}；边缘 {e.PointCount}；RMS {e.RmsError:F4}px；半径 {e.Radius:F4}px。",
        TemplateLocationResult t => $"平移定位 Found={t.Found}；Score={t.Score:F5}（非概率）；相对于固定搜索姿态平移。", 
        _ => $"帧 {frame.FrameId}；{frame.Image.Info.Width}×{frame.Image.Info.Height}；{frame.Image.Info.Layout}"
    };

    private static IEnumerable<Visual> Visuals(object? facts)
    {
        if (facts is IVisionGeometryFact geometry)
            for (var index = 0; index < geometry.DisplayGeometry.Count; index++)
                yield return new Visual("geometry-" + index, geometry.DisplayGeometry[index], 0xFFFFCC00, geometry.Summary);
        if (facts is RegionAnalysisResult region)
            yield return new Visual("region", region.Region, 0xFF22DD88, $"精确区域面积 {region.Area}");
        if (facts is CaliperResult caliper)
        {
            yield return new Visual("profile", new ContourGeometry(new[] { caliper.Start, caliper.End }), 0xFF33BBFF, "卡尺采样方向");
            for (int i = 0; i < caliper.Count; i++)
                yield return new Visual($"edge-{i}", new EllipseGeometry(caliper.Edges[i].Position, 1, 1), 0xFFFFCC00,
                    $"边缘 ({caliper.Edges[i].Position.X:F4},{caliper.Edges[i].Position.Y:F4})；梯度 {caliper.Edges[i].Gradient:F3}");
        }
        if (facts is TemplatePoseResult { Transform: { } pose })
        {
            var corners = new[] { new Coordinate2D(0, 0), new Coordinate2D(pose.TemplateWidth, 0), new Coordinate2D(pose.TemplateWidth, pose.TemplateHeight), new Coordinate2D(0, pose.TemplateHeight) }
                .Select(pose.ToImage).Select(p => new PointD(p.X, p.Y)).ToArray();
            yield return new Visual("pose", new ContourGeometry(corners, closed: true), 0xFF33BBFF, $"模板姿态 {pose.AngleRadians * 180 / Math.PI:F2}° ×{pose.Scale:F4}");
        }
        if (facts is RobustLineResult line)
            yield return new Visual("robust-line", new ContourGeometry(new[] { line.A, line.B }), 0xFFFFCC00, $"内点 {line.InlierCount}；RMS {line.RmsError:F4}");
        if (facts is BlobAnalysisResult blobs)
            for (int i = 0; i < blobs.Count; i++)
            {
                var b = blobs.Blobs[i];
                yield return new Visual($"blob-{i}", b.Region, 0xFF22DD88, $"#{i + 1} 面积 {b.Area}；质心 ({b.Centroid.X:F3},{b.Centroid.Y:F3})；栅格周长 {b.Features.GridPerimeter}；圆度 {b.Features.Circularity:F4}；轴比 {b.Features.Elongation:F4}");
            }
        if (facts is EdgeMeasurementResult e)
            yield return new Visual("measurement", e.Model == EEdgeModel.Line
                ? new ContourGeometry(new[] { e.A, e.B }) : new EllipseGeometry(e.A, e.Radius, e.Radius),
                0xFFFFCC00, $"{e.Model} RMS={e.RmsError:F4}px");
        if (facts is TemplateLocationResult { MatchGeometry: { } match })
            yield return new Visual("match", match, 0xFF33BBFF, "模板实际匹配范围；非外接框");
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return; _disposed = true;
            _lifetime.Cancel(); _manual?.Dispose(); _manual = null;
            Template?.Dispose();
            Editor.DocumentChanged -= OnDocumentChanged; Editor.Cancel();
        }
        foreach (var release in _releaseViews) release();
        _releaseViews.Clear();
        _lifetime.Dispose();
    }
}
