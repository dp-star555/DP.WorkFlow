using System.ComponentModel;
using System.Text.Json;
using DP.LabelInspection.Contracts;
using DP.Vision;
using DP.Vision.Algorithms;

namespace DP.WorkFlow;

/// <summary>一次明确图像输入的完整标签检测业务节点，不携带通用ROI/坐标系配置。</summary>
[WorkflowNode("LabelInspection.Inspect", DisplayName = "标签检测", Category = "业务检测")]
public sealed class InspectLabelNodeModel : WorkflowNodeModel, IWorkflowNodeConfigurationValidator
{
    /// <inheritdoc/>
    public override string NodeType => "LabelInspection.Inspect";
    /// <summary>生产待检帧，只允许绑定。</summary>
    [WorkflowProperty("输入图像", "绑定上游ImageFrame；支持Gray8/Bgr24，不从配置页的调试图取生产输入。", Category = "输入")]
    public WorkflowInput<ImageFrame> Frame { get; set; } = WorkflowInput<ImageFrame>.FromLiteral(null);
    /// <summary>独立的业务采集周期，不自动使用流程RunId。</summary>
    [WorkflowProperty("采集周期", "由采集/任务侧提供；使用任务数据时必须与其周期一致。未使用任务数据可留空。", Category = "输入")]
    public WorkflowInput<string> CycleId { get; set; } = WorkflowInput<string>.FromLiteral(null);
    /// <summary>本周期外部期望数据，只允许绑定或空。</summary>
    [WorkflowProperty("任务期望数据", "可选绑定TaskDataSnapshot，不从识别结果生成期望值，也不保存本次任务数据。", Category = "输入")]
    public WorkflowInput<TaskDataSnapshot> TaskData { get; set; } = WorkflowInput<TaskDataSnapshot>.FromLiteral(null);
    /// <summary>可选：标签所在的本帧坐标系（来自模板定位后的“构建本帧坐标系”等），ROI随之平移、旋转和缩放。</summary>
    [WorkflowProperty("标签坐标系", "可选。绑定定位节点输出的CoordinateSystem后，配方ROI画在标签坐标下，每帧按坐标系放置到原图并只对ROI范围取样（支持平移/旋转/缩放）；不绑定时配方坐标即原图坐标。", Category = "定位")]
    public WorkflowInput<VisionCoordinateSystem> LabelCoordinates { get; set; } = WorkflowInput<VisionCoordinateSystem>.FromLiteral(null);
    /// <summary>配方左上角在标签坐标系中的X。</summary>
    [WorkflowProperty("标签原点X", "配方图左上角在标签坐标系中的X（坐标系单位）。", Category = "定位")]
    public double LabelOriginX { get; set; }
    /// <summary>配方左上角在标签坐标系中的Y。</summary>
    [WorkflowProperty("标签原点Y", "配方图左上角在标签坐标系中的Y（坐标系单位）。", Category = "定位")]
    public double LabelOriginY { get; set; }
    /// <summary>每个配方像素对应的坐标系单位。</summary>
    [WorkflowProperty("配方像素尺寸", "每个配方像素对应的坐标系单位；坐标系以像素为单位且不缩放时为1。", Category = "定位")]
    public double LabelPixelSize { get; set; } = 1;
    /// <summary>新建配方时的标签宽度（配方像素）；0表示使用预览图尺寸。已有配方以配方尺寸为准。</summary>
    [WorkflowProperty("新建配方宽度", "绑定标签坐标系后，配置页按此尺寸摆正标签预览（配方像素）；0为预览图宽度。已有配方时以配方尺寸为准。", Category = "定位")]
    public int NewLabelWidth { get; set; }
    /// <summary>新建配方时的标签高度（配方像素）；0表示使用预览图尺寸。</summary>
    [WorkflowProperty("新建配方高度", "绑定标签坐标系后，配置页按此尺寸摆正标签预览（配方像素）；0为预览图高度。已有配方时以配方尺寸为准。", Category = "定位")]
    public int NewLabelHeight { get; set; }
    /// <summary>SDK原生配方JSON，使用专用页面导入或捕获；完整保留项目选择、约束和库修订。</summary>
    [Browsable(false)]
    public string RecipeJson { get; set; } = string.Empty;
    /// <summary>资源路径的显式根目录。</summary>
    [WorkflowProperty("资源根目录", "相对流程文件目录解析；未保存流程相对宿主目录。其余资源路径必须位于此目录内。", Category = "标签资源")]
    [WorkflowPropertyEditor(WorkflowPropertyEditorKeys.FolderPath)]
    public string ResourceRoot { get; set; } = ".";
    /// <summary>含libraries/anomaly-libraries等子目录的SDK数据根。</summary>
    [WorkflowProperty("字库/模型库目录", "SDK存储根目录，相对资源根目录；保持配方指定的库ID和修订，不自动升级。", Category = "标签资源")]
    [WorkflowPropertyEditor(WorkflowPropertyEditorKeys.FolderPath)]
    public string DataDirectory { get; set; } = "LabelInspectionData";
    /// <summary>模板模式的整图参考。</summary>
    [WorkflowProperty("参考图", "模板配方需要同尺寸参考图；自由模式不读取此项。相对资源根目录。", Category = "标签资源")]
    [WorkflowPropertyEditor(WorkflowPropertyEditorKeys.FilePath)]
    public string ReferenceImagePath { get; set; } = string.Empty;
    /// <summary>可选OCR模型，不为纯空白检测强制加载。</summary>
    [WorkflowProperty("OCR识别模型", "可选PP-OCR ONNX识别模型（含内嵌字典），相对资源根目录；没有模型仍由SDK报告未覆盖检查。", Category = "标签资源")]
    [WorkflowPropertyEditor(WorkflowPropertyEditorKeys.FilePath)]
    public string RecognitionModelPath { get; set; } = string.Empty;
    /// <summary>方法B的可选CNN骨干。</summary>
    [WorkflowProperty("异常检测骨干", "可选ONNX骨干，相对资源根目录；CNN异常模型需要它，手工特征模型不需要。", Category = "标签资源")]
    [WorkflowPropertyEditor(WorkflowPropertyEditorKeys.FilePath)]
    public string AnomalyBackbonePath { get; set; } = string.Empty;
    /// <summary>配置页作者样张，不作为生产输入。</summary>
    [WorkflowProperty("配置样张", "仅打开配置页时使用；生产始终读取输入图像绑定。相对资源根目录。", Category = "标签资源")]
    [WorkflowPropertyEditor(WorkflowPropertyEditorKeys.FilePath)]
    public string AuthorImagePath { get; set; } = string.Empty;
    /// <summary>同一次检测的ROI并行预算。</summary>
    [WorkflowProperty("ROI最大并行数", "1为串行；同一节点引擎的不同检测调用仍串行。", Category = "执行")]
    public int MaximumParallelRois { get; set; } = 1;
    /// <summary>判定不是OK时走失败出口，便于直接接NG处理支路。</summary>
    [WorkflowProperty("NG走失败出口", "开启后判定不是OK（NG或复判）时，报告照常输出并沿“失败”出口继续，失败支路可绑定本节点报告；请在“输出端口”中启用“失败”并连线，未连线时本路径到此结束。关闭时所有有效报告都走“成功”。", Category = "执行")]
    public bool NgToFailedPort { get; set; }

