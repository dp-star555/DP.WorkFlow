using System.Collections.Concurrent;

namespace DP.WorkFlow.Tests;

/// <summary>第 8 类：引擎的停点、去向和控制动作（停住、中断、就地恢复、回退再继续、结束本圈、停止）。</summary>
public sealed class WorkflowStopPointTests
{
    [Fact]
    public async Task RecoverInPlace_SideEffectFreeNodeRunsAgain()
    {
        var flow = new TestFlow();
        flow.Add(new StepNode { Id = "S", FailTimes = 1 });
        flow.Add(new StepNode { Id = "After" });
        flow.Connect("S", "After");
        var handler = new ScriptedHandler(_ => WorkflowResumeDirective.RecoverInPlace());

        var result = await flow.RunAsync(handler);

        Assert.True(result.Success, result.Message);
        Assert.Equal(2, flow.Probe.Count("S"));
        Assert.Equal(1, flow.Probe.Count("After"));
        var point = Assert.Single(handler.Points);
        Assert.Equal(WorkflowStopReason.Fault, point.Reason);
        Assert.Equal("$/S", point.Position.ToString());
        Assert.True(point.CanRecoverInPlace);
    }

    [Fact]
    public async Task RecoverInPlace_NotAllowedForNodeWithoutIt_AsksAgainWithRejection()
    {
        var flow = new TestFlow();
        flow.Add(new PlainNode { Id = "A", FailTimes = 1 });
        var handler = new ScriptedHandler(point => point.Rejection is null
            ? WorkflowResumeDirective.RecoverInPlace()
            : WorkflowResumeDirective.Stop("操作员停止"));

        var result = await flow.RunAsync(handler);

        Assert.False(result.Success);
        Assert.Contains("操作员停止", result.Message);
        Assert.Equal(2, handler.Points.Count);
        Assert.Contains("不支持就地恢复", handler.Points[1].Rejection);
        Assert.Equal(1, flow.Probe.Count("A"));
    }

    [Fact]
    public async Task RecoverInPlace_CallsNodeRecoveryInsteadOfRerunning()
    {
        var flow = new TestFlow();
        flow.Add(new ActNode { Id = "A", FailTimes = 1 });
        flow.Add(new StepNode { Id = "After" });
        flow.Connect("A", "After");
        var handler = new ScriptedHandler(_ => WorkflowResumeDirective.RecoverInPlace());

        var result = await flow.RunAsync(handler);

        Assert.True(result.Success, result.Message);
        Assert.Equal(1, flow.Probe.Count("A"));
        Assert.Equal(1, flow.Probe.Count("A:verify"));
        Assert.Equal("已破真空", flow.Probe.LastProgress);
        Assert.Equal(1, flow.Probe.Count("After"));
        Assert.Equal("已破真空", Assert.Single(handler.Points).Progress);
    }

    [Fact]
    public async Task RollBack_ReturnsToAnchorInSameLapAndInvalidatesLaterOutputs()
    {
        var flow = new TestFlow();
        flow.Add(new LapNode { Id = "L", Iterations = 2 });
        flow.Add(new EntryNode { Id = "E", EntryKey = "Start" });
        flow.Add(new StepNode { Id = "X" });
        // 第 2 圈的 S 第一次执行失败（S 的第 2 次执行）。
        flow.Add(new StepNode { Id = "S", FailOnExecution = 2 });
        flow.Add(new StepNode { Id = "End" });
        flow.Connect("L", "E", WorkflowPorts.Loop);
        flow.Connect("E", "X");
        flow.Connect("X", "S");
        flow.Connect("S", "L");
        flow.Connect("L", "End", WorkflowPorts.Completed);
        var handler = new ScriptedHandler(_ => WorkflowResumeDirective.RollBack("Start"));

        var result = await flow.RunAsync(handler);

        Assert.True(result.Success, result.Message);
        var point = Assert.Single(handler.Points);
        Assert.Equal("Start", Assert.Single(point.Entries).Key);
        Assert.Equal(2, point.Position.LoopIterations["L"]);
        Assert.Equal(3, flow.Probe.Count("E"));
        Assert.Equal(3, flow.Probe.Count("S"));
        // 回退再继续不改变圈次：循环节点共执行 3 次（第 1、2 圈，结束）。
        Assert.Equal(3, flow.Probe.Count("L"));
        Assert.Equal(1, flow.Probe.Count("End"));
    }

