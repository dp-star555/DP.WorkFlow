using DP.Vision;
using DP.Vision.Algorithms;

namespace DP.WorkFlow;

/// <summary>新版分析节点的强类型帧输入与显式矩形范围配置；不持有活动ROI编辑器。</summary>
public abstract class AnalyzeVisionFrameNodeModel : WorkflowNodeModel, IWorkflowNodeConfigurationValidator, IWorkflowNodeDocumentConfigurationValidator
{
    /// <summary>上游帧绑定；禁止把运行图像作为Literal序列化到配置。</summary>
    [WorkflowProperty("输入图像", "绑定新版文件读取节点的ImageFrame标准输出。", Category = "输入")]
    public WorkflowInput<ImageFrame> Frame { get; set; } = WorkflowInput<ImageFrame>.FromLiteral(null);
    /// <summary>使用完整原图，否则使用下列半开矩形。</summary>
    [WorkflowProperty("全图", "关闭后使用显式矩形；不自动裁剪越界范围。", Category = "范围")]
    public bool FullImage { get; set; } = true;
    /// <summary>原图左边界。</summary>
    [WorkflowProperty("左边界", "原图像素边界X坐标。", Category = "范围")]
    [WorkflowPropertyVisibleWhen(nameof(FullImage), "False")]
    public int X { get; set; }
    /// <summary>原图上边界。</summary>
    [WorkflowProperty("上边界", "原图像素边界Y坐标。", Category = "范围")]
    [WorkflowPropertyVisibleWhen(nameof(FullImage), "False")]
    public int Y { get; set; }
    /// <summary>范围像素宽。</summary>
    [WorkflowProperty("宽度", "矩形像素宽度。", Category = "范围")]
    [WorkflowPropertyVisibleWhen(nameof(FullImage), "False")]
    public int Width { get; set; } = 1;
    /// <summary>范围像素高。</summary>
    [WorkflowProperty("高度", "矩形像素高度。", Category = "范围")]
    [WorkflowPropertyVisibleWhen(nameof(FullImage), "False")]
    public int Height { get; set; } = 1;

    /// <summary>Blob/颜色支持的多形状包含/排除配置，非空时与矩形范围相交。</summary>
    public List<WorkflowVisionRoi> Regions { get; set; } = new();

    /// <summary>可选的上游精确Region事实绑定；与配置ROI取交集，必须同帧。</summary>
    [WorkflowProperty("上游区域掩膜", "可选绑定创建区域、阈值分割或形态学结果；与本节点包含/排除ROI取交集，必须与输入图像同帧。手绘掩膜直接在图像页编辑ROI。", Category = "范围")]
    [WorkflowPropertyVisibleWhen(nameof(SupportsRegionMask), "True")]
    public WorkflowInput<RegionAnalysisResult> Mask { get; set; } = WorkflowInput<RegionAnalysisResult>.FromLiteral(null);

    /// <summary>面积节点可使用精确区域掩膜；其他节点不展示此属性。</summary>
    [System.ComponentModel.Browsable(false), System.Text.Json.Serialization.JsonIgnore]
    public virtual bool SupportsRegionMask => RangeCapability == EWorkflowVisionRange.Region;

    /// <summary>可空业务坐标绑定；启用后Regions/卡尺端点使用局部单位，运行中不改配置。属性面板以“坐标系”下拉编辑（绑定时换算范围），不直接编辑此对象。</summary>
    [System.ComponentModel.Browsable(false)]
    public WorkflowVisionCoordinateBinding? Coordinates { get; set; }

    /// <summary>此节点是否支持显式定位；不支持的算子不能用外接框假装随动。</summary>
    [System.ComponentModel.Browsable(false)]
    public bool SupportsCoordinates => RangeCapability is EWorkflowVisionRange.Region or EWorkflowVisionRange.SamplingBand or EWorkflowVisionRange.GeometryFacts;
    /// <summary>可扩展节点必须显式声明范围执行能力。</summary>
    [System.ComponentModel.Browsable(false)]
    public virtual EWorkflowVisionRange RangeCapability => EWorkflowVisionRange.None;

    /// <summary>为外部领域Handler解析并验证定位；不读取活动编辑器或缓存上一帧。</summary>
    /// <param name="frame">本次输入帧。</param><param name="context">节点执行上下文。</param><returns>未绑定时为空。</returns>
    public VisionCoordinateSystem? ResolveCoordinates(ImageFrame frame, IWorkflowNodeExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(frame); ArgumentNullException.ThrowIfNull(context);
        if (Coordinates is null) return null;
        if (!SupportsCoordinates) throw new InvalidOperationException("此算子不支持定位坐标系。");
        return Coordinates.Resolve(context, frame);
    }

