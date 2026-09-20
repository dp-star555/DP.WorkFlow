using DP.Vision;
using DP.Vision.Algorithms;

namespace DP.WorkFlow;

/// <summary>新版单文件图像输入，标准输出ImageFrame，不写隐式变量或旧显示通道。</summary>
[WorkflowNode("Vision.LoadFile", DisplayName = "读取图像文件", Category = "5.Vision/ImageBuffer")]
public sealed class LoadVisionFileNodeModel : WorkflowNodeModel, IWorkflowNodeConfigurationValidator
{
    /// <inheritdoc/>
    public override string NodeType => "Vision.LoadFile";
    /// <summary>静态文件路径，编译前必须存在。</summary>
    [WorkflowProperty("图像文件", "单图文件，格式由宿主读取器决定。", Category = "图像来源")]
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
    protected override async ValueTask<NodeExecutionResult> ExecuteAsync(LoadVisionFileNodeModel node,
        IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
    {
        var reader = context.GetRequiredCapability<IImageFileReader>();
        using var image = await reader.ReadAsync(node.FilePath, cancellationToken).ConfigureAwait(false);
        return Output(image, context, cancellationToken);
    }

    internal static NodeExecutionResult Output(IImageSource image, IWorkflowNodeExecutionContext context, CancellationToken token)
    {
        using var frame = new ImageFrame(Guid.NewGuid().ToString("N"), image);
        return Output(frame, context, token);
    }

    /// <summary>把已有帧身份交给运行帧作用域；采集帧必须保留CaptureId作为FrameId，不能重新编号。</summary>
    internal static NodeExecutionResult Output(ImageFrame frame, IWorkflowNodeExecutionContext context, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var retained = context.GetRequiredCapability<IWorkflowVisionFrameScope>().Retain(frame);
        var projection = WorkflowVisionFrameScope.Stage(context, retained);
        return NodeExecutionResult.Continue(output: retained, projection: projection);
    }
}
