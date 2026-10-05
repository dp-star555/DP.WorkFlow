using DP.Vision;
using DP.Vision.Algorithms;

namespace DP.WorkFlow;

/// <summary>按角度及尺度区间搜索模板，输出姿态及正反坐标变换。</summary>
[WorkflowNode("Vision.LocateTemplatePose", DisplayName = "旋转尺度模板定位", Category = "5.Vision/Location")]
public sealed class LocateVisionTemplatePoseNodeModel : AnalyzeVisionFrameNodeModel, IWorkflowVisionAlgorithmNode, IWorkflowVisionTemplateNode
{
    /// <summary>选择动态模板图像绑定或已发布模型资源。</summary>
    [WorkflowProperty("模板来源", "资源模式使用已发布模板，图像绑定保留动态模板。", Category = "模板")]
    public EWorkflowVisionTemplateSource TemplateSource { get; set; }
    /// <inheritdoc/>
    [System.ComponentModel.Browsable(false)]
    public VisionAlgorithmSelection ModelAlgorithm { get; set; } = new() { ImplementationId = "opencv.template-pose-model" };
    /// <inheritdoc/>
    [System.ComponentModel.Browsable(false)]
    public string TemplateResourceId { get; set; } = Guid.NewGuid().ToString("N");
    /// <inheritdoc/>
    [System.ComponentModel.Browsable(false)]
    public VisionTemplateDefinition? TemplateReferenceDefinition { get; set; }
    /// <inheritdoc/>
    [System.ComponentModel.Browsable(false)]
    public string TemplateSourceHash { get; set; } = "";

    /// <summary>打开挂载在本匹配节点上的独立模板制作模型。</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    [WorkflowProperty("模板", "制作、查看或选择本地模板；确认后应用到本节点。", Category = "模板")]
    [WorkflowPropertyEditor(WorkflowPropertyEditorKeys.VisionTemplateEditor, IsAction = true, DialogTitle = "模板制作/选择")]
    public string EditTemplate => string.IsNullOrEmpty(TemplateResourcePath) ? "未选择模板 · 制作/选择…" : "已选择模板 · 查看/修改…";
    /// <summary>已发布版本的清单引用。</summary>
    [System.ComponentModel.Browsable(false), System.Text.Json.Serialization.JsonIgnore]
    [WorkflowProperty("模板资源", "双击节点展开模板制作；支持配方相对路径和resource:。", Category = "模板")]
    [WorkflowPropertyVisibleWhen(nameof(TemplateSource), nameof(EWorkflowVisionTemplateSource.Resource))]
    [WorkflowPropertyEditor(WorkflowPropertyEditorKeys.FilePath)]
    public string TemplateResourcePath { get => ModelAlgorithm.Settings.GetValueOrDefault("templatePath", ""); set => ModelAlgorithm.Settings["templatePath"] = value; }
    /// <inheritdoc/>
    [System.ComponentModel.Browsable(false), System.Text.Json.Serialization.JsonIgnore]
    public bool RequiresPoseSearch => true;
    /// <summary>动态模板图像模式的节点专属实现选择。</summary>
    [System.ComponentModel.Browsable(false)]
    public VisionAlgorithmSelection Algorithm { get; set; } = new() { ImplementationId = "opencv.template-pose" };

    /// <inheritdoc/>
    public IReadOnlyList<WorkflowVisionAlgorithmSlot> GetAlgorithmSlots() => TemplateSource == EWorkflowVisionTemplateSource.Resource
        ? new[] { WorkflowVisionTemplateResource.Slot(this) } : new[] { new WorkflowVisionAlgorithmSlot("algorithm", typeof(ITemplatePoseLocator), Algorithm) };