    /// <summary>面积算子的共享入口：验证定位、转换连续ROI、组合孔洞及同帧Mask。</summary>
    /// <param name="frame">本次输入帧。</param><param name="context">执行上下文。</param><param name="token">取消。</param><returns>原图精确范围与定位来源。</returns>
    public WorkflowVisionResolvedRange ResolveRange(ImageFrame frame, IWorkflowNodeExecutionContext context, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(frame); ArgumentNullException.ThrowIfNull(context); token.ThrowIfCancellationRequested();
        if (RangeCapability != EWorkflowVisionRange.Region) throw new InvalidOperationException("此算子不声明面积范围能力。");
        var coordinates = ResolveCoordinates(frame, context);
        if (coordinates is not null && !FullImage) throw new InvalidOperationException("局部ROI不能叠加原图整数矩形。");
        return new WorkflowVisionResolvedRange(Bounds(frame), Region(frame, token, context, coordinates), coordinates);
    }

    private RegionGeometry? Region(ImageFrame frame, CancellationToken token, IWorkflowNodeExecutionContext context, VisionCoordinateSystem? coordinates = null)
    {
        var configured = coordinates is null
            ? (Regions.Count == 0 ? null : WorkflowVisionRoi.Compose(Regions, frame.Image, token))
            : coordinates.ResolveRegion(frame, Regions.Where(r => r.Enabled && !r.Exclude).Select(r => r.ToGeometry()),
                Regions.Where(r => r.Enabled && r.Exclude).Select(r => r.ToGeometry()), token);
        if (Mask.Source == WorkflowValueSource.Literal && Mask.LiteralValue is null) return configured;
        var mask = context.ResolveInput(Mask) ?? throw new InvalidOperationException("绑定区域为空。");
        mask.ValidateFrame(frame);
        return configured is null ? mask.Region : RegionAnalysisResult.Intersect(configured, mask.Region, token);
    }

    /// <summary>制作试匹配的同帧范围，保持运行时ROI和掩码规则。</summary>
    public WorkflowVisionResolvedRange ResolvePreviewRange(ImageFrame frame, VisionCoordinateSystem? coordinates, RegionAnalysisResult? mask, CancellationToken token = default)
    {
        coordinates?.ValidateFrame(frame);
        if (Coordinates != null && coordinates == null) throw new InvalidOperationException("试匹配缺少本帧搜索坐标系。");
        if (coordinates != null && !FullImage) throw new InvalidOperationException("局部ROI不能叠加原图整数矩形。");
        var region = coordinates == null ? (Regions.Count == 0 ? null : WorkflowVisionRoi.Compose(Regions, frame.Image, token))
            : coordinates.ResolveRegion(frame, Regions.Where(r => r.Enabled && !r.Exclude).Select(r => r.ToGeometry()), Regions.Where(r => r.Enabled && r.Exclude).Select(r => r.ToGeometry()), token);
        if (!(Mask.Source == WorkflowValueSource.Literal && Mask.LiteralValue == null))
        {
            if (mask == null) throw new InvalidOperationException("试匹配缺少本帧绑定区域预览。");
            mask.ValidateFrame(frame); region = region == null ? mask.Region : RegionAnalysisResult.Intersect(region, mask.Region, token);
        }
        return new WorkflowVisionResolvedRange(Bounds(frame), region, coordinates);
    }

