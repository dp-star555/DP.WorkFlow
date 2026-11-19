namespace DP.WorkFlow.Tests;

/// <summary>
/// R4-a 出口事件：暂停 / 继续、外部挂起的增减都写入运行记录，带原因；运行开始带流程身份。
/// </summary>
public sealed class WorkflowControlEventTests
{
    [Fact]
    public async Task PauseHoldResume_AreRecordedInOrderWithReasons()
    {
        using var entered = new ManualResetEventSlim();
        using var proceed = new ManualResetEventSlim();
        var sink = new CollectingSink();
        var engine = CreateEngine(sink, entered, proceed);

        var run = Task.Run(() => engine.RunAsync());
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
        engine.Pause();
        engine.AddExternalHold("门未关");
        engine.AddExternalHold("门未关"); // 重复原因不再记录。
        engine.Resume();                  // 仍有挂起：不继续，也不记 Resumed。
        engine.RemoveExternalHold("门未关");
        proceed.Set();
        var result = await run;

        Assert.True(result.Success, result.Message);
        var control = sink.Events
            .Where(item => item.EventType is "Paused" or "Resumed" or "HoldAdded" or "HoldRemoved")
            .Select(item => item.EventType + ":" + Text(item, "Reason") + Text(item, "HoldReasons"))
            .ToArray();
        Assert.Equal(new[] { "Paused:", "HoldAdded:门未关", "HoldRemoved:门未关", "Resumed:" }, control);
        var paused = sink.Events.Single(item => item.EventType == "Paused");
        Assert.Equal(true, paused.Data!["ManualPause"].Scalar);
    }

    [Fact]
    public async Task RunStartedWhileHeld_RecordsPausedRightAfterRunStarted()
    {
        using var entered = new ManualResetEventSlim();
        using var proceed = new ManualResetEventSlim(initialState: true);
        var sink = new CollectingSink();
        var engine = CreateEngine(sink, entered, proceed);
        engine.AddExternalHold("设备未就绪"); // 运行前：不记录，只决定初始状态。

        var run = Task.Run(() => engine.RunAsync());
        await WaitUntilAsync(() => engine.State == E_WorkflowExecutionState.Paused);
        engine.RemoveExternalHold("设备未就绪");
        var result = await run;

        Assert.True(result.Success, result.Message);
        var types = sink.Events.Select(item => item.EventType).ToArray();
        Assert.Equal("RunStarted", types[0]);
        Assert.Equal("Paused", types[1]);
        Assert.Equal("设备未就绪", Text(sink.Events[1], "HoldReasons"));
        Assert.Equal(new[] { "HoldRemoved", "Resumed" }, types.Skip(2).Take(2));
        var started = sink.Events[0];
        Assert.Equal("控制事件", Text(started, "WorkflowName"));
        Assert.Equal("$", Text(started, "PlanPath"));
        Assert.Equal("Gate", Text(started, "EntryNodeId"));
    }

    private static WorkflowEngine CreateEngine(CollectingSink sink, ManualResetEventSlim entered, ManualResetEventSlim proceed)
    {
        var gate = new ControlTestNode { Id = "Gate", Title = "门" };
        var document = new WorkflowDocument { Name = "控制事件", EntryNodeId = gate.Id };
        document.CanvasProjection.Nodes.Add(new WorkflowCanvasNode { Node = gate });
        var catalog = new WorkflowNodeCatalog().Register<ControlTestNode>(1, WorkflowPortDescriptor.Input(), WorkflowPortDescriptor.Output());
        var handlers = new WorkflowNodeHandlerCatalog().Register(new GateHandler(entered, proceed));
        return new WorkflowEngine(
            new WorkflowCompiler(catalog).Compile(document),
            handlers,
            new WorkflowContext(),
            new WorkflowExecutionOptions { Recording = new WorkflowRunRecordingOptions { Sink = sink } });
    }

    private static string? Text(WorkflowRunEvent item, string key) =>
        item.Data is not null && item.Data.TryGetValue(key, out var value) ? value.Scalar?.ToString() ?? value.Text : null;

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition())
            await Task.Delay(5, timeout.Token);
    }

    [WorkflowNode("ControlTest")]
    private sealed class ControlTestNode : WorkflowNodeModel
    {
        public override string NodeType => "ControlTest";
    }

    private sealed class GateHandler(ManualResetEventSlim entered, ManualResetEventSlim proceed) : WorkflowNodeHandler<ControlTestNode>
    {
        protected override ValueTask<NodeExecutionResult> ExecuteAsync(
            ControlTestNode node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
        {
            entered.Set();
            proceed.Wait(cancellationToken);
            return ValueTask.FromResult(NodeExecutionResult.Complete());
        }
    }

    private sealed class CollectingSink : IWorkflowRunEventSink
    {
        private readonly List<WorkflowRunEvent> _events = new();

        public IReadOnlyList<WorkflowRunEvent> Events
        {
            get
            {
                lock (_events)
                    return _events.OrderBy(item => item.Sequence).ToArray();
            }
        }

        public ValueTask<WorkflowRunEventWriteResult> WriteAsync(
            IReadOnlyList<WorkflowRunEvent> events, bool requestImmediateFlush, CancellationToken cancellationToken)
        {
            lock (_events)
                _events.AddRange(events);
            return ValueTask.FromResult(WorkflowRunEventWriteResult.Success);
        }

        public ValueTask FlushAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }
}
