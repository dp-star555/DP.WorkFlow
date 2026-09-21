using DP.Vision;
using DP.Vision.Acquisition;

namespace DP.WorkFlow.Tests;

/// <summary>
/// 阶段C验收：采集节点只保存逻辑SourceId，由机器配置决定实际Provider。
/// 同一流程文档在不同机器的Source映射下应使用不同Provider；源不存在时必须在首节点执行前失败。
/// </summary>
public sealed class VisionAcquisitionNodeTests
{
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
        // 只注册IVisionAcquisition：如果采集节点仍声明ICameraCapture，能力预检会直接失败。
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
        EVisionAcquisitionMode mode = EVisionAcquisitionMode.OnDemand) =>
        new(sourceId, providerId, policy, isAvailable, diagnostic, mode);

    [Fact]
    public void 采集节点旧文档迁移为逻辑源与带单位参数()
    {
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
        var node = Assert.IsType<CaptureVisionFrameNodeModel>(Assert.Single(loaded.Document.CanvasProjection.Nodes).Node);

        Assert.Equal("GigEVision2|cam-top", node.Source!.SourceId);
        Assert.Equal(1200, node.ExposureMicroseconds);
        // 旧实现用0表示"保持设备当前设置"；新契约必须留空，不能把0当成有效物理量。
        Assert.Null(node.GainDecibels);
        Assert.Equal(EVisionTriggerMode.External, node.TriggerMode);
        Assert.Contains(loaded.Migration.Warnings, warning => warning.Contains("CameraId"));
    }

    private sealed class Rig : IDisposable
    {
        public Rig(
            string providerId = "dp.vision.halcon",
            IEnumerable<WorkflowVisionSourceInfo>? sources = null,
            bool registerSourceCatalog = true,
            bool registerRunScopeOwner = false,
            double? exposureMicroseconds = null,
            double? gainDecibels = null)
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
            var capture = new CaptureVisionFrameNodeModel
            {
                Id = "capture",
                Source = new VisionSourceReference("Camera.Top"),
                ExposureMicroseconds = exposureMicroseconds,
                GainDecibels = gainDecibels
            };
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
