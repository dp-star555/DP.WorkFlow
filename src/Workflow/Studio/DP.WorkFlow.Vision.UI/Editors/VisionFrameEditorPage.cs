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
    public bool CanProvide(WorkflowNodeEditorContext context) => context.Node is AnalyzeVisionFrameNodeModel or AcquireVisionImageNodeModel;
    /// <inheritdoc/>
    public IEnumerable<WorkflowNodeEditorPageDescriptor> CreatePages(WorkflowNodeEditorContext context)
    {
        if (!CanProvide(context)) yield break;
        yield return new WorkflowNodeEditorPageDescriptor("Image", "图像与测量范围",
            WorkflowNodeEditorPageKind.Custom, 450, new VisionFrameEditorPageModel(context.Node, frames, reader,
                templates, enableTemplateEditing: false, configurationChanged: context.Session.NotifyNodeConfigurationChanged),
            IconKey: "Image", RendererKey: RendererKey, Priority: 100);
        if (context.Node is IWorkflowVisionTemplateNode && context.RequestedPropertyEditor == WorkflowPropertyEditorKeys.VisionTemplateEditor)
            yield return new WorkflowNodeEditorPageDescriptor("Template", "模板制作/选择", WorkflowNodeEditorPageKind.Custom, 460,
                new VisionTemplateAuthoringPageModel(new VisionFrameEditorPageModel(context.Node, frames, reader,
                    templates: templates, templateEditorOnly: true)), IconKey: "Image",
                RendererKey: VisionTemplateAuthoringPageModel.RendererKey, PropertyEditorKey: WorkflowPropertyEditorKeys.VisionTemplateEditor);
    }
}

