using DP.Vision;
using DP.Vision.Algorithms;

namespace DP.WorkFlow;

/// <summary>保持尺寸/坐标系的整图显式预处理。</summary>
[WorkflowNode("Vision.PreprocessImage", DisplayName = "图像预处理", Category = "5.Vision/Processing")]
public sealed class PreprocessVisionImageNodeModel : AnalyzeVisionFrameNodeModel
{
    /// <inheritdoc/>
    public override string NodeType => "Vision.PreprocessImage";
    /// <summary>显式操作，非灰度转换操作拒绝彩色输入。</summary>
    [WorkflowProperty("预处理操作", "Gray16须显式选择Gray16ToGray8并设置增益；不自动归一化。", Category = "预处理")]
    public EImagePreprocessing Operation { get; set; }
    /// <summary>核边长3..63奇数。</summary>
    [WorkflowProperty("核边长", "3..63奇数，仅滤波使用。", Category = "预处理")]
    public int KernelSize { get; set; } = 3;
    /// <summary>Gaussian sigma，像素。</summary>
    [WorkflowProperty("高斯标准差", "Gaussian sigma，单位像素。", Category = "预处理")]
    public double Sigma { get; set; } = 1;
    /// <summary>固定增益。</summary>
    [WorkflowProperty("像素增益", "固定增益；16位满量程映射8位可设1/257。", Category = "预处理")]
    public double Gain { get; set; } = 1;
    /// <summary>固定偏置。</summary>
    [WorkflowProperty("像素偏置", "增益后添加，饱和到0..255。", Category = "预处理")]
    public double Offset { get; set; }
    internal ImagePreprocessingOptions Options() => new(Operation, KernelSize, Sigma, Gain, Offset);
    /// <inheritdoc/>
    public override IReadOnlyList<string> ValidateConfiguration()
    {
        var errors = base.ValidateConfiguration().ToList();
        if (!FullImage) errors.Add("预处理仅支持整图，不忽略矩形配置。");
        try { _ = Options(); } catch (ArgumentException ex) { errors.Add(ex.Message); }
        return errors;
    }
}