    [Fact]
    public async Task RollBack_UnknownEntryIsRejected()
    {
        var flow = new TestFlow();
        flow.Add(new EntryNode { Id = "E", EntryKey = "Start" });
        flow.Add(new ActNode { Id = "A", FailTimes = 1 });
        flow.Connect("E", "A");
        var handler = new ScriptedHandler(point => point.Rejection is null
            ? WorkflowResumeDirective.RollBack("Elsewhere")
            : WorkflowResumeDirective.RollBack("Start"));

        var result = await flow.RunAsync(handler);

        Assert.True(result.Success, result.Message);
        Assert.Contains("不在本圈", handler.Points[1].Rejection);
        Assert.Equal(2, flow.Probe.Count("E"));
    }

    [Fact]
    public async Task EndLap_AbandonsCurrentLapOfCanvasLoop()
    {
        var flow = new TestFlow();
        flow.Add(new LapNode { Id = "L", Iterations = 3 });
        flow.Add(new ActNode { Id = "A", FailOnExecution = 2 });
        flow.Add(new StepNode { Id = "B" });
        flow.Connect("L", "A", WorkflowPorts.Loop);
        flow.Connect("A", "B");
        flow.Connect("B", "L");
        var handler = new ScriptedHandler(_ => WorkflowResumeDirective.EndLap());

        var result = await flow.RunAsync(handler);

        Assert.True(result.Success, result.Message);
        Assert.True(Assert.Single(handler.Points).CanEndLap);
        Assert.Equal(3, flow.Probe.Count("A"));
        // 第 2 圈的 B 没有执行。
        Assert.Equal(2, flow.Probe.Count("B"));
        Assert.Equal(4, flow.Probe.Count("L"));
    }

    [Fact]
    public async Task EndLap_WithoutLoopIsRejected()
    {
        var flow = new TestFlow();
        flow.Add(new ActNode { Id = "A", FailTimes = 1 });
        var handler = new ScriptedHandler(point => point.Rejection is null
            ? WorkflowResumeDirective.EndLap()
            : WorkflowResumeDirective.Stop());

        var result = await flow.RunAsync(handler);

        Assert.False(result.Success);
        Assert.False(handler.Points[0].CanEndLap);
        Assert.Contains("没有可以结束本圈的循环", handler.Points[1].Rejection);
    }

    [Fact]
    public async Task RollBack_FromInsideSubCanvas_ReturnsToAnchorOnOuterCanvas()
    {
        var inner = new TestFlow();
        inner.Add(new ActNode { Id = "Inner", FailTimes = 1 });
        var flow = new TestFlow(inner.Probe);
        flow.Add(new EntryNode { Id = "E", EntryKey = "Outer" });
        flow.Add(new SubNode { Id = "Sub", SubDocument = inner.Document });
        flow.Add(new StepNode { Id = "After" });
        flow.Connect("E", "Sub");
        flow.Connect("Sub", "After");
        var handler = new ScriptedHandler(_ => WorkflowResumeDirective.RollBack("Outer"));

        var result = await flow.RunAsync(handler);

        Assert.True(result.Success, result.Message);
        var point = Assert.Single(handler.Points);
        Assert.Equal("$/Sub", point.Position.CanvasPath);
        Assert.Equal("Outer", Assert.Single(point.Entries).Key);
        Assert.Equal(2, flow.Probe.Count("E"));
        Assert.Equal(2, flow.Probe.Count("Inner"));
        Assert.Equal(1, flow.Probe.Count("After"));
    }

