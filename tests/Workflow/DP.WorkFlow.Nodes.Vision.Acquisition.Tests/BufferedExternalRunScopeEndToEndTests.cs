using DP.Vision;
using DP.Vision.Acquisition;

namespace DP.WorkFlow.Tests;

/// <summary>
/// V1-C 验收：把"外部回调缓冲源"的根运行作用域接进真实工作流宿主之后的端到端行为。
/// <para>
/// 与 <c>VisionAcquisitionNodeTests</c> 的分工：那里用假采集入口验证宿主的调用顺序与运行前校验，
/// 这里接真实 <see cref="VisionAcquisitionRuntime"/> 与可控假流式设备，验证"回调早于采集节点"
/// 与"上一根运行的帧不进入下一根运行"这两条只能由两侧接线后才成立的结论。
/// </para>
/// </summary>
public sealed class BufferedExternalRunScopeEndToEndTests
{
    private const string ProviderId = "dp.fake.stream";
    private const string SourceId = "Camera.Top";
    private const string ResourceKey = "camera:serial:STREAM-1";

    /// <summary>
    /// 回调在首节点执行期间到达，采集节点随后直接领取——这正是缓冲模式存在的理由。
    /// <para>
    /// 若宿主没有在首节点之前布防，这次回调会被设备直接丢弃（<c>Emit</c> 返回 false），
    /// 采集节点随后只会超时，因此本用例同时锁住"布防早于首节点"。
    /// </para>
    /// </summary>
    [Fact]
    public async Task 回调早于采集节点到达时采集节点直接领取()
    {
        FakeStreamingDevice? device = null;
        var trigger = new TriggerHandler(() => Device(device), ("f11", 11));
        var sink = new SinkHandler();

        await using var runtime = new FakeStreamingRuntime(created => device = created);
        using var rig = new HostRig(runtime, Linear(trigger.Id, "capture", sink.Id), trigger, sink);

        var result = await rig.Host.RunAsync();

        Assert.True(result.Success, result.Message);
        Assert.True(trigger.DeviceWasStreaming, "首节点执行时设备必须已经布防，否则外部回调只会被丢弃。");
        Assert.True(trigger.AllEmitsAccepted, "宿主没有在首节点之前布防，外部回调被丢弃。");
        Assert.Equal(new long?[] { 11 }, CaptureDeviceSequences(rig));
        Assert.Equal(1, runtime.Device!.StreamStartCount);
        Assert.Empty(runtime.Device.SinkFailures);
        AssertReleasedExactlyOnce(runtime.Device, "f11");
    }

    /// <summary>根运行退役后必须停流：否则回调会继续进入已经没有领取者的队列，等于跨运行泄漏。</summary>
    [Fact]
    public async Task 根运行退役后相机停流且不再交付回调()
    {
        FakeStreamingDevice? device = null;
        var trigger = new TriggerHandler(() => Device(device), ("f21", 21));
        var sink = new SinkHandler();

        await using var runtime = new FakeStreamingRuntime(created => device = created);
        using var rig = new HostRig(runtime, Linear(trigger.Id, "capture", sink.Id), trigger, sink);

        var result = await rig.Host.RunAsync();

        Assert.True(result.Success, result.Message);
        Assert.False(runtime.Device!.IsStreaming, "根运行退役后必须停流。");
        Assert.False(runtime.Device.Emit("after-run", 22), "停流之后不得再交付帧。");
        Assert.Equal(1, runtime.Device.StreamStartCount);
        Assert.Equal(1, runtime.Device.StreamStopCount);
        // 停流之后到达的帧根本没有被创建，因此"交付过的帧"必须全部恰好释放一次。
        AssertReleasedExactlyOnce(runtime.Device, "f21");
    }