/// <summary>新像素产生新FrameId，输出交给有界帧仓。</summary>
public sealed class PreprocessVisionImageNodeHandler : WorkflowNodeHandler<PreprocessVisionImageNodeModel>
{
    /// <inheritdoc/>
    protected override ValueTask<NodeExecutionResult> ExecuteAsync(PreprocessVisionImageNodeModel node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
    {
        var input = context.ResolveInput(node.Frame) ?? throw new InvalidOperationException("输入帧为空。");
        using var pixels = context.GetRequiredCapability<IImagePreprocessor>().Process(input.Image, node.Options(), cancellationToken)
            ?? throw new InvalidOperationException("预处理返回空图像。");
        if (pixels.Info.Width != input.Image.Info.Width || pixels.Info.Height != input.Image.Info.Height || pixels.Info.Layout != EPixelLayout.Gray8)
            throw new InvalidOperationException("预处理返回了错误尺寸或格式。");
        using var frame = new ImageFrame(Guid.NewGuid().ToString("N"), pixels);
        cancellationToken.ThrowIfCancellationRequested();
        var output = context.GetRequiredCapability<IWorkflowVisionFrameScope>().Retain(frame);
        var projection = WorkflowVisionFrameScope.Stage(context, output);
        return ValueTask.FromResult(NodeExecutionResult.Continue(output: output, projection: projection));
    }
}

/// <summary>灰度闭区间产生可绑定精确Region。</summary>
[WorkflowNode("Vision.ThresholdRegion", DisplayName = "阈值分割区域", Category = "5.Vision/Region")]
public sealed class ThresholdVisionRegionNodeModel : AnalyzeVisionFrameNodeModel
{
    /// <inheritdoc/>
    public override string NodeType => "Vision.ThresholdRegion";
    /// <inheritdoc/>
    public override EWorkflowVisionRange RangeCapability => EWorkflowVisionRange.Region;
    /// <summary>包含的灰度下界。</summary>
    [WorkflowProperty("最小灰度", "分割灰度闭区间下界。", Category = "分割")]
    public int MinimumGray { get; set; }
    /// <summary>包含的灰度上界。</summary>
    [WorkflowProperty("最大灰度", "分割灰度闭区间上界。", Category = "分割")]
    public int MaximumGray { get; set; } = 127;
    /// <inheritdoc/>
    public override IReadOnlyList<string> ValidateConfiguration()
    {
        var errors = base.ValidateConfiguration().ToList();
        if (MinimumGray < 0 || MaximumGray > 255 || MinimumGray > MaximumGray) errors.Add("灰度闭区间必须在0..255。");
        return errors;
    }
}

/// <summary>显式Region分割处理器。</summary>
public sealed class ThresholdVisionRegionNodeHandler : WorkflowNodeHandler<ThresholdVisionRegionNodeModel>
{
    /// <inheritdoc/>
    protected override ValueTask<NodeExecutionResult> ExecuteAsync(ThresholdVisionRegionNodeModel node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
    {
        var frame = context.ResolveInput(node.Frame) ?? throw new InvalidOperationException("输入帧为空。");
        var range = node.ResolveRange(frame, context, cancellationToken); var coordinates = range.Coordinates;
        var result = context.GetRequiredCapability<IRegionProcessor>().Threshold(frame, range.Bounds, node.MinimumGray, node.MaximumGray,
            range.Region, cancellationToken) ?? throw new InvalidOperationException("分割返回空结果。");
        result.ValidateFrame(frame);
        if (coordinates is not null) result = result.InCoordinates(coordinates);
        var projection = WorkflowVisionFrameScope.Stage(context, frame, result);
        return ValueTask.FromResult(NodeExecutionResult.Continue(output: result, projection: projection));
    }
}

/// <summary>已分割Region的形态学；输入帧仅用于身份校验和预览。</summary>
[WorkflowNode("Vision.MorphRegion", DisplayName = "区域形态学", Category = "5.Vision/Region")]
public sealed class MorphVisionRegionNodeModel : AnalyzeVisionFrameNodeModel
{
    /// <inheritdoc/>
    public override string NodeType => "Vision.MorphRegion";
    /// <summary>上游Region绑定。</summary>
    [WorkflowProperty("输入区域", "同帧RegionAnalysisResult绑定。", Category = "输入")]
    public WorkflowInput<RegionAnalysisResult> InputRegion { get; set; } = WorkflowInput<RegionAnalysisResult>.FromLiteral(null);
    /// <summary>形态学操作。</summary>
    [WorkflowProperty("形态学操作", "画布外恒为背景；填孔不使用核。", Category = "形态学")]
    public ERegionMorphology Operation { get; set; }
    /// <summary>核形状。</summary>
    [WorkflowProperty("结构元素", "方形、离散椭圆或十字。", Category = "形态学")]
    public ERegionKernel Kernel { get; set; }
    /// <summary>核半径0..31，0恒等；填孔不使用核。</summary>
    [WorkflowProperty("核半径", "0..31，0为恒等；填孔忽略。", Category = "形态学")]
    public int Radius { get; set; } = 1;
    /// <inheritdoc/>
    public override IReadOnlyList<string> ValidateConfiguration()
    {
        var errors = base.ValidateConfiguration().ToList();
        if (InputRegion is null || InputRegion.Source != WorkflowValueSource.Binding || InputRegion.Binding is null || InputRegion.LiteralValue is not null)
            errors.Add("区域输入必须为绑定。");
        if (!FullImage || Radius < 0 || Radius > 31 || !Enum.IsDefined(Operation) || !Enum.IsDefined(Kernel)) errors.Add("无效形态学参数；操作作用于完整输入Region。");
        return errors;
    }
}

/// <summary>保留FrameId的精确Region处理器。</summary>
public sealed class MorphVisionRegionNodeHandler : WorkflowNodeHandler<MorphVisionRegionNodeModel>
{
    /// <inheritdoc/>
    protected override ValueTask<NodeExecutionResult> ExecuteAsync(MorphVisionRegionNodeModel node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
    {
        var frame = context.ResolveInput(node.Frame) ?? throw new InvalidOperationException("输入帧为空。");
        var input = context.ResolveInput(node.InputRegion) ?? throw new InvalidOperationException("输入区域为空。"); input.ValidateFrame(frame);
        var result = context.GetRequiredCapability<IRegionProcessor>().Morphology(input, node.Operation, node.Radius, node.Kernel, cancellationToken)
            ?? throw new InvalidOperationException("形态学返回空结果。");
        result.ValidateFrame(frame);
        if (input.CoordinateSystem is not null) result = result.InCoordinates(input.CoordinateSystem);
        var projection = WorkflowVisionFrameScope.Stage(context, frame, result);
        return ValueTask.FromResult(NodeExecutionResult.Continue(output: result, projection: projection));
    }
}

/// <summary>按明确特征范围筛选连通域，空结果成功。</summary>
[WorkflowNode("Vision.SelectBlobs", DisplayName = "筛选连通域", Category = "5.Vision/Processing")]
public sealed class SelectVisionBlobsNodeModel : AnalyzeVisionFrameNodeModel
{
    /// <inheritdoc/>
    public override string NodeType => "Vision.SelectBlobs";
    /// <summary>上游Blob事实绑定。</summary>
    [WorkflowProperty("连通域输入", "上游BlobAnalysisResult绑定，必须同帧。", Category = "输入")]
    public WorkflowInput<BlobAnalysisResult> Blobs { get; set; } = WorkflowInput<BlobAnalysisResult>.FromLiteral(null);
    /// <summary>最小面积。</summary>
    [WorkflowProperty("最小面积", "包含的最小像素面积。", Category = "筛选")]
    public long MinimumArea { get; set; } = 1;
    /// <summary>最大面积。</summary>
    [WorkflowProperty("最大面积", "包含的最大像素面积。", Category = "筛选")]
    public long MaximumArea { get; set; } = 16777216;
    /// <summary>最小栅格圆度。</summary>
    [WorkflowProperty("最小栅格圆度", "4π面积/栅格周长²，包含孔洞边界；不是亚像素圆度。", Category = "筛选")]
    public double MinimumCircularity { get; set; }
    /// <summary>最大长短轴比。</summary>
    [WorkflowProperty("最大轴比", "面积矩等效椭圆长短轴比。", Category = "筛选")]
    public double MaximumElongation { get; set; } = 1000000000;
    internal BlobSelectionOptions Options() => new(MinimumArea, MaximumArea, MinimumCircularity, MaximumElongation);
    /// <inheritdoc/>
    public override IReadOnlyList<string> ValidateConfiguration()
    {
        var errors = base.ValidateConfiguration().ToList();
        if (Blobs is null || Blobs.Source != WorkflowValueSource.Binding || Blobs.Binding is null || Blobs.LiteralValue is not null) errors.Add("Blob输入必须为绑定。");
        if (!FullImage) errors.Add("筛选作用于输入事实，不接受忽略的矩形配置。");
        try { _ = Options(); } catch (ArgumentException ex) { errors.Add(ex.Message); }
        return errors;
    }
}

/// <summary>只选择事实，不重新分割或生成产品判定。</summary>
public sealed class SelectVisionBlobsNodeHandler : WorkflowNodeHandler<SelectVisionBlobsNodeModel>
{
    /// <inheritdoc/>
    protected override ValueTask<NodeExecutionResult> ExecuteAsync(SelectVisionBlobsNodeModel node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
    {
        var frame = context.ResolveInput(node.Frame) ?? throw new InvalidOperationException("输入帧为空。");
        var input = context.ResolveInput(node.Blobs) ?? throw new InvalidOperationException("输入事实为空。");
        if (input.FrameId != frame.FrameId) throw new InvalidOperationException("Blob与预览帧不一致。");
        var result = context.GetRequiredCapability<IBlobSelector>().Select(input, node.Options(), cancellationToken);
        if (input.CoordinateSystem is not null) result = result.InCoordinates(input.CoordinateSystem);
        var projection = WorkflowVisionFrameScope.Stage(context, frame, result);
        return ValueTask.FromResult(NodeExecutionResult.Continue(output: result, projection: projection));
    }
}
