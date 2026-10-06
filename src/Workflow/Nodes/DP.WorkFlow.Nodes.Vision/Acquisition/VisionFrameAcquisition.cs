using DP.Vision;
using DP.Vision.Algorithms;

namespace DP.WorkFlow;

/// <summary>“图像获取”的离线读图与输出提交：文件、文件夹读图，以及把帧交给运行帧作用域。</summary>
internal static class VisionFrameAcquisition
{
    /// <summary>读取单个文件并提交输出，每次产生新的帧身份。</summary>
    /// <param name="path">图像文件。</param><param name="algorithm">节点选择的解码实现。</param>
    /// <param name="context">节点执行上下文。</param><param name="format">输出像素格式。</param><param name="cancellationToken">取消。</param>
    /// <returns>节点执行结果。</returns>
    public static async ValueTask<NodeExecutionResult> ReadFileAsync(string path, VisionAlgorithmSelection algorithm,
        IWorkflowNodeExecutionContext context, EWorkflowVisionPixelFormat format, CancellationToken cancellationToken)
    {
        using var image = await WorkflowVisionAlgorithmInvocation.InvokeAsync(context, algorithm, "opencv.image-read",
            (IImageFileReader reader, CancellationToken token) => reader.ReadAsync(path, token), cancellationToken).ConfigureAwait(false);
        return Output(image, context, format, cancellationToken);
    }

    /// <summary>通过运行级清单读取文件夹的下一张并提交输出。</summary>
    /// <param name="nodeId">已准备的节点身份。</param><param name="algorithm">节点选择的解码实现。</param>
    /// <param name="context">节点执行上下文。</param><param name="format">输出像素格式。</param><param name="cancellationToken">取消。</param>
    /// <returns>节点执行结果。</returns>
    public static async ValueTask<NodeExecutionResult> ReadFolderAsync(string nodeId, VisionAlgorithmSelection algorithm,
        IWorkflowNodeExecutionContext context, EWorkflowVisionPixelFormat format, CancellationToken cancellationToken)
    {
        var source = context.GetRequiredCapability<IWorkflowVisionFolderSource>();
        Task<IImageSource> Read(string path, CancellationToken token) => WorkflowVisionAlgorithmInvocation.InvokeAsync(context, algorithm, "opencv.image-read",
            (IImageFileReader reader, CancellationToken cancellation) => reader.ReadAsync(path, cancellation), token);
        Task<IImageSource> next;
        if (context.Services.GetService(typeof(IWorkflowVisionAlgorithmBindings)) is IWorkflowVisionAlgorithmBindings)
        {
            if (source is not IWorkflowVisionAlgorithmFolderSource algorithmSource)
                throw new InvalidOperationException("文件夹来源未支持节点算法选择，请使用支持解码回调的文件夹来源。");
            next = algorithmSource.NextAsync(nodeId, Read, cancellationToken);
        }
        else
        {
            WorkflowVisionAlgorithmInvocation.RequireLegacySelection(algorithm, "opencv.image-read");
            next = source.NextAsync(nodeId, cancellationToken);
        }
        using var image = await next.ConfigureAwait(false);
        return Output(image, context, format, cancellationToken);
    }

    /// <summary>为离线读到的图像分配新帧身份并提交。</summary>
    public static NodeExecutionResult Output(IImageSource image, IWorkflowNodeExecutionContext context, EWorkflowVisionPixelFormat format, CancellationToken token)
    {
        using var frame = new ImageFrame(Guid.NewGuid().ToString("N"), image);
        return Output(frame, context, format, token);
    }

    /// <summary>把已有帧身份交给运行帧作用域；采集帧必须保留CaptureId作为FrameId，不能重新编号。</summary>
    public static NodeExecutionResult Output(ImageFrame frame, IWorkflowNodeExecutionContext context, EWorkflowVisionPixelFormat format, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (format == EWorkflowVisionPixelFormat.Gray8 && frame.Image.Info.Layout != EPixelLayout.Gray8)
        {
            // 同一次读取或采集，转换后沿用原帧身份。
            using var gray = VisionImage.ToGray8(frame.Image, token);
            using var converted = new ImageFrame(frame.FrameId, gray);
            return Output(converted, context, EWorkflowVisionPixelFormat.Original, token);
        }
        var retained = context.GetRequiredCapability<IWorkflowVisionFrameScope>().Retain(frame);
        var projection = WorkflowVisionFrameScope.Stage(context, retained);
        return NodeExecutionResult.Continue(output: retained, projection: projection);
    }
}