    /// <inheritdoc/>
    public IReadOnlyList<string> ValidateConfiguration()
    {
        var errors = new List<string>();
        if (!Bound(Frame)) errors.Add("标签输入图像必须绑定上游或公开数据，不能保存运行图像Literal。");
        if (TaskData is null || !(Bound(TaskData) || TaskData.Source == WorkflowValueSource.Literal && TaskData.LiteralValue is null))
            errors.Add("任务期望数据只允许绑定或空Literal。");
        if (CycleId is null) errors.Add("采集周期输入不能为空引用。");
        if (Frame?.Binding is { IsPublicData: false } b && b.NodeId == Id) errors.Add("输入图像不能绑定自身。");
        if (string.IsNullOrWhiteSpace(ResourceRoot) || string.IsNullOrWhiteSpace(DataDirectory)) errors.Add("资源根目录和字库/模型库目录不能为空。");
        if (ReferenceImagePath is null || RecognitionModelPath is null || AnomalyBackbonePath is null || AuthorImagePath is null)
            errors.Add("资源路径不能为空引用，未启用时使用空字符串。");
        if (MaximumParallelRois is < 1 or > 128) errors.Add("ROI最大并行数必须在1至128之间。");
        if (LabelCoordinates is null || !(Bound(LabelCoordinates) || LabelCoordinates.Source == WorkflowValueSource.Literal && LabelCoordinates.LiteralValue is null))
            errors.Add("标签坐标系只允许绑定或空Literal。");
        if (LabelCoordinates?.Binding is { IsPublicData: false } c && c.NodeId == Id) errors.Add("标签坐标系不能绑定自身。");
        if (NewLabelWidth is < 0 or > 12000 || NewLabelHeight is < 0 or > 12000) errors.Add("新建配方宽高必须在0至12000之间。");
        if (!double.IsFinite(LabelOriginX) || !double.IsFinite(LabelOriginY) || !double.IsFinite(LabelPixelSize) || LabelPixelSize <= 0)
            errors.Add("标签原点必须有限，配方像素尺寸必须为正数。");
        if (string.IsNullOrWhiteSpace(RecipeJson) || RecipeJson.Length > 1024 * 1024) errors.Add("请在标签配置页创建/导入配方；配方不能超过1MB。");
        else
        {
            try
            {
                using var json = JsonDocument.Parse(RecipeJson, new JsonDocumentOptions { MaxDepth = 64 });
                if (json.RootElement.ValueKind != JsonValueKind.Object) errors.Add("配方必须是JSON对象。");
            }
            catch (JsonException error) { errors.Add("标签配方JSON无效：" + error.Message); }
        }
        return errors;
    }
    /// <summary>配方像素坐标到原图坐标的放置；未提供坐标系时为空（配方坐标即原图坐标）。</summary>
    /// <param name="system">本帧标签坐标系。</param>
    public InspectionPlacement? Placement(VisionCoordinateSystem? system)
    {
        if (system is null) return null;
        var recipeToImage = system.LocalToImage.Multiply(CoordinateMatrix2D.FromAffine(LabelPixelSize, 0, LabelOriginX, 0, LabelPixelSize, LabelOriginY));
        return new InspectionPlacement(recipeToImage.M11, recipeToImage.M12, recipeToImage.Tx, recipeToImage.M21, recipeToImage.M22, recipeToImage.Ty);
    }
    private static bool Bound<T>(WorkflowInput<T>? input) => input is { Source: WorkflowValueSource.Binding, Binding: not null, LiteralValue: null };
}

