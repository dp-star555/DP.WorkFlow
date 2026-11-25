using System.Collections.Concurrent;
using static DP.WorkFlow.Tests.WorkflowStopPointTests;

namespace DP.WorkFlow.Tests;

/// <summary>第 8 类：统管层的故障会话——上报、停下、处理流程、去向、兜底、一起放行。</summary>
public sealed class WorkflowFaultSessionTests
{
    [Fact]
    public async Task NodeFault_RunsDefaultHandlerAndAppliesItsDirective()
    {
        var probe = new Probe();
        var flow = new TestFlow(probe);
        flow.Add(new StepNode { Id = "S", FailTimes = 1 });
        var handler = Handler(probe, new DirectNode { Id = "D", Kind = WorkflowResumeKind.RecoverInPlace });
        var ended = new TaskCompletionSource<WorkflowFaultSession>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var center = CreateCenter(probe, handler);
        center.FaultSessionEnded += session => ended.TrySetResult(session);

        var result = await center.AddFlow(flow.Document, "A").RunAsync().WaitAsync(TimeSpan.FromSeconds(10));

        Assert.True(result.Success, result.Message);
        Assert.Equal(2, probe.Count("S"));
        var session = await ended.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("A", session.OriginFlow);
        Assert.Equal(1, session.Attempt);
        Assert.Equal(WorkflowResumeKind.RecoverInPlace, session.Directives["A"].Kind);
        Assert.Equal(WorkflowFaultSessionState.Ended, session.State);
        Assert.Null(center.CurrentFaultSession);
    }

    [Fact]
    public async Task RaiseFault_HoldsTargetFlowsAndReleasesThemTogether()
    {
        var probe = new Probe();
        var a = LoopingFlow("A");
        var b = LoopingFlow("B");
        // 处理流程：A 照常继续（未写去向），B 停止。
        var handler = Handler(probe, new DirectNode { Id = "D", Flow = "B", Kind = WorkflowResumeKind.Stop });
        using var center = CreateCenter(probe, handler);
        var flowA = center.AddFlow(a.Document, "A");
        var flowB = center.AddFlow(b.Document, "B");
        var started = new TaskCompletionSource<WorkflowFaultSession>(TaskCreationOptions.RunContinuationsAsynchronously);
        center.FaultSessionStarted += session => started.TrySetResult(session);

        var runA = flowA.RunAsync();
        var runB = flowB.RunAsync();
        await probe.Started("A.Tick").WaitAsync(TimeSpan.FromSeconds(5));
        await probe.Started("B.Tick").WaitAsync(TimeSpan.FromSeconds(5));
        var raised = center.RaiseFault(new WorkflowFaultReport { Message = "安全门打开", AlarmCode = 77, Source = "安全回路" });
        var session = await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Same(raised, session);
        Assert.Equal(new[] { "A", "B" }, session.StopPoints.Select(point => point.FlowName).OrderBy(name => name));
        Assert.All(session.StopPoints, point => Assert.Equal(WorkflowStopReason.Requested, point.Reason));
        // 让 A 结束循环：放行后 A 照常跑完。
        Gates.GetOrAdd("A", _ => new TaskCompletionSource()).TrySetResult();

        var resultB = await runB.WaitAsync(TimeSpan.FromSeconds(10));
        var resultA = await runA.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False(resultB.Success);
        Assert.True(resultA.Success, resultA.Message);
        Assert.Equal(77, session.PrimaryFault.AlarmCode);
    }

    [Fact]
    public async Task HandlerFailure_FallbackRerunsHandler()
    {
        var probe = new Probe();
        var flow = new TestFlow(probe);
        flow.Add(new StepNode { Id = "S", FailTimes = 1 });
        // 处理流程第一次出错，第二次给出就地恢复。
        var handler = Handler(probe, new StepNode { Id = "H", FailTimes = 1 }, new DirectNode { Id = "D", Kind = WorkflowResumeKind.RecoverInPlace });
        var fallback = new RecordingFallback(_ => WorkflowFaultFallbackDecision.Rerun);
        using var center = CreateCenter(probe, handler, fallback);

        var result = await center.AddFlow(flow.Document, "A").RunAsync().WaitAsync(TimeSpan.FromSeconds(10));

        Assert.True(result.Success, result.Message);
        var request = Assert.Single(fallback.Requests);
        Assert.Contains("处理流程出错", request.Failure);
        Assert.True(request.HasHandler);
        Assert.Equal(2, request.Session.Attempt);
        Assert.Equal(2, probe.Count("H"));
    }

