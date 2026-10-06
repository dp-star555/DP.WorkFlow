using DP.Vision;
using DP.Vision.Algorithms;

namespace DP.WorkFlow;

/// <summary>新版单文件图像输入，标准输出ImageFrame，不写隐式变量或旧显示通道。</summary>
[WorkflowNode("Vision.LoadFile", DisplayName = "读取图像文件", Category = "5.Vision/Acquisition")]
[System.ComponentModel.Browsable(false)]
public sealed class LoadVisionFileNodeModel : WorkflowNodeModel, IWorkflowNodeConfigurationValidator, IWorkflowVisionAlgorithmNode
{
    /// <summary>节点专属实现选择；旧配方缺字段时保持原实现。</summary>
    [System.ComponentModel.Browsable(false)]
    public VisionAlgorithmSelection Algorithm { get; set; } = new() { ImplementationId = "opencv.image-read" };

    /// <inheritdoc/>
    public IReadOnlyList<WorkflowVisionAlgorithmSlot> GetAlgorithmSlots() => new[] { new WorkflowVisionAlgorithmSlot("algorithm", typeof(IImageFileReader), Algorithm) };

    /// <inheritdoc/>
    public override string NodeType => "Vision.LoadFile";
    /// <summary>静态文件路径，编译前必须存在。</summary>
    [WorkflowProperty("图像文件", "单图文件，格式由节点所选读取器决定。", Category = "图像来源")]
    [WorkflowPropertyEditor(WorkflowPropertyEditorKeys.FilePath, CheckExists = true)]
    public string FilePath { get; set; } = string.Empty;
    /// <inheritdoc/>
    public IReadOnlyList<string> ValidateConfiguration() =>
        string.IsNullOrWhiteSpace(FilePath) || !File.Exists(FilePath)
            ? new[] { "图像文件路径为空或文件不存在。" } : Array.Empty<string>();
}

/// <summary>读取文件并将输出租约交给宿主的运行帧作用域。</summary>
public sealed class LoadVisionFileNodeHandler : WorkflowNodeHandler<LoadVisionFileNodeModel>
{
    /// <inheritdoc/>
    protected override ValueTask<NodeExecutionResult> ExecuteAsync(LoadVisionFileNodeModel node,
        IWorkflowNodeExecutionContext context, CancellationToken cancellationToken) => ReadAsync(node.FilePath, node.Algorithm, context, cancellationToken);

    internal static async ValueTask<NodeExecutionResult> ReadAsync(string path, VisionAlgorithmSelection algorithm,
        IWorkflowNodeExecutionContext context, CancellationToken cancellationToken, EWorkflowVisionPixelFormat format = EWorkflowVisionPixelFormat.Original)
    {
        using var image = await WorkflowVisionAlgorithmInvocation.InvokeAsync(context, algorithm, "opencv.image-read",
            (IImageFileReader reader, CancellationToken token) => reader.ReadAsync(path, token), cancellationToken).ConfigureAwait(false);
        return Output(image, context, cancellationToken, format);
    }

    internal static NodeExecutionResult Output(IImageSource image, IWorkflowNodeExecutionContext context, CancellationToken token,
        EWorkflowVisionPixelFormat format = EWorkflowVisionPixelFormat.Original)
    {
        using var frame = new ImageFrame(Guid.NewGuid().ToString("N"), image);
        return Output(frame, context, token, format);
    }

    /// <summary>把已有帧身份交给运行帧作用域；采集帧必须保留CaptureId作为FrameId，不能重新编号。</summary>
    internal static NodeExecutionResult Output(ImageFrame frame, IWorkflowNodeExecutionContext context, CancellationToken token,
        EWorkflowVisionPixelFormat format = EWorkflowVisionPixelFormat.Original)
    {
        token.ThrowIfCancellationRequested();
        if (format == EWorkflowVisionPixelFormat.Gray8 && frame.Image.Info.Layout != EPixelLayout.Gray8)
        {
            // 同一次读取或采集，转换后沿用原帧身份。
            using var gray = VisionImage.ToGray8(frame.Image, token);
            using var converted = new ImageFrame(frame.FrameId, gray);
            return Output(converted, context, token);
        }
        var retained = context.GetRequiredCapability<IWorkflowVisionFrameScope>().Retain(frame);
        var projection = WorkflowVisionFrameScope.Stage(context, retained);
        return NodeExecutionResult.Continue(output: retained, projection: projection);
    }
}