    /// <summary>
    /// 上一根运行没有领取的帧不会进入下一根运行，而且同一运行时不会重复打开相机。
    /// <para>
    /// 同一采集运行时连续跑两根根运行：第一根只领走队首那一帧，第二帧留在队列里；
    /// 第二根必须拿到新代次的帧，而不是第一根遗留的那一帧。
    /// </para>
    /// </summary>
    [Fact]
    public async Task 上一根运行未领取的帧不会进入下一根运行()
    {
        FakeStreamingDevice? device = null;
        await using var runtime = new FakeStreamingRuntime(created => device = created);

        var firstTrigger = new TriggerHandler(() => Device(device), ("r1-f1", 1), ("r1-f2", 2));
        var firstSink = new SinkHandler();
        using (var first = new HostRig(runtime, Linear(firstTrigger.Id, "capture", firstSink.Id), firstTrigger, firstSink))
        {
            var firstResult = await first.Host.RunAsync();

            Assert.True(firstResult.Success, firstResult.Message);
            Assert.Equal(new long?[] { 1 }, CaptureDeviceSequences(first));
            Assert.Equal(2, runtime.Device!.EmittedFrames.Count);
        }

        // 退役时未领取的帧必须当场释放，不能留给下一根运行。
        AssertReleasedExactlyOnce(runtime.Device!, "r1-f1", "r1-f2");

        var secondTrigger = new TriggerHandler(() => Device(device), ("r2-f1", 3));
        var secondSink = new SinkHandler();
        using (var second = new HostRig(runtime, Linear(secondTrigger.Id, "capture", secondSink.Id), secondTrigger, secondSink))
        {
            var secondResult = await second.Host.RunAsync();

            Assert.True(secondResult.Success, secondResult.Message);
            // 第二根只认自己代次的帧：既不是第一根遗留的 2，也不是任何更早的帧。
            Assert.Equal(new long?[] { 3 }, CaptureDeviceSequences(second));
        }

        Assert.Equal(2, runtime.Device!.StreamStartCount);
        // 设备在两次布防之间保持打开：重复打开会让真实相机第二次直接失败，并漏掉上一根持有的设备对象。
        Assert.Equal(1, runtime.Provider.OpenCount);
        AssertReleasedExactlyOnce(runtime.Device, "r1-f1", "r1-f2", "r2-f1");
    }

    /// <summary>
    /// 运行准备校验失败时设备根本没有被打开。
    /// <para>
    /// 这锁的是顺序：取得运行作用域（会打开设备并布防）必须晚于准备校验，
    /// 否则一次注定失败的运行也会先把相机抢过来。
    /// </para>
    /// </summary>
    [Fact]
    public async Task 运行准备校验失败时设备没有被打开()
    {
        FakeStreamingDevice? device = null;
        var trigger = new TriggerHandler(() => Device(device), ("f31", 31));
        var sink = new SinkHandler();

        await using var runtime = new FakeStreamingRuntime(created => device = created);
        // 缓冲源不接受节点级曝光覆盖：这是准备阶段就能判定的配置错误。
        using var rig = new HostRig(runtime, Linear(trigger.Id, "capture", sink.Id), trigger, sink, exposureMicroseconds: 1500);

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => rig.Host.RunAsync());

