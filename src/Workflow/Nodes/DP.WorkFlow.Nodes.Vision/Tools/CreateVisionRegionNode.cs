using DP.Vision.Algorithms;

namespace DP.WorkFlow;

/// <summary>将绘制的包含/排除ROI构建为同帧区域，供下游作为掩膜复用。</summary>
[WorkflowNode("Vision.CreateRegion", DisplayName = "创建区域／掩膜", Category = "5.Vision/Region")]
public sealed class CreateVisionRegionNodeModel : AnalyzeVisionFrameNodeModel
{
    /// <inheritdoc/>
    public override string NodeType => "Vision.CreateRegion";
    /// <inheritdoc/>
    public override EWorkflowVisionRange RangeCapability => EWorkflowVisionRange.Region;
}

/// <summary>使用共同范围与坐标转换，不需要厂商引擎或持久化运行图像。</summary>
public sealed class CreateVisionRegionNodeHandler : WorkflowNodeHandler<CreateVisionRegionNodeModel>
{
    /// <inheritdoc/>
    protected override ValueTask<NodeExecutionResult> ExecuteAsync(CreateVisionRegionNodeModel node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
    {
        var frame = context.ResolveInput(node.Frame) ?? throw new InvalidOperationException("输入帧为空。");
        var range = node.ResolveRange(frame, context, cancellationToken);
        var result = new RegionAnalysisResult(frame.FrameId, frame.Image.Info.Width, frame.Image.Info.Height, range.ToRegion(cancellationToken));
        if (range.Coordinates is not null) result = result.InCoordinates(range.Coordinates);
        return ValueTask.FromResult(NodeExecutionResult.Continue(output: result, projection: WorkflowVisionFrameScope.Stage(context, frame, result)));
    }
}
