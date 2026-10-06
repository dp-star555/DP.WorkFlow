using DP.Vision;
using DP.Vision.Algorithms;

namespace DP.WorkFlow.Tests;

/// <summary>
/// AR-29 验收：Vision预览必须是"已提交输出"的派生投影。
/// 修复前Handler在返回NodeExecutionResult之前就调用Publish，预览会先于正式输出出现；
/// 运行输出失效时也没有同步撤销预览。
/// </summary>
public sealed class WorkflowNodeOutputProjectionTests
{
    [Fact]
    public async Task 派生投影只在输出正式提交后发布()
    {
        var projection = new RecordingProjection();
        var catalog = new WorkflowNodeCatalog()
            .Register(WorkflowNodeDescriptor.Create<ProjectingNode>(ports: new[] { WorkflowPortDescriptor.Output() }));
        var handlers = new WorkflowNodeHandlerCatalog().Register(new ProjectingNodeHandler(projection));

        var document = new WorkflowDocument { Name = "投影流程", EntryNodeId = "project" };
        document.CanvasProjection.Nodes.Add(new WorkflowCanvasNode { Node = new ProjectingNode { Id = "project" } });

        var engine = new WorkflowEngine(
            new WorkflowCompiler(catalog).Compile(document),
            handlers,
            new WorkflowContext(new WorkflowServiceProvider()));
        var result = await engine.RunAsync();

        Assert.True(result.Success, result.Message);

        // 投影必须被发布一次，且拿到的就是本次提交在运行输出序列中的序号。
        Assert.Equal(1, projection.CommitCount);
        var committed = Assert.Single(engine.RunState.NodeOutputs);
        Assert.Equal(committed.ExecutionSequence, projection.LastExecutionSequence);
        Assert.Equal("value", committed.Value);
    }

    [Fact]
    public async Task 节点失败时不发布派生投影()
    {
        var projection = new RecordingProjection();
        var catalog = new WorkflowNodeCatalog()
            .Register(WorkflowNodeDescriptor.Create<ProjectingNode>(ports: new[] { WorkflowPortDescriptor.Output() }));
        var handlers = new WorkflowNodeHandlerCatalog().Register(new ProjectingNodeHandler(projection, fail: true));

        var document = new WorkflowDocument { Name = "投影失败流程", EntryNodeId = "project" };
        document.CanvasProjection.Nodes.Add(new WorkflowCanvasNode { Node = new ProjectingNode { Id = "project" } });

        var engine = new WorkflowEngine(
            new WorkflowCompiler(catalog).Compile(document),
            handlers,
            new WorkflowContext(new WorkflowServiceProvider()));
        var result = await engine.RunAsync();

        Assert.False(result.Success);
        Assert.Equal(0, projection.CommitCount);
        Assert.Empty(engine.RunState.NodeOutputs);
    }

    [Fact]
    public async Task 输出失效时同步撤销该序号起的预览()
    {
        using var scope = new WorkflowVisionFrameScope();
        await RunLoadFileAsync(scope);

        using (var before = scope.Capture("file"))
            Assert.NotNull(before);

        // 模拟运行输出从序号1起失效（恢复重跑）：预览必须一并撤销，不能让界面继续显示旧结果。
        scope.InvalidateFrom(1);

        Assert.Null(scope.Capture("file"));
    }

    [Fact]
    public async Task 失效序号晚于预览提交时不撤销该预览()
    {
        using var scope = new WorkflowVisionFrameScope();
        await RunLoadFileAsync(scope);

        // 预览来自序号1的提交；从序号2起失效不应牵连它。
        scope.InvalidateFrom(2);

        using var after = scope.Capture("file");
        Assert.NotNull(after);
    }