/// <summary>宿主提供的已准备标签检测能力，调用期间持有输入和模型租约。</summary>
public interface IWorkflowLabelInspectionService
{
    /// <summary>使用当前计划位置的固定配置；取消/异常不返回旧报告。</summary>
    /// <param name="context">执行上下文。</param><param name="frame">本帧原图。</param>
    /// <param name="cycleId">采集周期。</param><param name="taskData">任务期望数据。</param>
    /// <param name="placement">配方坐标到原图的放置；为空时配方坐标即原图坐标。</param>
    /// <param name="cancellationToken">取消。</param>
    Task<WorkflowLabelInspectionResult> InspectAsync(IWorkflowNodeExecutionContext context, ImageFrame frame,
        string? cycleId, TaskDataSnapshot? taskData, InspectionPlacement? placement, CancellationToken cancellationToken);
}

/// <summary>完整标签业务输出；契约完成、检查覆盖和产品合格保持分离。</summary>
public sealed class WorkflowLabelInspectionResult : IWorkflowVisionFrameFact
{
    /// <summary>关联实际帧及所用资源快照，不重算SDK判定。</summary>
    /// <param name="frameId">输入帧标识。</param><param name="cycleId">采集周期。</param>
    /// <param name="recipeName">配方名称。</param><param name="recipeSha256">配方摘要。</param>
    /// <param name="resourceIdentity">资源快照标识。</param><param name="report">SDK完整报告。</param>
    /// <param name="recipeRegions">本次所用配方的ROI定义，供报告页叠加显示；为空时不画ROI框。</param>
    /// <param name="placement">配方坐标到原图的放置；为空时报告坐标即原图坐标。</param>
    public WorkflowLabelInspectionResult(string frameId, string? cycleId, string recipeName, string recipeSha256,
        string resourceIdentity, InspectionReport report, IReadOnlyList<InspectionRegion>? recipeRegions = null, InspectionPlacement? placement = null)
    {
        Placement = placement;
        FrameId = frameId; CycleId = cycleId; RecipeName = recipeName; RecipeSha256 = recipeSha256;
        ResourceIdentity = resourceIdentity; Report = report ?? throw new ArgumentNullException(nameof(report));
        RecipeRegions = recipeRegions ?? Array.Empty<InspectionRegion>();
    }
    /// <summary>配方坐标到原图的放置；报告与ROI坐标均为配方（标签）坐标，显示到原图时按此换算。</summary>
    [Browsable(false)] public InspectionPlacement? Placement { get; }
    /// <summary>本次所用配方的ROI定义（检测范围），不是检测结果。</summary>
    [Browsable(false)] public IReadOnlyList<InspectionRegion> RecipeRegions { get; }
    [DisplayName("图像标识")] public string FrameId { get; }
    [DisplayName("采集周期")] public string? CycleId { get; }
    [DisplayName("配方名称")] public string RecipeName { get; }
    [DisplayName("配方摘要")] public string RecipeSha256 { get; }
    [DisplayName("资源快照标识")] public string ResourceIdentity { get; }
    [DisplayName("完整报告")] public InspectionReport Report { get; }
    [DisplayName("标签判定")] public EInspectionVerdict Verdict => Report.Verdict;
    [DisplayName("是否合格")] public bool IsQualified => Verdict == EInspectionVerdict.Ok;
    [DisplayName("检测耗时(ms)")] public double ElapsedMilliseconds => Report.ElapsedMilliseconds;
    [DisplayName("ROI结果")] public IReadOnlyList<RegionInspectionResult> Regions => Report.Analysis.Regions;
    [DisplayName("摘要")] public string Summary => $"{RecipeName}：{Verdict}；{Regions.Count}个ROI；{ElapsedMilliseconds:F1}ms";
}