    [Fact]
    public async Task DirectiveNotAllowed_FallbackCanGiveDirectivesDirectly()
    {
        var probe = new Probe();
        var flow = new TestFlow(probe);
        flow.Add(new ActNode { Id = "A1", FailTimes = 1 });
        // 没有先回退就直接给"回退再继续"：处理流程出错，兜底里改为"就地恢复"。
        var handler = Handler(probe, new DirectNode { Id = "D", Kind = WorkflowResumeKind.RollBack });
        var fallback = new RecordingFallback(_ => new WorkflowFaultFallbackDecision(WorkflowFaultFallbackChoice.UseDirectives,
            new Dictionary<string, WorkflowResumeDirective> { ["A"] = WorkflowResumeDirective.RecoverInPlace() }));
        using var center = CreateCenter(probe, handler, fallback);

        var result = await center.AddFlow(flow.Document, "A").RunAsync().WaitAsync(TimeSpan.FromSeconds(10));

        Assert.True(result.Success, result.Message);
        Assert.Contains("RollBackAsync", Assert.Single(fallback.Requests).Failure);
        Assert.Equal(1, probe.Count("A1:verify"));
    }

    [Fact]
    public async Task FallbackRollBack_UndoesAfterEntryThenReentersAtEntry()
    {
        var probe = new Probe();
        var flow = new TestFlow(probe);
        flow.Add(new EntryNode { Id = "E", EntryKey = "Op" });
        flow.Add(new StepNode { Id = "S1" });
        flow.Add(new PlainNode { Id = "P", FailTimes = 1 });
        flow.Connect("E", "S1");
        flow.Connect("S1", "P");
        // 不支持就地恢复的节点：处理流程给"就地恢复"被拒，兜底里选"回退到入口 Op 再继续"。
        var handler = Handler(probe, new DirectNode { Id = "D", Kind = WorkflowResumeKind.RecoverInPlace });
        var fallback = new RecordingFallback(_ => new WorkflowFaultFallbackDecision(WorkflowFaultFallbackChoice.UseDirectives,
            new Dictionary<string, WorkflowResumeDirective> { ["A"] = WorkflowResumeDirective.RollBack("Op") }));
        using var center = CreateCenter(probe, handler, fallback);

        var result = await center.AddFlow(flow.Document, "A").RunAsync().WaitAsync(TimeSpan.FromSeconds(10));

        Assert.True(result.Success, result.Message);
        Assert.Contains("不支持就地恢复", Assert.Single(fallback.Requests).Failure);
        Assert.Equal(2, probe.Count("E"));
        Assert.Equal(2, probe.Count("S1"));
        Assert.Equal(2, probe.Count("P"));
        Assert.Contains(center.ExecutionRecord.EntriesOf("A"), entry => entry.NodeId == "S1" && entry.Round == 0 && entry.Undone);
    }

    [Fact]
    public async Task NoHandlerAndNoFallback_StopsFlow()
    {
        var probe = new Probe();
        var flow = new TestFlow(probe);
        flow.Add(new StepNode { Id = "S", FailTimes = 1 });
        using var center = new WorkflowRunCenter(TestFlow.Catalog(), flow.Handlers(), new WorkflowRunCenterOptions { FaultSettleMilliseconds = 0 });

        var result = await center.AddFlow(flow.Document, "A").RunAsync().WaitAsync(TimeSpan.FromSeconds(10));

        Assert.False(result.Success);
        Assert.Equal(1, probe.Count("S"));
    }

    [Fact]
    public async Task RoutingTable_IsUsedBeforeDefaultHandler()
    {
        var probe = new Probe();
        var flow = new TestFlow(probe);
        flow.Add(new StepNode { Id = "S", FailTimes = 1 });
        var routed = Handler(probe, new DirectNode { Id = "Routed", Kind = WorkflowResumeKind.RecoverInPlace });
        var fallbackHandler = Handler(probe, new DirectNode { Id = "Default", Kind = WorkflowResumeKind.Stop });
        var options = new WorkflowRunCenterOptions { DefaultFaultHandler = fallbackHandler, FaultSettleMilliseconds = 0 };
        options.FaultRoutes.Add(new WorkflowFaultRoute("报警 0", session => session.PrimaryFault.AlarmCode == 0, routed));
        using var center = new WorkflowRunCenter(TestFlow.Catalog().Register(DirectDescriptor()), HandlersWithDirect(probe), options);

        var result = await center.AddFlow(flow.Document, "A").RunAsync().WaitAsync(TimeSpan.FromSeconds(10));

        Assert.True(result.Success, result.Message);
        Assert.Equal(1, probe.Count("Routed"));
        Assert.Equal(0, probe.Count("Default"));
    }

