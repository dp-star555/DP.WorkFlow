using DP.Vision;
using DP.Vision.Algorithms;

namespace DP.WorkFlow;

/// <summary>有界离散旋转/尺度模板搜索，输出姿态及正反坐标变换。</summary>
[WorkflowNode("Vision.LocateTemplatePose", DisplayName = "旋转尺度模板定位", Category = "5.Vision/Location")]
public sealed class LocateVisionTemplatePoseNodeModel : AnalyzeVisionFrameNodeModel
{
    /// <inheritdoc/>
    public override string NodeType => "Vision.LocateTemplatePose";
    /// <inheritdoc/>
    public override EWorkflowVisionRange RangeCapability => EWorkflowVisionRange.Region;
    /// <summary>持久化模板坐标系定义ID；重建局部原点时须更换，不能使用运行FrameId。</summary>
    [WorkflowProperty("坐标系定义ID", "模板局部原点的稳定身份；下游制作ROI时同时锁定模板像素签名。", Category = "定位坐标系")]
    public string CoordinateSystemId { get; set; } = Guid.NewGuid().ToString("N");
    /// <summary>独立模板帧绑定。</summary>
    [WorkflowProperty("模板图像", "ImageFrame绑定，模板与目标身份分别校验。", Category = "输入")]
    public WorkflowInput<ImageFrame> Template { get; set; } = WorkflowInput<ImageFrame>.FromLiteral(null);
    /// <summary>离散顺时针弧度候选。</summary>
    [WorkflowProperty("角度候选", "顺时针弧度，最多181项；不是连续角度估计。", Category = "搜索")]
    public List<double> AnglesRadians { get; set; } = new() { 0 };
    /// <summary>离散尺度候选。</summary>
    [WorkflowProperty("尺度候选", "0.1..10，最多32项；角度×尺度不超过512。", Category = "搜索")]
    public List<double> Scales { get; set; } = new() { 1 };
    /// <summary>最小分数，非概率。</summary>
    [WorkflowProperty("最小分数", "1减有效模板掩码内归一化均方差。", Category = "搜索")]
    public double MinimumScore { get; set; } = .9;
    /// <summary>保守工作量预算。</summary>
    [WorkflowProperty("比较预算", "位置数×模板面积累计上限，最多20亿；超限失败，不截断候选。", Category = "搜索")]
    public long MaximumWork { get; set; } = 200000000;
    internal TemplatePoseOptions Options(LocatedCoordinateSystem? parent = null) => parent is null
        ? new(AnglesRadians, Scales, MinimumScore, MaximumWork)
        : new(AnglesRadians.Select(a => Math.Atan2(Math.Sin(a + parent.Pose.AngleRadians), Math.Cos(a + parent.Pose.AngleRadians))),
            Scales.Select(s => s * parent.Pose.Scale), MinimumScore, MaximumWork);
    /// <inheritdoc/>
    public override IReadOnlyList<string> ValidateConfiguration()
    {
        var errors = base.ValidateConfiguration().ToList();
        if (string.IsNullOrWhiteSpace(CoordinateSystemId)) errors.Add("模板坐标系定义ID不能为空。");
        if (Template is null || Template.Source != WorkflowValueSource.Binding || Template.Binding is null || Template.LiteralValue is not null) errors.Add("模板必须使用图像绑定。");
        try { _ = Options(); } catch (ArgumentException ex) { errors.Add(ex.Message); }
        return errors;
    }
}

