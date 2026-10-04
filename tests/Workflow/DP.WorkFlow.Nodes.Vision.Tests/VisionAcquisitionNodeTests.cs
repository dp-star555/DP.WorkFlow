using DP.Vision;
using DP.Vision.Acquisition;

namespace DP.WorkFlow.Tests;

/// <summary>
/// 阶段C验收：采集节点只保存逻辑SourceId，由机器配置决定实际Provider。
/// 同一流程文档在不同机器的Source映射下应使用不同Provider；源不存在时必须在首节点执行前失败。
/// </summary>
public sealed class VisionAcquisitionNodeTests
{
    [Theory]
    [InlineData(EWorkflowVisionImageSource.AreaCamera, EVisionAcquisitionKind.AreaScan)]
    [InlineData(EWorkflowVisionImageSource.LineCamera, EVisionAcquisitionKind.LineScan)]
    public async Task 统一相机入口只需要采集能力并保留采集帧身份(EWorkflowVisionImageSource mode, EVisionAcquisitionKind kind)
    {
        using var rig = new Rig(sources: [Source("Camera.Top", "dp.vision.halcon", kind: kind)],
            captureNode: new AcquireVisionImageNodeModel { Id = "capture", SourceMode = mode, Source = new("Camera.Top"),
                Algorithm = new() { ImplementationId = "missing.file-decoder" } });
        var result = await rig.Host.RunAsync();
        Assert.True(result.Success, result.Message);
        Assert.Equal(1, rig.Acquisition.CaptureCount);
        var frame = Assert.IsType<ImageFrame>(rig.Host.Engine!.RunState.NodeOutputs.Single(output => output.NodeId == "capture").Value);
        Assert.Equal("capture-1", frame.FrameId);
        using var preview = rig.FrameScope.Capture("capture");
        Assert.Equal(frame.FrameId, preview!.Frame.FrameId);
    }

