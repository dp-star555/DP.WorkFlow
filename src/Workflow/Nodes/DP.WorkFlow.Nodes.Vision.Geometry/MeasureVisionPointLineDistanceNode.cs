using DP.Vision.Algorithms;

namespace DP.WorkFlow;

/// <summary>点到无限直线或有限线段的距离节点。</summary>
[WorkflowNode("Vision.MeasurePointLineDistance", DisplayName = "点到线距离", Category = WorkflowVisionCategories.Measurement)]
public sealed class MeasureVisionPointLineDistanceNodeModel : WorkflowVisionGeometryAlgorithmNodeModel
{
    /// <inheritdoc/>
    public override string NodeType => "Vision.MeasurePointLineDistance";
    /// <summary>视觉点。</summary>
    public WorkflowInput<VisionPoint> Point { get; set; } = WorkflowInput<VisionPoint>.FromLiteral(null);
    /// <summary>上游生成或拟合的直线。</summary>
    public WorkflowInput<VisionLine> Line { get; set; } = WorkflowInput<VisionLine>.FromLiteral(null);
    /// <summary>测量空间。</summary>
    [WorkflowProperty("测量空间", "共同原图像素或共同业务局部单位。", Category = "测量")]
    public EVisionCoordinateSpace Space { get; set; }
    /// <summary>投影是否限制在线段内部。</summary>
    [WorkflowProperty("距离模式", "无限直线垂足可超出端点；有限线段返回最近端点或内部投影。", Category = "测量")]
    public EVisionLineDistanceMode Mode { get; set; }
    /// <inheritdoc/>
    public override IReadOnlyList<string> ValidateConfiguration()
    {
        var errors = base.ValidateConfiguration().ToList();
        if (!IsBound(Point) || !IsBound(Line) || !Enum.IsDefined(Space) || !Enum.IsDefined(Mode)) errors.Add("点到线必须配置强类型输入和有效测量模式。");
        return errors;
    }
}

/// <summary>在共同空间测量，并在原图预览实际距离连线。</summary>
public sealed class MeasureVisionPointLineDistanceNodeHandler : WorkflowNodeHandler<MeasureVisionPointLineDistanceNodeModel>
{
    /// <inheritdoc/>
    protected override ValueTask<NodeExecutionResult> ExecuteAsync(MeasureVisionPointLineDistanceNodeModel node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
    {
        var (frame, coordinates) = WorkflowVisionGeometryExecution.Resolve(node, context);
        var point = WorkflowVisionGeometryExecution.Align(context.ResolveInput(node.Point), frame, coordinates);
        var line = WorkflowVisionGeometryExecution.Align(context.ResolveInput(node.Line), frame, coordinates);
        var result = WorkflowVisionGeometryExecution.Invoke(context, algorithm => algorithm.PointToLine(point, line, node.Space, node.Mode, cancellationToken), cancellationToken);
        return WorkflowVisionGeometryExecution.Output(context, frame, result);
    }
}