/// <summary>调用固定装配定位能力，不在失败后自动切换实现。</summary>
public sealed class LocateVisionTemplatePoseNodeHandler : WorkflowNodeHandler<LocateVisionTemplatePoseNodeModel>
{
    /// <inheritdoc/>
    protected override ValueTask<NodeExecutionResult> ExecuteAsync(LocateVisionTemplatePoseNodeModel node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
    {
        var frame = context.ResolveInput(node.Frame) ?? throw new InvalidOperationException("输入帧为空。");
        var template = context.ResolveInput(node.Template) ?? throw new InvalidOperationException("模板帧为空。");
        var range = node.ResolveRange(frame, context, cancellationToken); var coordinates = range.Coordinates;
        var result = context.GetRequiredCapability<ITemplatePoseLocator>().Locate(frame, template, range.Bounds, node.Options(coordinates), cancellationToken, range.Region)
            ?? throw new InvalidOperationException("姿态定位返回空结果。");
        if (result.TemplateFrameId != template.FrameId) throw new InvalidOperationException("定位模板身份不一致。");
        if (result.Transform is { } pose && (pose.TemplateWidth != template.Image.Info.Width || pose.TemplateHeight != template.Image.Info.Height))
            throw new InvalidOperationException("定位变换的模板尺寸不一致。");
        result = result.InCoordinateSystem(node.CoordinateSystemId, frame, template, cancellationToken);
        if (coordinates is not null) result = result.WithSearchCoordinates(coordinates);
        WorkflowVisionFrameScope.Publish(context, frame, result);
        return ValueTask.FromResult(NodeExecutionResult.Continue(output: result));
    }
}

/// <summary>定位坐标变换节点，未检出不能产生伪造坐标。</summary>
[WorkflowNode("Vision.MapPoseCoordinate", DisplayName = "定位坐标映射", Category = "5.Vision/Location")]
public sealed class MapVisionPoseCoordinateNodeModel : WorkflowNodeModel, IWorkflowNodeConfigurationValidator
{
    /// <inheritdoc/>
    public override string NodeType => "Vision.MapPoseCoordinate";
    /// <summary>定位事实绑定。</summary>
    [WorkflowProperty("定位结果", "TemplatePoseResult绑定，必须Found。", Category = "输入")]
    public WorkflowInput<TemplatePoseResult> Pose { get; set; } = WorkflowInput<TemplatePoseResult>.FromLiteral(null);
    /// <summary>X常量或绑定。</summary>
    public WorkflowInput<double> X { get; set; } = WorkflowInput<double>.FromLiteral(0);
    /// <summary>Y常量或绑定。</summary>
    public WorkflowInput<double> Y { get; set; } = WorkflowInput<double>.FromLiteral(0);
    /// <summary>启用时图像→模板，否则模板→图像。</summary>
    [WorkflowProperty("反向映射", "启用：图像到模板；关闭：模板到图像。坐标都是像素边界。", Category = "映射")]
    public bool Inverse { get; set; }
    /// <inheritdoc/>
    public IReadOnlyList<string> ValidateConfiguration()
    {
        var errors = new List<string>();
        if (Pose is null || Pose.Source != WorkflowValueSource.Binding || Pose.Binding is null || Pose.LiteralValue is not null) errors.Add("定位输入必须为绑定。");
        if (X is null || Y is null || X.Source == WorkflowValueSource.Literal && !double.IsFinite(X.LiteralValue)
            || Y.Source == WorkflowValueSource.Literal && !double.IsFinite(Y.LiteralValue)) errors.Add("坐标必须为有限常量或绑定。");
        return errors;
    }
}

/// <summary>纯坐标变换，不伪装成标定拟合，不自动偏移半像素。</summary>
public sealed class MapVisionPoseCoordinateNodeHandler : WorkflowNodeHandler<MapVisionPoseCoordinateNodeModel>
{
    /// <inheritdoc/>
    protected override ValueTask<NodeExecutionResult> ExecuteAsync(MapVisionPoseCoordinateNodeModel node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var pose = context.ResolveInput(node.Pose)?.Transform ?? throw new InvalidOperationException("没有达标定位，不能映射坐标。");
        var point = new Coordinate2D(context.ResolveInput(node.X), context.ResolveInput(node.Y));
        return ValueTask.FromResult(NodeExecutionResult.Continue(output: node.Inverse ? pose.ToTemplate(point) : pose.ToImage(point)));
    }
}
