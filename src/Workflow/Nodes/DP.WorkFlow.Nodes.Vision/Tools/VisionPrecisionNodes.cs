using DP.Vision;
using DP.Vision.Algorithms;

namespace DP.WorkFlow;

/// <summary>显式采样带卡尺，参数为原图像素边界坐标。</summary>
[WorkflowNode("Vision.MeasureCaliper", DisplayName = "亚像素采样卡尺", Category = "5.Vision/Measurement")]
public sealed class MeasureVisionCaliperNodeModel : AnalyzeVisionFrameNodeModel
{
    /// <inheritdoc/>
    public override string NodeType => "Vision.MeasureCaliper";
    /// <inheritdoc/>
    public override EWorkflowVisionRange RangeCapability => EWorkflowVisionRange.SamplingBand;
    /// <summary>起点X。</summary>
    [WorkflowProperty("起点X", "原图像素边界坐标。", Category = "采样带")]
    public double StartX { get; set; } = .5;
    /// <summary>起点Y。</summary>
    [WorkflowProperty("起点Y", "原图像素边界坐标。", Category = "采样带")]
    public double StartY { get; set; } = 2.5;
    /// <summary>终点X。</summary>
    [WorkflowProperty("终点X", "起终点决定扫描方向。", Category = "采样带")]
    public double EndX { get; set; } = 20.5;
    /// <summary>终点Y。</summary>
    [WorkflowProperty("终点Y", "起终点决定扫描方向。", Category = "采样带")]
    public double EndY { get; set; } = 2.5;
    /// <summary>垂直采样半宽。</summary>
    [WorkflowProperty("采样半宽", "垂直方向每像素采样并平均；0..63。", Category = "采样带")]
    public int HalfWidth { get; set; } = 2;
    /// <summary>最小绝对灰度梯度/像素。</summary>
    [WorkflowProperty("最小梯度", "绝对灰度变化/像素。", Category = "边缘")]
    public double MinimumGradient { get; set; } = 5;
    /// <summary>极性。</summary>
    [WorkflowProperty("边缘极性", "Rising暗到亮，Falling亮到暗，均沿起点到终点。", Category = "边缘")]
    public ECaliperPolarity Polarity { get; set; }
    /// <summary>边缘最小间距。</summary>
    [WorkflowProperty("边缘最小间距", "梯度强者优先的非极大抑制，像素。", Category = "边缘")]
    public double MinimumSeparation { get; set; } = 2;
    internal CaliperOptions Options(LocatedCoordinateSystem? coordinates = null)
    {
        PointD Map(double x, double y)
        {
            var point = new Coordinate2D(x, y);
            if (coordinates is not null) point = coordinates.LocalToImage.Map(point);
            return new PointD(point.X, point.Y);
        }
        double scale = coordinates?.Pose.Scale ?? 1;
        return new CaliperOptions(Map(StartX, StartY), Map(EndX, EndY), HalfWidth, MinimumGradient, Polarity, MinimumSeparation * scale, scale);
    }
    /// <inheritdoc/>
    public override IReadOnlyList<string> ValidateConfiguration()
    {
        var errors = base.ValidateConfiguration().ToList();
        if (!FullImage) errors.Add("卡尺由起终点和带宽定义，不接受额外矩形范围。");
        try { _ = Options(); } catch (ArgumentException ex) { errors.Add(ex.Message); }
        return errors;
    }
}

