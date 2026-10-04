using DP.Vision;
using DP.Vision.Algorithms;

namespace DP.WorkFlow;

/// <summary>基于Canny边缘的新版线/圆拟合节点。</summary>
[WorkflowNode("Vision.MeasureEdges", DisplayName = "边缘线圆测量", Category = "5.Vision/ImageBuffer")]
public sealed class MeasureVisionEdgesNodeModel : AnalyzeVisionFrameNodeModel, IWorkflowVisionAlgorithmNode
{
    /// <summary>节点专属实现选择；旧配方缺字段时保持原实现。</summary>
    [System.ComponentModel.Browsable(false)]
    public VisionAlgorithmSelection Algorithm { get; set; } = new() { ImplementationId = "opencv.edges" };

    /// <inheritdoc/>
    public IReadOnlyList<WorkflowVisionAlgorithmSlot> GetAlgorithmSlots() => new[] { new WorkflowVisionAlgorithmSlot("algorithm", typeof(IEdgeMeasurer), Algorithm) };

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
        var result = WorkflowVisionAlgorithmInvocation.Invoke(context, node.Algorithm, "opencv.edges",
            (IEdgeMeasurer algorithm) => algorithm.Measure(frame, range.Bounds,
                new EdgeMeasurementOptions(node.Model, node.LowThreshold, node.HighThreshold, node.MinimumPoints), cancellationToken, range.Region), cancellationToken);
        if (coordinates is not null) result = result.InCoordinates(coordinates);
        var projection = WorkflowVisionFrameScope.Stage(context, frame, result);
        return ValueTask.FromResult(NodeExecutionResult.Continue(output: result, projection: projection));
    }
}

/// <summary>原图与上游模板帧的平移定位，不支持旋转/尺度搜索。</summary>
[WorkflowNode("Vision.LocateTemplate", DisplayName = "平移模板定位", Category = "5.Vision/Location")]
public sealed class LocateVisionTemplateNodeModel : AnalyzeVisionFrameNodeModel, IWorkflowVisionAlgorithmNode, IWorkflowVisionTemplateNode
{
    /// <summary>旧配方默认图像绑定；节点内制作后选择资源。</summary>
    [WorkflowProperty("模板来源", "资源模式使用节点内制作的模型；图像绑定模式保留动态模板。", Category = "模板")]
    public EWorkflowVisionTemplateSource TemplateSource { get; set; }
    /// <inheritdoc/>
    [System.ComponentModel.Browsable(false)]
    public VisionAlgorithmSelection ModelAlgorithm { get; set; } = new() { ImplementationId = "opencv.template-model" };
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
    /// <summary>已发布清单引用，可使用配方相对路径或resource:。</summary>
    [System.ComponentModel.Browsable(false), System.Text.Json.Serialization.JsonIgnore]
    [WorkflowProperty("模板资源", "确定版本的manifest.json；双击节点展开模板制作。", Category = "模板")]
    [WorkflowPropertyVisibleWhen(nameof(TemplateSource), nameof(EWorkflowVisionTemplateSource.Resource))]
    [WorkflowPropertyEditor(WorkflowPropertyEditorKeys.FilePath)]
    public string TemplateResourcePath { get => ModelAlgorithm.Settings.GetValueOrDefault("templatePath", ""); set => ModelAlgorithm.Settings["templatePath"] = value; }
    /// <inheritdoc/>
    [System.ComponentModel.Browsable(false), System.Text.Json.Serialization.JsonIgnore]
    public bool RequiresPoseSearch => false;
    /// <summary>本模板的稳定坐标系定义身份，下游绑定时同时锁定内容签名。</summary>
    [WorkflowProperty("坐标系定义ID", "本模板局部坐标定义，区别于搜索父坐标系。", Category = "定位坐标系")]
    public string CoordinateSystemId { get; set; } = Guid.NewGuid().ToString("N");
    /// <summary>节点选择，旧配方缺字段时显式保持原OpenCV实现。</summary>
    [System.ComponentModel.Browsable(false)]
    public VisionAlgorithmSelection Algorithm { get; set; } = new() { ImplementationId = "opencv.template" };

    /// <summary>属性面板按已安装实现提供选择；完整配置由 Algorithm 持久化。</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    [WorkflowProperty("算法实现", "从已安装的模板定位实现中选择。切换实现后需重新确认专有配置。", Category = "算法")]
    [WorkflowPropertyVisibleWhen(nameof(TemplateSource), nameof(EWorkflowVisionTemplateSource.ImageBinding))]
    [WorkflowPropertyEditor(WorkflowPropertyEditorKeys.VisionTemplateAlgorithm)]
    public string ImplementationId
    {
        get => Algorithm.ImplementationId;
        set
        {
            if (!string.Equals(value, Algorithm.ImplementationId, StringComparison.Ordinal)
                && (Algorithm.SettingsVersion != 1 || Algorithm.Settings.Count != 0 || Algorithm.Dependencies.Count != 0))
                throw new InvalidOperationException("请先清空原实现的初始化参数和依赖选择，再切换实现。");
            Algorithm.ImplementationId = value;
        }
    }