        Assert.Contains("BufferedExternal", failure.Message);
        Assert.Contains("曝光/增益", failure.Message);
        Assert.Equal(0, runtime.Provider.OpenCount);
        Assert.Null(device);
        Assert.Equal(0, trigger.ExecutionCount);
        Assert.Equal(0, sink.ExecutionCount);
    }

    /// <summary>已有根运行持有采集所有权时，本轮必须在首节点之前明确失败，而不是抢走别人的相机。</summary>
    [Fact]
    public async Task 已有根运行持有采集所有权时本轮在首节点前失败()
    {
        FakeStreamingDevice? device = null;
        var trigger = new TriggerHandler(() => Device(device), ("f41", 41));
        var sink = new SinkHandler();

        await using var runtime = new FakeStreamingRuntime(created => device = created);
        using var rig = new HostRig(runtime, Linear(trigger.Id, "capture", sink.Id), trigger, sink);

        // 手工占住所有权，模拟另一根仍在运行的根运行。
        await using var holder = await runtime.Runtime.BeginRunAsync("manual-run", CancellationToken.None);

        var failure = await Assert.ThrowsAsync<VisionResourceConflictException>(() => rig.Host.RunAsync());

        Assert.Contains("manual-run", failure.Message);
        Assert.Equal(0, trigger.ExecutionCount);
        Assert.Equal(0, sink.ExecutionCount);
        // 冲突在取得阶段暴露，本轮没有重新布防。
        Assert.Equal(1, runtime.Device!.StreamStartCount);
    }

    /// <summary>
    /// 故障处置子流程（嵌套运行）不得重新布防，也不得清空父运行的待领取队列。
    /// <para>
    /// 触发节点先推两帧，采集节点只领走第一帧；中间发生一次故障并跑完处置子流程之后，
    /// 第二个采集节点必须还能领到第二帧。嵌套若重新布防，代次推进与重复布防都会当场失败；
    /// 嵌套若清空队列，第二帧就已经被释放，第二个采集节点只能超时。
    /// </para>
    /// </summary>
    [Fact]
    public async Task 处置子流程不清空父运行的待领取队列()
    {
        FakeStreamingDevice? device = null;
        await using var runtime = new FakeStreamingRuntime(created => device = created);

        var treat = new TreatNode { Id = "treat" };
        var subflow = new WorkflowDocument { Name = "处置子流程", EntryNodeId = treat.Id };
        subflow.CanvasProjection.Nodes.Add(new WorkflowCanvasNode { Node = treat });

        var trigger = new TriggerHandler(() => Device(device), ("f51", 51), ("f52", 52));
        var faultNode = new FaultNode { Id = "fault" };
        var faultHandler = new FaultHandler();
        var sink = new SinkHandler();

        var catalog = new WorkflowNodeCatalog()
            .RegisterImageNodes()
            .Register(WorkflowNodeDescriptor.Create<TriggerNode>(ports: new[] { WorkflowPortDescriptor.Output() }))
            .Register(WorkflowNodeDescriptor.Create<TreatNode>(ports: new[] { WorkflowPortDescriptor.Output() }))
            .Register(WorkflowNodeDescriptor.Create<FaultNode>(ports: new[] { WorkflowPortDescriptor.Input(), WorkflowPortDescriptor.Output() }))
            .Register(WorkflowNodeDescriptor.Create<SinkNode>(ports: new[] { WorkflowPortDescriptor.Input(), WorkflowPortDescriptor.Output() }));
        var handlers = new WorkflowNodeHandlerCatalog()
            .RegisterImageNodeHandlers()
            .Register(trigger)
            .Register(new TreatNodeHandler())
            .Register(faultHandler)
            .Register(sink);

        var document = new WorkflowDocument { Name = "主流程" };
        var firstCapture = new CaptureVisionFrameNodeModel { Id = "capture1", Source = new VisionSourceReference(SourceId) };
        var secondCapture = new CaptureVisionFrameNodeModel { Id = "capture2", Source = new VisionSourceReference(SourceId) };
        document.EntryNodeId = trigger.Node.Id;
        foreach (var node in new IWorkflowNodeModel[] { trigger.Node, firstCapture, faultNode, secondCapture, sink.Node })
            document.CanvasProjection.Nodes.Add(new WorkflowCanvasNode { Node = node });
        Connect(document, trigger.Node.Id, firstCapture.Id);
        Connect(document, firstCapture.Id, faultNode.Id);
        Connect(document, faultNode.Id, secondCapture.Id);
        Connect(document, secondCapture.Id, sink.Node.Id);

        using var rig = new HostRig(
            runtime,
            document,
            trigger,
            sink,
            catalog: catalog,
            handlers: handlers,
            recoveryCoordinator: new SubflowRecoveryCoordinator(subflow, catalog, handlers));

        var result = await rig.Host.RunAsync();

        Assert.True(result.Success, result.Message);
        Assert.Equal(2, faultHandler.ExecutionCount);
        // 嵌套运行没有重新布防，也没有清空队列：第二帧仍由第二个采集节点领走。
        Assert.Equal(1, runtime.Device!.StreamStartCount);
        Assert.Equal(new long?[] { 51, 52 }, CaptureDeviceSequences(rig));
        AssertReleasedExactlyOnce(runtime.Device, "f51", "f52");
    }

    /// <summary>读取本次运行 Trace 里每个采集节点实际领取到的设备帧序号，按执行顺序。</summary>
    private static long?[] CaptureDeviceSequences(HostRig rig) =>
        rig.Host.Engine!.GetTraceBatch().Entries
            .Where(entry => entry.Step == "Vision.Capture")
            .Select(entry => (long?)entry.Data!["DeviceSequence"])
            .ToArray();

    /// <summary>
    /// 断言每个交付过的帧恰好释放一次。
    /// <para>
    /// 只断言"至少释放过一次"无法区分漏释放与重复释放；数量相等 + 无多余条目才能同时锁住两个方向。
    /// </para>
    /// </summary>
    private static void AssertReleasedExactlyOnce(FakeStreamingDevice device, params string[] emittedTags)
    {
        Assert.Equal(
            emittedTags.OrderBy(tag => tag, StringComparer.Ordinal).ToArray(),
            device.FrameReleases.Released.OrderBy(tag => tag, StringComparer.Ordinal).ToArray());
    }

    private static FakeStreamingDevice Device(FakeStreamingDevice? device) =>
        device ?? throw new InvalidOperationException("设备尚未打开：宿主没有在首节点之前布防。");

    private static WorkflowDocument Linear(string triggerId, string captureId, string sinkId)
    {
        var document = new WorkflowDocument { Name = "缓冲采集流程" };
        var trigger = new TriggerNode { Id = triggerId };
        var capture = new CaptureVisionFrameNodeModel { Id = captureId, Source = new VisionSourceReference(SourceId) };
        var sink = new SinkNode { Id = sinkId };
        document.EntryNodeId = trigger.Id;
        foreach (var node in new IWorkflowNodeModel[] { trigger, capture, sink })
            document.CanvasProjection.Nodes.Add(new WorkflowCanvasNode { Node = node });
        Connect(document, trigger.Id, capture.Id);
        Connect(document, capture.Id, sink.Id);
        return document;
    }

    private static void Connect(WorkflowDocument document, string from, string to) =>
        document.CanvasProjection.Connections.Add(new WorkflowConnectionModel
        { FromNodeId = from, FromPort = WorkflowPorts.Success, ToNodeId = to, ToPort = WorkflowPorts.Input });

    /// <summary>站点级采集运行时 + 一个可控假流式Provider；可跨多根根运行复用。</summary>
    private sealed class FakeStreamingRuntime : IAsyncDisposable
    {
        public FakeStreamingRuntime(Action<FakeStreamingDevice> onDeviceCreated)
        {
            Provider = new FakeStreamingProvider(ProviderId, ResourceKey, device =>
            {
                Device = device;
                onDeviceCreated(device);
            });
            Runtime = new VisionAcquisitionRuntime(new VisionAcquisitionProviderComposer().Compose(
                new[] { new FakeStreamingProviderModule("m.stream", ProviderId, Provider) },
                new[]
                {
                    new VisionAcquisitionSourceBinding(
                        SourceId,
                        ProviderId,
                        "stream-camera",
                        ResourceKey,
                        EVisionSourceSharingPolicy.ExclusiveRun,
                        EVisionAcquisitionMode.BufferedExternal,
                        new VisionFrameInboxPolicy(
                            capacity: 4,
                            byteBudget: 1024,
                            maximumFrameAge: TimeSpan.FromSeconds(30)))
                }));
        }

        public FakeStreamingProvider Provider { get; }

        public VisionAcquisitionRuntime Runtime { get; }

        /// <summary>最近一次打开的设备；未布防时为空。</summary>
        public FakeStreamingDevice? Device { get; private set; }

        public ValueTask DisposeAsync() => Runtime.DisposeAsync();
    }

    /// <summary>一根根运行的宿主装配：真实宿主 + 真实桥接 + 真实采集运行时。</summary>
    private sealed class HostRig : IDisposable
    {
        private readonly WorkflowVisionFrameScope _frameScope = new();

        public HostRig(
            FakeStreamingRuntime runtime,
            WorkflowDocument document,
            TriggerHandler trigger,
            SinkHandler sink,
            double? exposureMicroseconds = null,
            WorkflowNodeCatalog? catalog = null,
            WorkflowNodeHandlerCatalog? handlers = null,
            IWorkflowFaultRecoveryCoordinator? recoveryCoordinator = null)
        {
            if (exposureMicroseconds is not null)
            {
                foreach (var node in document.CanvasProjection.Nodes
                    .Select(item => item.Node).OfType<CaptureVisionFrameNodeModel>())
                    node.ExposureMicroseconds = exposureMicroseconds;
            }

            catalog ??= new WorkflowNodeCatalog()
                .RegisterImageNodes()
                .Register(WorkflowNodeDescriptor.Create<TriggerNode>(ports: new[] { WorkflowPortDescriptor.Output() }))
                .Register(WorkflowNodeDescriptor.Create<SinkNode>(ports: new[] { WorkflowPortDescriptor.Input(), WorkflowPortDescriptor.Output() }));
            handlers ??= new WorkflowNodeHandlerCatalog()
                .RegisterImageNodeHandlers()
                .Register(trigger)
                .Register(sink);

            var services = new WorkflowServiceProvider()
                .Add<IWorkflowVisionFrameScope>(_frameScope)
                .Add<IWorkflowRunPreparationService>(_frameScope)
                .Add<IWorkflowRunResourceOwner>(_frameScope)
                .Add<IVisionAcquisition>(runtime.Runtime)
                .Add<IWorkflowVisionSourceCatalog>(new WorkflowVisionSourceCatalog(new[]
                {
                    new WorkflowVisionSourceInfo(
                        SourceId,
                        ProviderId,
                        EVisionSourceSharingPolicy.ExclusiveRun,
                        isAvailable: true,
                        diagnostic: null,
                        acquisitionMode: EVisionAcquisitionMode.BufferedExternal)
                }))
                // 生产装配同款：桥接是 Kernel 与采集侧之间唯一的连接点。
                .Add<IWorkflowRunScopeOwner>(new VisionAcquisitionRunScope(runtime.Runtime));
            if (recoveryCoordinator is not null)
                services.Add<IWorkflowFaultRecoveryCoordinator>(recoveryCoordinator);

            Host = new WorkflowRuntimeHost(catalog, handlers);
            Host.Configure(document, new WorkflowContext(services));
        }

        public WorkflowRuntimeHost Host { get; }

        public void Dispose()
        {
            Host.Dispose();
            _frameScope.Dispose();
        }
    }

    /// <summary>模拟外部触发：在首节点执行期间推动回调，并记录当时相机是否已经布防。</summary>
    private sealed class TriggerHandler : WorkflowNodeHandler<TriggerNode>
    {
        private readonly Func<FakeStreamingDevice> _device;
        private readonly (string Tag, long Sequence)[] _emissions;

        public TriggerHandler(Func<FakeStreamingDevice> device, params (string Tag, long Sequence)[] emissions)
        {
            _device = device;
            _emissions = emissions;
            Node = new TriggerNode { Id = "trigger" };
        }

        /// <summary>本处理器对应的节点实例；文档装配需要同一个实例。</summary>
        public TriggerNode Node { get; }

        /// <summary>节点身份。</summary>
        public string Id => Node.Id;

        public int ExecutionCount { get; private set; }

        /// <summary>首节点执行时相机是否已经布防。</summary>
        public bool DeviceWasStreaming { get; private set; } = true;

        /// <summary>是否每一次回调都被设备接受；为 false 说明宿主还没有布防。</summary>
        public bool AllEmitsAccepted { get; private set; } = true;

        protected override ValueTask<NodeExecutionResult> ExecuteAsync(
            TriggerNode node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
        {
            ExecutionCount++;
            var device = _device();
            DeviceWasStreaming &= device.IsStreaming;
            foreach (var emission in _emissions)
                AllEmitsAccepted &= device.Emit(emission.Tag, emission.Sequence);
            return ValueTask.FromResult(NodeExecutionResult.Continue(output: _emissions.Length));
        }
    }

    private sealed class SinkHandler : WorkflowNodeHandler<SinkNode>
    {
        public SinkHandler() => Node = new SinkNode { Id = "sink" };

        /// <summary>本处理器对应的节点实例；文档装配需要同一个实例。</summary>
        public SinkNode Node { get; }

        /// <summary>节点身份。</summary>
        public string Id => Node.Id;

        public int ExecutionCount { get; private set; }

        protected override ValueTask<NodeExecutionResult> ExecuteAsync(
            SinkNode node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
        {
            ExecutionCount++;
            return ValueTask.FromResult(NodeExecutionResult.Continue(output: ExecutionCount));
        }
    }

    private sealed class FaultHandler : WorkflowNodeHandler<FaultNode>
    {
        public int ExecutionCount { get; private set; }

        protected override ValueTask<NodeExecutionResult> ExecuteAsync(
            FaultNode node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
        {
            ExecutionCount++;
            return ValueTask.FromResult(ExecutionCount == 1
                ? NodeExecutionResult.Fail("主操作故障")
                : NodeExecutionResult.Continue(output: ExecutionCount));
        }
    }

    private sealed class TreatNodeHandler : WorkflowNodeHandler<TreatNode>
    {
        protected override ValueTask<NodeExecutionResult> ExecuteAsync(
            TreatNode node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken) =>
            ValueTask.FromResult(NodeExecutionResult.Continue());
    }

    /// <summary>走与生产一致的受监管入口：处置子流程经 <c>RunRecoverySubflowAsync</c> 嵌套运行。</summary>
    private sealed class SubflowRecoveryCoordinator : IWorkflowFaultRecoveryCoordinator
    {
        private readonly WorkflowBoundExecutionPlan _plan;

        public SubflowRecoveryCoordinator(
            WorkflowDocument subflow, WorkflowNodeCatalog catalog, WorkflowNodeHandlerCatalog handlers) =>
            _plan = new WorkflowRuntimeBinder(handlers).Bind(new WorkflowCompiler(catalog).Compile(subflow));

        public async ValueTask<WorkflowFaultRecoveryDecision> RecoverAsync(
            WorkflowFaultRecoveryRequest request, IWorkflowFaultRecoveryContext context, CancellationToken cancellationToken)
        {
            if (context is IWorkflowRecoverySubflowContext supervised)
                await supervised.RunRecoverySubflowAsync(
                    _plan,
                    new WorkflowContext(context.Services),
                    new WorkflowExecutionOptions { MaxRecoveryAttempts = 1 },
                    cancellationToken).ConfigureAwait(false);
            return WorkflowFaultRecoveryDecision.Retry("已处置");
        }
    }

    [WorkflowNode("TestBufferedTrigger")]
    private sealed class TriggerNode : WorkflowNodeModel
    {
        public override string NodeType => "TestBufferedTrigger";
    }

    [WorkflowNode("TestBufferedSink")]
    private sealed class SinkNode : WorkflowNodeModel
    {
        public override string NodeType => "TestBufferedSink";
    }

    [WorkflowNode("TestBufferedTreat")]
    private sealed class TreatNode : WorkflowNodeModel
    {
        public override string NodeType => "TestBufferedTreat";
    }

    [WorkflowNode("TestBufferedFault")]
    private sealed class FaultNode : WorkflowNodeModel, IWorkflowRetrySafetyNode
    {
        public override string NodeType => "TestBufferedFault";

        public WorkflowRetrySafety RetrySafety => WorkflowRetrySafety.Idempotent;
    }
}