    /// <inheritdoc/>
    public override string NodeType => "Vision.LocateTemplatePose";
    /// <inheritdoc/>
    public override EWorkflowVisionRange RangeCapability => EWorkflowVisionRange.Region;
    /// <summary>持久化模板坐标系定义ID；重建局部原点时须更换，不能使用运行FrameId。</summary>
    [WorkflowProperty("坐标系定义ID", "模板局部原点的稳定身份；下游制作ROI时同时锁定模板像素签名。", Category = "定位坐标系")]
    public string CoordinateSystemId { get; set; } = Guid.NewGuid().ToString("N");
    /// <summary>独立模板帧绑定。</summary>
    [WorkflowProperty("模板图像", "ImageFrame绑定，模板与目标身份分别校验。", Category = "输入")]
    [WorkflowPropertyVisibleWhen(nameof(TemplateSource), nameof(EWorkflowVisionTemplateSource.ImageBinding))]
    public WorkflowInput<ImageFrame> Template { get; set; } = WorkflowInput<ImageFrame>.FromLiteral(null);
    /// <summary>搜索顺时针弧度下限。</summary>
    [WorkflowProperty("匹配最小角度", "相对制作样图的角度下限；绑定父坐标时相对父坐标。搜索区间须在模型制作范围内。", Category = "搜索", DisplayRadiansAsDegrees = true)]
    public double MinimumAngleRadians { get; set; }
    /// <summary>搜索顺时针弧度上限。</summary>
    [WorkflowProperty("匹配最大角度", "例如最小85°、最大95°表示90°附近±5°。上下限相同表示固定角度，跨度最多360°。", Category = "搜索", DisplayRadiansAsDegrees = true)]
    public double MaximumAngleRadians { get; set; }
    /// <summary>搜索尺度下限。</summary>
    [WorkflowProperty("匹配最小尺度", "0.1至10；绑定父坐标时乘以父坐标尺度。", Category = "搜索")]
    public double MinimumScale { get; set; } = 1;
    /// <summary>搜索尺度上限。</summary>
    [WorkflowProperty("匹配最大尺度", "例如0.9至1.1表示搜索整个区间；NCC须设为1至1。", Category = "搜索")]
    public double MaximumScale { get; set; } = 1;
    /// <summary>采样引擎的角度步长。</summary>
    [WorkflowProperty("采样角度步长", "OpenCV按此步长采样搜索区间；HALCON使用原生范围搜索，不使用此参数。与模型制作步长独立。", Category = "搜索采样", DisplayRadiansAsDegrees = true)]
    public double AngleStepRadians { get; set; } = Math.PI / 180;
    /// <summary>采样引擎的尺度步长。</summary>
    [WorkflowProperty("采样尺度步长", "OpenCV按此步长采样，包含区间端点；HALCON不使用此参数。", Category = "搜索采样")]
    public double ScaleStep { get; set; } = .01;
    /// <summary>最小分数，非概率。</summary>
    [WorkflowProperty("最小分数", "得分由所选引擎定义；OpenCV平方差、HALCON NCC相关性或形状得分需分别确认阈值。", Category = "搜索")]
    public double MinimumScore { get; set; } = .9;
    /// <summary>保守工作量预算。</summary>
    [WorkflowProperty("比较预算", "最多20亿；OpenCV约束像素比较量，HALCON约束候选ROI验证量。超限失败，不截断候选。", Category = "搜索")]
    public long MaximumWork { get; set; } = 200000000;
    internal TemplatePoseOptions Options(VisionCoordinateSystem? parent = null)
    {
        double rotation = parent == null ? 0 : Math.Atan2(Math.Sin(parent.RotationRadians), Math.Cos(parent.RotationRadians));
        double scale = parent?.SimilarityScale ?? 1;
        return new(MinimumAngleRadians + rotation, MaximumAngleRadians + rotation, MinimumScale * scale, MaximumScale * scale,
            MinimumScore, MaximumWork, AngleStepRadians, ScaleStep * scale);
    }
    /// <summary>制作界面沿用运行搜索区间、采样步长和比较预算。</summary>
    public TemplatePoseOptions OptionsForPreview(VisionCoordinateSystem? parent = null) => Options(parent);
    /// <inheritdoc/>
    public override IReadOnlyList<string> ValidateConfiguration()
    {
        var errors = base.ValidateConfiguration().ToList();
        if (string.IsNullOrWhiteSpace(CoordinateSystemId)) errors.Add("模板坐标系定义ID不能为空。");
        errors.AddRange(WorkflowVisionTemplateResource.Validate(this));
        if (TemplateSource == EWorkflowVisionTemplateSource.ImageBinding && (Template is null || Template.Source != WorkflowValueSource.Binding || Template.Binding is null || Template.LiteralValue is not null)) errors.Add("模板必须使用图像绑定。");
        try { _ = Options(); } catch (ArgumentException ex) { errors.Add(ex.Message); }
        return errors;
    }
}

