using DP.Vision;
using DP.Vision.Acquisition;

namespace DP.WorkFlow;

/// <summary>
/// 按逻辑视觉源采集一张面阵整图。节点只保存SourceId，不解释Provider、设备地址或打开方式；
/// 同一流程文档在不同机器的Source映射下会由不同Provider完成采集。
/// <para>
/// 与线扫节点分开建模，是为了让候选过滤、参数绑定与运行前校验各自说出自己要求什么；
/// 两者的执行主干与输出都走 <see cref="VisionCaptureNodeExecution"/>，不产生第二套采集语义。
/// </para>
/// </summary>
[WorkflowNode("Vision.CaptureAreaFrame", DisplayName = "采集面阵帧", Category = "5.Vision/ImageBuffer")]
public sealed class CaptureAreaFrameNodeModel : WorkflowNodeModel, IWorkflowNodeConfigurationValidator
{
    /// <inheritdoc/>
    public override string NodeType => "Vision.CaptureAreaFrame";

    /// <summary>机器配置中发布的逻辑视觉源；采集节点唯一的来源身份。</summary>
    [WorkflowProperty("逻辑图像源", "机器配置中已发布的面阵逻辑源标识。同一流程文档在不同机器上可映射到不同Provider；源不存在时在首节点执行前失败。", Category = "图像来源")]
    [WorkflowPropertyEditor(WorkflowPropertyEditorKeys.VisionAreaSource)]
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
/// 执行面阵采集：与线扫节点共用同一个无状态执行主干，本类只负责把模型翻译成中立请求。
/// 预览不在这里发布——它只能由"节点输出成功提交"事件驱动。
/// </summary>
public sealed class CaptureAreaFrameNodeHandler : WorkflowNodeHandler<CaptureAreaFrameNodeModel>
{
    /// <inheritdoc/>
    protected override ValueTask<NodeExecutionResult> ExecuteAsync(
        CaptureAreaFrameNodeModel node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken) =>
        VisionCaptureNodeExecution.ExecuteAsync(context, node.Id, node.Source, node.CreateRequest(), cancellationToken);
}