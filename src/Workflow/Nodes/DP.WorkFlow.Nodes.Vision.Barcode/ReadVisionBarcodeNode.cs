using System.ComponentModel;
using System.Text.RegularExpressions;
using DP.Vision;
using DP.Vision.Algorithms;

namespace DP.WorkFlow;

/// <summary>读到的码按位置排序的方式。</summary>
public enum EWorkflowBarcodeOrder
{
    /// <summary>保持引擎读出顺序。</summary>
    [Description("读取顺序")]
    Decoded,
    /// <summary>按码位置X从小到大；绑定坐标系时按业务坐标。</summary>
    [Description("从左到右")]
    LeftToRight,
    /// <summary>按码位置Y从小到大；绑定坐标系时按业务坐标。</summary>
    [Description("从上到下")]
    TopToBottom
}

/// <summary>独立条码节点插件；只依赖中立读码契约。</summary>
[WorkflowNode("Vision.ReadBarcode", DisplayName = "读取条码", Category = WorkflowVisionCategories.Recognition)]
public sealed class ReadVisionBarcodeNodeModel : AnalyzeVisionFrameNodeModel, IWorkflowVisionAlgorithmNode
{
    /// <summary>明确的引擎选择；不会自动切换到其他读码器。</summary>
    [Browsable(false)]
    public VisionAlgorithmSelection Algorithm { get; set; } = new() { ImplementationId = "zxing.code" };
    /// <inheritdoc/>
    public override string NodeType => "Vision.ReadBarcode";
    /// <inheritdoc/>
    public override EWorkflowVisionRange RangeCapability => EWorkflowVisionRange.Region;
    /// <inheritdoc/>
    public IReadOnlyList<WorkflowVisionAlgorithmSlot> GetAlgorithmSlots() => new[]
    {
        new WorkflowVisionAlgorithmSlot("reader", typeof(IBarcodeReader), Algorithm,
            Coordinates != null || Regions?.Count > 0 || Mask is { Source: WorkflowValueSource.Binding } ? new[] { "masked" } : Array.Empty<string>())
    };

    /// <summary>只保留的码制。</summary>
    [WorkflowProperty("码制", "只保留这些码制，用分号分隔，例如 QR_CODE;DATA_MATRIX;CODE_128；留空保留全部。", Category = "结果")]
    public string Formats { get; set; } = string.Empty;
    /// <summary>码内容须匹配的正则表达式。</summary>
    [WorkflowProperty("文本规则", "正则表达式，只保留内容匹配的码，例如 ^SN 表示以 SN 开头；留空保留全部。", Category = "结果")]
    public string TextPattern { get; set; } = string.Empty;
    /// <summary>结果排序方式。</summary>
    [WorkflowProperty("排序", "多个码时的输出顺序；“首个文本”取排序后的第一个。", Category = "结果")]
    public EWorkflowBarcodeOrder Order { get; set; }
    /// <summary>期望码数量。</summary>
    [WorkflowProperty("期望个数", "过滤后应读到的码数量，决定“个数合格”；0表示至少读到一个即合格。", Category = "结果")]
    public int ExpectedCount { get; set; } = 1;
    /// <summary>合并文本的分隔符。</summary>
    [WorkflowProperty("分隔符", "“合并文本”中各码内容之间的分隔符。", Category = "结果")]
    public string Separator { get; set; } = ",";

    /// <inheritdoc/>
    public override IReadOnlyList<string> ValidateConfiguration()
    {
        var errors = base.ValidateConfiguration().ToList();
        if (ExpectedCount < 0) errors.Add("期望个数不能为负。");
        if (Separator is null) errors.Add("分隔符不能为空引用。");
        if (Formats is null || TextPattern is null) errors.Add("码制和文本规则不能为空引用。");
        else
            try { _ = new Regex(TextPattern); }
            catch (ArgumentException error) { errors.Add("文本规则不是有效的正则表达式：" + error.Message); }
        return errors;
    }

    /// <summary>按码制、文本规则过滤并排序引擎结果。</summary>
    /// <param name="reading">引擎读取结果。</param><param name="coordinates">节点使用的同帧坐标系；绑定时按业务坐标排序。</param>
    /// <returns>过滤排序后的码。</returns>
    internal IReadOnlyList<BarcodeObservation> Select(BarcodeReadResult reading, VisionCoordinateSystem? coordinates)
    {
        var formats = Formats.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var pattern = TextPattern.Length == 0 ? null : new Regex(TextPattern);
        var codes = reading.Observations.Where(o => (formats.Count == 0 || formats.Contains(o.Format)) && (pattern is null || pattern.IsMatch(o.Text)));
        if (Order == EWorkflowBarcodeOrder.Decoded) return codes.ToArray();
        // 没有定位点的码排在最后，彼此保持读出顺序。
        double Key(BarcodeObservation code)
        {
            if (code.Location is not { } location) return double.PositiveInfinity;
            var point = coordinates?.Locate(location).LocalPosition ?? new Coordinate2D(location.X, location.Y);
            return Order == EWorkflowBarcodeOrder.LeftToRight ? point.X : point.Y;
        }
        return codes.OrderBy(Key).ToArray();
    }
}

