using DP.Vision.Algorithms;

namespace DP.WorkFlow;

/// <summary>带身份的点间距；旧裸坐标距离节点保持兼容。</summary>
[WorkflowNode("Vision.MeasurePointDistance", DisplayName = "视觉点间距", Category = "5.Vision/Measurement")]
public sealed class MeasureVisionPointDistanceNodeModel : WorkflowVisionGeometryAlgorithmNodeModel
{
    /// <inheritdoc/>
    public override string NodeType => "Vision.MeasurePointDistance";
    /// <summary>第一点。</summary>
    public WorkflowInput<VisionPoint> A { get; set; } = WorkflowInput<VisionPoint>.FromLiteral(null);
    /// <summary>第二点。</summary>
    public WorkflowInput<VisionPoint> B { get; set; } = WorkflowInput<VisionPoint>.FromLiteral(null);
    /// <summary>距离单位空间。</summary>
    [WorkflowProperty("测量空间", "原图像素或共同业务局部单位，毫米须有明确标定。", Category = "测量")]
    public EVisionCoordinateSpace Space { get; set; }
    /// <inheritdoc/>
    public override IReadOnlyList<string> ValidateConfiguration()
    {
        var errors = base.ValidateConfiguration().ToList(); if (!IsBound(A) || !IsBound(B) || !Enum.IsDefined(Space)) errors.Add("点间距必须绑定两个视觉点并选择有效空间。"); return errors;
    }
}

/// <summary>保持帧及定位身份的点间距调用。</summary>
public sealed class MeasureVisionPointDistanceNodeHandler : WorkflowNodeHandler<MeasureVisionPointDistanceNodeModel>
{
    /// <inheritdoc/>
    protected override ValueTask<NodeExecutionResult> ExecuteAsync(MeasureVisionPointDistanceNodeModel node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
    {
        var (frame, coordinates) = WorkflowVisionGeometryExecution.Resolve(node, context);
        var a = WorkflowVisionGeometryExecution.Align(context.ResolveInput(node.A), frame, coordinates);
        var b = WorkflowVisionGeometryExecution.Align(context.ResolveInput(node.B), frame, coordinates);
        var result = WorkflowVisionGeometryExecution.Invoke(context, algorithm => algorithm.PointToPoint(a, b, node.Space, cancellationToken), cancellationToken);
        return WorkflowVisionGeometryExecution.Output(context, frame, result);
    }
}
