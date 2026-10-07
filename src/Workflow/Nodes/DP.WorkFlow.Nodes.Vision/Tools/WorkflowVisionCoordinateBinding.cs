using DP.Vision;
using DP.Vision.Algorithms;

namespace DP.WorkFlow;

/// <summary>持久化的坐标来源绑定；使用来源的本帧输出，不保存矩阵或锁定来源内部定义。</summary>
public sealed class WorkflowVisionCoordinateBinding
{
    /// <summary>绑定本帧构建或定位输出的CoordinateSystem成员。</summary>
    public WorkflowInput<VisionCoordinateSystem> System { get; set; } = WorkflowInput<VisionCoordinateSystem>.FromLiteral(null);
    /// <summary>制作时的坐标ID记录，仅用于追踪，不限制来源后续输出。</summary>
    public string CoordinateSystemId { get; set; } = "";
    /// <summary>制作时的定义版本记录，不参与绑定有效性检查。</summary>
    public int DefinitionVersion { get; set; } = 1;
    /// <summary>制作时的定义签名记录，不参与编译、执行或编辑有效性检查。</summary>
    public string DefinitionSignature { get; set; } = "";

    internal bool IsValid => System is { Source: WorkflowValueSource.Binding, Binding: not null, LiteralValue: null };
    internal VisionCoordinateSystem Resolve(IWorkflowNodeExecutionContext context, ImageFrame frame)
    {
        if (!IsValid) throw new InvalidOperationException("定位坐标系来源绑定缺失或无效。");
        // System 是嵌套对象属性（Node.Coordinates.System），不是节点模型的顶层输入槽，
        // 自动槽发现匹配不到它，因此必须用显式动态键，否则正常节点会被误判为记录降级。
        var system = context.ResolveDynamicInput(DynamicInputKey, System) ?? throw new InvalidOperationException("本帧未定位，不能沿用旧坐标系。");
        Validate(system, frame);
        return system;
    }
    /// <summary>验证来源输出适用于当前图像；制作记录不是执行契约。</summary>
    /// <param name="system">本帧坐标。</param><param name="frame">当前图。</param>
    public void Validate(VisionCoordinateSystem system, ImageFrame frame)
    {
        ArgumentNullException.ThrowIfNull(system);
        if (!IsValid) throw new InvalidOperationException("坐标来源绑定无效。");
        system.ValidateFrame(frame);
    }
    /// <summary>记录数据来源及可选制作信息；不保存本帧矩阵，不冻结后续输出定义。</summary>
    /// <param name="sourceNodeId">坐标来源节点。</param><param name="system">本帧坐标。</param><returns>配方绑定。</returns>
    public static WorkflowVisionCoordinateBinding Capture(string sourceNodeId, VisionCoordinateSystem system) => new()
    {
        System = WorkflowInput<VisionCoordinateSystem>.FromBinding(new WorkflowBindingKey(sourceNodeId, "CoordinateSystem")),
        CoordinateSystemId = system.Definition.Id, DefinitionVersion = system.Definition.Version, DefinitionSignature = system.Definition.Signature
    };

    /// <summary>嵌套定位坐标系绑定的稳定动态输入键；与节点模型的嵌套路径一致。</summary>
    internal const string DynamicInputKey = "Coordinates.System";
}
