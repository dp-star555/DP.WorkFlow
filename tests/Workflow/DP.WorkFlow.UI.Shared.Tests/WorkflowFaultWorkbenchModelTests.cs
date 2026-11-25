using DP.WorkFlow.UI;

namespace DP.WorkFlow.Tests;

public sealed class WorkflowFaultWorkbenchModelTests
{
    [Fact]
    public async Task Fallback_ShowsStopPointAndReleasesWithChosenDirective()
    {
        var options = new WorkflowRunCenterOptions { FaultSettleMilliseconds = 10 };
        using var center = CreateCenter(options);
        using var model = new WorkflowFaultWorkbenchModel(center, options);
        Assert.Same(model, options.FaultFallback);

        var run = center.AddFlow(FailingOnce("S"), "A").RunAsync();
        await WaitUntilAsync(() => model.IsAwaitingDecision);

        var view = Assert.IsType<WorkflowFaultSessionView>(model.GetCurrent());
        Assert.Equal("等待操作员选择", view.StateText);
        Assert.NotNull(view.Fallback);
        Assert.False(view.Fallback!.HasHandler);
        var flow = Assert.Single(view.Flows);
        Assert.Equal("A", flow.FlowName);
        Assert.True(flow.NeedsDirective);
        var stop = Assert.Single(flow.StopPoints);
        Assert.Equal(WorkflowStopReason.Fault, stop.Reason);
        Assert.Equal("S", stop.Position.NodeId);
        // 可直接重做、没有核实并补完、不在循环里：能重做和停止，不能继续原操作和结束本圈。
        Assert.True(Option(flow, WorkflowResumeKind.Redo).Allowed);
        Assert.True(Option(flow, WorkflowResumeKind.Stop).Allowed);
        Assert.False(Option(flow, WorkflowResumeKind.Continue).Allowed);
        Assert.False(Option(flow, WorkflowResumeKind.EndLap).Allowed);
        Assert.NotNull(Option(flow, WorkflowResumeKind.Continue).Reason);

        // 不允许的去向、缺去向都不放行，询问保留。
        Assert.Throws<InvalidOperationException>(() => model.Release(new Dictionary<string, WorkflowResumeDirective> { ["A"] = WorkflowResumeDirective.Continue() }));
        Assert.Throws<InvalidOperationException>(() => model.Release(new Dictionary<string, WorkflowResumeDirective>()));
        Assert.Throws<InvalidOperationException>(() => model.RerunHandler());
        Assert.True(model.IsAwaitingDecision);

        model.Release(new Dictionary<string, WorkflowResumeDirective> { ["A"] = WorkflowResumeDirective.Redo() });

        var result = await run.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(result.Success, result.Message);
        Assert.False(model.IsAwaitingDecision);
        await WaitUntilAsync(() => model.History.Count == 1);
        Assert.Equal(WorkflowFaultSessionState.Ended, model.History[0].State);
    }

    [Fact]
    public async Task StopAll_EndsAffectedFlows()
    {
        var options = new WorkflowRunCenterOptions { FaultSettleMilliseconds = 10 };
        using var center = CreateCenter(options);
        using var model = new WorkflowFaultWorkbenchModel(center, options);

        var run = center.AddFlow(FailingOnce("S"), "A").RunAsync();
        await WaitUntilAsync(() => model.IsAwaitingDecision);
        model.StopAll();

        var result = await run.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False(result.Success);
    }

    [Fact]
    public void Dispose_RestoresPreviousFallback()
    {
        var previous = new NeverFallback();
        var options = new WorkflowRunCenterOptions { FaultFallback = previous };
        using var center = CreateCenter(options);
        var model = new WorkflowFaultWorkbenchModel(center, options);
        Assert.Same(model, options.FaultFallback);

        model.Dispose();

        Assert.Same(previous, options.FaultFallback);
    }

    [Fact]
    public void WithoutOptions_OnlyDisplays()
    {
        var options = new WorkflowRunCenterOptions();
        using var center = CreateCenter(options);
        using var model = new WorkflowFaultWorkbenchModel(center);

        Assert.False(model.UsesFallback);
        Assert.Null(options.FaultFallback);
        Assert.Null(model.GetCurrent());
        Assert.Throws<InvalidOperationException>(() => model.StopAll());
    }

    [Fact]
    public async Task Overview_ShowsFlowStateNoticesAndFault()
    {
        var options = new WorkflowRunCenterOptions { FaultSettleMilliseconds = 10 };
        using var center = CreateCenter(options);
        using var model = new WorkflowFaultWorkbenchModel(center, options);
        var overview = new WorkflowRunCenterOverviewModel(center);
        var flow = center.AddFlow(FailingOnce("S"), "A");
        flow.Host.Notices.Raise(Guid.Empty, "$", "S", "超时", "相机响应慢");

        var idle = Assert.Single(overview.GetRows());
        Assert.Equal("未运行", idle.StateText);
        Assert.Equal(1, idle.NoticeCount);

        var run = overview.Start("A");
        await WaitUntilAsync(() => model.IsAwaitingDecision);
        Assert.Equal("故障处理中", Assert.Single(overview.GetRows()).StateText);
        model.Release(new Dictionary<string, WorkflowResumeDirective> { ["A"] = WorkflowResumeDirective.Redo() });
        Assert.True((await run.WaitAsync(TimeSpan.FromSeconds(10))).Success);

        var done = Assert.Single(overview.GetRows());
        Assert.Equal("已完成", done.StateText);
        Assert.Equal(1, done.RecoveryRound);
        Assert.Throws<InvalidOperationException>(() => { _ = overview.Start("missing"); });
    }

    private static WorkflowFaultDirectiveOption Option(WorkflowFaultFlowView flow, WorkflowResumeKind kind) =>
        flow.Options.First(option => option.Directive.Kind == kind);

    private static WorkflowRunCenter CreateCenter(WorkflowRunCenterOptions options) =>
        new(new WorkflowNodeCatalog().Register(WorkflowNodeDescriptor.Create<FlakyNode>(ports: new[] { WorkflowPortDescriptor.Input(), WorkflowPortDescriptor.Output() })),
            new WorkflowNodeHandlerCatalog().Register(new FlakyHandler()),
            options);

    private static WorkflowDocument FailingOnce(string id)
    {
        var document = new WorkflowDocument { Name = "工作台测试", EntryNodeId = id };
        document.Nodes.Add(new WorkflowCanvasNode { Node = new FlakyNode { Id = id, Title = id } });
        return document;
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!condition())
            await Task.Delay(5, timeout.Token);
    }

    /// <summary>可直接重做的步骤：第一次执行失败，之后成功。</summary>
    [WorkflowNode("UiTest.Flaky")]
    [WorkflowRecovery(WorkflowRecoveryCategory.Redoable)]
    private sealed class FlakyNode : WorkflowNodeModel
    {
        public override string NodeType => "UiTest.Flaky";
    }

    private sealed class FlakyHandler : WorkflowNodeHandler<FlakyNode>
    {
        private int _count;

        protected override ValueTask<NodeExecutionResult> ExecuteAsync(FlakyNode node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken) =>
            ValueTask.FromResult(Interlocked.Increment(ref _count) == 1
                ? NodeExecutionResult.Fail("第一次失败")
                : NodeExecutionResult.Complete());
    }

    private sealed class NeverFallback : IWorkflowFaultFallback
    {
        public Task<WorkflowFaultFallbackDecision> DecideAsync(WorkflowFaultFallbackRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(WorkflowFaultFallbackDecision.StopAll);
    }
}
