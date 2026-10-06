using DP.Vision;
using DP.Vision.Algorithms;

namespace DP.WorkFlow;

/// <summary>显式采样带卡尺（直线或圆弧），参数为原图像素边界坐标。</summary>
[WorkflowNode("Vision.MeasureCaliper", DisplayName = "亚像素采样卡尺", Category = "5.Vision/Measurement")]
public sealed class MeasureVisionCaliperNodeModel : AnalyzeVisionFrameNodeModel, IWorkflowVisionAlgorithmNode
{
    /// <summary>节点专属实现选择；旧配方缺字段时保持原实现。</summary>
    [System.ComponentModel.Browsable(false)]
    public VisionAlgorithmSelection Algorithm { get; set; } = new() { ImplementationId = "managed.caliper" };

    /// <inheritdoc/>
    public IReadOnlyList<WorkflowVisionAlgorithmSlot> GetAlgorithmSlots() => new[] { new WorkflowVisionAlgorithmSlot("algorithm", typeof(ICaliperMeasurer), Algorithm) };

    /// <inheritdoc/>
    public override string NodeType => "Vision.MeasureCaliper";
    /// <inheritdoc/>
    public override EWorkflowVisionRange RangeCapability => EWorkflowVisionRange.SamplingBand;
    /// <summary>扫描路径形状。</summary>
    [WorkflowProperty("卡尺形状", "直线卡尺沿起点→终点扫描；圆弧卡尺沿圆弧按起始角→扫描角度扫描，采样带沿半径方向。", Category = "采样带")]
    public EVisionCaliperShape Shape { get; set; }
    /// <summary>起点X。</summary>
    [WorkflowPropertyVisibleWhen(nameof(Shape), nameof(EVisionCaliperShape.Line))]
    [WorkflowProperty("起点X", "原图像素边界坐标；绑定定位后为所选坐标系的局部单位。", Category = "采样带")]
    public double StartX { get; set; } = .5;
    /// <summary>起点Y。</summary>
    [WorkflowPropertyVisibleWhen(nameof(Shape), nameof(EVisionCaliperShape.Line))]
    [WorkflowProperty("起点Y", "原图像素边界坐标；绑定定位后为所选坐标系的局部单位。", Category = "采样带")]
    public double StartY { get; set; } = 2.5;
    /// <summary>终点X。</summary>
    [WorkflowPropertyVisibleWhen(nameof(Shape), nameof(EVisionCaliperShape.Line))]
    [WorkflowProperty("终点X", "与起点同空间，绑定定位后为局部单位；起终点决定扫描方向。", Category = "采样带")]
    public double EndX { get; set; } = 20.5;
    /// <summary>终点Y。</summary>
    [WorkflowPropertyVisibleWhen(nameof(Shape), nameof(EVisionCaliperShape.Line))]
    [WorkflowProperty("终点Y", "与起点同空间，绑定定位后为局部单位；起终点决定扫描方向。", Category = "采样带")]
    public double EndY { get; set; } = 2.5;
    /// <summary>圆弧圆心X。</summary>
    [WorkflowPropertyVisibleWhen(nameof(Shape), nameof(EVisionCaliperShape.Arc))]
    [WorkflowProperty("圆心X", "原图像素边界坐标；绑定定位后为所选坐标系的局部单位。", Category = "采样带")]
    public double CenterX { get; set; } = 50;
    /// <summary>圆弧圆心Y。</summary>
    [WorkflowPropertyVisibleWhen(nameof(Shape), nameof(EVisionCaliperShape.Arc))]
    [WorkflowProperty("圆心Y", "原图像素边界坐标；绑定定位后为所选坐标系的局部单位。", Category = "采样带")]
    public double CenterY { get; set; } = 50;
    /// <summary>扫描圆弧半径。</summary>
    [WorkflowPropertyVisibleWhen(nameof(Shape), nameof(EVisionCaliperShape.Arc))]
    [WorkflowProperty("半径", "扫描圆弧半径，原图像素；绑定定位后为局部单位。采样带以此为中心沿半径方向展开。", Category = "采样带")]
    public double Radius { get; set; } = 30;
    /// <summary>起始角，度。</summary>
    [WorkflowPropertyVisibleWhen(nameof(Shape), nameof(EVisionCaliperShape.Arc))]
    [WorkflowProperty("起始角", "度，从X轴正向起顺时针（图像Y向下）；绑定定位后相对坐标系X轴。", Category = "采样带", Unit = "°")]
    public double StartAngle { get; set; }
    /// <summary>扫描角度范围，度。</summary>
    [WorkflowPropertyVisibleWhen(nameof(Shape), nameof(EVisionCaliperShape.Arc))]
    [WorkflowProperty("扫描角度", "度，绝对值 (0, 360]；正为顺时针、负为逆时针，决定扫描方向与极性方向。", Category = "采样带", Unit = "°")]
    public double SweepAngle { get; set; } = 90;
    /// <summary>圆弧卡尺的搜索方向。</summary>
    [WorkflowPropertyVisibleWhen(nameof(Shape), nameof(EVisionCaliperShape.Arc))]
    [WorkflowProperty("搜索方向", "由内到外/由外到内：沿半径搜索边缘（常用于找圆），带宽为半径方向的搜索范围；沿圆弧：从起始角扫到终止角。极性均相对搜索方向。", Category = "采样带")]
    public EVisionArcScanDirection ArcDirection { get; set; } = EVisionArcScanDirection.InnerToOuter;
    /// <summary>卡尺数量。</summary>
    [WorkflowProperty("卡尺数量", "直线：采样带沿宽度均分为 N 个并排子卡尺，各自沿起点→终点搜索（1..64，不超过 2×采样半宽+1）；圆弧径向搜索：圆弧均分为 N 段，每段一个径向卡尺（1..128）；沿圆弧扫描时忽略。N ≥ 3 时在节点内拟合直线/圆。", Category = "采样带")]
    public int CaliperCount { get; set; } = 1;
    /// <summary>沿搜索方向的采样间隔。</summary>
    [WorkflowProperty("搜索间隔", "沿搜索方向（直线为起点→终点，圆弧为扫描/径向方向）每隔多少像素取一个剖面点，原图 0.1..10；绑定定位后为局部单位。", Category = "采样带")]
    public double ScanStep { get; set; } = 1;
    /// <summary>拟合时每个卡尺取哪个边缘。</summary>
    [WorkflowProperty("拟合取点", "卡尺数量 ≥ 3 时，每个卡尺取一个边缘点参与拟合：最强边缘/沿搜索方向第一个/最后一个。", Category = "拟合")]
    public EVisionCaliperFitPoint FitPoint { get; set; }
    /// <summary>拟合内点距离阈值。</summary>
    [WorkflowProperty("拟合距离阈值", "点到拟合直线/圆的距离不超过该值为计算点，否则为忽略点；原图像素，绑定定位后为局部单位。", Category = "拟合")]
    public double FitDistanceThreshold { get; set; } = 1;
    /// <summary>垂直采样半宽。</summary>
    [WorkflowProperty("采样半宽", "单侧垂直采样步数0..63（圆弧为半径方向），与垂直采样间隔共同决定实际带宽。", Category = "采样带")]
    public int HalfWidth { get; set; } = 2;
    /// <summary>垂直采样间隔；绑定定位时为局部单位。</summary>
    [WorkflowProperty("垂直采样间隔", "半宽为采样步数；实际半宽=半宽×间隔。随定位尺度转换，原图有效间隔必须在0.1..10。", Category = "采样带")]
    public double BandSampleStep { get; set; } = 1;
    /// <summary>最小绝对灰度梯度/像素。</summary>
    [WorkflowProperty("最小梯度", "绝对灰度变化/像素。", Category = "边缘")]
    public double MinimumGradient { get; set; } = 5;
    /// <summary>极性。</summary>
    [WorkflowProperty("边缘极性", "Rising暗到亮，Falling亮到暗，均沿扫描方向（直线为起点到终点，圆弧为起始角到终止角）。", Category = "边缘")]
    public ECaliperPolarity Polarity { get; set; }
    /// <summary>边缘最小间距。</summary>
    [WorkflowProperty("边缘最小间距", "梯度强者优先的非极大抑制；原图像素，绑定定位后为局部单位。", Category = "边缘")]
    public double MinimumSeparation { get; set; } = 2;
    internal VisionLineCaliperOptions LineOptions(VisionCoordinateSystem? coordinates = null)
    {
        var whole = Options(coordinates);
        double scale = coordinates?.SimilarityScale ?? 1;
        return new VisionLineCaliperOptions(whole.Start, whole.End, HalfWidth, MinimumGradient, Polarity, whole.MinimumSeparation, whole.BandSampleStep,
            ScanStep * scale, CaliperCount, FitPoint, FitDistanceThreshold * scale);
    }
    internal CaliperOptions Options(VisionCoordinateSystem? coordinates = null)
    {
        PointD Map(double x, double y)
        {
            var point = new Coordinate2D(x, y);
            if (coordinates is not null) point = coordinates.LocalToImage.Map(point);
            return new PointD(point.X, point.Y);
        }
        double scale = coordinates?.SimilarityScale ?? 1;
        return new CaliperOptions(Map(StartX, StartY), Map(EndX, EndY), HalfWidth, MinimumGradient, Polarity, MinimumSeparation * scale, BandSampleStep * scale);
    }
    internal VisionArcCaliperOptions ArcOptions(VisionCoordinateSystem? coordinates = null)
    {
        var center = new Coordinate2D(CenterX, CenterY);
        if (coordinates is not null) center = coordinates.LocalToImage.Map(center);
        double scale = coordinates?.SimilarityScale ?? 1, rotation = coordinates is null ? 0 : coordinates.RotationRadians * 180 / Math.PI;
        return new VisionArcCaliperOptions(new PointD(center.X, center.Y), Radius * scale, StartAngle + rotation, SweepAngle, HalfWidth,
            MinimumGradient, Polarity, MinimumSeparation * scale, BandSampleStep * scale, ArcDirection, CaliperCount, ScanStep * scale,
            FitPoint, FitDistanceThreshold * scale);
    }
    /// <inheritdoc/>
    public override IReadOnlyList<string> ValidateConfiguration()
    {
        var errors = base.ValidateConfiguration().ToList();
        if (!FullImage) errors.Add("卡尺由起终点和带宽定义，不接受额外矩形范围。");
        if (!Enum.IsDefined(Shape)) { errors.Add("未知卡尺形状。"); return errors; }
        try
        {
            if (Shape == EVisionCaliperShape.Arc)
            {
                // 局部单位的尺度要到运行时才知道：这里只校验形状，弧长与原图有效间隔在运行时按坐标系尺度校验。
                if (Coordinates is null) _ = ArcOptions();
                else if (!double.IsFinite(CenterX) || !double.IsFinite(CenterY) || !double.IsFinite(Radius) || Radius <= 0
                    || !double.IsFinite(StartAngle) || !double.IsFinite(SweepAngle) || Math.Abs(SweepAngle) < 1e-9 || Math.Abs(SweepAngle) > 360
                    || HalfWidth < 0 || HalfWidth > 63 || !double.IsFinite(BandSampleStep) || BandSampleStep < .01 || BandSampleStep > 100
                    || Radius - HalfWidth * BandSampleStep < 0 || !Enum.IsDefined(ArcDirection)
                    || ArcDirection != EVisionArcScanDirection.AlongArc && (CaliperCount < 1 || CaliperCount > 128 || HalfWidth < 1))
                    throw new ArgumentException("局部圆弧采样带配置无效。");
                if (Coordinates is not null) _ = new CaliperOptions(new PointD(0, 0), new PointD(4, 0), HalfWidth, MinimumGradient, Polarity, MinimumSeparation);
            }
            else if (Coordinates is null) _ = LineOptions();
            else
            {
                _ = new Coordinate2D(StartX, StartY); _ = new Coordinate2D(EndX, EndY);
                if (Math.Abs(StartX) > 1e9 || Math.Abs(StartY) > 1e9 || Math.Abs(EndX) > 1e9 || Math.Abs(EndY) > 1e9
                    || Math.Sqrt(Math.Pow(EndX - StartX, 2) + Math.Pow(EndY - StartY, 2)) < 1e-9
                    || !double.IsFinite(BandSampleStep) || BandSampleStep < .01 || BandSampleStep > 100)
                    throw new ArgumentException("局部采样带配置无效。");
                _ = new CaliperOptions(new PointD(0, 0), new PointD(4, 0), HalfWidth, MinimumGradient, Polarity, MinimumSeparation);
                if (CaliperCount < 1 || CaliperCount > 64 || CaliperCount > HalfWidth * 2 + 1) throw new ArgumentException("卡尺数量须为 1..64，且不超过垂直采样点数（2×采样半宽+1）。");
            }
            // 局部单位的尺度运行时才知道：搜索间隔与拟合阈值只校验为正的有限值。
            if (Coordinates is not null && (!double.IsFinite(ScanStep) || ScanStep <= 0 || !double.IsFinite(FitDistanceThreshold) || FitDistanceThreshold <= 0
                || !Enum.IsDefined(FitPoint)))
                throw new ArgumentException("搜索间隔、拟合参数无效。");
        }
        catch (ArgumentException ex) { errors.Add(ex.Message); }
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
        var result = node.Shape == EVisionCaliperShape.Arc
            ? VisionArcCaliper.Measure(frame, node.ArcOptions(coordinates), cancellationToken)
            : VisionLineCaliper.Measure(frame, node.LineOptions(coordinates), options => WorkflowVisionAlgorithmInvocation.Invoke(context, node.Algorithm,
                "managed.caliper", (ICaliperMeasurer algorithm) => algorithm.Measure(frame, options, cancellationToken), cancellationToken), cancellationToken);
        if (coordinates is not null) result = result.InCoordinates(coordinates);
        var projection = WorkflowVisionFrameScope.Stage(context, frame, result);
        return ValueTask.FromResult(NodeExecutionResult.Continue(output: result, projection: projection));
    }
}

