using DP.Vision;
using DP.Vision.Algorithms;

namespace DP.WorkFlow.Tests;

/// <summary>
/// AR-01 验收第 4 条：生产路径端到端。
/// 「采图 → 主操作故障 → 执行处置 → 继续消费原图」。
///
/// 与 <c>WorkflowVisionFrameScopeRunScopeTests</c> 的区别：那里直接调用准备服务，
/// 这里走真实引擎的恢复路径（<c>IWorkflowRecoverySubflowContext</c> →
/// <c>RunTrackedChildAsync(prepare: true)</c>），并由一个下游节点真正去用那张图。
/// 修复前该下游节点会拿到已释放的帧。
/// </summary>
public sealed class WorkflowVisionFrameScopeRecoveryEndToEndTests
{
    [Fact]
    public async Task 处置子流程运行后父运行仍能消费原图()
    {
        string path = Path.Combine(Path.GetTempPath(), "07.png");
        File.WriteAllBytes(path, new byte[] { 0 });
        try
        {
            var treat = new TreatNode { Id = "treat" };
            var consumeHandler = new ConsumeFrameHandler();

            var catalog = new WorkflowNodeCatalog()
                .RegisterImageNodes()
                .Register(WorkflowNodeDescriptor.Create<TreatNode>(ports: new[] { WorkflowPortDescriptor.Output() }))
                .Register(WorkflowNodeDescriptor.Create<FaultNode>(ports: new[] { WorkflowPortDescriptor.Input(), WorkflowPortDescriptor.Output() }))
                .Register(WorkflowNodeDescriptor.Create<ConsumeFrameNode>(ports: new[] { WorkflowPortDescriptor.Input(), WorkflowPortDescriptor.Output() }));
            var handlers = new WorkflowNodeHandlerCatalog()
                .RegisterImageNodeHandlers()
                .Register(new TreatNodeHandler())
                .Register(new FaultNodeHandler())
                .Register(consumeHandler);

            // 处置子流程：单节点，只要它被真的跑起来就会触发 prepare: true 的嵌套运行。
            var subflow = new WorkflowDocument { Name = "处置子流程", EntryNodeId = treat.Id };
            subflow.CanvasProjection.Nodes.Add(new WorkflowCanvasNode { Node = treat });

            // 采图 → 故障 → 消费原图。消费节点绑定的是采图节点的输出。
            var document = new WorkflowDocument { Name = "主流程" };
            var file = new LoadVisionFileNodeModel { Id = "file", FilePath = path };
            var fault = new FaultNode { Id = "fault" };
            var consume = new ConsumeFrameNode { Id = "consume", Frame = Input<ImageFrame>("file") };
            document.EntryNodeId = file.Id;
            foreach (var node in new IWorkflowNodeModel[] { file, fault, consume })
                document.CanvasProjection.Nodes.Add(new WorkflowCanvasNode { Node = node });
            Connect(document, file.Id, fault.Id);
            Connect(document, fault.Id, consume.Id);

            using var scope = new WorkflowVisionFrameScope();
            var services = new WorkflowServiceProvider()
                .Add<IImageFileReader>(new NumberedReader())
                .Add<IWorkflowVisionFrameScope>(scope)
                .Add<IWorkflowRunPreparationService>(scope)
                .Add<IWorkflowFaultRecoveryCoordinator>(new SubflowRecoveryCoordinator(subflow, catalog, handlers));

            var result = await new WorkflowEngine(
                    new WorkflowCompiler(catalog).Compile(document), handlers, new WorkflowContext(services),
                    new WorkflowExecutionOptions { MaxRecoveryAttempts = 2 })
                .RunAsync();

            // 修复前：处置子流程清空了帧仓，父运行的节点输出被释放 → 这里记录到 ObjectDisposedException。
            Assert.Null(consumeHandler.DisposedOutput);
            Assert.True(result.Success, result.Message);
            Assert.Equal(7, consumeHandler.Pixel);
        }
        finally { File.Delete(path); }
    }

    private static WorkflowInput<T> Input<T>(string nodeId) =>
        WorkflowInput<T>.FromBinding(new WorkflowBindingKey(nodeId, "$"));

    private static void Connect(WorkflowDocument document, string from, string to) =>
        document.CanvasProjection.Connections.Add(new WorkflowConnectionModel
        { FromNodeId = from, FromPort = WorkflowPorts.Success, ToNodeId = to, ToPort = WorkflowPorts.Input });

