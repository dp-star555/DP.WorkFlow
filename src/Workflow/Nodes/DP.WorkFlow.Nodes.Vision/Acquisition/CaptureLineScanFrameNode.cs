using DP.Vision;
using DP.Vision.Acquisition;

namespace DP.WorkFlow;

/// <summary>
/// 按逻辑视觉源采集一张线扫整图。线扫的拼接、行数与触发时序全部由Provider/Adapter完成，
/// 节点只保存SourceId与通用整图参数；公共模型里不出现Line/Chunk等设备接口概念。
/// <para>
/// V2 刻意不预设线扫工艺参数（行频、扫描长度、编码器分频等）：它们随真实设备差异极大，
/// 在没有确定设备需求之前先加字段，只会把厂商私有刻度写进流程文档，让文档无法跨机器使用。
/// </para>
/// </summary>
[WorkflowNode("Vision.CaptureLineScanFrame", DisplayName = "采集线扫帧", Category = "5.Vision/Acquisition")]
[System.ComponentModel.Browsable(false)]
public sealed class CaptureLineScanFrameNodeModel : WorkflowNodeModel, IWorkflowNodeConfigurationValidator
{
    /// <inheritdoc/>
    public override string NodeType => "Vision.CaptureLineScanFrame";

    /// <summary>机器配置中发布的逻辑视觉源；采集节点唯一的来源身份。</summary>
    [WorkflowProperty("逻辑图像源", "机器配置中已发布的线扫逻辑源标识。同一流程文档在不同机器上可映射到不同Provider；源不存在时在首节点执行前失败。", Category = "图像来源")]
    [WorkflowPropertyEditor(WorkflowPropertyEditorKeys.VisionLineScanSource)]
    public VisionSourceReference? Source { get; set; }

    /// <summary>等待整图采集完成的最长时间。</summary>
    [WorkflowProperty("采集超时", "等待整张线扫图完成的最长时间；扫描未结束即按此超时失败。", Category = "采集参数", Unit = "ms")]
    public int TimeoutMilliseconds { get; set; } = 5000;

    /// <summary>曝光；空表示保持设备当前设置。</summary>
    [WorkflowProperty("曝光", "留空表示保持设备当前设置，不会被当成0。线扫描曝光单位与行周期相关，最终由机器Source/Profile解释。", Category = "采集参数", Unit = "µs")]
    public double? ExposureMicroseconds { get; set; }

    /// <summary>增益；空表示保持设备当前设置。</summary>
    [WorkflowProperty("增益", "留空表示保持设备当前设置。", Category = "采集参数", Unit = "dB")]
    public double? GainDecibels { get; set; }

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
        // 线扫描的触发时序属于设备与Adapter：整图由收线或外部编码器驱动，节点级"触发模式"
        // 只会与扫描过程冲突。需要改触发时改机器Source/Profile；因此这里明确保持设备当前设置。
        new(TimeSpan.FromMilliseconds(TimeoutMilliseconds), ExposureMicroseconds, GainDecibels, EVisionTriggerMode.KeepCurrent);
}

/// <summary>
/// 执行线扫采集：与面阵节点共用同一个无状态执行主干，本类只负责把模型翻译成中立请求。
/// </summary>
public sealed class CaptureLineScanFrameNodeHandler : WorkflowNodeHandler<CaptureLineScanFrameNodeModel>
{
    /// <inheritdoc/>
    protected override ValueTask<NodeExecutionResult> ExecuteAsync(
        CaptureLineScanFrameNodeModel node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken) =>
        VisionCaptureNodeExecution.ExecuteAsync(context, node.Id, node.Source, node.CreateRequest(), cancellationToken);
}