/// <summary>调用本轮准备的节点定位实现，不在失败后自动切换实现。</summary>
public sealed class LocateVisionTemplatePoseNodeHandler : WorkflowNodeHandler<LocateVisionTemplatePoseNodeModel>
{
    /// <inheritdoc/>
    protected override ValueTask<NodeExecutionResult> ExecuteAsync(LocateVisionTemplatePoseNodeModel node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
    {
        var frame = context.ResolveInput(node.Frame) ?? throw new InvalidOperationException("输入帧为空。");
        if (node.TemplateSource == EWorkflowVisionTemplateSource.Resource)
        {
            var modelRange = node.ResolveRange(frame, context, cancellationToken);
            var modelResult = WorkflowVisionTemplateResource.Match(node, context, frame, modelRange.Bounds, modelRange.Region,
                modelRange.Coordinates, node.Options(modelRange.Coordinates), cancellationToken);
            return ValueTask.FromResult(NodeExecutionResult.Continue(output: modelResult, projection: WorkflowVisionFrameScope.Stage(context, frame, modelResult)));
        }
        var template = context.ResolveInput(node.Template) ?? throw new InvalidOperationException("模板帧为空。");
        var range = node.ResolveRange(frame, context, cancellationToken); var coordinates = range.Coordinates;
        var result = WorkflowVisionAlgorithmInvocation.Invoke(context, node.Algorithm, "opencv.template-pose",
            (ITemplatePoseLocator algorithm) => algorithm.Locate(frame, template, range.Bounds, node.Options(coordinates), cancellationToken, range.Region), cancellationToken)
            ?? throw new InvalidOperationException("姿态定位返回空结果。");
        if (result.TemplateFrameId != template.FrameId) throw new InvalidOperationException("定位模板身份不一致。");
        if (result.Transform is { } pose && (pose.TemplateWidth != template.Image.Info.Width || pose.TemplateHeight != template.Image.Info.Height))
            throw new InvalidOperationException("定位变换的模板尺寸不一致。");
        result = result.InCoordinateSystem(node.CoordinateSystemId, frame, template, cancellationToken);
        if (coordinates is not null) result = result.WithSearchCoordinates(coordinates);
        var projection = WorkflowVisionFrameScope.Stage(context, frame, result);
        return ValueTask.FromResult(NodeExecutionResult.Continue(output: result, projection: projection));
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
    [WorkflowProperty("反向映射", "启用：图像到参考；关闭：参考到图像。资源模式采用制作原点和方向，旧图像模式仍采用模板像素边界。", Category = "映射")]
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
        var result = context.ResolveInput(node.Pose) ?? throw new InvalidOperationException("没有定位结果，不能映射坐标。");
        var pose = result.Transform ?? throw new InvalidOperationException("没有达标定位，不能映射坐标。");
        var point = new Coordinate2D(context.ResolveInput(node.X), context.ResolveInput(node.Y));
        var mapped = result.CoordinateSystem is { } coordinates
            ? (node.Inverse ? coordinates.ImageToLocal : coordinates.LocalToImage).Map(point)
            : node.Inverse ? pose.ToTemplate(point) : pose.ToImage(point);
        return ValueTask.FromResult(NodeExecutionResult.Continue(output: mapped));
    }
}