/// <summary>产生真实采样剖面及梯度峰插值证据。</summary>
public sealed class MeasureVisionCaliperNodeHandler : WorkflowNodeHandler<MeasureVisionCaliperNodeModel>
{
    /// <inheritdoc/>
    protected override ValueTask<NodeExecutionResult> ExecuteAsync(MeasureVisionCaliperNodeModel node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
    {
        var frame = context.ResolveInput(node.Frame) ?? throw new InvalidOperationException("输入帧为空。");
        var coordinates = node.ResolveCoordinates(frame, context);
        var result = context.GetRequiredCapability<ICaliperMeasurer>().Measure(frame, node.Options(coordinates), cancellationToken);
        if (coordinates is not null) result = result.InCoordinates(coordinates);
        WorkflowVisionFrameScope.Publish(context, frame, result);
        return ValueTask.FromResult(NodeExecutionResult.Continue(output: result));
    }
}

/// <summary>聚合同帧多个卡尺证据，以RANSAC和正交TLS拟合直线。</summary>
[WorkflowNode("Vision.FitRobustLine", DisplayName = "鲁棒拟合直线", Category = "5.Vision/Measurement")]
public sealed class FitVisionRobustLineNodeModel : AnalyzeVisionFrameNodeModel
{
    /// <inheritdoc/>
    public override string NodeType => "Vision.FitRobustLine";
    /// <inheritdoc/>
    public override EWorkflowVisionRange RangeCapability => EWorkflowVisionRange.GeometryFacts;
    /// <summary>同帧卡尺输出绑定列表，顺序决定点索引。</summary>
    [WorkflowProperty("卡尺证据列表", "同帧CaliperResult绑定；列表顺序决定内点索引。", Category = "输入")]
    public List<WorkflowInput<CaliperResult>> Samples { get; set; } = new();
    /// <summary>内点距离，像素。</summary>
    [WorkflowProperty("内点距离阈值", "正交距离，像素。", Category = "拟合")]
    public double DistanceThreshold { get; set; } = .5;
    /// <summary>RANSAC预算。</summary>
    [WorkflowProperty("采样迭代预算", "1..1024，总距离评估不超过400万。", Category = "拟合")]
    public int Iterations { get; set; } = 256;
    /// <summary>最小内点数。</summary>
    [WorkflowProperty("最少内点", "至少3，证据不足则执行失败。", Category = "拟合")]
    public int MinimumInliers { get; set; } = 3;
    /// <inheritdoc/>
    public override IReadOnlyList<string> ValidateConfiguration()
    {
        var errors = base.ValidateConfiguration().ToList();
        if (!FullImage || !double.IsFinite(DistanceThreshold) || DistanceThreshold <= 0 || DistanceThreshold > 1000000
            || Iterations < 1 || Iterations > 1024 || MinimumInliers < 3 || MinimumInliers > 8192) errors.Add("无效鲁棒拟合参数。");
        if (Samples is null || Samples.Count < 1 || Samples.Count > 512 || Samples.Any(s => s is null || s.Source != WorkflowValueSource.Binding || s.Binding is null || s.LiteralValue is not null))
            errors.Add("必须配置1..512个卡尺事实绑定。");
        return errors;
    }
}

/// <summary>拒绝混帧采样和超过预算的证据，失败不提交输出。</summary>
public sealed class FitVisionRobustLineNodeHandler : WorkflowNodeHandler<FitVisionRobustLineNodeModel>
{
    /// <inheritdoc/>
    protected override ValueTask<NodeExecutionResult> ExecuteAsync(FitVisionRobustLineNodeModel node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
    {
        var frame = context.ResolveInput(node.Frame) ?? throw new InvalidOperationException("输入帧为空。");
        var coordinates = node.ResolveCoordinates(frame, context);
        var points = new List<PointD>();
        foreach (var binding in node.Samples)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sample = context.ResolveInput(binding) ?? throw new InvalidOperationException("采样为空。");
            if (sample.FrameId != frame.FrameId) throw new InvalidOperationException("禁止混合不同帧的卡尺证据。");
            foreach (var edge in sample.Edges)
            {
                if (points.Count >= 8192) throw new InvalidOperationException("拟合点预算超限。");
                points.Add(edge.Position);
            }
        }
        var result = context.GetRequiredCapability<IRobustLineFitter>().Fit(frame.FrameId, points, node.DistanceThreshold * (coordinates?.Pose.Scale ?? 1), node.Iterations, node.MinimumInliers, cancellationToken);
        if (coordinates is not null) result = result.InCoordinates(coordinates);
        WorkflowVisionFrameScope.Publish(context, frame, result);
        return ValueTask.FromResult(NodeExecutionResult.Continue(output: result));
    }
}