/// <summary>聚合同帧多个卡尺证据，以RANSAC和正交TLS拟合直线。</summary>
[WorkflowNode("Vision.FitRobustLine", DisplayName = "鲁棒拟合直线", Category = "5.Vision/Measurement")]
public sealed class FitVisionRobustLineNodeModel : AnalyzeVisionFrameNodeModel, IWorkflowVisionAlgorithmNode
{
    /// <summary>节点专属实现选择；旧配方缺字段时保持原实现。</summary>
    [System.ComponentModel.Browsable(false)]
    public VisionAlgorithmSelection Algorithm { get; set; } = new() { ImplementationId = "managed.robust-line" };

    /// <inheritdoc/>
    public IReadOnlyList<WorkflowVisionAlgorithmSlot> GetAlgorithmSlots() => new[] { new WorkflowVisionAlgorithmSlot("algorithm", typeof(IRobustLineFitter), Algorithm) };

    /// <inheritdoc/>
    public override string NodeType => "Vision.FitRobustLine";
    /// <inheritdoc/>
    public override EWorkflowVisionRange RangeCapability => EWorkflowVisionRange.GeometryFacts;
    /// <summary>同帧卡尺输出绑定列表，顺序决定点索引。</summary>
    [WorkflowProperty("卡尺证据列表", "同帧卡尺结果绑定；列表顺序决定内点索引。", Category = "输入")]
    public List<WorkflowInput<VisionCaliperMeasurement>> Samples { get; set; } = new();
    /// <summary>内点距离，像素。</summary>
    [WorkflowProperty("内点距离阈值", "正交距离；原图像素，绑定定位后为业务局部单位。", Category = "拟合")]
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
        for (var index = 0; index < node.Samples.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // 集合元素不是节点模型的顶层输入属性，必须用显式动态键，不能伪装成静态输入槽。
            var sample = context.ResolveDynamicInput($"Samples[{index}]", node.Samples[index])
                ?? throw new InvalidOperationException("采样为空。");
            if (sample.FrameId != frame.FrameId) throw new InvalidOperationException("禁止混合不同帧的卡尺证据。");
            foreach (var edge in sample.Edges)
            {
                if (points.Count >= 8192) throw new InvalidOperationException("拟合点预算超限。");
                points.Add(edge.Position);
            }
        }
        var result = WorkflowVisionAlgorithmInvocation.Invoke(context, node.Algorithm, "managed.robust-line",
            (IRobustLineFitter algorithm) => algorithm.Fit(frame.FrameId, points, node.DistanceThreshold * (coordinates?.SimilarityScale ?? 1), node.Iterations, node.MinimumInliers, cancellationToken), cancellationToken);
        if (coordinates is not null) result = result.InCoordinates(coordinates);
        var projection = WorkflowVisionFrameScope.Stage(context, frame, result);
        return ValueTask.FromResult(NodeExecutionResult.Continue(output: result, projection: projection));
    }
}
