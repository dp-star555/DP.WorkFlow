using DP.Vision.Algorithms;

namespace DP.WorkFlow;

/// <summary>两直线或两有限线段的最短距离。</summary>
[WorkflowNode("Vision.MeasureLineDistance", DisplayName = "线到线距离", Category = "5.Vision/Measurement")]
public sealed class MeasureVisionLineDistanceNodeModel : WorkflowVisionGeometryAlgorithmNodeModel
{
    /// <inheritdoc/>
    public override string NodeType => "Vision.MeasureLineDistance";
    /// <summary>第一直线。</summary>
    public WorkflowInput<VisionLine> A { get; set; } = WorkflowInput<VisionLine>.FromLiteral(null);
    /// <summary>第二直线。</summary>
    public WorkflowInput<VisionLine> B { get; set; } = WorkflowInput<VisionLine>.FromLiteral(null);
    /// <summary>测量空间。</summary>
    [WorkflowProperty("测量空间", "距离始终带image-px或template-px单位。", Category = "测量")]
    public EVisionCoordinateSpace Space { get; set; }
    /// <summary>无限直线非平行时距离为零；有限线段可以不相交。</summary>
    [WorkflowProperty("距离模式", "相交的无限直线最短距离为零；工件端点间隙应显式使用有限线段。", Category = "测量")]
    public EVisionLineDistanceMode Mode { get; set; }
    /// <inheritdoc/>
    public override IReadOnlyList<string> ValidateConfiguration()
    {
        var errors = base.ValidateConfiguration().ToList();
        if (!IsBound(A) || !IsBound(B) || !Enum.IsDefined(Space) || !Enum.IsDefined(Mode)) errors.Add("线到线必须配置两条视觉直线和有效模式。");
        return errors;
    }
}

/// <summary>返回最近点而非仅返回无来源的double。</summary>
public sealed class MeasureVisionLineDistanceNodeHandler : WorkflowNodeHandler<MeasureVisionLineDistanceNodeModel>
{
    /// <inheritdoc/>
    protected override ValueTask<NodeExecutionResult> ExecuteAsync(MeasureVisionLineDistanceNodeModel node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
    {
        var (frame, coordinates) = WorkflowVisionGeometryExecution.Resolve(node, context);
        var a = WorkflowVisionGeometryExecution.Align(context.ResolveInput(node.A), frame, coordinates);
        var b = WorkflowVisionGeometryExecution.Align(context.ResolveInput(node.B), frame, coordinates);
        var result = WorkflowVisionGeometryExecution.Invoke(context, algorithm => algorithm.LineToLine(a, b, node.Space, node.Mode, cancellationToken), cancellationToken);
        return WorkflowVisionGeometryExecution.Output(context, frame, result);
    }
}
