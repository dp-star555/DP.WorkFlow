using DP.Vision;
using DP.Vision.Algorithms;

namespace DP.WorkFlow;

/// <summary>持久化的显式定位绑定；只保存定义身份与绑定声明，不保存本帧矩阵。</summary>
public sealed class WorkflowVisionCoordinateBinding
{
    /// <summary>绑定成功定位输出的CoordinateSystem成员。</summary>
    public WorkflowInput<LocatedCoordinateSystem> System { get; set; } = WorkflowInput<LocatedCoordinateSystem>.FromLiteral(null);
    /// <summary>制作ROI时确认的模板坐标系定义ID。</summary>
    public string CoordinateSystemId { get; set; } = "";
    /// <summary>制作ROI时确认的模板像素签名；换模板必须显式重新制作/确认。</summary>
    public string TemplateSignature { get; set; } = "";

    internal bool IsValid => System is { Source: WorkflowValueSource.Binding, Binding: not null, LiteralValue: null }
        && !string.IsNullOrWhiteSpace(CoordinateSystemId) && !string.IsNullOrWhiteSpace(TemplateSignature);
    internal LocatedCoordinateSystem Resolve(IWorkflowNodeExecutionContext context, ImageFrame frame)
    {
        if (!IsValid) throw new InvalidOperationException("定位坐标系绑定或制作身份缺失。");
        var system = context.ResolveInput(System) ?? throw new InvalidOperationException("本帧未定位，不能沿用旧坐标系。");
        system.Validate(frame, CoordinateSystemId, TemplateSignature);
        return system;
    }
}
