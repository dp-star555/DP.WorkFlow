using DP.Vision;
using DP.Vision.Acquisition;

namespace DP.WorkFlow;

/// <summary>
/// 面阵与线扫采集节点的共享无状态执行主干。
/// <para>
/// 两个节点模型只在参数绑定、候选过滤与运行前校验上分开；一旦进入执行，采集调用、发起方身份翻译、
/// 来源事实Trace与输出提交必须完全一致，否则同一条流程会因选了面阵还是线扫节点而产生不同的运行语义。
/// 这里刻意用静态辅助方法而不是公共基类：节点模型之间没有需要复用的状态，
/// 引入"超级采集节点"继承层次只会让参数绑定与执行主干重新耦合。
/// </para>
/// </summary>
internal static class VisionCaptureNodeExecution
{
    /// <summary>执行一次整图采集并提交标准图像输出。</summary>
    /// <param name="context">节点执行上下文。</param>
    /// <param name="nodeId">节点身份；失败诊断与发起方名称需要能指出是哪个节点。</param>
    /// <param name="source">逻辑视觉源；运行前已校验，这里做最后一道兜底。</param>
    /// <param name="request">节点配置翻译出的中立采集请求。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <param name="format">输出像素格式。</param>
    /// <returns>节点执行结果。</returns>
    /// <exception cref="InvalidOperationException">节点没有绑定逻辑图像源。</exception>
    public static async ValueTask<NodeExecutionResult> ExecuteAsync(
        IWorkflowNodeExecutionContext context,
        string nodeId,
        VisionSourceReference? source,
        VisionCaptureRequest request,
        CancellationToken cancellationToken,
        EWorkflowVisionPixelFormat format = EWorkflowVisionPixelFormat.Original)
    {
        if (source is null)
            throw new InvalidOperationException($"节点 {nodeId} 未配置逻辑图像源；采集节点必须在运行前绑定到已发布的源。");
        var acquisition = context.GetRequiredCapability<IVisionAcquisition>();
        using var captured = await acquisition.CaptureAsync(
            source, request, CreateOwner(nodeId, context), cancellationToken).ConfigureAwait(false);
        ReportSourceFacts(captured.Metadata, context);
        return LoadVisionFileNodeHandler.Output(captured.Frame, context, cancellationToken, format);
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