    /// <summary>实现专有的初始化参数，以完整 JSON 编辑和保存。</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    [WorkflowProperty("初始化参数", "只用于创建算法资源；检测阈值仍由标准节点属性传入。", Category = "算法")]
    [WorkflowPropertyVisibleWhen(nameof(TemplateSource), nameof(EWorkflowVisionTemplateSource.ImageBinding))]
    public Dictionary<string, string> InitializationSettings { get => Algorithm.Settings; set => Algorithm.Settings = value; }

    /// <summary>初始化参数结构版本。</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    [WorkflowProperty("参数版本", "工厂不支持此版本时，运行前准备会报错。", Category = "算法")]
    [WorkflowPropertyVisibleWhen(nameof(TemplateSource), nameof(EWorkflowVisionTemplateSource.ImageBinding))]
    public int SettingsVersion { get => Algorithm.SettingsVersion; set => Algorithm.SettingsVersion = value; }

    /// <inheritdoc/>
    public IReadOnlyList<WorkflowVisionAlgorithmSlot> GetAlgorithmSlots() => TemplateSource == EWorkflowVisionTemplateSource.Resource
        ? new[] { WorkflowVisionTemplateResource.Slot(this) } : new[] { new WorkflowVisionAlgorithmSlot("locator", typeof(ITemplateLocator), Algorithm) };
    /// <inheritdoc/>
    public override string NodeType => "Vision.LocateTemplate";
    /// <inheritdoc/>
    public override EWorkflowVisionRange RangeCapability => EWorkflowVisionRange.Region;
    /// <summary>模板整图绑定；模板文件可使用独立LoadFile节点。</summary>
    [WorkflowProperty("模板图像", "上游模板帧，整张作为模板；必须不大于搜索区。", Category = "定位")]
    [WorkflowPropertyVisibleWhen(nameof(TemplateSource), nameof(EWorkflowVisionTemplateSource.ImageBinding))]
    public WorkflowInput<ImageFrame> Template { get; set; } = WorkflowInput<ImageFrame>.FromLiteral(null);
    /// <summary>1减灰度归一化均方差阈值。</summary>
    public double MinimumScore { get; set; } = .9;
    /// <inheritdoc/>
    public override IReadOnlyList<string> ValidateConfiguration()
    {
        var errors = base.ValidateConfiguration().ToList();
        if (string.IsNullOrWhiteSpace(CoordinateSystemId)) errors.Add("模板坐标系定义ID不能为空。");
        errors.AddRange(WorkflowVisionTemplateResource.Validate(this));
        if (TemplateSource == EWorkflowVisionTemplateSource.ImageBinding && (Template is null || Template.Source != WorkflowValueSource.Binding || Template.Binding is null || Template.LiteralValue is not null))
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
        if (node.TemplateSource == EWorkflowVisionTemplateSource.Resource)
        {
            var modelRange = node.ResolveRange(frame, context, cancellationToken); var parent = modelRange.Coordinates;
            var options = new TemplatePoseOptions(new[] { parent?.RotationRadians ?? 0 }, new[] { parent?.SimilarityScale ?? 1 }, node.MinimumScore);
            var pose = WorkflowVisionTemplateResource.Match(node, context, frame, modelRange.Bounds, modelRange.Region, parent, options, cancellationToken);
            var modelResult = TemplateLocationResult.FromModelPose(pose);
            return ValueTask.FromResult(NodeExecutionResult.Continue(output: modelResult, projection: WorkflowVisionFrameScope.Stage(context, frame, modelResult)));
        }
        var template = context.ResolveInput(node.Template) ?? throw new InvalidOperationException("模板帧为空。");
        var range = node.ResolveRange(frame, context, cancellationToken);
        TemplateLocationResult Locate(ITemplateLocator locator) => locator.Locate(frame, range.Bounds, template,
            new PixelBounds(0, 0, template.Image.Info.Width, template.Image.Info.Height), node.MinimumScore, cancellationToken, range.Region, range.Coordinates);
        var result = context.Services.GetService(typeof(IWorkflowVisionAlgorithmBindings)) is IWorkflowVisionAlgorithmBindings bindings
            ? bindings.Invoke<ITemplateLocator, TemplateLocationResult>(context, "locator", Locate, cancellationToken)
            : Locate(GetLegacyLocator(node, context));
        if (result.TemplateFrameId != template.FrameId) throw new InvalidOperationException("定位结果模板身份与本次输入不一致。");
        result = result.InCoordinateSystem(node.CoordinateSystemId, frame, template, cancellationToken);
        var projection = WorkflowVisionFrameScope.Stage(context, frame, result);
        return ValueTask.FromResult(NodeExecutionResult.Continue(output: result, projection: projection));
    }

    private static ITemplateLocator GetLegacyLocator(LocateVisionTemplateNodeModel node, IWorkflowNodeExecutionContext context)
    {
        if (node.Algorithm.ImplementationId != "opencv.template" || node.Algorithm.SettingsVersion != 1
            || node.Algorithm.Settings.Count != 0 || node.Algorithm.Dependencies.Count != 0)
            throw new InvalidOperationException("宿主未注册算法绑定服务，无法应用节点的引擎选择或初始化配置。");
        return context.GetRequiredCapability<ITemplateLocator>();
    }
}
