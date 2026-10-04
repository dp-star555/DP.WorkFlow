using DP.Vision.Algorithms;

namespace DP.WorkFlow;

/// <summary>生成直线而非拟合直线；两点必须具有可验证来源。</summary>
[WorkflowNode("Vision.GenerateLine", DisplayName = "两点生成直线", Category = "5.Vision/Measurement")]
public sealed class GenerateVisionLineNodeModel : WorkflowVisionGeometryAlgorithmNodeModel
{
    /// <inheritdoc/>
    public override string NodeType => "Vision.GenerateLine";
    /// <summary>第一视觉点。</summary>
    public WorkflowInput<VisionPoint> A { get; set; } = WorkflowInput<VisionPoint>.FromLiteral(null);
    /// <summary>第二视觉点。</summary>
    public WorkflowInput<VisionPoint> B { get; set; } = WorkflowInput<VisionPoint>.FromLiteral(null);
    /// <inheritdoc/>
    public override IReadOnlyList<string> ValidateConfiguration()
    {
        var errors = base.ValidateConfiguration().ToList(); if (!IsBound(A) || !IsBound(B)) errors.Add("生成直线必须绑定两个带来源的视觉点。"); return errors;
    }
}

/// <summary>从本轮选定算法生成非退化直线。</summary>
public sealed class GenerateVisionLineNodeHandler : WorkflowNodeHandler<GenerateVisionLineNodeModel>
{
    /// <inheritdoc/>
    protected override ValueTask<NodeExecutionResult> ExecuteAsync(GenerateVisionLineNodeModel node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
    {
        var (frame, coordinates) = WorkflowVisionGeometryExecution.Resolve(node, context);
        var a = WorkflowVisionGeometryExecution.Align(context.ResolveInput(node.A), frame, coordinates);
        var b = WorkflowVisionGeometryExecution.Align(context.ResolveInput(node.B), frame, coordinates);
        var line = WorkflowVisionGeometryExecution.Invoke(context, algorithm => algorithm.GenerateLine(a, b, cancellationToken), cancellationToken);
        return WorkflowVisionGeometryExecution.Output(context, frame, line);
    }
}