    /// <summary>确定性读取器：像素值取文件名序号，不依赖 OpenCV 与真实解码。</summary>
    private sealed class NumberedReader : IImageFileReader
    {
        public Task<IImageSource> ReadAsync(string path, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            byte value = byte.Parse(Path.GetFileNameWithoutExtension(path));
            return Task.FromResult<IImageSource>(
                VisionImage.CopyFrom(new ImageInfo(1, 1, EPixelLayout.Gray8), new[] { value }));
        }
    }

    /// <summary>走与生产一致的受监管入口。</summary>
    private sealed class SubflowRecoveryCoordinator : IWorkflowFaultRecoveryCoordinator
    {
        private readonly WorkflowBoundExecutionPlan _plan;

        public SubflowRecoveryCoordinator(WorkflowDocument subflow, WorkflowNodeCatalog catalog, WorkflowNodeHandlerCatalog handlers) =>
            _plan = new WorkflowRuntimeBinder(handlers).Bind(new WorkflowCompiler(catalog).Compile(subflow));

        public async ValueTask<WorkflowFaultRecoveryDecision> RecoverAsync(
            WorkflowFaultRecoveryRequest request, IWorkflowFaultRecoveryContext context, CancellationToken cancellationToken)
        {
            if (context is IWorkflowRecoverySubflowContext supervised)
                await supervised.RunRecoverySubflowAsync(_plan, new WorkflowContext(context.Services),
                    new WorkflowExecutionOptions { MaxRecoveryAttempts = 1 }, cancellationToken).ConfigureAwait(false);
            return WorkflowFaultRecoveryDecision.Retry("已处置");
        }
    }

    [WorkflowNode("TestE2eTreat")]
    private sealed class TreatNode : WorkflowNodeModel
    {
        public override string NodeType => "TestE2eTreat";
    }

    private sealed class TreatNodeHandler : WorkflowNodeHandler<TreatNode>
    {
        protected override ValueTask<NodeExecutionResult> ExecuteAsync(
            TreatNode node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken) =>
            ValueTask.FromResult(NodeExecutionResult.Continue());
    }

    [WorkflowNode("TestE2eFault")]
    private sealed class FaultNode : WorkflowNodeModel, IWorkflowRetrySafetyNode
    {
        public override string NodeType => "TestE2eFault";
        public WorkflowRetrySafety RetrySafety => WorkflowRetrySafety.Idempotent;
    }

    private sealed class FaultNodeHandler : WorkflowNodeHandler<FaultNode>
    {
        public int ExecutionCount;

        protected override ValueTask<NodeExecutionResult> ExecuteAsync(
            FaultNode node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
        {
            ExecutionCount++;
            return ValueTask.FromResult(ExecutionCount == 1
                ? NodeExecutionResult.Fail("主操作故障")
                : NodeExecutionResult.Continue(output: ExecutionCount));
        }
    }

    [WorkflowNode("TestE2eConsume")]
    private sealed class ConsumeFrameNode : WorkflowNodeModel, IWorkflowRetrySafetyNode
    {
        public override string NodeType => "TestE2eConsume";
        public WorkflowRetrySafety RetrySafety => WorkflowRetrySafety.Idempotent;
        /// <summary>上游帧绑定；与真实视觉节点一致，禁止把运行图像序列化到配置。</summary>
        [WorkflowProperty("输入图像", "绑定采图节点的ImageFrame标准输出。", Category = "输入")]
        public WorkflowInput<ImageFrame> Frame { get; set; } = WorkflowInput<ImageFrame>.FromLiteral(null);
    }

    /// <summary>真正的"继续消费原图"：从绑定取回父运行的节点输出并使用它。</summary>
    private sealed class ConsumeFrameHandler : WorkflowNodeHandler<ConsumeFrameNode>
    {
        public int Pixel = -1;
        /// <summary>父运行输出已失效时记录原始异常，避免被二次恢复的安全守卫掩盖。</summary>
        public ObjectDisposedException? DisposedOutput;

        protected override ValueTask<NodeExecutionResult> ExecuteAsync(
            ConsumeFrameNode node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
        {
            try
            {
                var frame = context.ResolveInput(node.Frame)
                    ?? throw new InvalidOperationException("未绑定上游图像。");
                var pixel = new byte[1];
                frame.Image.CopyTo(0, pixel, 0, 1);
                Pixel = pixel[0];
                return ValueTask.FromResult(NodeExecutionResult.Continue(output: Pixel));
            }
            catch (ObjectDisposedException failure)
            {
                DisposedOutput = failure;
                return ValueTask.FromResult(NodeExecutionResult.Fail("父运行的节点输出已失效：" + failure.Message));
            }
        }
    }
}
