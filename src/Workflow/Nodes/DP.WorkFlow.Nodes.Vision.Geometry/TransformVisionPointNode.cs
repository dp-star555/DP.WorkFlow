using DP.Vision.Algorithms;

namespace DP.WorkFlow;

/// <summary>显式变更点的局部表达；没有目标绑定时转为原图表达。</summary>
[WorkflowNode("Vision.TransformPoint", DisplayName = "点坐标系转换", Category = WorkflowVisionCategories.Geometry)]
public sealed class TransformVisionPointNodeModel : WorkflowVisionGeometryNodeModel
{
    /// <inheritdoc/>
    public override string NodeType => "Vision.TransformPoint";
    /// <summary>带来源的视觉点。</summary>
    [WorkflowProperty("输入点", "绑定目标定位后转换局部表达；不绑定则显式去除局部表达，原图位置始终保留。", Category = "输入")]
    public WorkflowInput<VisionPoint> Point { get; set; } = WorkflowInput<VisionPoint>.FromLiteral(null);
    /// <inheritdoc/>
    public override IReadOnlyList<string> ValidateConfiguration()
    {
        var errors = base.ValidateConfiguration().ToList(); if (!IsBound(Point)) errors.Add("必须绑定视觉点。"); return errors;
    }
}

/// <summary>经过原图进行同帧转换，不重复补偿定位矩阵。</summary>
public sealed class TransformVisionPointNodeHandler : WorkflowNodeHandler<TransformVisionPointNodeModel>
{
    /// <inheritdoc/>
    protected override ValueTask<NodeExecutionResult> ExecuteAsync(TransformVisionPointNodeModel node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (frame, coordinates) = WorkflowVisionGeometryExecution.Resolve(node, context);
        var point = WorkflowVisionGeometryExecution.Align(context.ResolveInput(node.Point), frame, null).InCoordinates(coordinates);
        return WorkflowVisionGeometryExecution.Output(context, frame, point);
    }
}