/// <summary>帧编辑页视图下拉框的一项：稳定编号与显示文字。</summary>
/// <param name="Code">视图编号，传给<see cref="VisionFrameEditorPageModel.Capture"/>。</param>
/// <param name="Text">显示文字。</param>
public sealed record VisionFrameView(int Code, string Text)
{
    /// <inheritdoc/>
    public override string ToString() => Text;
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
    private readonly Action? _configurationChanged;
    private double _imagePixelsPerScreenPixel = 1;
    private IReadOnlyList<Visual> _visuals = Array.Empty<Visual>();
    // 显示项的点击说明，按显示项标识查找；没有单独说明的项为结果摘要。
    private IReadOnlyDictionary<string, string> _details = new Dictionary<string, string>();
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
    /// <param name="templates">宿主模板制作运行时，可为空。</param>
    /// <param name="enableTemplateEditing">是否创建模板制作草稿。</param>
    /// <param name="templateEditorOnly">是否只编辑模板，保留匹配节点搜索范围。</param>
    /// <param name="configurationChanged">图上编辑修改了节点参数后的通知（例如刷新参数页）。</param>
    public VisionFrameEditorPageModel(IWorkflowNodeModel node, IWorkflowVisionPreviewSource? frames = null, IImageFileReader? reader = null,
        VisionTemplateEditingRuntime? templates = null,
        bool enableTemplateEditing = true, bool templateEditorOnly = false, Action? configurationChanged = null)
    {
        IsTemplateEditor = templateEditorOnly;
        _configurationChanged = configurationChanged;
        if (!templateEditorOnly && node is MeasureVisionCaliperNodeModel caliper) Caliper = new VisionCaliperGizmo(caliper);
        if (!templateEditorOnly && node is FindVisionShapeNodeModel find) Caliper = new VisionFindShapeGizmo(find);
        _node = node ?? throw new ArgumentNullException(nameof(node)); _frames = frames; _reader = reader;
        Editor = new RoiEditor();
        if (node is AnalyzeVisionFrameNodeModel { Coordinates: not null }) { /* 等待同帧定位后显示局部ROI，不在原图上误画局部数值。 */ }
        else if (node is AnalyzeVisionFrameNodeModel { Regions.Count: > 0 } regionNode)
            Editor.Load(new RoiDocument(regionNode.Regions.Select(r => EditableRoi(regionNode, r, r.ToGeometry()))));
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

    /// <summary>节点ROI转为编辑定义；找圆的期望圆保持正圆约束，拖动时不会变成椭圆。</summary>
    private static RoiDefinition EditableRoi(AnalyzeVisionFrameNodeModel node, WorkflowVisionRoi roi, Geometry shape)
    {
        var purpose = roi.Exclude ? ERoiPurpose.Exclude : ERoiPurpose.Include;
        return node is FindVisionCircleNodeModel && shape is EllipseGeometry e && Math.Abs(e.RadiusX - e.RadiusY) <= 1e-6 * Math.Max(e.RadiusX, e.RadiusY)
            ? new RoiDefinition(roi.Id, shape, purpose, roi.Enabled, ERoiConstraint.Circle)
            : new RoiDefinition(roi.Id, shape, purpose, roi.Enabled);
    }

    /// <summary>
    /// 可编辑的ROI、卡尺、找线/找圆搜索范围只在输入图像（及模板测试图像）上显示和拖动；结果图像只显示运行结果。
    /// </summary>
    /// <param name="view">视图编号。</param>
    public static bool ShowsEditableOverlays(int view) => view is 0 or 3;

    /// <summary>打开窗口时的默认视图：已运行出结果时进入结果图像；还没有结果且可编辑范围的节点进入输入图像。</summary>
    public int DefaultView
    {
        get
        {
            bool hasResult;
            lock (_gate)
            {
                using var current = _disposed ? null : _frames?.Capture(_node.Id);
                hasResult = current?.Facts is not null;
            }
            return DefaultViewFor(IsTemplateEditor, CanEdit || Caliper is not null, hasResult);
        }
    }

    /// <summary>默认视图规则：模板制作 4；有结果 1；无结果但可编辑 0；其它 1。</summary>
    /// <param name="templateEditor">是否独立模板编辑。</param>
    /// <param name="editable">是否有可编辑的ROI或卡尺。</param>
    /// <param name="hasResult">节点是否已有运行结果。</param>
    public static int DefaultViewFor(bool templateEditor, bool editable, bool hasResult)
        => templateEditor ? 4 : hasResult || !editable ? 1 : 0;

    /// <summary>卡尺节点的图上编辑器；其他节点为空。</summary>
    public IVisionCanvasGizmo? Caliper { get; }

    /// <summary>当前缩放下 1 个屏幕像素对应的原图像素；由画布在缩放变化时设置，用于固定控制点的屏幕大小。</summary>
    public double ImagePixelsPerScreenPixel
    {
        get => _imagePixelsPerScreenPixel;
        set { if (value > 0 && Math.Abs(value - _imagePixelsPerScreenPixel) > _imagePixelsPerScreenPixel * 0.05) { _imagePixelsPerScreenPixel = value; InvalidatePreview(); } }
    }

    /// <summary>强制下一次 <see cref="Capture"/> 重新生成叠加图形（例如图上拖动修改参数后）。</summary>
    public void InvalidatePreview() { lock (_gate) _lastKey = null; }

    /// <summary>图上编辑结束后通知宿主节点参数已变化。</summary>
    public void NotifyConfigurationChanged() => _configurationChanged?.Invoke();
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
            var options = ((LocateVisionTemplatePoseNodeModel)_node).OptionsForPreview(parent);
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
        var pose = (LocateVisionTemplatePoseNodeModel)_node;
        var options = roiSelfTest ? new TemplatePoseOptions(0d, 0d, 1d, 1d, pose.MinimumScore, pose.MaximumWork, maximumCandidates: pose.MaximumCandidates) : pose.OptionsForPreview();
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
        using var frame = (manual ? _manual : preview?.Frame)?.Retain() ?? throw new InvalidOperationException("没有试匹配图像，请先读取测试图像或运行输入。");
        var parent = node.Coordinates == null ? null : CaptureCoordinates(node.Coordinates, frame);
        using var maskPreview = node.Mask.Binding is { IsPublicData: false } maskBinding ? _frames?.Capture(maskBinding.NodeId) : null;
        var range = node.ResolvePreviewRange(frame, parent, maskPreview?.Facts as RegionAnalysisResult);
        var options = ((LocateVisionTemplatePoseNodeModel)_node).OptionsForPreview(parent);
        await template.TryMatchAsync(frame, range.Bounds, options, range.Region);
    }
    /// <summary>共享ROI编辑器，原图坐标。</summary>
    public RoiEditor Editor { get; }
    /// <summary>是否可以修改测量范围。</summary>
    public bool CanEdit => SupportsRegions || _node is AnalyzeVisionFrameNodeModel { RangeCapability: EWorkflowVisionRange.Rectangle };
    /// <summary>根据节点范围能力启用完整面积形状及包含/排除，不维护节点类型白名单；找线/找圆由专用图上编辑器编辑搜索范围。</summary>
    public bool SupportsRegions => IsTemplateEditor || _node is AnalyzeVisionFrameNodeModel { RangeCapability: EWorkflowVisionRange.Region } and not FindVisionShapeNodeModel;
    /// <summary>
    /// “区域类型”下拉框的工具。模板制作样图（画布编辑模板制作区域，结果存为ROI文档）可用全部面积形状及画笔/橡皮；
    /// 节点范围只能保存矩形、椭圆和多边形，有面积范围能力时为选择、矩形、旋转矩形、椭圆、多边形，只支持矩形时为选择和矩形。
    /// </summary>
    /// <param name="templateMaking">当前画布是否在编辑模板制作区域。</param>
    public IReadOnlyList<RoiToolChoice> RegionTools(bool templateMaking) => templateMaking
        ? RoiToolChoice.Areas.Where(c => c.Tool is not (ERoiTool.InsertVertex or ERoiTool.DeleteVertex)).ToArray()
        : SupportsRegions
            ? RoiToolChoice.Areas.Where(c => c.Tool is ERoiTool.Select or ERoiTool.Rectangle or ERoiTool.RotatedRectangle or ERoiTool.Ellipse or ERoiTool.Polygon).ToArray()
            : RoiToolChoice.Areas.Where(c => c.Tool is ERoiTool.Select or ERoiTool.Rectangle).ToArray();
    /// <summary>
    /// 视图下拉框的项。编号保持不变（0输入、1结果、2模板、3测试图像、4模板制作样图、5模板试匹配），
    /// 测试图像只能由模板试匹配读取，所以只在有模板的页面出现。
    /// </summary>
    public IReadOnlyList<VisionFrameView> Views => Template == null
        ? new VisionFrameView[] { new(0, "输入图像"), new(1, "结果图像"), new(2, "模板图像") }
        : new VisionFrameView[] { new(0, "输入图像"), new(1, "结果图像"), new(2, "模板图像"), new(3, "测试图像"), new(4, "模板制作样图"), new(5, "模板试匹配") };
    /// <summary>最近的明确状态或错误。</summary>
    public string Status { get; private set; } = "选择运行输入或结果图像。范围使用原图整数半开矩形。";

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
            var mapped = e.After.Rois.Select(r => VisionCoordinateRebinding.MapRegion(node.Coordinates is null ? r : new RoiDefinition(r.Id,
                _displayCoordinates!.ToLocalGeometry(r.Shape), r.Purpose, r.Enabled))).ToList();
            node.Regions = mapped; node.FullImage = true;
            Status = $"已编辑{mapped.Count}个ROI；包含并集减排除并集，确认节点后提交。";
        }
        catch (Exception ex) when (ex is ArgumentException or OverflowException) { Status = ex.Message; }
    }

    /// <summary>显式选择全图，不在打开页面时自动修复。</summary>
    public void UseFullImage()
    {
        if (_node is AnalyzeVisionFrameNodeModel { Coordinates: not null })
            throw new InvalidOperationException("定位随动必须保留显式局部ROI；需要固定全图时请先解除定位。");
        if (CanEdit) Editor.Load(new RoiDocument(Array.Empty<RoiDefinition>()));
    }

    /// <summary>只读文件预览，不执行算法、不更改节点文件路径；模板节点用于试匹配，彩色文件按亮度转换为8位灰度。</summary>
    /// <param name="path">用户显式选择的预览文件。</param>
    public async Task ReadPreviewAsync(string path)
    {
        var token = _lifetime.Token;
        if (_reader is null) throw new InvalidOperationException("宿主未配置文件预览读取器。");
        using var read = await _reader.ReadAsync(path, token).ConfigureAwait(false);
        using var image = _node is IWorkflowVisionTemplateNode ? VisionImage.ToGray8(read, token) : read.Retain();
        lock (_gate)
        {
            if (_disposed) return;
            var next = new ImageFrame(Guid.NewGuid().ToString("N"), image);
            _manual?.Dispose(); _manual = next; _lastKey = null;
        }
    }

    /// <summary>捕获要提交到平台画布的新快照，未变化时为空。0输入、1结果、2模板、3测试图像、4模板制作样图、5模板试匹配。</summary>
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
            if (view == 2 && _node is LocateVisionTemplatePoseNodeModel p && p.Template.Binding is { IsPublicData: false } poseTemplate) id = poseTemplate.NodeId;
            using var current = view == 3 ? null : _frames?.Capture(id);
            var frame = view == 3 ? _manual : current?.Frame;
            if (frame is null)
            {
                CoordinateEditingReady = false;
                if (_node is FindVisionShapeNodeModel)
                    Status = "还没有图像：先运行一次流程取图，再在图上调整搜索范围；新建节点已带默认搜索范围，取到图像后自动居中。";
                return null;
            }
            // 新建的找线/找圆带占位搜索范围：首次拿到图像时按图像尺寸居中，并同步到ROI编辑器和参数页。
            if (view is 0 or 1 && _node is FindVisionShapeNodeModel placeholder
                && placeholder.FitPlaceholderSearchRoi(frame.Image.Info.Width, frame.Image.Info.Height))
            {
                _loading = true;
                try { Editor.Cancel(); Editor.Load(new RoiDocument(placeholder.Regions.Select(r => EditableRoi(placeholder, r, r.ToGeometry())))); }
                finally { _loading = false; }
                _lastKey = null;
                _configurationChanged?.Invoke();
            }
            UpdateCoordinatePreview(frame, view);
            // 绑定坐标系的卡尺按本帧坐标系换算到原图显示和拖动；没有同帧定位时不显示。
            if (Caliper is not null) Caliper.Coordinates = CoordinateEditingReady ? _displayCoordinates : null;
            var analysis = _node as AnalyzeVisionFrameNodeModel;
            using var maskPreview = analysis?.Mask.Binding is { IsPublicData: false } maskBinding ? _frames?.Capture(maskBinding.NodeId) : null;
            string key = $"{view}:{frame.FrameId}:{current?.Sequence}:{maskPreview?.Sequence}:{analysis?.Mask.Source}:{analysis?.Mask.Binding}:{analysis?.FullImage}:{analysis?.X}:{analysis?.Y}:{analysis?.Width}:{analysis?.Height}:{ShowMask}:{Caliper?.Key}:{_imagePixelsPerScreenPixel:0.###}";
            if (_lastKey == key) return null;
            var facts = view == 1 ? current?.Facts : null;
            _lastKey = key; // 失败的显示包不在定时器中反复分配；切换来源或新帧才重试。
            var shown = Visuals(facts, Describe(facts, frame), _imagePixelsPerScreenPixel).Take(10001).ToArray();
            var visuals = shown.Select(v => v.Visual).ToArray();
            if (visuals.Length > 10000) throw new InvalidOperationException("结果超过画布显示预算；未截断运行事实，当前结果不显示叠加。");
            // 显示预算不是算法结果裁剪；大结果明确拒绝显示并保留完整运行事实。
            var layers = new List<CanvasLayer>();
            string maskStatus = "";
            if (ShowMask && analysis?.RangeCapability == EWorkflowVisionRange.Region && analysis is not FindVisionShapeNodeModel && ShowsEditableOverlays(view))
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
            if (Caliper is { IsEditable: true } && ShowsEditableOverlays(view))
                layers.Add(new CanvasLayer("caliper", ELayerKind.Annotation, Caliper.Visuals(_imagePixelsPerScreenPixel), 10, name: "卡尺"));
            var overlay = new GeometryOverlay(frame.FrameId, layers);
            var canvas = new CanvasFrame(frame.FrameId, ++_sequence, frame.Image, overlay);
            _visuals = visuals; _details = shown.ToDictionary(v => v.Visual.Id, v => v.Detail, StringComparer.Ordinal); _lastKey = key;
            Status = Describe(facts, frame) + maskStatus;
            // 卡尺尺寸说明放在状态栏，图上不画文字标签。
            if (Caliper is not null)
                Status += ShowsEditableOverlays(view)
                    ? (Caliper.IsEditable ? " " + Caliper.Caption + "。" : "") + " " + Caliper.Hint
                    : " 切换到“输入图像”可查看和拖动编辑范围。";
            else if (CanEdit && view == 1) Status += " 切换到“输入图像”可查看和编辑ROI。";
            if (_node is AnalyzeVisionFrameNodeModel { Coordinates: { } binding })
                Status += CoordinateEditingReady ? $" {(SupportsRegions ? "ROI" : "几何表达")}使用坐标来源 {binding.System.Binding?.NodeId} 的本帧坐标系 {_displayCoordinates?.Definition.Name} 显示。" : " 当前视图只读，不使用其他帧的定位。";
            return canvas;
        }
    }

    /// <summary>按原图坐标拾取实际结果，不把ROI控制点当检测证据。</summary>
    /// <param name="point">原图坐标。</param>
    /// <param name="tolerance">原图像素容差。</param>
    /// <returns>结果说明。</returns>
    public string? Pick(PointD point, double tolerance)
    {
        var hit = _visuals.Reverse().FirstOrDefault(v => v.Geometry.Contains(point, tolerance));
        return hit is null ? null : _details.TryGetValue(hit.Id, out var detail) ? detail : hit.Caption;
    }

    private static string Describe(object? facts, ImageFrame frame) => facts switch
    {
        IWorkflowVisionFrameFact result => result.Summary,
        IVisionDisplayFact result => result.Summary,
        IVisionGeometryFact result => result.Summary,
        ColorAnalysisResult c => $"RGB=({c.Red:F3},{c.Green:F3},{c.Blue:F3})；像素 {c.PixelCount}；Alpha不加权。",
        _ => $"帧 {frame.FrameId}；{frame.Image.Info.Width}×{frame.Image.Info.Height}；{frame.Image.Info.Layout}"
    };

    // 所有结果按显示角色统一配色；结果类型只声明角色，不决定颜色。
    private static uint RoleColor(EVisionDisplayRole role) => role switch
    {
        EVisionDisplayRole.Input => 0xFF94A3B8,
        EVisionDisplayRole.Measurement => 0xFF22D3EE,
        EVisionDisplayRole.Accepted => 0xFF22C55E,
        EVisionDisplayRole.Rejected => 0xFFEF4444,
        EVisionDisplayRole.Model => 0xFFA78BFA,
        EVisionDisplayRole.Region => 0xFF22DD88,
        EVisionDisplayRole.AxisX => 0xFFF87171,
        EVisionDisplayRole.AxisY => 0xFF4ADE80,
        _ => 0xFFFFCC00
    };

    // 颜色优先级：显式颜色 > 判定状态 > 角色；状态同时改几何和标签的颜色。
    private static uint ItemColor(VisionDisplayItem item) => item.Argb ?? item.Tone switch
    {
        EVisionDisplayTone.Ok => 0xFF22C55E,
        EVisionDisplayTone.Warning => 0xFFF59E0B,
        EVisionDisplayTone.Ng => 0xFFEF4444,
        _ => RoleColor(item.Role)
    };

    /// <summary>
    /// 把结果的显示项画成图层：几何按显式颜色、判定状态、角色的顺序着色，标记点画成约 10 屏幕像素的 ×，图上只写显示项的短标签；
    /// 点击说明取显示项自带说明，没有时为结果摘要。只有显示几何的旧结果按“主结果”画。
    /// </summary>
    private static IEnumerable<(Visual Visual, string Detail)> Visuals(object? facts, string summary, double unit)
    {
        var items = facts switch
        {
            IVisionDisplayFact display => display.DisplayItems,
            IVisionGeometryFact geometry => geometry.DisplayGeometry.Select(g => new VisionDisplayItem(g, EVisionDisplayRole.Result)).ToArray(),
            _ => Array.Empty<VisionDisplayItem>()
        };
        double r = 5 * Math.Max(1e-6, unit);
        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var (color, detail) = (ItemColor(item), item.Detail ?? summary);
            if (item.Marker is { } p)
            {
                yield return (new Visual($"item-{i}", new ContourGeometry(new[] { new PointD(p.X - r, p.Y - r), new PointD(p.X + r, p.Y + r) }), color, item.Label), detail);
                yield return (new Visual($"item-{i}-x", new ContourGeometry(new[] { new PointD(p.X - r, p.Y + r), new PointD(p.X + r, p.Y - r) }), color), detail);
            }
            else yield return (new Visual($"item-{i}", item.Geometry!, color, item.Label), detail);
        }
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
