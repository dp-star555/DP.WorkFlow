using DP.Vision;
using DP.Vision.Algorithms;

namespace DP.WorkFlow;

/// <summary>按角度及尺度区间搜索模板，输出中心、角度、缩放和参考点；角度和尺度区间都固定时即平移定位。坐标系由“构建本帧坐标系”生成。</summary>
[WorkflowNode("Vision.LocateTemplatePose", DisplayName = "模板定位", Category = WorkflowVisionCategories.Location)]
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
    public bool RequiresPoseSearch => RequiresRotation || RequiresScale;
    /// <summary>角度区间不是单一角度时需要旋转搜索。</summary>
    [System.ComponentModel.Browsable(false), System.Text.Json.Serialization.JsonIgnore]
    public bool RequiresRotation => Math.Abs(MaximumAngleRadians - MinimumAngleRadians) > 1e-12;
    /// <summary>尺度区间不是1至1时需要尺度搜索。</summary>
    [System.ComponentModel.Browsable(false), System.Text.Json.Serialization.JsonIgnore]
    public bool RequiresScale => Math.Abs(MinimumScale - 1) > 1e-9 || Math.Abs(MaximumScale - 1) > 1e-9;
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
    /// <summary>独立模板帧绑定。</summary>
    [WorkflowProperty("模板图像", "ImageFrame绑定，模板与目标身份分别校验。", Category = "输入")]
    [WorkflowPropertyVisibleWhen(nameof(TemplateSource), nameof(EWorkflowVisionTemplateSource.ImageBinding))]
    public WorkflowInput<ImageFrame> Template { get; set; } = WorkflowInput<ImageFrame>.FromLiteral(null);
    /// <summary>搜索顺时针弧度下限。</summary>
    [WorkflowProperty("匹配最小角度", "相对制作样图的角度下限；绑定父坐标时相对父坐标。搜索区间须在模型制作范围内。", Category = "搜索", DisplayRadiansAsDegrees = true)]
    public double MinimumAngleRadians { get; set; }
    /// <summary>搜索顺时针弧度上限。</summary>
    [WorkflowProperty("匹配最大角度", "例如最小85°、最大95°表示90°附近±5°。上下限相同表示固定角度（角度和尺度都固定即平移定位），跨度最多360°。", Category = "搜索", DisplayRadiansAsDegrees = true)]
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
            var modelResult = WorkflowVisionTemplateResource.Match(context, frame, modelRange.Bounds, modelRange.Region,
                node.Options(modelRange.Coordinates), cancellationToken);
            return ValueTask.FromResult(NodeExecutionResult.Continue(output: modelResult, projection: WorkflowVisionFrameScope.Stage(context, frame, modelResult)));
        }
        var template = context.ResolveInput(node.Template) ?? throw new InvalidOperationException("模板帧为空。");
        var range = node.ResolveRange(frame, context, cancellationToken);
        var result = WorkflowVisionAlgorithmInvocation.Invoke(context, node.Algorithm, "opencv.template-pose",
            (ITemplatePoseLocator algorithm) => algorithm.Locate(frame, template, range.Bounds, node.Options(range.Coordinates), cancellationToken, range.Region), cancellationToken)
            ?? throw new InvalidOperationException("姿态定位返回空结果。");
        if (result.TemplateFrameId != template.FrameId) throw new InvalidOperationException("定位模板身份不一致。");
        if (result.Transform is { } pose && (pose.TemplateWidth != template.Image.Info.Width || pose.TemplateHeight != template.Image.Info.Height))
            throw new InvalidOperationException("定位变换的模板尺寸不一致。");
        var projection = WorkflowVisionFrameScope.Stage(context, frame, result);
        return ValueTask.FromResult(NodeExecutionResult.Continue(output: result, projection: projection));
    }
}