    [Fact]
    public async Task 运行输出失效时通知投影接收方()
    {
        string path = Path.Combine(Path.GetTempPath(), "ar29-recovery.png");
        File.WriteAllBytes(path, new byte[] { 0 });
        try
        {
            using var scope = new WorkflowVisionFrameScope();
            var sink = new RecordingSink(scope);
            var faultHandler = new FaultOnceHandler();
            var entryHandler = new RecoveryEntryTestHandler();

            var catalog = new WorkflowNodeCatalog()
                .RegisterImageNodes()
                .Register(WorkflowNodeDescriptor.Create<RecoveryEntryTestNode>(
                    ports: new[] { WorkflowPortDescriptor.Input(), WorkflowPortDescriptor.Output() }))
                .Register(WorkflowNodeDescriptor.Create<FaultOnceNode>(
                    ports: new[] { WorkflowPortDescriptor.Input(), WorkflowPortDescriptor.Output() }));
            var handlers = new WorkflowNodeHandlerCatalog()
                .RegisterImageNodeHandlers()
                .Register(entryHandler)
                .Register(faultHandler);

            // 入口 → 采图 → 故障：采图预览的提交序号落在入口之后，
            // 因此"从命名入口重跑"必然要撤销这张预览——正是AR-29的现场。
            var document = new WorkflowDocument { Name = "恢复失效流程" };
            var entry = new RecoveryEntryTestNode { Id = "entry" };
            var file = new AcquireVisionImageNodeModel { Id = "file", FilePath = path };
            var fault = new FaultOnceNode { Id = "fault" };
            document.EntryNodeId = entry.Id;
            foreach (var node in new IWorkflowNodeModel[] { entry, file, fault })
                document.CanvasProjection.Nodes.Add(new WorkflowCanvasNode { Node = node });
            Connect(document, "entry", "file");
            Connect(document, "file", "fault");

            // 生产装配里 WorkflowVisionFrameScope 本身就是投影接收方；这里换成转发型探针，
            // 只是为了让"引擎发出的序号"与"作用域的实际反应"能在同一处被断言。
            var services = new WorkflowServiceProvider()
                .Add<IImageFileReader>(new NumberedReader())
                .Add<IWorkflowVisionFrameScope>(scope)
                .Add<IWorkflowRunPreparationService>(scope)
                .Add<IWorkflowRunResourceOwner>(scope)
                .Add<IWorkflowNodeOutputProjectionSink>(sink)
                .Add<IWorkflowRecoveryEntryGuard>(new AllowGuard())
                .Add<IWorkflowFaultRecoveryCoordinator>(new RestartCoordinator());

            var engine = new WorkflowEngine(
                new WorkflowCompiler(catalog).Compile(document), handlers, new WorkflowContext(services),
                new WorkflowExecutionOptions { MaxRecoveryAttempts = 2 });
            var result = await engine.RunAsync();

            Assert.True(result.Success, result.Message);
            // 故障节点重跑一次才成功；命名入口也随之重执行一次。
            Assert.Equal(2, faultHandler.ExecutionCount);
            Assert.Equal(2, entryHandler.ExecutionCount);

            // 投影接收方必须收到与运行状态一致的失效起点序号，
            // 否则界面会继续显示已经不可绑定的旧预览。
            Assert.Equal(1, sink.CallCount);
            // 入口自身（序号1）与采图节点（序号2）的输出都被撤销。
            Assert.Equal(new long[] { 1, 2 }, engine.RunState.InvalidatedOutputSequences);
            Assert.Equal(engine.RunState.InvalidatedOutputSequences[0], sink.LastSequence);
            Assert.Equal(1, sink.LastSequence);

            // 撤销前界面还显示着上一次采图的预览；撤销后同一瞬间必须消失。
            Assert.True(sink.PreviewVisibleBefore);
            Assert.False(sink.PreviewVisibleAfter);

            // 重跑成功后预览被重新发布，且序号新于被撤销的那一张。
            using var restored = scope.Capture("file");
            Assert.NotNull(restored);
            Assert.True(restored!.ExecutionSequence > sink.LastSequence);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static void Connect(WorkflowDocument document, string from, string to) =>
        document.CanvasProjection.Connections.Add(new WorkflowConnectionModel
        { FromNodeId = from, FromPort = WorkflowPorts.Success, ToNodeId = to, ToPort = WorkflowPorts.Input });

    private static async Task RunLoadFileAsync(WorkflowVisionFrameScope scope)
    {
        string path = Path.Combine(Path.GetTempPath(), "ar29.png");
        File.WriteAllBytes(path, new byte[] { 0 });
        try
        {
            var catalog = new WorkflowNodeCatalog().RegisterImageNodes();
            var handlers = new WorkflowNodeHandlerCatalog().RegisterImageNodeHandlers();

            var document = new WorkflowDocument { Name = "采图流程" };
            var file = new AcquireVisionImageNodeModel { Id = "file", FilePath = path };
            document.EntryNodeId = file.Id;
            document.CanvasProjection.Nodes.Add(new WorkflowCanvasNode { Node = file });

            var services = new WorkflowServiceProvider()
                .Add<IImageFileReader>(new NumberedReader())
                .Add<IWorkflowVisionFrameScope>(scope)
                .Add<IWorkflowRunPreparationService>(scope)
                .Add<IWorkflowRunResourceOwner>(scope);

            var result = await new WorkflowEngine(
                    new WorkflowCompiler(catalog).Compile(document), handlers, new WorkflowContext(services))
                .RunAsync();
            Assert.True(result.Success, result.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>确定性读取器：像素值取文件名序号，不依赖 OpenCV 与真实解码。</summary>
    private sealed class NumberedReader : IImageFileReader
    {
        public Task<IImageSource> ReadAsync(string path, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            return Task.FromResult<IImageSource>(
                VisionImage.CopyFrom(new ImageInfo(1, 1, EPixelLayout.Gray8), new byte[] { 7 }));
        }
    }

    private sealed class RecordingProjection : IWorkflowNodeOutputProjection
    {
        public int CommitCount;
        public long LastExecutionSequence = -1;

        public void Commit(long executionSequence)
        {
            CommitCount++;
            LastExecutionSequence = executionSequence;
        }
    }

    [WorkflowNode("TestProjection")]
    private sealed class ProjectingNode : WorkflowNodeModel
    {
        public override string NodeType => "TestProjection";
    }

    private sealed class ProjectingNodeHandler : WorkflowNodeHandler<ProjectingNode>
    {
        private readonly IWorkflowNodeOutputProjection _projection;
        private readonly bool _fail;

        public ProjectingNodeHandler(IWorkflowNodeOutputProjection projection, bool fail = false)
        {
            _projection = projection;
            _fail = fail;
        }

        protected override ValueTask<NodeExecutionResult> ExecuteAsync(
            ProjectingNode node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
        {
            if (_fail)
                return ValueTask.FromResult(NodeExecutionResult.Fail("节点执行失败。"));

            return ValueTask.FromResult(NodeExecutionResult.Continue(output: "value", projection: _projection));
        }
    }

    /// <summary>投影接收方探针；可选地转发给真实帧作用域，并在转发前后各探一次预览可见性。</summary>
    private sealed class RecordingSink : IWorkflowNodeOutputProjectionSink
    {
        private readonly WorkflowVisionFrameScope? _scope;

        public RecordingSink(WorkflowVisionFrameScope? scope = null) => _scope = scope;

        public int CallCount;
        public long LastSequence = -1;
        public bool? PreviewVisibleBefore;
        public bool? PreviewVisibleAfter;

        public void InvalidateFrom(long executionSequence)
        {
            CallCount++;
            LastSequence = executionSequence;
            if (_scope is null) return;
            using (var before = _scope.Capture("file")) PreviewVisibleBefore = before is not null;
            _scope.InvalidateFrom(executionSequence);
            using (var after = _scope.Capture("file")) PreviewVisibleAfter = after is not null;
        }
    }

    [WorkflowNode("TestAr29Entry")]
    private sealed class RecoveryEntryTestNode : WorkflowNodeModel, IWorkflowRecoveryEntryNode
    {
        public override string NodeType => "TestAr29Entry";
        public string RecoveryEntryKey => "station";
    }

    private sealed class RecoveryEntryTestHandler : WorkflowNodeHandler<RecoveryEntryTestNode>
    {
        public int ExecutionCount;

        protected override ValueTask<NodeExecutionResult> ExecuteAsync(
            RecoveryEntryTestNode node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
        {
            ExecutionCount++;
            return ValueTask.FromResult(NodeExecutionResult.Continue(output: "entered"));
        }
    }

    [WorkflowNode("TestAr29Fault")]
    private sealed class FaultOnceNode : WorkflowNodeModel, IWorkflowRetrySafetyNode
    {
        public override string NodeType => "TestAr29Fault";
        public WorkflowRetrySafety RetrySafety => WorkflowRetrySafety.Idempotent;
    }

    private sealed class FaultOnceHandler : WorkflowNodeHandler<FaultOnceNode>
    {
        public int ExecutionCount;

        protected override ValueTask<NodeExecutionResult> ExecuteAsync(
            FaultOnceNode node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
        {
            ExecutionCount++;
            return ValueTask.FromResult(ExecutionCount == 1
                ? NodeExecutionResult.Fail("主操作故障")
                : NodeExecutionResult.Continue(output: ExecutionCount));
        }
    }

    private sealed class RestartCoordinator : IWorkflowFaultRecoveryCoordinator
    {
        public ValueTask<WorkflowFaultRecoveryDecision> RecoverAsync(
            WorkflowFaultRecoveryRequest request, IWorkflowFaultRecoveryContext context, CancellationToken cancellationToken) =>
            ValueTask.FromResult(WorkflowFaultRecoveryDecision.Restart("station"));
    }

    private sealed class AllowGuard : IWorkflowRecoveryEntryGuard
    {
        public ValueTask<WorkflowRecoveryEntryValidation> ValidateAsync(
            WorkflowRecoveryEntryPlan plan, IWorkflowFaultRecoveryContext context, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new WorkflowRecoveryEntryValidation(true));
    }
}
