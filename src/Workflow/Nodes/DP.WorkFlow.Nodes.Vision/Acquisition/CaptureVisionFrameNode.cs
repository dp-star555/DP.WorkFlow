using DP.Vision;
using DP.Vision.Acquisition;

namespace DP.WorkFlow;

/// <summary>
/// 按逻辑视觉源采集新版ImageFrame。节点只保存SourceId，不解释Provider、设备地址或打开方式；
/// 同一流程文档在不同机器的Source映射下会由不同Provider完成采集。
/// </summary>
[WorkflowNode("Vision.CaptureFrame", DisplayName = "采集相机帧", Category = "5.Vision/ImageBuffer")]
public sealed class CaptureVisionFrameNodeModel : WorkflowNodeModel, IWorkflowNodeConfigurationValidator
{
    /// <inheritdoc/>
    public override string NodeType => "Vision.CaptureFrame";

    /// <summary>机器配置中发布的逻辑视觉源；采集节点唯一的来源身份。</summary>
    [WorkflowProperty("逻辑图像源", "机器配置中发布的逻辑源标识。同一流程文档在不同机器上可映射到不同Provider；源不存在时在首节点执行前失败。", Category = "图像来源")]
    [WorkflowPropertyEditor(WorkflowPropertyEditorKeys.VisionSource)]
    public VisionSourceReference? Source { get; set; }

    /// <summary>等待采集完成的最长时间。</summary>
    [WorkflowProperty("采集超时", "等待采集完成的最长时间；外部触发未到达即按此超时失败。", Category = "采集参数", Unit = "ms")]
    public int TimeoutMilliseconds { get; set; } = 5000;

    /// <summary>曝光；空表示保持设备当前设置。</summary>
    [WorkflowProperty("曝光", "留空表示保持设备当前设置，不会被当成0。", Category = "采集参数", Unit = "µs")]
    public double? ExposureMicroseconds { get; set; }

    /// <summary>增益；空表示保持设备当前设置。</summary>
    [WorkflowProperty("增益", "留空表示保持设备当前设置。", Category = "采集参数", Unit = "dB")]
    public double? GainDecibels { get; set; }

    /// <summary>触发模式；不支持的模式由Provider明确拒绝。</summary>
    [WorkflowProperty("触发模式", "保持当前/自由运行/软件触发/外部触发；Provider不支持时明确拒绝而不是静默忽略。", Category = "采集参数")]
    public EVisionTriggerMode TriggerMode { get; set; } = EVisionTriggerMode.KeepCurrent;

    /// <inheritdoc/>
    public IReadOnlyList<string> ValidateConfiguration()
    {
        var errors = new List<string>();
        if (Source is null)
            errors.Add("逻辑图像源不能为空；请在机器配置中发布源后选择。");
        try
        {
            _ = CreateRequest();
        }
        catch (ArgumentOutOfRangeException exception)
        {
            errors.Add(exception.Message);
        }

        return errors;
    }

    /// <summary>把节点配置翻译成中立采集请求；公共物理量必须带明确单位。</summary>
    /// <returns>可直接交给采集运行时的请求。</returns>
    /// <exception cref="ArgumentOutOfRangeException">超时不是正值，或曝光/增益为负数、NaN、无穷。</exception>
    public VisionCaptureRequest CreateRequest() =>
        new(TimeSpan.FromMilliseconds(TimeoutMilliseconds), ExposureMicroseconds, GainDecibels, TriggerMode);
}

/// <summary>
/// 执行采集：把Workflow执行身份转成中立发起方身份，调用采集运行时，并把来源事实写入本次运行的Trace。
/// 预览不在这里发布——它只能由"节点输出成功提交"事件驱动。
/// </summary>
public sealed class CaptureVisionFrameNodeHandler : WorkflowNodeHandler<CaptureVisionFrameNodeModel>
{
    /// <inheritdoc/>
    protected override async ValueTask<NodeExecutionResult> ExecuteAsync(
        CaptureVisionFrameNodeModel node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
    {
        var source = node.Source
            ?? throw new InvalidOperationException($"节点 {node.Id} 未配置逻辑图像源；采集节点必须在运行前绑定到已发布的源。");
        var acquisition = context.GetRequiredCapability<IVisionAcquisition>();
        using var captured = await acquisition.CaptureAsync(
            source, node.CreateRequest(), CreateOwner(node, context), cancellationToken).ConfigureAwait(false);
        ReportSourceFacts(captured.Metadata, context);
        return LoadVisionFileNodeHandler.Output(captured.Frame, context, cancellationToken);
    }

    /// <summary>把Workflow执行身份翻译成中立发起方身份；设备冲突诊断需要能指出是谁在请求。</summary>
    private static VisionAcquisitionOwner CreateOwner(CaptureVisionFrameNodeModel node, IWorkflowNodeExecutionContext context)
    {
        var identity = context.ExecutionIdentity;
        return new VisionAcquisitionOwner(
            identity.RunId.ToString("N"),
            $"{identity.TokenId}:{context.NodeExecutionCount}",
            $"{node.Id}@第{context.NodeExecutionCount}次");
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