    [Fact]
    public async Task FaultInParallelBranch_HoldsSiblingsUntilDirective()
    {
        var flow = new TestFlow();
        flow.Add(new ForkNode { Id = "Fork" });
        flow.Add(new StepNode { Id = "S", FailTimes = 1 });
        flow.Add(new StepNode { Id = "P1", DelayMs = 30 });
        flow.Add(new StepNode { Id = "P2" });
        flow.Add(new JoinNode { Id = "Join" });
        flow.Connect("Fork", "S", WorkflowPorts.Branch);
        flow.Connect("Fork", "P1", WorkflowPorts.Branch);
        flow.Connect("S", "Join");
        flow.Connect("P1", "P2");
        flow.Connect("P2", "Join");
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        WorkflowEngine? engine = null;
        var holds = new List<string>();
        var handler = new ScriptedHandler(async (stopped, point) =>
        {
            engine = stopped;
            await Task.Delay(120);
            holds.AddRange(stopped.ExternalHoldReasons);
            // 停在停点期间，另一条分支在节点之间停住：P2 还没有执行。
            Assert.Equal(0, flow.Probe.Count("P2"));
            return WorkflowResumeDirective.RecoverInPlace();
        });

        var result = await flow.RunAsync(handler);

        Assert.True(result.Success, result.Message);
        Assert.Contains(holds, reason => reason.StartsWith("停点 S", StringComparison.Ordinal));
        Assert.Empty(engine!.ExternalHoldReasons);
        Assert.Equal(1, flow.Probe.Count("P2"));
        Assert.Single(handler.Points[0].Position.ScopeIds);
    }

    [Fact]
    public async Task Interrupt_StopsRunningNodeWithProgressAndRecoversInPlace()
    {
        var flow = new TestFlow();
        flow.Add(new BlockNode { Id = "B" });
        flow.Add(new StepNode { Id = "After" });
        flow.Connect("B", "After");
        var handler = new ScriptedHandler(_ => WorkflowResumeDirective.RecoverInPlace());
        var engine = flow.CreateEngine(handler);

        var run = engine.RunAsync();
        await flow.Probe.Started("B").WaitAsync(TimeSpan.FromSeconds(5));
        engine.Interrupt("急停");
        var result = await run.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(result.Success, result.Message);
        var point = Assert.Single(handler.Points);
        Assert.Equal(WorkflowStopReason.Interrupted, point.Reason);
        Assert.Equal("命令已发", point.Progress);
        Assert.Equal(1, flow.Probe.Count("B:verify"));
        Assert.Equal(1, flow.Probe.Count("After"));
    }

    [Fact]
    public async Task RequestStop_StopsBeforeNextNodeAndRecoverInPlaceRunsIt()
    {
        var flow = new TestFlow();
        flow.Add(new GateNode { Id = "G" });
        flow.Add(new StepNode { Id = "Next" });
        flow.Connect("G", "Next");
        var handler = new ScriptedHandler(point =>
        {
            Assert.Equal(0, flow.Probe.Count("Next"));
            return WorkflowResumeDirective.RecoverInPlace();
        });
        var engine = flow.CreateEngine(handler);

        var run = engine.RunAsync();
        await flow.Probe.Started("G").WaitAsync(TimeSpan.FromSeconds(5));
        engine.RequestStop("换料");
        flow.Probe.Gate.TrySetResult();
        var result = await run.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(result.Success, result.Message);
        var point = Assert.Single(handler.Points);
        Assert.Equal(WorkflowStopReason.Requested, point.Reason);
        Assert.Equal("Next", point.Position.NodeId);
        Assert.Equal("换料", point.Message);
        Assert.Equal(1, flow.Probe.Count("Next"));
    }

    [Fact]
    public async Task WithoutHandler_FaultEndsRunAndRequestStopIsIgnored()
    {
        var flow = new TestFlow();
        flow.Add(new StepNode { Id = "S", FailTimes = 1 });

        var result = await flow.CreateEngine(null).RunAsync();

        Assert.False(result.Success);
        Assert.Equal(1, flow.Probe.Count("S"));
    }

    // ---- 测试流程 ----