    /// <inheritdoc/>
    public virtual IReadOnlyList<string> ValidateConfiguration()
    {
        var errors = new List<string>();
        if (!Enum.IsDefined(RangeCapability)) errors.Add("范围能力声明无效。");
        if (Frame is null || Frame.Source != WorkflowValueSource.Binding || Frame.Binding is null || Frame.LiteralValue is not null)
            errors.Add("输入图像必须配置上游/公共数据绑定，不能保存运行帧Literal。");
        if (Coordinates is not null)
        {
            if (!SupportsCoordinates || !Coordinates.IsValid) errors.Add("此算子不支持定位，或坐标来源绑定无效。");
            if (!FullImage) errors.Add("随动范围使用局部Regions，不能再叠加原图整数矩形。");
            if (RangeCapability == EWorkflowVisionRange.Region
                && (Regions is null || !Regions.Any(r => r is { Enabled: true, Exclude: false })))
                errors.Add("随动检测必须显式绘制至少一个包含ROI，不能隐式使用全图。");
        }
        bool hasMask = Mask is not null && !(Mask.Source == WorkflowValueSource.Literal && Mask.LiteralValue is null);
        if (Mask is null || hasMask && (Mask!.Source != WorkflowValueSource.Binding || Mask.Binding is null || Mask.LiteralValue is not null))
            errors.Add("区域掩码只能使用绑定或空Literal表示未启用。");
        if (hasMask && !SupportsRegionMask)
            errors.Add("此算子不支持区域掩码。");
        if (Mask?.Binding is { IsPublicData: false } maskInput && maskInput.NodeId == Id)
            errors.Add("区域掩膜不能绑定自身结果。");
        if (!FullImage && (X < 0 || Y < 0 || Width < 1 || Height < 1 || (long)X + Width > int.MaxValue || (long)Y + Height > int.MaxValue))
            errors.Add("矩形范围必须非负且宽高为正，不能溢出。");
        if (Regions is null || Regions.Count > 512 || Regions.Any(r => r is null || string.IsNullOrWhiteSpace(r.Id))
            || Regions.Select(r => r.Id).Distinct(StringComparer.Ordinal).Count() != Regions.Count)
            errors.Add("ROI集合为空、重复标识或超过预算。");
        else
        {
            if (Regions.Count > 0 && !Regions.Any(r => r.Enabled)) errors.Add("全部ROI已禁用。");
            if (Regions.Count > 0 && RangeCapability != EWorkflowVisionRange.Region)
                errors.Add("此算子不接受面积ROI。");
            foreach (var roi in Regions)
                try { _ = roi.ToGeometry(); } catch (ArgumentException ex) { errors.Add(ex.Message); }
        }
        if (Coordinates?.System.Binding is { IsPublicData: false } coordinateInput && coordinateInput.NodeId == Id)
            errors.Add("定位坐标系不能绑定自身结果。");
        if (!FullImage && RangeCapability is not (EWorkflowVisionRange.Region or EWorkflowVisionRange.Rectangle)) errors.Add("此算子不接受矩形范围。");
        return errors;
    }

    /// <summary>文档级扩展检查；坐标来源的路径和类型由通用绑定编译器验证，不锁定来源内部定义。</summary>
    /// <param name="nodes">当前文档节点。</param><returns>校验信息。</returns>
    public virtual IReadOnlyList<string> ValidateDocumentConfiguration(IReadOnlyList<IWorkflowNodeModel> nodes) => [];

    internal PixelBounds Bounds(ImageFrame frame) => FullImage
        ? new PixelBounds(0, 0, frame.Image.Info.Width, frame.Image.Info.Height)
        : new PixelBounds(X, Y, Width, Height);
}

/// <summary>新版Blob节点，输出中立BlobAnalysisResult。</summary>
[WorkflowNode("Vision.AnalyzeBlobs", DisplayName = "分析连通域", Category = WorkflowVisionCategories.Inspection)]
public sealed class AnalyzeVisionBlobsNodeModel : AnalyzeVisionFrameNodeModel, IWorkflowVisionAlgorithmNode
{
    /// <summary>节点专属实现选择；旧配方缺字段时保持原实现。</summary>
    [System.ComponentModel.Browsable(false)]
    public VisionAlgorithmSelection Algorithm { get; set; } = new() { ImplementationId = "opencv.blob" };

    /// <inheritdoc/>
    public IReadOnlyList<WorkflowVisionAlgorithmSlot> GetAlgorithmSlots() => new[] { new WorkflowVisionAlgorithmSlot("algorithm", typeof(IBlobAnalyzer), Algorithm) };

