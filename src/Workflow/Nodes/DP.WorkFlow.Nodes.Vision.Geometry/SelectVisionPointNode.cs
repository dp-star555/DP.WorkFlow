using DP.Vision.Algorithms;

namespace DP.WorkFlow;

/// <summary>从卡尺边缘、Blob质心等强类型点列表中明确选点。</summary>
[WorkflowNode("Vision.SelectPoint", DisplayName = "选择视觉点", Category = "5.Vision/Geometry")]
public sealed class SelectVisionPointNodeModel : WorkflowVisionGeometryNodeModel
{
    /// <inheritdoc/>
    public override string NodeType => "Vision.SelectPoint";
    /// <summary>上游 MeasuredEdges 或 MeasuredCentroids。</summary>
    [WorkflowProperty("点集合", "绑定带帧和坐标来源的点集合，不能使用裸PointD列表。", Category = "输入")]
    public WorkflowInput<IReadOnlyList<VisionPoint>> Points { get; set; } = WorkflowInput<IReadOnlyList<VisionPoint>>.FromLiteral(null);
    /// <summary>从零开始的索引。</summary>
    [WorkflowProperty("点索引", "按上游顺序选点；空集合或越界明确失败。", Category = "选择")]
    public int Index { get; set; }
    /// <inheritdoc/>
    public override IReadOnlyList<string> ValidateConfiguration()
    {
        var errors = base.ValidateConfiguration().ToList();
        if (!IsBound(Points) || Index < 0 || Index > 8191) errors.Add("必须绑定点集合并选择0..8191索引。");
        return errors;
    }
}

/// <summary>越界及混帧输入不产生伪造点。</summary>
public sealed class SelectVisionPointNodeHandler : WorkflowNodeHandler<SelectVisionPointNodeModel>
{
    /// <inheritdoc/>
    protected override ValueTask<NodeExecutionResult> ExecuteAsync(SelectVisionPointNodeModel node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (frame, coordinates) = WorkflowVisionGeometryExecution.Resolve(node, context);
        var points = context.ResolveInput(node.Points) ?? throw new InvalidOperationException("点集合为空。");
        if (node.Index < 0 || node.Index >= points.Count) throw new InvalidOperationException("点索引超出实际集合。");
        var point = WorkflowVisionGeometryExecution.Align(points[node.Index], frame, coordinates);
        return WorkflowVisionGeometryExecution.Output(context, frame, point);
    }
}