    internal sealed class Probe
    {
        private readonly ConcurrentDictionary<string, int> _counts = new(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, TaskCompletionSource> _started = new(StringComparer.Ordinal);
        public TaskCompletionSource Gate { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string? LastProgress { get; set; }
        public int Hit(string key)
        {
            Source(key).TrySetResult();
            return _counts.AddOrUpdate(key, 1, (_, value) => value + 1);
        }
        public int Count(string key) => _counts.TryGetValue(key, out var value) ? value : 0;
        public Task Started(string key) => Source(key).Task;
        private TaskCompletionSource Source(string key) =>
            _started.GetOrAdd(key, _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
    }

    internal sealed class TestFlow
    {
        public TestFlow(Probe? probe = null) => Probe = probe ?? new Probe();
        public Probe Probe { get; }
        public WorkflowDocument Document { get; } = new() { Name = "停点测试" };

        public void Add(WorkflowNodeModel node)
        {
            if (string.IsNullOrWhiteSpace(node.Title)) node.Title = node.Id;
            Document.Nodes.Add(new WorkflowCanvasNode { Node = node });
            if (string.IsNullOrWhiteSpace(Document.EntryNodeId)) Document.EntryNodeId = node.Id;
        }

        public void Connect(string from, string to, string port = WorkflowPorts.Success) =>
            Document.Connections.Add(new WorkflowConnectionModel { FromNodeId = from, FromPort = port, ToNodeId = to });

        public WorkflowNodeHandlerCatalog Handlers() => new WorkflowNodeHandlerCatalog()
            .Register(new StepHandler(Probe)).Register(new ActHandler(Probe)).Register(new EntryHandler(Probe))
            .Register(new LapHandler(Probe)).Register(new BlockHandler(Probe)).Register(new GateHandler(Probe))
            .Register(new ForkHandler()).Register(new JoinHandler()).Register(new SubHandler()).Register(new PlainHandler(Probe));

        public static WorkflowNodeCatalog Catalog() => new WorkflowNodeCatalog()
            .Register(WorkflowNodeDescriptor.Create<StepNode>(ports: new[] { WorkflowPortDescriptor.Input(), WorkflowPortDescriptor.Output(), WorkflowPortDescriptor.Output(WorkflowPorts.Failed) }))
            .Register(WorkflowNodeDescriptor.Create<PlainNode>(ports: new[] { WorkflowPortDescriptor.Input(), WorkflowPortDescriptor.Output() }))
            .Register(WorkflowNodeDescriptor.Create<ActNode>(ports: new[] { WorkflowPortDescriptor.Input(), WorkflowPortDescriptor.Output() }))
            .Register(WorkflowNodeDescriptor.Create<EntryNode>(ports: new[] { WorkflowPortDescriptor.Input(), WorkflowPortDescriptor.Output() }))
            .Register(WorkflowNodeDescriptor.Create<LapNode>(ports: new[] { WorkflowPortDescriptor.Input(maxConnections: int.MaxValue), WorkflowPortDescriptor.Output(WorkflowPorts.Loop), WorkflowPortDescriptor.Output(WorkflowPorts.Completed) }))
            .Register(WorkflowNodeDescriptor.Create<BlockNode>(ports: new[] { WorkflowPortDescriptor.Input(), WorkflowPortDescriptor.Output() }))
            .Register(WorkflowNodeDescriptor.Create<GateNode>(ports: new[] { WorkflowPortDescriptor.Input(), WorkflowPortDescriptor.Output() }))
            .Register(WorkflowNodeDescriptor.Create<ForkNode>(ports: new[] { WorkflowPortDescriptor.Input(), WorkflowPortDescriptor.Output(WorkflowPorts.Branch, maxConnections: int.MaxValue) }))
            .Register(WorkflowNodeDescriptor.Create<JoinNode>(ports: new[] { WorkflowPortDescriptor.Input(maxConnections: int.MaxValue), WorkflowPortDescriptor.Output() }))
            .Register(WorkflowNodeDescriptor.Create<SubNode>(ports: new[] { WorkflowPortDescriptor.Input(), WorkflowPortDescriptor.Output() }));

        public WorkflowEngine CreateEngine(IWorkflowStopPointHandler? handler) =>
            new(new WorkflowCompiler(Catalog()).Compile(Document), Handlers(), new WorkflowContext(),
                new WorkflowExecutionOptions { StopPointHandler = handler });

        public Task<WorkflowRunResult> RunAsync(IWorkflowStopPointHandler handler) =>
            CreateEngine(handler).RunAsync().WaitAsync(TimeSpan.FromSeconds(10));
    }

    internal sealed class ScriptedHandler : IWorkflowStopPointHandler
    {
        private readonly Func<WorkflowEngine, WorkflowStopPoint, Task<WorkflowResumeDirective>> _decide;
        public ScriptedHandler(Func<WorkflowStopPoint, WorkflowResumeDirective> decide) =>
            _decide = (_, point) => Task.FromResult(decide(point));
        public ScriptedHandler(Func<WorkflowEngine, WorkflowStopPoint, Task<WorkflowResumeDirective>> decide) => _decide = decide;
        public List<WorkflowStopPoint> Points { get; } = new();
        public Task<WorkflowResumeDirective> OnStoppedAsync(WorkflowEngine engine, WorkflowStopPoint stopPoint, CancellationToken cancellationToken)
        {
            lock (Points) Points.Add(stopPoint);
            return _decide(engine, stopPoint);
        }
    }

    /// <summary>无副作用的步骤：前 FailTimes 次（或第 FailOnExecution 次）执行失败。</summary>
    [WorkflowNode("T.Step")]
    internal sealed class StepNode : WorkflowNodeModel
    {
        public override string NodeType => "T.Step";
        public int FailTimes { get; set; }
        public int FailOnExecution { get; set; }
        public int DelayMs { get; set; }
    }

    /// <summary>没有恢复能力的动作：前 FailTimes 次执行失败。</summary>
    [WorkflowNode("T.Plain")]
    internal sealed class PlainNode : WorkflowNodeModel
    {
        public override string NodeType => "T.Plain";
        public int FailTimes { get; set; }
    }

    /// <summary>有副作用、实现了就地恢复的动作。</summary>
    [WorkflowNode("T.Act")]
    internal sealed class ActNode : WorkflowNodeModel
    {
        public override string NodeType => "T.Act";
        public int FailTimes { get; set; }
        public int FailOnExecution { get; set; }
    }

    [WorkflowNode("T.Entry")]
    internal sealed class EntryNode : WorkflowNodeModel, IWorkflowRecoveryEntryNode
    {
        public override string NodeType => "T.Entry";
        public string EntryKey { get; set; } = string.Empty;
        public string RecoveryEntryKey => EntryKey;
    }

    [WorkflowNode("T.Lap")]
    internal sealed class LapNode : WorkflowNodeModel, IWorkflowLoopNode
    {
        public override string NodeType => "T.Lap";
        public int Iterations { get; set; }
    }

    [WorkflowNode("T.Block")]
    internal sealed class BlockNode : WorkflowNodeModel
    {
        public override string NodeType => "T.Block";
    }

    [WorkflowNode("T.Gate")]
    internal sealed class GateNode : WorkflowNodeModel
    {
        public override string NodeType => "T.Gate";
    }

    [WorkflowNode("T.Fork")]
    internal sealed class ForkNode : WorkflowNodeModel, IWorkflowParallelForkNode
    {
        public override string NodeType => "T.Fork";
    }

    [WorkflowNode("T.Join")]
    internal sealed class JoinNode : WorkflowNodeModel, IWorkflowParallelJoinNode
    {
        public override string NodeType => "T.Join";
    }

    [WorkflowNode("T.Sub")]
    internal sealed class SubNode : WorkflowNodeModel, IWorkflowSubDocumentNode
    {
        public override string NodeType => "T.Sub";
        public WorkflowDocument SubDocument { get; set; } = new();
    }

    private static bool ShouldFail(int execution, int failTimes, int failOnExecution) =>
        execution <= failTimes || execution == failOnExecution;

    private sealed class StepHandler(Probe probe) : WorkflowSideEffectFreeNodeHandler<StepNode>
    {
        protected override async ValueTask<NodeExecutionResult> ExecuteAsync(StepNode node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
        {
            var count = probe.Hit(node.Id);
            if (node.DelayMs > 0) await Task.Delay(node.DelayMs, cancellationToken);
            return ShouldFail(count, node.FailTimes, node.FailOnExecution)
                ? NodeExecutionResult.Fail($"{node.Id} 第 {count} 次失败")
                : NodeExecutionResult.Continue(output: count);
        }
    }

    private sealed class PlainHandler(Probe probe) : WorkflowNodeHandler<PlainNode>
    {
        protected override ValueTask<NodeExecutionResult> ExecuteAsync(PlainNode node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken) =>
            ValueTask.FromResult(probe.Hit(node.Id) <= node.FailTimes
                ? NodeExecutionResult.Fail($"{node.Id} 失败")
                : NodeExecutionResult.Continue());
    }

    private sealed class ActHandler(Probe probe) : WorkflowNodeHandler<ActNode>, IWorkflowInPlaceRecoveryNodeHandler
    {
        protected override ValueTask<NodeExecutionResult> ExecuteAsync(ActNode node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
        {
            var count = probe.Hit(node.Id);
            context.ReportProgress("已破真空");
            return ValueTask.FromResult(ShouldFail(count, node.FailTimes, node.FailOnExecution)
                ? NodeExecutionResult.Fail($"{node.Id} 第 {count} 次失败")
                : NodeExecutionResult.Continue(output: count));
        }

        public ValueTask<NodeExecutionResult> RecoverInPlaceAsync(IWorkflowNodeModel node, IWorkflowNodeExecutionContext context,
            WorkflowNodeInterruption interruption, CancellationToken cancellationToken)
        {
            probe.Hit(node.Id + ":verify");
            probe.LastProgress = interruption.Progress;
            return ValueTask.FromResult(NodeExecutionResult.Continue(output: -1));
        }
    }

    private sealed class EntryHandler(Probe probe) : WorkflowNodeHandler<EntryNode>
    {
        protected override ValueTask<NodeExecutionResult> ExecuteAsync(EntryNode node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
        {
            probe.Hit(node.Id);
            return ValueTask.FromResult(NodeExecutionResult.Continue(output: node.EntryKey));
        }
    }

    private sealed class LapHandler(Probe probe) : WorkflowNodeHandler<LapNode>
    {
        protected override ValueTask<NodeExecutionResult> ExecuteAsync(LapNode node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
        {
            probe.Hit(node.Id);
            var iteration = context.ExecutionIdentity.LoopIteration ?? 1;
            return ValueTask.FromResult(NodeExecutionResult.Continue(
                iteration <= node.Iterations ? WorkflowPorts.Loop : WorkflowPorts.Completed, iteration));
        }
    }

    private sealed class BlockHandler(Probe probe) : WorkflowNodeHandler<BlockNode>, IWorkflowInPlaceRecoveryNodeHandler
    {
        protected override async ValueTask<NodeExecutionResult> ExecuteAsync(BlockNode node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
        {
            context.ReportProgress("命令已发");
            probe.Hit(node.Id);
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return NodeExecutionResult.Continue();
        }

        public ValueTask<NodeExecutionResult> RecoverInPlaceAsync(IWorkflowNodeModel node, IWorkflowNodeExecutionContext context,
            WorkflowNodeInterruption interruption, CancellationToken cancellationToken)
        {
            probe.Hit(node.Id + ":verify");
            return ValueTask.FromResult(NodeExecutionResult.Continue());
        }
    }

    private sealed class GateHandler(Probe probe) : WorkflowNodeHandler<GateNode>
    {
        protected override async ValueTask<NodeExecutionResult> ExecuteAsync(GateNode node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
        {
            probe.Hit(node.Id);
            await probe.Gate.Task.WaitAsync(cancellationToken);
            return NodeExecutionResult.Continue();
        }
    }

    private sealed class ForkHandler : WorkflowNodeHandler<ForkNode>
    {
        protected override ValueTask<NodeExecutionResult> ExecuteAsync(ForkNode node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken) =>
            ValueTask.FromResult(NodeExecutionResult.Continue(WorkflowPorts.Branch));
    }

    private sealed class JoinHandler : WorkflowNodeHandler<JoinNode>
    {
        protected override ValueTask<NodeExecutionResult> ExecuteAsync(JoinNode node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken) =>
            ValueTask.FromResult(NodeExecutionResult.Continue());
    }

    private sealed class SubHandler : WorkflowNodeHandler<SubNode>
    {
        protected override async ValueTask<NodeExecutionResult> ExecuteAsync(SubNode node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
        {
            var child = (IWorkflowChildExecutionContext)context;
            var result = await child.RunChildWorkflowAsync(child.GetChildWorkflowExecutionPlan(), child.CreateChildScope(), cancellationToken);
            return result.Success ? NodeExecutionResult.Continue() : NodeExecutionResult.Fail(result.Message ?? "子画布失败", WorkflowFaultDisposition.StopRun);
        }
    }
}
