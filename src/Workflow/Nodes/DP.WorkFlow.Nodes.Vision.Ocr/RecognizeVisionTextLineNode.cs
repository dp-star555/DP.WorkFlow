using System.ComponentModel;
using DP.Vision;
using DP.Vision.Algorithms;

namespace DP.WorkFlow;

/// <summary>明确水平单行的独立 OCR 节点；不包含文本检测和旋转矫正。</summary>
[WorkflowNode("Vision.RecognizeTextLine", DisplayName = "识别水平单行文字", Category = WorkflowVisionCategories.Recognition)]
public sealed class RecognizeVisionTextLineNodeModel : AnalyzeVisionFrameNodeModel, IWorkflowVisionAlgorithmNode
{
    /// <summary>默认要求显式水平单行矩形。</summary>
    public RecognizeVisionTextLineNodeModel() { FullImage = false; Width = 100; Height = 32; }
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
    public override EWorkflowVisionRange RangeCapability => EWorkflowVisionRange.Rectangle;
    /// <inheritdoc/>
    public IReadOnlyList<WorkflowVisionAlgorithmSlot> GetAlgorithmSlots() => new[] { new WorkflowVisionAlgorithmSlot("recognizer", typeof(ITextLineRecognizer), Algorithm) };
}

/// <summary>同帧单行 OCR 证据；字符识别不能作为外观合格依据。</summary>
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
        var bounds = node.FullImage ? new PixelBounds(0, 0, frame.Image.Info.Width, frame.Image.Info.Height) : new PixelBounds(node.X, node.Y, node.Width, node.Height);
        var bindings = context.Services.GetService(typeof(IWorkflowVisionAlgorithmBindings)) as IWorkflowVisionAlgorithmBindings
            ?? throw new InvalidOperationException("宿主没有提供节点算法绑定入口。");
        var recognition = bindings.Invoke<ITextLineRecognizer, TextLineRecognition>(context, "recognizer",
            recognizer => recognizer.Recognize(frame.Image, bounds, cancellationToken), cancellationToken);
        var fact = new WorkflowVisionTextLineFact(frame.FrameId, recognition);
        return ValueTask.FromResult(NodeExecutionResult.Continue(output: fact, projection: WorkflowVisionFrameScope.Stage(context, frame, fact)));
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