/// <summary>无界面调用；默认产品NG/Review仍使用Success出口输出原报告，开启“NG走失败出口”时走Failed出口。</summary>
public sealed class InspectLabelNodeHandler : WorkflowNodeHandler<InspectLabelNodeModel>
{
    /// <inheritdoc/>
    protected override async ValueTask<NodeExecutionResult> ExecuteAsync(InspectLabelNodeModel node,
        IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var frame = context.ResolveInput(node.Frame) ?? throw new InvalidOperationException("标签输入帧为空。");
        // 绑定了标签坐标系时必须拿到本帧定位；不沿用其它帧的坐标系。
        VisionCoordinateSystem? system = null;
        if (node.LabelCoordinates.Source == WorkflowValueSource.Binding)
        {
            system = context.ResolveInput(node.LabelCoordinates) ?? throw new InvalidOperationException("本帧未定位到标签，不能放置ROI。");
            system.ValidateFrame(frame);
        }
        using var retained = frame.Retain();
        var result = await context.GetRequiredCapability<IWorkflowLabelInspectionService>().InspectAsync(context, retained,
            context.ResolveInput(node.CycleId), context.ResolveInput(node.TaskData), node.Placement(system), cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (result is null || result.FrameId != frame.FrameId) throw new InvalidOperationException("标签报告为空或与输入帧身份不一致。");
        // 报告已正式产生：即使走失败出口也提交输出，失败支路可直接读取判定与报告。
        var port = node.NgToFailedPort && !result.IsQualified ? WorkflowPorts.Failed : WorkflowPorts.Success;
        return NodeExecutionResult.Continue(port, result, WorkflowVisionFrameScope.Stage(context, frame, result));
    }
}

/// <summary>独立业务节点模块；不把标签算法或UI加入Workflow内核。</summary>
public sealed class WorkflowLabelInspectionModule : IWorkflowRuntimePluginModule
{
    /// <inheritdoc/>
    public string ExtensionId => "workflow.label-inspection";
    /// <inheritdoc/>
    public void Register(WorkflowRuntimePluginCatalog extensions)
    {
        ArgumentNullException.ThrowIfNull(extensions);
        extensions.Nodes.Register(WorkflowNodeDescriptor.Create<InspectLabelNodeModel, WorkflowLabelInspectionResult>(ports: new[]
        { WorkflowPortDescriptor.Input(maxConnections: int.MaxValue), WorkflowPortDescriptor.Output(WorkflowPorts.Success), WorkflowPortDescriptor.Failure() }));
        extensions.Handlers.Register(new InspectLabelNodeHandler(), WorkflowRuntimeCapabilityRequirement.Require<IWorkflowLabelInspectionService>(),
            WorkflowRuntimeCapabilityRequirement.Require<IWorkflowVisionFrameScope>());
    }
}
