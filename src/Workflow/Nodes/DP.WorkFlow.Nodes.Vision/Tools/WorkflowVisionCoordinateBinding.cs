using DP.Vision;
using DP.Vision.Algorithms;

namespace DP.WorkFlow;

/// <summary>持久化的显式定位绑定；只保存定义身份与绑定声明，不保存本帧矩阵。</summary>
public sealed class WorkflowVisionCoordinateBinding
{
    /// <summary>绑定本帧构建或定位输出的CoordinateSystem成员。</summary>
    public WorkflowInput<VisionCoordinateSystem> System { get; set; } = WorkflowInput<VisionCoordinateSystem>.FromLiteral(null);
    /// <summary>制作ROI时确认的业务坐标定义ID。</summary>
    public string CoordinateSystemId { get; set; } = "";
    /// <summary>制作时业务坐标定义版本。</summary>
    public int DefinitionVersion { get; set; } = 1;
    /// <summary>原点、轴和单位语义签名；由模板构建的坐标系包含模板参考签名，换模板必须重新确认。</summary>
    public string DefinitionSignature { get; set; } = "";

    internal bool IsValid => System is { Source: WorkflowValueSource.Binding, Binding: not null, LiteralValue: null }
        && !string.IsNullOrWhiteSpace(CoordinateSystemId) && DefinitionVersion > 0 && !string.IsNullOrWhiteSpace(DefinitionSignature);
    internal VisionCoordinateSystem Resolve(IWorkflowNodeExecutionContext context, ImageFrame frame)
    {
        if (!IsValid) throw new InvalidOperationException("定位坐标系绑定或制作身份缺失。");
        // System 是嵌套对象属性（Node.Coordinates.System），不是节点模型的顶层输入槽，
        // 自动槽发现匹配不到它，因此必须用显式动态键，否则正常节点会被误判为记录降级。
        var system = context.ResolveDynamicInput(DynamicInputKey, System) ?? throw new InvalidOperationException("本帧未定位，不能沿用旧坐标系。");
        Validate(system, frame);
        return system;
    }
    /// <summary>验证本帧坐标与制作时确认的定义一致。</summary>
    /// <param name="system">本帧坐标。</param><param name="frame">当前图。</param>
    public void Validate(VisionCoordinateSystem system, ImageFrame frame)
    {
        if (!IsValid) throw new InvalidOperationException("坐标绑定与制作身份无效。");
        system.ValidateDefinition(frame, CoordinateSystemId, DefinitionVersion, DefinitionSignature);
    }
    /// <summary>制作时捕获稳定定义，记录数据来源；不保存本帧矩阵。</summary>
    /// <param name="sourceNodeId">坐标来源节点。</param><param name="system">本帧坐标。</param><returns>配方绑定。</returns>
    public static WorkflowVisionCoordinateBinding Capture(string sourceNodeId, VisionCoordinateSystem system) => new()
    {
        System = WorkflowInput<VisionCoordinateSystem>.FromBinding(new WorkflowBindingKey(sourceNodeId, "CoordinateSystem")),
        CoordinateSystemId = system.Definition.Id, DefinitionVersion = system.Definition.Version, DefinitionSignature = system.Definition.Signature
    };

    /// <summary>嵌套定位坐标系绑定的稳定动态输入键；与节点模型的嵌套路径一致。</summary>
    internal const string DynamicInputKey = "Coordinates.System";
}