    /// <inheritdoc/>
    public override string NodeType => "Vision.AnalyzeBlobs";
    /// <inheritdoc/>
    public override EWorkflowVisionRange RangeCapability => EWorkflowVisionRange.Region;
    /// <summary>包含的灰度下界。</summary>
    [WorkflowProperty("最小灰度", "包含的灰度下界，0至255。", Category = "连通域")]
    public int MinimumGray { get; set; }
    /// <summary>包含的灰度上界。</summary>
    [WorkflowProperty("最大灰度", "包含的灰度上界，0至255。", Category = "连通域")]
    public int MaximumGray { get; set; } = 127;
    /// <summary>最小原图像素面积。</summary>
    [WorkflowProperty("最小面积", "保留连通域的最小原图像素面积。", Category = "连通域")]
    public int MinimumArea { get; set; } = 1;
    /// <summary>8连通；关闭为4连通。</summary>
    [WorkflowProperty("八连通", "启用时对角相邻也连通；关闭为四连通。", Category = "连通域")]
    public bool EightConnected { get; set; } = true;
    /// <inheritdoc/>
    public override IReadOnlyList<string> ValidateConfiguration()
    {
        var errors = base.ValidateConfiguration().ToList();
        if (MinimumGray < 0 || MaximumGray > 255 || MinimumGray > MaximumGray || MinimumArea < 1)
            errors.Add("Blob灰度闭区间必须位于0至255，最小面积必须为正。");
        return errors;
    }
}

/// <summary>通过声明的Blob能力产生标准输出。</summary>
public sealed class AnalyzeVisionBlobsNodeHandler : WorkflowNodeHandler<AnalyzeVisionBlobsNodeModel>
{
    /// <inheritdoc/>
    protected override ValueTask<NodeExecutionResult> ExecuteAsync(AnalyzeVisionBlobsNodeModel node,
        IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
    {
        var frame = context.ResolveInput(node.Frame) ?? throw new InvalidOperationException("输入帧为空。");
        var range = node.ResolveRange(frame, context, cancellationToken); var coordinates = range.Coordinates;
        var result = WorkflowVisionAlgorithmInvocation.Invoke(context, node.Algorithm, "opencv.blob",
            (IBlobAnalyzer algorithm) => algorithm.Analyze(frame, range.Bounds,
                new BlobOptions(node.MinimumGray, node.MaximumGray, node.MinimumArea, node.EightConnected), cancellationToken, range.Region), cancellationToken);
        if (coordinates is not null) result = result.InCoordinates(coordinates);
        var projection = WorkflowVisionFrameScope.Stage(context, frame, result);
        return ValueTask.FromResult(NodeExecutionResult.Continue(output: result, projection: projection));
    }
}

/// <summary>新版颜色统计节点，输出中立ColorAnalysisResult，不输出产品OK/NG。</summary>
[WorkflowNode("Vision.AnalyzeColor", DisplayName = "统计RGB颜色", Category = WorkflowVisionCategories.Inspection)]
public sealed class AnalyzeVisionColorNodeModel : AnalyzeVisionFrameNodeModel, IWorkflowVisionAlgorithmNode
{
    /// <summary>节点专属实现选择；旧配方缺字段时保持原实现。</summary>
    [System.ComponentModel.Browsable(false)]
    public VisionAlgorithmSelection Algorithm { get; set; } = new() { ImplementationId = "managed.color" };

    /// <inheritdoc/>
    public IReadOnlyList<WorkflowVisionAlgorithmSlot> GetAlgorithmSlots() => new[] { new WorkflowVisionAlgorithmSlot("algorithm", typeof(IColorAnalyzer), Algorithm) };

    /// <inheritdoc/>
    public override string NodeType => "Vision.AnalyzeColor";
    /// <inheritdoc/>
    public override EWorkflowVisionRange RangeCapability => EWorkflowVisionRange.Region;
}

/// <summary>通过声明的颜色能力产生标准输出。</summary>
public sealed class AnalyzeVisionColorNodeHandler : WorkflowNodeHandler<AnalyzeVisionColorNodeModel>
{
    /// <inheritdoc/>
    protected override ValueTask<NodeExecutionResult> ExecuteAsync(AnalyzeVisionColorNodeModel node,
        IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
    {
        var frame = context.ResolveInput(node.Frame) ?? throw new InvalidOperationException("输入帧为空。");
        var range = node.ResolveRange(frame, context, cancellationToken); var coordinates = range.Coordinates;
        var result = WorkflowVisionAlgorithmInvocation.Invoke(context, node.Algorithm, "managed.color",
            (IColorAnalyzer algorithm) => algorithm.Analyze(frame, range.Bounds, cancellationToken, range.Region), cancellationToken);
        if (coordinates is not null) result = result.InCoordinates(coordinates);
        var projection = WorkflowVisionFrameScope.Stage(context, frame, result);
        return ValueTask.FromResult(NodeExecutionResult.Continue(output: result, projection: projection));
    }
}
