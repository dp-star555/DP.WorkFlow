using System.ComponentModel;
using DP.Vision;
using DP.Vision.Algorithms;

namespace DP.WorkFlow;

/// <summary>独立条码节点插件；只依赖中立读码契约。</summary>
[WorkflowNode("Vision.ReadBarcode", DisplayName = "读取条码", Category = "5.Vision/读码")]
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
}

/// <summary>同帧的读码观测；未读到和多码均保留事实，不代替产品判定。</summary>
public sealed class WorkflowVisionBarcodeFact : IWorkflowVisionFrameFact
{
    /// <summary>包装实际引擎结果及范围来源。</summary>
    public WorkflowVisionBarcodeFact(string frameId, BarcodeReadResult reading, WorkflowVisionResolvedRange range)
    { FrameId = frameId; Reading = reading; Bounds = range.Bounds; Coordinates = range.Coordinates; }
    /// <inheritdoc/>
    [DisplayName("图像标识")]
    public string FrameId { get; }
    /// <summary>原始码数据、选定范围及完成状态。</summary>
    [DisplayName("读码结果")]
    public BarcodeReadResult Reading { get; }
    /// <summary>原图搜索范围。</summary>
    [DisplayName("搜索范围")]
    public PixelBounds Bounds { get; }
    /// <summary>使用的同帧定位来源；观测仍为原图坐标。</summary>
    [DisplayName("定位来源")]
    public VisionCoordinateSystem? Coordinates { get; }
    /// <inheritdoc/>
    [DisplayName("摘要")]
    public string Summary => Reading.Observations.Count == 0 ? "未读到条码（not_decoded）"
        : string.Join("；", Reading.Observations.Select(o => o.Text)) + (Reading.Observations.Count > 1 ? "（多码，ambiguous）" : "");
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
        var fact = new WorkflowVisionBarcodeFact(frame.FrameId, reading, range);
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