    // ---- 辅助 ----

    private readonly ConcurrentDictionary<string, TaskCompletionSource> Gates = new(StringComparer.Ordinal);

    private TestFlow LoopingFlow(string name)
    {
        var flow = new TestFlow(new Probe());
        flow.Add(new TickNode { Id = "Tick", Name = name });
        flow.Add(new StepNode { Id = "Wait" });
        flow.Connect("Tick", "Wait");
        flow.Connect("Wait", "Tick");
        return flow;
    }

    private static WorkflowDocument Handler(Probe probe, params WorkflowNodeModel[] nodes)
    {
        var document = new WorkflowDocument { Name = "处理流程", EntryNodeId = nodes[0].Id };
        foreach (var node in nodes)
            document.Nodes.Add(new WorkflowCanvasNode { Node = node });
        for (var index = 1; index < nodes.Length; index++)
            document.Connections.Add(new WorkflowConnectionModel { FromNodeId = nodes[index - 1].Id, FromPort = WorkflowPorts.Success, ToNodeId = nodes[index].Id });
        return document;
    }

    private WorkflowRunCenter CreateCenter(Probe probe, WorkflowDocument handler, IWorkflowFaultFallback? fallback = null) =>
        new(TestFlow.Catalog().Register(DirectDescriptor()).Register(WorkflowNodeDescriptor.Create<TickNode>(ports: new[] { WorkflowPortDescriptor.Input(maxConnections: int.MaxValue), WorkflowPortDescriptor.Output() })),
            HandlersWithDirect(probe).Register(new TickHandler(probe, Gates)),
            new WorkflowRunCenterOptions { DefaultFaultHandler = handler, FaultFallback = fallback, FaultSettleMilliseconds = 20 });

    private static WorkflowNodeDescriptor DirectDescriptor() =>
        WorkflowNodeDescriptor.Create<DirectNode>(ports: new[] { WorkflowPortDescriptor.Input(), WorkflowPortDescriptor.Output() });

    private static WorkflowNodeHandlerCatalog HandlersWithDirect(Probe probe) =>
        new TestFlow(probe).Handlers().Register(new DirectHandler(probe));

    [WorkflowNode("T.Direct")]
    private sealed class DirectNode : WorkflowNodeModel
    {
        public override string NodeType => "T.Direct";
        public string? Flow { get; set; }
        public WorkflowResumeKind Kind { get; set; }
    }

    private sealed class DirectHandler(Probe probe) : WorkflowNodeHandler<DirectNode>
    {
        protected override ValueTask<NodeExecutionResult> ExecuteAsync(DirectNode node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
        {
            probe.Hit(node.Id);
            var session = (WorkflowFaultSession)context.Services.GetService(typeof(WorkflowFaultSession))!;
            session.SetDirective(node.Flow, new WorkflowResumeDirective(node.Kind, Message: "测试去向"));
            return ValueTask.FromResult(NodeExecutionResult.Continue());
        }
    }

    /// <summary>循环里的一拍：记下到达，直到本流程的闸门打开后结束本路径。</summary>
    [WorkflowNode("T.Tick")]
    private sealed class TickNode : WorkflowNodeModel
    {
        public override string NodeType => "T.Tick";
        public string Name { get; set; } = string.Empty;
    }

    private sealed class TickHandler(Probe probe, ConcurrentDictionary<string, TaskCompletionSource> gates) : WorkflowNodeHandler<TickNode>
    {
        protected override async ValueTask<NodeExecutionResult> ExecuteAsync(TickNode node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
        {
            probe.Hit(node.Name + ".Tick");
            if (gates.GetOrAdd(node.Name, _ => new TaskCompletionSource()).Task.IsCompleted)
                return NodeExecutionResult.Complete();
            await Task.Delay(10, cancellationToken);
            return NodeExecutionResult.Continue();
        }
    }

    private sealed class RecordingFallback(Func<WorkflowFaultFallbackRequest, WorkflowFaultFallbackDecision> decide) : IWorkflowFaultFallback
    {
        public List<WorkflowFaultFallbackRequest> Requests { get; } = new();
        public Task<WorkflowFaultFallbackDecision> DecideAsync(WorkflowFaultFallbackRequest request, CancellationToken cancellationToken)
        {
            lock (Requests) Requests.Add(request);
            return Task.FromResult(decide(request));
        }
    }
}
