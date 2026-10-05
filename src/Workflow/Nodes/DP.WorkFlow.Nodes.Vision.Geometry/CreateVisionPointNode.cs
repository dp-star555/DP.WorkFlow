using DP.Vision;
using DP.Vision.Algorithms;

namespace DP.WorkFlow;

/// <summary>在明确空间生成带身份的视觉点。</summary>
[WorkflowNode("Vision.CreatePoint", DisplayName = "生成视觉点", Category = "5.Vision/Geometry")]
public sealed class CreateVisionPointNodeModel : WorkflowVisionGeometryNodeModel
{
    /// <inheritdoc/>
    public override string NodeType => "Vision.CreatePoint";
    /// <summary>坐标第一分量。</summary>
    [WorkflowProperty("点X", "数值在所选输入空间中解释。", Category = "点")]
    public WorkflowInput<double> PointX { get; set; } = WorkflowInput<double>.FromLiteral(0);
    /// <summary>坐标第二分量。</summary>
    [WorkflowProperty("点Y", "数值在所选输入空间中解释。", Category = "点")]
    public WorkflowInput<double> PointY { get; set; } = WorkflowInput<double>.FromLiteral(0);
    /// <summary>显式输入空间。</summary>
    [WorkflowProperty("输入空间", "局部模式必须绑定本帧坐标系；数值不会因切换空间而自动改写。", Category = "点")]
    public EVisionCoordinateSpace Space { get; set; }
    /// <inheritdoc/>
    public override IReadOnlyList<string> ValidateConfiguration()
    {
        var errors = base.ValidateConfiguration().ToList();
        if (!Enum.IsDefined(Space) || Space == EVisionCoordinateSpace.Local && Coordinates is null) errors.Add("局部点必须明确绑定本帧坐标系。");
        if (PointX is null || PointY is null || PointX.Source == WorkflowValueSource.Literal && (!double.IsFinite(PointX.LiteralValue) || Math.Abs(PointX.LiteralValue) > 1e9)
            || PointY.Source == WorkflowValueSource.Literal && (!double.IsFinite(PointY.LiteralValue) || Math.Abs(PointY.LiteralValue) > 1e9)) errors.Add("视觉点坐标必须有限且不超过正负十亿。");
        return errors;
    }
}

/// <summary>将显式输入空间转换为保持原图位置的事实。</summary>
public sealed class CreateVisionPointNodeHandler : WorkflowNodeHandler<CreateVisionPointNodeModel>
{
    /// <inheritdoc/>
    protected override ValueTask<NodeExecutionResult> ExecuteAsync(CreateVisionPointNodeModel node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (frame, coordinates) = WorkflowVisionGeometryExecution.Resolve(node, context);
        var point = VisionPoint.Create(frame.FrameId, new PointD(context.ResolveInput(node.PointX), context.ResolveInput(node.PointY)), node.Space, coordinates);
        return WorkflowVisionGeometryExecution.Output(context, frame, point);
    }
}
