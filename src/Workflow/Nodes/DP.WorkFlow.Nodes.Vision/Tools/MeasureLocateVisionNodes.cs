using DP.Vision;
using DP.Vision.Algorithms;

namespace DP.WorkFlow;

/// <summary>基于Canny边缘的新版线/圆拟合节点。</summary>
[WorkflowNode("Vision.MeasureEdges", DisplayName = "边缘线圆测量", Category = "5.Vision/ImageBuffer")]
public sealed class MeasureVisionEdgesNodeModel : AnalyzeVisionFrameNodeModel
{
    /// <inheritdoc/>
    public override string NodeType => "Vision.MeasureEdges";
    /// <inheritdoc/>
    public override EWorkflowVisionRange RangeCapability => EWorkflowVisionRange.Region;
    /// <summary>拟合模型。</summary>
    [WorkflowProperty("拟合模型", "线为正交最小二乘，圆为代数最小二乘；不是亚像素卡尺。", Category = "测量")]
    public EEdgeModel Model { get; set; }
    /// <summary>Canny低阈值。</summary>
    public double LowThreshold { get; set; } = 50;
    /// <summary>Canny高阈值。</summary>
    public double HighThreshold { get; set; } = 100;
    /// <summary>最少边缘点数量。</summary>
    public int MinimumPoints { get; set; } = 6;
    /// <inheritdoc/>
    public override IReadOnlyList<string> ValidateConfiguration()
    {
        var errors = base.ValidateConfiguration().ToList();
        try { _ = new EdgeMeasurementOptions(Model, LowThreshold, HighThreshold, MinimumPoints); }
        catch (ArgumentException ex) { errors.Add(ex.Message); }
        return errors;
    }
}

/// <summary>调用中立测量能力，输出测量事实。</summary>
public sealed class MeasureVisionEdgesNodeHandler : WorkflowNodeHandler<MeasureVisionEdgesNodeModel>
{
    /// <inheritdoc/>
    protected override ValueTask<NodeExecutionResult> ExecuteAsync(MeasureVisionEdgesNodeModel node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
    {
        var frame = context.ResolveInput(node.Frame) ?? throw new InvalidOperationException("输入帧为空。");
        var range = node.ResolveRange(frame, context, cancellationToken); var coordinates = range.Coordinates;
        var result = context.GetRequiredCapability<IEdgeMeasurer>().Measure(frame, range.Bounds,
            new EdgeMeasurementOptions(node.Model, node.LowThreshold, node.HighThreshold, node.MinimumPoints), cancellationToken, range.Region);
        if (coordinates is not null) result = result.InCoordinates(coordinates);
        var projection = WorkflowVisionFrameScope.Stage(context, frame, result);
        return ValueTask.FromResult(NodeExecutionResult.Continue(output: result, projection: projection));
    }
}

/// <summary>原图与上游模板帧的平移定位，不支持旋转/尺度搜索。</summary>
[WorkflowNode("Vision.LocateTemplate", DisplayName = "平移模板定位", Category = "5.Vision/ImageBuffer")]
public sealed class LocateVisionTemplateNodeModel : AnalyzeVisionFrameNodeModel
{
    /// <inheritdoc/>
    public override string NodeType => "Vision.LocateTemplate";
    /// <inheritdoc/>
    public override EWorkflowVisionRange RangeCapability => EWorkflowVisionRange.Region;
    /// <summary>模板整图绑定；模板文件可使用独立LoadFile节点。</summary>
    [WorkflowProperty("模板图像", "上游模板帧，整张作为模板；必须不大于搜索区。", Category = "定位")]
    public WorkflowInput<ImageFrame> Template { get; set; } = WorkflowInput<ImageFrame>.FromLiteral(null);
    /// <summary>1减灰度归一化均方差阈值。</summary>
    public double MinimumScore { get; set; } = .9;
    /// <inheritdoc/>
    public override IReadOnlyList<string> ValidateConfiguration()
    {
        var errors = base.ValidateConfiguration().ToList();
        if (Template is null || Template.Source != WorkflowValueSource.Binding || Template.Binding is null || Template.LiteralValue is not null)
            errors.Add("模板必须为图像帧绑定。");
        if (!double.IsFinite(MinimumScore) || MinimumScore < 0 || MinimumScore > 1) errors.Add("定位分数必须为0至1。");
        return errors;
    }
}

/// <summary>执行固定旋转与尺度下的平移定位。</summary>
public sealed class LocateVisionTemplateNodeHandler : WorkflowNodeHandler<LocateVisionTemplateNodeModel>
{
    /// <inheritdoc/>
    protected override ValueTask<NodeExecutionResult> ExecuteAsync(LocateVisionTemplateNodeModel node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
    {
        var frame = context.ResolveInput(node.Frame) ?? throw new InvalidOperationException("输入帧为空。");
        var template = context.ResolveInput(node.Template) ?? throw new InvalidOperationException("模板帧为空。");
        var range = node.ResolveRange(frame, context, cancellationToken);
        var result = context.GetRequiredCapability<ITemplateLocator>().Locate(frame, range.Bounds, template,
            new PixelBounds(0, 0, template.Image.Info.Width, template.Image.Info.Height), node.MinimumScore, cancellationToken, range.Region, range.Coordinates);
        if (result.TemplateFrameId != template.FrameId) throw new InvalidOperationException("定位结果模板身份与本次输入不一致。");
        var projection = WorkflowVisionFrameScope.Stage(context, frame, result);
        return ValueTask.FromResult(NodeExecutionResult.Continue(output: result, projection: projection));
    }
}