    [Theory]
    [InlineData(EWorkflowVisionImageSource.AreaCamera, EVisionAcquisitionKind.LineScan)]
    [InlineData(EWorkflowVisionImageSource.LineCamera, EVisionAcquisitionKind.AreaScan)]
    public async Task 统一相机入口在首节点前拒绝形态不匹配(EWorkflowVisionImageSource mode, EVisionAcquisitionKind kind)
    {
        using var rig = new Rig(sources: [Source("Camera.Top", "dp.vision.halcon", kind: kind)],
            captureNode: new AcquireVisionImageNodeModel { Id = "capture", SourceMode = mode, Source = new("Camera.Top") });
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => rig.Host.RunAsync());
        Assert.Contains("禁止跨类型绑定", error.Message);
        Assert.Equal(0, rig.Acquisition.CaptureCount);
        Assert.Equal(0, rig.Sink.ExecutionCount);
    }

    [Fact]
    public async Task 统一回调入口在首节点前拒绝曝光覆盖()
    {
        using var rig = new Rig(sources: [Buffered("Camera.Top", "dp.vision.basler")],
            captureNode: new AcquireVisionImageNodeModel { Id = "capture", SourceMode = EWorkflowVisionImageSource.AreaCamera,
                Source = new("Camera.Top"), ExposureMicroseconds = 100 });
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => rig.Host.RunAsync());
        Assert.Contains("不支持节点级曝光", error.Message);
        Assert.Equal(0, rig.Acquisition.CaptureCount);
    }
    [Fact]
    public async Task 采集节点在首节点前拒绝未发布的源()
    {
        using var rig = new Rig(sources: new[] { Source("Camera.Other", "dp.vision.other") });

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => rig.Host.RunAsync());

        Assert.Contains("Camera.Top", failure.Message);
        Assert.Contains("未在当前机器配置中发布", failure.Message);
        // 首节点之前就失败：采集没发生，下游节点也没执行。
        Assert.Equal(0, rig.Acquisition.CaptureCount);
        Assert.Equal(0, rig.Sink.ExecutionCount);
    }

    [Fact]
    public async Task 宿主未发布源目录时采集节点在首节点前失败()
    {
        using var rig = new Rig(registerSourceCatalog: false);

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => rig.Host.RunAsync());

        Assert.Contains("IWorkflowVisionSourceCatalog", failure.Message);
        Assert.Equal(0, rig.Acquisition.CaptureCount);
        Assert.Equal(0, rig.Sink.ExecutionCount);
    }

    [Fact]
    public async Task 不可用的源在首节点前给出Provider级诊断()
    {
        using var rig = new Rig(sources: new[]
        {
            Source("Camera.Top", "dp.vision.halcon", isAvailable: false, diagnostic: "HALCON SDK 未部署。")
        });

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => rig.Host.RunAsync());

        Assert.Contains("dp.vision.halcon", failure.Message);
        Assert.Contains("HALCON SDK 未部署", failure.Message);
        Assert.Equal(0, rig.Acquisition.CaptureCount);
    }

    [Fact]
    public async Task 主动采集源配置为ExclusiveRun时在首节点前明确拒绝()
    {
        using var rig = new Rig(sources: new[]
        {
            Source("Camera.Top", "dp.vision.halcon", policy: EVisionSourceSharingPolicy.ExclusiveRun)
        });

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => rig.Host.RunAsync());

        // ExclusiveRun 只对"相机长期布防"有意义；主动采集源逐次开关设备，靠操作级互斥协调。
        Assert.Contains("ExclusiveRun", failure.Message);
        Assert.Contains("主动采集源", failure.Message);
        Assert.Equal(0, rig.Acquisition.CaptureCount);
    }

    [Fact]
    public async Task 同一流程文档在不同机器映射下使用不同Provider()
    {
        // 同一份节点配置跑两次，只换机器侧的Provider映射：工作流文档不需要任何改动。
        using var first = new Rig(providerId: "dp.vision.halcon", sources: new[] { Source("Camera.Top", "dp.vision.halcon") });
        var firstResult = await first.Host.RunAsync();

        using var second = new Rig(providerId: "dp.vision.hikvision", sources: new[] { Source("Camera.Top", "dp.vision.hikvision") });
        var secondResult = await second.Host.RunAsync();

        Assert.True(firstResult.Success, firstResult.Message);
        Assert.True(secondResult.Success, secondResult.Message);
        Assert.Equal("Camera.Top", first.Acquisition.LastSourceId);
        Assert.Equal("Camera.Top", second.Acquisition.LastSourceId);

        // 来源事实进入本运行的Trace：操作员能看到实际是哪台Provider完成的采集。
        Assert.Contains(first.Host.Engine!.GetTraceBatch().Entries,
            entry => entry.Step == "Vision.Capture" && Equals(entry.Data!["ProviderId"], "dp.vision.halcon"));
        Assert.Contains(second.Host.Engine!.GetTraceBatch().Entries,
            entry => entry.Step == "Vision.Capture" && Equals(entry.Data!["ProviderId"], "dp.vision.hikvision"));
    }

    [Fact]
    public async Task 采集输出保留CaptureId作为帧身份()
    {
        using var rig = new Rig(sources: new[] { Source("Camera.Top", "dp.vision.halcon") });

        var result = await rig.Host.RunAsync();

        Assert.True(result.Success, result.Message);
        var output = Assert.Single(rig.Host.Engine!.RunState.NodeOutputs, item => item.NodeId == "capture");
        var frame = Assert.IsType<ImageFrame>(output.Value);
        // 不能用新GUID重新编号，否则预览、Trace和下游看到的帧身份会对不上采集事实。
        Assert.Equal("capture-1", frame.FrameId);
    }

    [Fact]
    public async Task 采集节点只要求中立采集入口而不是裸相机能力()
    {
        // 只注册IVisionAcquisition：采集节点一旦另外声明任何"直接抓一帧"的能力契约，能力预检就会失败。
        // 旧 ICameraCapture 已经删除，这条断言守住的是"采集只有 Provider 插件一条路径"这个不变式。
        using var rig = new Rig(sources: new[] { Source("Camera.Top", "dp.vision.halcon") });

        var result = await rig.Host.RunAsync();

        Assert.True(result.Success, result.Message);
        Assert.Equal(1, rig.Acquisition.CaptureCount);
    }

    [Fact]
    public async Task 缓冲源缺少根运行作用域所有者时在首节点前失败()
    {
        // 外部回调缓冲源需要"哪一根运行的帧"这一界定；没有作用域所有者就不会建立采集代次，
        // 回调帧永远无法领取。与其让节点在领取处超时，不如在首节点之前说清楚缺什么。
        using var rig = new Rig(
            sources: new[] { Buffered("Camera.Top", "dp.vision.basler") },
            registerRunScopeOwner: false);

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => rig.Host.RunAsync());

        Assert.Contains("IWorkflowRunScopeOwner", failure.Message);
        Assert.Contains("Camera.Top", failure.Message);
        Assert.Equal(0, rig.Acquisition.CaptureCount);
        Assert.Equal(0, rig.Sink.ExecutionCount);
    }

    [Fact]
    public async Task 缓冲源在首节点前拒绝节点级曝光覆盖()
    {
        // 相机正在长期布防出图；改写曝光会让已在途的帧参数不一致，因此运行前就拒绝，
        // 而不是等设备已经打开、节点开始执行时才失败。
        using var rig = new Rig(
            sources: new[] { Buffered("Camera.Top", "dp.vision.basler") },
            exposureMicroseconds: 1500);

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => rig.Host.RunAsync());

        Assert.Contains("BufferedExternal", failure.Message);
        Assert.Contains("曝光/增益", failure.Message);
        Assert.Equal(0, rig.Acquisition.CaptureCount);
        Assert.Equal(0, rig.Sink.ExecutionCount);
    }

    [Fact]
    public async Task 注册运行作用域所有者后缓冲源可以运行()
    {
        // 对照用例：证明上面两条不是"一律拒绝"，而是确实在校验缺失项。
        using var rig = new Rig(
            sources: new[] { Buffered("Camera.Top", "dp.vision.basler") },
            registerRunScopeOwner: true);

        var result = await rig.Host.RunAsync();

        Assert.True(result.Success, result.Message);
        Assert.Equal(1, rig.RunScope.BeginCount);
        Assert.Equal(1, rig.RunScope.DisposeCount);
        Assert.Equal(1, rig.Acquisition.CaptureCount);
        Assert.Equal("Camera.Top", rig.Acquisition.LastSourceId);
    }

    /// <summary>外部回调缓冲源：相机长期布防，帧由有界队列待领取。</summary>
    private static WorkflowVisionSourceInfo Buffered(string sourceId, string providerId) =>
        Source(sourceId, providerId, mode: EVisionAcquisitionMode.BufferedExternal);

    private static WorkflowVisionSourceInfo Source(
        string sourceId,
        string providerId,
        EVisionSourceSharingPolicy policy = EVisionSourceSharingPolicy.ExclusiveOperation,
        bool isAvailable = true,
        string? diagnostic = null,
        EVisionAcquisitionMode mode = EVisionAcquisitionMode.OnDemand,
        EVisionAcquisitionKind? kind = null) =>
        new(sourceId, providerId, policy, isAvailable, diagnostic, mode, kind);

    /// <summary>面阵源：形态已声明，面阵节点可绑定、线扫节点必须拒绝。</summary>
    private static WorkflowVisionSourceInfo AreaSource(string sourceId, string providerId) =>
        Source(sourceId, providerId, kind: EVisionAcquisitionKind.AreaScan);

    /// <summary>线扫源：形态已声明，线扫节点可绑定、面阵节点必须拒绝。</summary>
    private static WorkflowVisionSourceInfo LineSource(string sourceId, string providerId) =>
        Source(sourceId, providerId, kind: EVisionAcquisitionKind.LineScan);

    [Fact]
    public async Task 线扫节点输出与面阵节点相同的中立ImageFrame()
    {
        // 两个节点只在参数绑定和候选过滤上分开；执行主干和输出必须完全一致，
        // 否则同一条流程会因为"选了面阵还是线扫"而产生不同的下游语义。
        using var rig = new Rig(
            sources: new[] { LineSource("Camera.Line", "dp.vision.halcon") },
            captureNode: new CaptureLineScanFrameNodeModel
            { Id = "capture", Source = new VisionSourceReference("Camera.Line") });

        var result = await rig.Host.RunAsync();

        Assert.True(result.Success, result.Message);
        var output = Assert.Single(rig.Host.Engine!.RunState.NodeOutputs, item => item.NodeId == "capture");
        var frame = Assert.IsType<ImageFrame>(output.Value);
        Assert.Equal("capture-1", frame.FrameId);
        Assert.Equal(1, rig.Acquisition.CaptureCount);
        Assert.Equal("Camera.Line", rig.Acquisition.LastSourceId);
    }

    [Fact]
    public async Task 面阵节点绑定线扫源时在首节点前拒绝()
    {
        using var rig = new Rig(
            sources: new[] { LineSource("Camera.Line", "dp.vision.halcon") },
            captureNode: new CaptureAreaFrameNodeModel
            { Id = "capture", Source = new VisionSourceReference("Camera.Line") });

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => rig.Host.RunAsync());

        Assert.Contains("只能绑定面阵源", failure.Message);
        Assert.Contains("线扫源", failure.Message);
        Assert.Contains("Camera.Line", failure.Message);
        // 跨类型绑定在运行前就拒绝：设备没有被打开，下游也没有执行。
        Assert.Equal(0, rig.Acquisition.CaptureCount);
        Assert.Equal(0, rig.Sink.ExecutionCount);
    }

    [Fact]
    public async Task 线扫节点绑定面阵源时在首节点前拒绝()
    {
        using var rig = new Rig(
            sources: new[] { AreaSource("Camera.Top", "dp.vision.halcon") },
            captureNode: new CaptureLineScanFrameNodeModel
            { Id = "capture", Source = new VisionSourceReference("Camera.Top") });

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => rig.Host.RunAsync());

        Assert.Contains("只能绑定线扫源", failure.Message);
        Assert.Contains("面阵源", failure.Message);
        Assert.Equal(0, rig.Acquisition.CaptureCount);
    }

    [Fact]
    public async Task 形态未声明的源不做类型拒绝()
    {
        // 宿主没有发布可判定的形态时不猜测：面阵节点仍然可以绑定，
        // 否则V1组合或对应Type未安装的机器会连既有流程都跑不起来。
        using var rig = new Rig(sources: new[] { Source("Camera.Top", "dp.vision.halcon") });

        var result = await rig.Host.RunAsync();

        Assert.True(result.Success, result.Message);
        Assert.Equal(1, rig.Acquisition.CaptureCount);
    }

    [Fact]
    public void 旧采集节点类型未注册时按未知节点保真保留配置()
    {
        // V2 用面阵/线扫两个强类型节点取代了旧的 Vision.CaptureFrame，不再提供兼容迁移器。
        // 旧文档必须仍然能打开：节点降级为 Unknown 并逐字保留原始配置，
        // 由用户自行改成新节点，而不是在持久化层猜测"它应该是面阵还是线扫"。
        var catalog = new WorkflowNodeCatalog().RegisterImageNodes();
        var store = new DP.WorkFlow.Persistence.Json.WorkflowDocumentJsonStore(catalog);
        const string json = """
        {
          "SchemaVersion": 4,
          "Name": "旧采集流程",
          "EntryNodeId": "capture",
          "Nodes": [
            {
              "Id": "capture",
              "Title": "采集",
              "NodeType": "Vision.CaptureFrame",
              "NodeVersion": 1,
              "Config": { "CameraId": "GigEVision2|cam-top", "Exposure": 1200, "Gain": 0, "Triggered": true }
            }
          ],
          "Connections": []
        }
        """;

        var loaded = store.Deserialize(json);
        var node = Assert.Single(loaded.Document.CanvasProjection.Nodes).Node;
        var unknown = Assert.IsType<DP.WorkFlow.Persistence.Json.UnknownWorkflowNodeModel>(node);

        Assert.Equal("Vision.CaptureFrame", unknown.NodeType);
        // 原始字段一个都不能丢：不迁移、不解释、不猜测。
        Assert.Equal("GigEVision2|cam-top", unknown.RawConfig.GetProperty("CameraId").GetString());
        Assert.Equal(1200, unknown.RawConfig.GetProperty("Exposure").GetDouble());
        Assert.Equal(0, unknown.RawConfig.GetProperty("Gain").GetDouble());
        Assert.True(unknown.RawConfig.GetProperty("Triggered").GetBoolean());
        Assert.Contains(loaded.Migration.Warnings, warning => warning.Contains("Vision.CaptureFrame"));
    }

    private sealed class Rig : IDisposable
    {
        public Rig(
            string providerId = "dp.vision.halcon",
            IEnumerable<WorkflowVisionSourceInfo>? sources = null,
            bool registerSourceCatalog = true,
            bool registerRunScopeOwner = false,
            double? exposureMicroseconds = null,
            double? gainDecibels = null,
            IWorkflowNodeModel? captureNode = null)
        {
            Acquisition = new FakeVisionAcquisition(providerId);
            Sink = new SinkHandler();
            RunScope = new FakeRunScopeOwner();

            var catalog = new WorkflowNodeCatalog()
                .RegisterImageNodes()
                .Register(WorkflowNodeDescriptor.Create<SinkNode>(ports: new[] { WorkflowPortDescriptor.Input(), WorkflowPortDescriptor.Output() }));
            var handlers = new WorkflowNodeHandlerCatalog()
                .RegisterImageNodeHandlers()
                .Register(Sink);

            var document = new WorkflowDocument { Name = "采集流程" };
            // 默认用面阵节点；线扫场景由调用方传入已经绑定好线扫源的节点。
            var capture = captureNode ?? new CaptureAreaFrameNodeModel
            { Id = "capture", Source = new VisionSourceReference("Camera.Top") };
            ApplyPhysicalOverrides(capture, exposureMicroseconds, gainDecibels);
            var sink = new SinkNode { Id = "sink" };
            document.EntryNodeId = capture.Id;
            document.CanvasProjection.Nodes.Add(new WorkflowCanvasNode { Node = capture });
            document.CanvasProjection.Nodes.Add(new WorkflowCanvasNode { Node = sink });
            document.CanvasProjection.Connections.Add(new WorkflowConnectionModel
            {
                FromNodeId = capture.Id, FromPort = WorkflowPorts.Success, ToNodeId = sink.Id, ToPort = WorkflowPorts.Input
            });

            var services = new WorkflowServiceProvider()
                .Add<IWorkflowVisionFrameScope>(FrameScope)
                .Add<IWorkflowRunPreparationService>(FrameScope)
                // AR-01 阶段2：退役上一轮租约是运行所有者的独立职责，与示例装配保持一致。
                .Add<IWorkflowRunResourceOwner>(FrameScope)
                .Add<IVisionAcquisition>(Acquisition);
            if (registerSourceCatalog)
                services.Add<IWorkflowVisionSourceCatalog>(new WorkflowVisionSourceCatalog(sources ?? Array.Empty<WorkflowVisionSourceInfo>()));
            // V1-C：只有根宿主解析运行作用域所有者；示例装配同样是"按需注册"。
            if (registerRunScopeOwner)
                services.Add<IWorkflowRunScopeOwner>(RunScope);
            Host = new WorkflowRuntimeHost(catalog, handlers);
            Host.Configure(document, new WorkflowContext(services));
        }

        public WorkflowVisionFrameScope FrameScope { get; } = new();

        public FakeVisionAcquisition Acquisition { get; }

        public SinkHandler Sink { get; }

        public FakeRunScopeOwner RunScope { get; }

        public WorkflowRuntimeHost Host { get; }

        /// <summary>两个节点模型不共享基类，因此这里按实际类型写参数；不覆盖时保持留空语义。</summary>
        private static void ApplyPhysicalOverrides(IWorkflowNodeModel capture, double? exposureMicroseconds, double? gainDecibels)
        {
            if (exposureMicroseconds is null && gainDecibels is null)
                return;
            switch (capture)
            {
                case CaptureAreaFrameNodeModel area:
                    area.ExposureMicroseconds = exposureMicroseconds;
                    area.GainDecibels = gainDecibels;
                    break;
                case CaptureLineScanFrameNodeModel line:
                    line.ExposureMicroseconds = exposureMicroseconds;
                    line.GainDecibels = gainDecibels;
                    break;
            }
        }

        public void Dispose()
        {
            Host.Dispose();
            FrameScope.Dispose();
        }
    }

    /// <summary>只计数的根运行作用域所有者：验证宿主确实在首节点前取得、在运行后退役。</summary>
    private sealed class FakeRunScopeOwner : IWorkflowRunScopeOwner
    {
        public int BeginCount;

        public int DisposeCount;

        public ValueTask<IWorkflowRunScopeLease> BeginRunAsync(Guid runId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            BeginCount++;
            return ValueTask.FromResult<IWorkflowRunScopeLease>(new Lease(this, runId));
        }

        private sealed class Lease(FakeRunScopeOwner owner, Guid runId) : IWorkflowRunScopeLease
        {
            public Guid RunId { get; } = runId;

            public IReadOnlyList<string> OwnedResourceIds => Array.Empty<string>();

            public ValueTask DisposeAsync()
            {
                owner.DisposeCount++;
                return default;
            }
        }
    }

    /// <summary>确定性假采集入口：不打开设备，只记录路由并返回带CaptureId的中立帧。</summary>
    private sealed class FakeVisionAcquisition : IVisionAcquisition
    {
        private readonly string _providerId;

        public FakeVisionAcquisition(string providerId) => _providerId = providerId;

        public int CaptureCount;
        public string? LastSourceId;

        public ValueTask<VisionCapturedImage> CaptureAsync(
            VisionSourceReference source, VisionCaptureRequest request,
            VisionAcquisitionOwner owner, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CaptureCount++;
            LastSourceId = source.SourceId;
            Assert.False(string.IsNullOrWhiteSpace(owner.OwnerId));
            var captureId = "capture-" + CaptureCount.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var image = VisionImage.CopyFrom(new ImageInfo(1, 1, EPixelLayout.Gray8), new byte[] { 9 });
            var frame = new ImageFrame(captureId, image);
            image.Dispose();
            return ValueTask.FromResult(new VisionCapturedImage(frame, new VisionCaptureMetadata(
                captureId, source.SourceId, _providerId, "camera:serial:DEMO0001", DateTimeOffset.UtcNow, null)));
        }
    }

    [WorkflowNode("TestAcquisitionSink")]
    private sealed class SinkNode : WorkflowNodeModel
    {
        public override string NodeType => "TestAcquisitionSink";
    }

    private sealed class SinkHandler : WorkflowNodeHandler<SinkNode>
    {
        public int ExecutionCount;

        protected override ValueTask<NodeExecutionResult> ExecuteAsync(
            SinkNode node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
        {
            ExecutionCount++;
            return ValueTask.FromResult(NodeExecutionResult.Continue(output: ExecutionCount));
        }
    }
}