/// <summary>同帧的读码观测；未读到和多码均保留事实，“个数合格”只比较期望个数，不代替产品判定。</summary>
public sealed class WorkflowVisionBarcodeFact : IWorkflowVisionFrameFact
{
    /// <summary>包装实际引擎结果、过滤排序后的码及范围来源。</summary>
    public WorkflowVisionBarcodeFact(string frameId, BarcodeReadResult reading, IReadOnlyList<BarcodeObservation> codes,
        int expectedCount, string separator, WorkflowVisionResolvedRange range)
    {
        FrameId = frameId; Reading = reading; Codes = codes; Bounds = range.Bounds; Coordinates = range.Coordinates;
        Texts = codes.Select(code => code.Text).ToArray();
        JoinedText = string.Join(separator, Texts);
        CountMatched = expectedCount == 0 ? codes.Count > 0 : codes.Count == expectedCount;
    }
    /// <inheritdoc/>
    [DisplayName("图像标识")]
    public string FrameId { get; }
    /// <summary>引擎原始读取结果，包含被过滤掉的码。</summary>
    [DisplayName("读码结果")]
    public BarcodeReadResult Reading { get; }
    /// <summary>按码制、文本规则过滤并排序后的码。</summary>
    [DisplayName("码列表")]
    public IReadOnlyList<BarcodeObservation> Codes { get; }
    /// <summary>过滤后的码数量。</summary>
    [DisplayName("个数")]
    public int Count => Codes.Count;
    /// <summary>过滤后的码数量是否等于期望个数。</summary>
    [DisplayName("个数合格")]
    public bool CountMatched { get; }
    /// <summary>排序后第一个码的内容；没有码时为空字符串。</summary>
    [DisplayName("首个文本")]
    public string Text => Texts.Count > 0 ? Texts[0] : string.Empty;
    /// <summary>按排序的各码内容。</summary>
    [DisplayName("文本列表")]
    public IReadOnlyList<string> Texts { get; }
    /// <summary>按排序用分隔符连接的全部内容。</summary>
    [DisplayName("合并文本")]
    public string JoinedText { get; }
    /// <summary>原图搜索范围。</summary>
    [DisplayName("搜索范围")]
    public PixelBounds Bounds { get; }
    /// <summary>使用的同帧定位来源；观测仍为原图坐标。</summary>
    [DisplayName("定位来源")]
    public VisionCoordinateSystem? Coordinates { get; }
    /// <inheritdoc/>
    [DisplayName("摘要")]
    public string Summary => Codes.Count == 0
        ? Reading.Observations.Count == 0 ? "未读到条码" : $"读到 {Reading.Observations.Count} 个码，均不符合码制或文本规则"
        : $"{Codes.Count} 个码{(CountMatched ? "" : "（个数不符）")}：{JoinedText}";
}

/// <summary>只从本轮已提交的 reader 槽位取得引擎，保持资源租约。</summary>
public sealed class ReadVisionBarcodeNodeHandler : WorkflowNodeHandler<ReadVisionBarcodeNodeModel>
{
    /// <inheritdoc/>
    protected override ValueTask<NodeExecutionResult> ExecuteAsync(ReadVisionBarcodeNodeModel node,
        IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
    {
        var frame = context.ResolveInput(node.Frame) ?? throw new InvalidOperationException("输入帧为空。");
        var range = node.ResolveRange(frame, context, cancellationToken);
        var bindings = context.Services.GetService(typeof(IWorkflowVisionAlgorithmBindings)) as IWorkflowVisionAlgorithmBindings
            ?? throw new InvalidOperationException("宿主没有提供节点算法绑定入口。");
        var reading = bindings.Invoke<IBarcodeReader, BarcodeReadResult>(context, "reader", reader =>
        {
            if (range.Region?.AreaPixels == 0) return new BarcodeReadResult(Array.Empty<BarcodeObservation>());
            if (range.Region == null) return reader.Read(frame.Image, range.Bounds, cancellationToken);
            if (reader is not IMaskedBarcodeReader masked) throw new InvalidOperationException("引擎声明 masked 特征，但未提供掩码读码接口。");
            return masked.Read(frame.Image, range.Bounds, range.Region, cancellationToken);
        }, cancellationToken);
        var fact = new WorkflowVisionBarcodeFact(frame.FrameId, reading, node.Select(reading, range.Coordinates), node.ExpectedCount, node.Separator, range);
        return ValueTask.FromResult(NodeExecutionResult.Continue(output: fact, projection: WorkflowVisionFrameScope.Stage(context, frame, fact)));
    }
}

/// <summary>由宿主自动发现的节点入口；与 ZXing 引擎包分别部署。</summary>
public sealed class WorkflowVisionBarcodeModule : IWorkflowRuntimePluginModule
{
    /// <inheritdoc/>
    public string ExtensionId => "workflow.vision.barcode";
    /// <inheritdoc/>
    public void Register(WorkflowRuntimePluginCatalog extensions)
    {
        extensions.Nodes.Register(WorkflowNodeDescriptor.Create<ReadVisionBarcodeNodeModel, WorkflowVisionBarcodeFact>(ports: new[]
        { WorkflowPortDescriptor.Input(maxConnections: int.MaxValue), WorkflowPortDescriptor.Output(WorkflowPorts.Success) }));
        extensions.Handlers.Register(new ReadVisionBarcodeNodeHandler(), WorkflowRuntimeCapabilityRequirement.Require<IBarcodeReader>(),
            WorkflowRuntimeCapabilityRequirement.Require<IWorkflowVisionAlgorithmBindings>(), WorkflowRuntimeCapabilityRequirement.Require<IWorkflowVisionFrameScope>());
    }
}
