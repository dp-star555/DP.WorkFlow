using System.ComponentModel;
using DP.Vision;
using DP.Vision.Algorithms;

namespace DP.WorkFlow;

/// <summary>
/// 单行 OCR 节点：在图像页画一个矩形文字框（可旋转，宽度方向为文字行方向），识别前按文字框校正成水平小图。
/// 绑定坐标系后文字框随工件移动和旋转；不包含文本检测。
/// </summary>
[WorkflowNode("Vision.RecognizeTextLine", DisplayName = "识别单行文字", Category = WorkflowVisionCategories.Recognition)]
public sealed class RecognizeVisionTextLineNodeModel : AnalyzeVisionFrameNodeModel, IWorkflowVisionAlgorithmNode
{
    /// <summary>模型及预处理依赖均保存在此节点的配方配置中。</summary>
    [Browsable(false)]
    public VisionAlgorithmSelection Algorithm { get; set; } = new()
    {
        ImplementationId = "ppocr.recognize",
        Dependencies = new() { ["preprocessor"] = new() { ImplementationId = "opencv.text-preprocess" } }
    };
    /// <inheritdoc/>
    public override string NodeType => "Vision.RecognizeTextLine";
    /// <inheritdoc/>
    public override EWorkflowVisionRange RangeCapability => EWorkflowVisionRange.Region;
    /// <summary>文字框只做几何校正，不叠加区域掩膜。</summary>
    public override bool SupportsRegionMask => false;
    /// <inheritdoc/>
    public IReadOnlyList<WorkflowVisionAlgorithmSlot> GetAlgorithmSlots() => new[] { new WorkflowVisionAlgorithmSlot("recognizer", typeof(ITextLineRecognizer), Algorithm) };

    /// <inheritdoc/>
    public override IReadOnlyList<string> ValidateConfiguration()
    {
        var errors = base.ValidateConfiguration().ToList();
        if (!FullImage) errors.Add("文字识别只使用图像页绘制的矩形文字框。");
        try { _ = TextBox(); } catch (InvalidOperationException error) { errors.Add(error.Message); }
        return errors;
    }

    /// <summary>唯一启用的包含矩形；绑定坐标系时为业务坐标表达。</summary>
    /// <returns>文字框。</returns>
    /// <exception cref="InvalidOperationException">没有、多于一个或不是矩形。</exception>
    internal RectangleGeometry TextBox()
    {
        var enabled = Regions?.Where(r => r.Enabled).ToArray() ?? [];
        if (enabled.Length != 1 || enabled[0].Exclude || enabled[0].Shape != EWorkflowVisionRoiShape.Rectangle)
            throw new InvalidOperationException("请在图像页绘制一个矩形文字框（只能有一个，且不能是排除ROI）。");
        return (RectangleGeometry)enabled[0].ToGeometry();
    }

    /// <summary>校正图到原图的映射：校正图像素边界坐标 → 文字框 → 原图。</summary>
    /// <param name="box">文字框。</param><param name="coordinates">文字框所在坐标系；空表示原图。</param>
    /// <returns>映射矩阵及校正图尺寸。</returns>
    internal static (CoordinateMatrix2D OutputToImage, int Width, int Height) Rectify(RectangleGeometry box, VisionCoordinateSystem? coordinates)
    {
        // 校正图按原图分辨率采样：业务单位乘以坐标系尺度得到像素。
        double scale = coordinates?.SimilarityScale ?? 1;
        int width = Math.Max(1, (int)Math.Round(box.Width * scale)), height = Math.Max(1, (int)Math.Round(box.Height * scale));
        double cos = Math.Cos(box.Angle), sin = Math.Sin(box.Angle), w = box.Width / 2, h = box.Height / 2;
        var outputToBox = CoordinateMatrix2D.FromAffine(
            cos / scale, -sin / scale, box.Center.X - cos * w + sin * h,
            sin / scale, cos / scale, box.Center.Y - sin * w - cos * h);
        return (coordinates is null ? outputToBox : coordinates.LocalToImage.Multiply(outputToBox), width, height);
    }
}

