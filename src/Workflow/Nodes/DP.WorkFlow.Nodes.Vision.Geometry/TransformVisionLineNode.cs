using DP.Vision.Algorithms;

namespace DP.WorkFlow;

/// <summary>显式转换直线的局部表达，原图端点不变。</summary>
[WorkflowNode("Vision.TransformLine", DisplayName = "直线坐标系转换", Category = "5.Vision/Location")]
public sealed class TransformVisionLineNodeModel : WorkflowVisionGeometryNodeModel
{
    /// <inheritdoc/>
    public override string NodeType => "Vision.TransformLine";
    /// <summary>上游生成或拟合的直线。</summary>
    [WorkflowProperty("输入直线", "绑定目标定位后转换；不绑定时显式转为原图表达。", Category = "输入")]
    public WorkflowInput<VisionLine> Line { get; set; } = WorkflowInput<VisionLine>.FromLiteral(null);
    /// <inheritdoc/>
    public override IReadOnlyList<string> ValidateConfiguration()
    {
        var errors = base.ValidateConfiguration().ToList(); if (!IsBound(Line)) errors.Add("必须绑定视觉直线。"); return errors;
    }
}

/// <summary>拒绝不同帧，经过共同原图变更表达。</summary>
public sealed class TransformVisionLineNodeHandler : WorkflowNodeHandler<TransformVisionLineNodeModel>
{
    /// <inheritdoc/>
    protected override ValueTask<NodeExecutionResult> ExecuteAsync(TransformVisionLineNodeModel node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (frame, coordinates) = WorkflowVisionGeometryExecution.Resolve(node, context);
        var line = WorkflowVisionGeometryExecution.Align(context.ResolveInput(node.Line), frame, null).InCoordinates(coordinates);
        return WorkflowVisionGeometryExecution.Output(context, frame, line);
    }
}
