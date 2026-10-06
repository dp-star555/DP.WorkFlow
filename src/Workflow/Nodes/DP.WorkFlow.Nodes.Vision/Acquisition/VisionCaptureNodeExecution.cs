using DP.Vision;
using DP.Vision.Acquisition;

namespace DP.WorkFlow;

/// <summary>
/// “图像获取”相机来源的无状态执行主干：面阵与线扫共用同一采集调用、发起方身份翻译、来源事实Trace与输出提交。
/// </summary>
internal static class VisionCaptureNodeExecution
{
    /// <summary>执行一次整图采集并提交标准图像输出。</summary>
    /// <param name="context">节点执行上下文。</param>
    /// <param name="nodeId">节点身份；失败诊断与发起方名称需要能指出是哪个节点。</param>
    /// <param name="source">逻辑视觉源；运行前已校验，这里做最后一道兜底。</param>
    /// <param name="request">节点配置翻译出的中立采集请求。</param>
    /// <param name="format">输出像素格式。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>节点执行结果。</returns>
    /// <exception cref="InvalidOperationException">节点没有绑定逻辑图像源。</exception>
    public static async ValueTask<NodeExecutionResult> ExecuteAsync(
        IWorkflowNodeExecutionContext context,
        string nodeId,
        VisionSourceReference? source,
        VisionCaptureRequest request,
        EWorkflowVisionPixelFormat format,
        CancellationToken cancellationToken)
    {
        if (source is null)
            throw new InvalidOperationException($"节点 {nodeId} 未配置逻辑图像源；采集节点必须在运行前绑定到已发布的源。");
        var acquisition = context.GetRequiredCapability<IVisionAcquisition>();
        using var captured = await acquisition.CaptureAsync(
            source, request, CreateOwner(nodeId, context), cancellationToken).ConfigureAwait(false);
        ReportSourceFacts(captured.Metadata, context);
        return VisionFrameAcquisition.Output(captured.Frame, context, format, cancellationToken);
    }

    /// <summary>把Workflow执行身份翻译成中立发起方身份；设备冲突诊断需要能指出是谁在请求。</summary>
    private static VisionAcquisitionOwner CreateOwner(string nodeId, IWorkflowNodeExecutionContext context)
    {
        var identity = context.ExecutionIdentity;
        return new VisionAcquisitionOwner(
            identity.RunId.ToString("N"),
            $"{identity.TokenId}:{context.NodeExecutionCount}",
            $"{nodeId}@第{context.NodeExecutionCount}次");
    }

    /// <summary>来源事实进入本次运行的Trace，不进入普通公共数据。</summary>
    private static void ReportSourceFacts(VisionCaptureMetadata metadata, IWorkflowNodeExecutionContext context)
    {
        context.Trace(
            "Vision.Capture",
            $"源 {metadata.SourceId} 由 {metadata.ProviderId} 采集。",
            new Dictionary<string, object?>
            {
                ["CaptureId"] = metadata.CaptureId,
                ["SourceId"] = metadata.SourceId,
                ["ProviderId"] = metadata.ProviderId,
                ["ResourceKey"] = metadata.ResourceKey,
                ["CapturedAtUtc"] = metadata.CapturedAtUtc,
                ["DeviceSequence"] = metadata.DeviceSequence
            });
    }
}