/// <summary>同帧单行 OCR 证据；识别在按文字框校正后的图像上进行，字符识别不能作为外观合格依据。</summary>
public sealed class WorkflowVisionTextLineFact : IWorkflowVisionFrameFact
{
    /// <summary>保留模型身份、置信度、CTC 观测及原图矩形。</summary>
    public WorkflowVisionTextLineFact(string frameId, TextLineRecognition recognition) { FrameId = frameId; Recognition = recognition; }
    /// <inheritdoc/>
    [DisplayName("图像标识")]
    public string FrameId { get; }
    /// <summary>原始识别证据。</summary>
    [DisplayName("识别结果")]
    public TextLineRecognition Recognition { get; }
    /// <inheritdoc/>
    [DisplayName("摘要")]
    public string Summary => string.IsNullOrEmpty(Recognition.Text) ? "未识别出文字" : $"{Recognition.Text}（置信度 {Recognition.Confidence:P1}）";
}

/// <summary>只调用准备好的单行识别器。</summary>
public sealed class RecognizeVisionTextLineNodeHandler : WorkflowNodeHandler<RecognizeVisionTextLineNodeModel>
{
    /// <inheritdoc/>
    protected override ValueTask<NodeExecutionResult> ExecuteAsync(RecognizeVisionTextLineNodeModel node,
        IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
    {
        var frame = context.ResolveInput(node.Frame) ?? throw new InvalidOperationException("输入帧为空。");
        var errors = node.ValidateConfiguration();
        if (errors.Count > 0) throw new InvalidOperationException(string.Join("；", errors));
        var coordinates = node.ResolveCoordinates(frame, context);
        var (outputToImage, width, height) = RecognizeVisionTextLineNodeModel.Rectify(node.TextBox(), coordinates);
        using var patch = Resample(frame.Image, outputToImage, width, height, cancellationToken);
        var bindings = context.Services.GetService(typeof(IWorkflowVisionAlgorithmBindings)) as IWorkflowVisionAlgorithmBindings
            ?? throw new InvalidOperationException("宿主没有提供节点算法绑定入口。");
        var recognition = bindings.Invoke<ITextLineRecognizer, TextLineRecognition>(context, "recognizer",
            recognizer => recognizer.Recognize(patch, new PixelBounds(0, 0, width, height), cancellationToken), cancellationToken);
        var fact = new WorkflowVisionTextLineFact(frame.FrameId, recognition);
        return ValueTask.FromResult(NodeExecutionResult.Continue(output: fact, projection: WorkflowVisionFrameScope.Stage(context, frame, fact)));
    }

    private static IImageSource Resample(IImageSource image, CoordinateMatrix2D outputToImage, int width, int height, CancellationToken token)
    {
        try { return AffineImageResampler.Nearest(image, outputToImage, width, height, token); }
        catch (ArgumentOutOfRangeException) { throw new InvalidOperationException("文字框过大，校正图超过1600万像素。"); }
        catch (ArgumentException) { throw new InvalidOperationException("文字框超出图像范围，请调整文字框或检查定位。"); }
    }
}

/// <summary>由宿主发现的 OCR 节点入口；不引用 ONNX/OpenCV 实现。</summary>
public sealed class WorkflowVisionTextLineModule : IWorkflowRuntimePluginModule
{
    /// <inheritdoc/>
    public string ExtensionId => "workflow.vision.ocr-line";
    /// <inheritdoc/>
    public void Register(WorkflowRuntimePluginCatalog extensions)
    {
        extensions.Nodes.Register(WorkflowNodeDescriptor.Create<RecognizeVisionTextLineNodeModel, WorkflowVisionTextLineFact>(ports: new[]
        { WorkflowPortDescriptor.Input(maxConnections: int.MaxValue), WorkflowPortDescriptor.Output(WorkflowPorts.Success) }));
        extensions.Handlers.Register(new RecognizeVisionTextLineNodeHandler(), WorkflowRuntimeCapabilityRequirement.Require<ITextLineRecognizer>(),
            WorkflowRuntimeCapabilityRequirement.Require<IWorkflowVisionAlgorithmBindings>(), WorkflowRuntimeCapabilityRequirement.Require<IWorkflowVisionFrameScope>());
    }
}
