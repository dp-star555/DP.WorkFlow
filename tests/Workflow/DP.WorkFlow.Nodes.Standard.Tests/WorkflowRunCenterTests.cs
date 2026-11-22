namespace DP.WorkFlow.Tests;

public sealed class WorkflowRunCenterTests
{
    private static WorkflowRunCenter CreateCenter(WorkflowRunCenterOptions? options = null) =>
        new(new WorkflowNodeCatalog().RegisterStandardNodes(), new WorkflowNodeHandlerCatalog().RegisterStandardNodeHandlers(), options);

    [Fact]
    public async Task Flows_ShareOnePublicDataCenter()
    {
        using var center = CreateCenter();
        var first = center.AddFlow(Increment("加一流程", "Count", "10"));
        var second = center.AddFlow(Increment("再加流程", "Count", "99"));

        Assert.Equal(10, center.PublicData.Values.ReadSnapshot("Count")!.Value);
        Assert.True((await first.RunAsync()).Success);
        Assert.True((await second.RunAsync()).Success);

        Assert.Equal(12, center.PublicData.Values.ReadSnapshot("Count")!.Value);
        Assert.Equal(new[] { "加一流程", "再加流程" }, center.GetStates().Select(item => item.Name));
        Assert.All(center.GetStates(), item => Assert.Equal(E_WorkflowExecutionState.Completed, item.State));
    }

    [Fact]
    public void AddFlow_RefusesManifestConflict()
    {
        using var center = CreateCenter();
        center.AddFlow(Increment("甲", "Shared", "1"));
        var document = Build("乙", new SignalSetNodeModel { Id = "Set", Title = "设置", SignalKey = "Shared" });
        document.PublicData.Add(WorkflowPublicDataDeclaration.Signal("Shared"));

        Assert.Throws<WorkflowPublicDataConflictException>(() => center.AddFlow(document));
        Assert.Single(center.Flows);
        Assert.Throws<InvalidOperationException>(() => center.AddFlow(Increment("甲", "Shared", "1")));
    }

    [Fact]
    public async Task Outlets_ReceiveEventsAndLogLinesWithTopLevelFlowName()
    {
        using var center = CreateCenter();
        var global = new List<string>();
        center.Outlet.OnLog = (_, line) => { lock (global) global.Add(line); };
        var flow = center.AddFlow(Increment("上料流程", "Count", "0"));
        var flowEvents = new List<WorkflowOutletEvent>();
        flow.Outlet.OnEvent = item => flowEvents.Add(item);

        Assert.True((await flow.RunAsync()).Success);
        Assert.True(flow.Outlet.WaitIdle(TimeSpan.FromSeconds(5)));
        Assert.True(center.Outlet.WaitIdle(TimeSpan.FromSeconds(5)));

        Assert.Contains(global, line => line.StartsWith("INFO [上料流程] 运行开始", StringComparison.Ordinal));
        Assert.Contains(global, line => line.StartsWith("INFO [上料流程] 节点 加一#1 完成，出口=Success，耗时 ", StringComparison.Ordinal));
        Assert.All(flowEvents, item => Assert.Equal("上料流程", item.FlowName));
        Assert.DoesNotContain(flowEvents, item => item.Level < WorkflowLogLevel.Info);
        Assert.Equal("RunCompleted", flowEvents[^1].Event.EventType);
    }

    [Fact]
    public async Task ExistingSink_StillReceivesEvents()
    {
        using var center = CreateCenter();
        var sink = new CountingSink();
        var options = new WorkflowExecutionOptions();
        options.Recording.Sink = sink;
        var flow = center.AddFlow(Increment("流程", "Count", "0"), executionOptions: options);

        Assert.True((await flow.RunAsync()).Success);

        Assert.True(sink.Count > 0);
    }

    [Fact]
    public void Outlet_DeliversInOrderAndSwallowsCallbackFailures()
    {
        using var outlet = new WorkflowOutlet(new WorkflowOutletOptions { MinimumLevel = WorkflowLogLevel.Debug });
        var seen = new List<long>();
        outlet.OnEvent = item =>
        {
            seen.Add(item.Event.Sequence);
            if (item.Event.Sequence == 2) throw new InvalidOperationException("外部出错");
        };

        for (var sequence = 1; sequence <= 50; sequence++)
            outlet.Post(Item(sequence));

        Assert.True(outlet.WaitIdle(TimeSpan.FromSeconds(5)));
        Assert.Equal(Enumerable.Range(1, 50).Select(value => (long)value), seen);
        Assert.Equal(1, outlet.CallbackFailureCount);
        Assert.Equal(50, outlet.DeliveredCount);
    }

    [Fact]
    public void Outlet_DropsOldestWhenFull()
    {
        using var gate = new ManualResetEventSlim();
        using var outlet = new WorkflowOutlet(new WorkflowOutletOptions { MinimumLevel = WorkflowLogLevel.Debug, Capacity = 2 });
        var seen = new List<long>();
        using var firstTaken = new ManualResetEventSlim();
        outlet.OnEvent = item =>
        {
            seen.Add(item.Event.Sequence);
            firstTaken.Set();
            gate.Wait();
        };

        outlet.Post(Item(1));
        Assert.True(firstTaken.Wait(TimeSpan.FromSeconds(5)));
        var dropped = 0;
        for (var sequence = 2; sequence <= 5; sequence++)
            dropped += outlet.Post(Item(sequence));
        gate.Set();

        Assert.True(outlet.WaitIdle(TimeSpan.FromSeconds(5)));
        Assert.Equal(2, dropped);
        Assert.Equal(2, outlet.DroppedCount);
        Assert.Equal(new long[] { 1, 4, 5 }, seen);
    }

    [Fact]
    public void Outlet_FiltersByLevelAndCategory()
    {
        using var outlet = new WorkflowOutlet(new WorkflowOutletOptions
        {
            MinimumLevel = WorkflowLogLevel.Debug,
            Categories = new HashSet<WorkflowRunEventCategory> { WorkflowRunEventCategory.Fault }
        });

        Assert.False(outlet.Accepts(WorkflowRunEventCategory.NodeExecution, WorkflowLogLevel.Info));
        Assert.True(outlet.Accepts(WorkflowRunEventCategory.Fault, WorkflowLogLevel.Error));
        Assert.False(new WorkflowOutlet().Accepts(WorkflowRunEventCategory.Trace, WorkflowLogLevel.Debug));
    }

    [Fact]
    public void FormatLine_ShowsLoopIterationAndChildFlowName()
    {
        var identity = new WorkflowExecutionIdentity(Guid.NewGuid(), 1, Array.Empty<long>(), Array.Empty<long>(), 3, 2);
        var runEvent = new WorkflowRunEvent(Guid.NewGuid(), 1, DateTimeOffset.UtcNow, WorkflowRunEventCategory.NodeExecution, "NodeCompleted",
            WorkflowEventWriteMode.Buffered, "子流程", "Shot", "Action", identity, null, null, 1,
            new Dictionary<string, WorkflowTraceValue>
            {
                ["SelectedPort"] = new(WorkflowTraceValueKind.Scalar, "System.String", "OK", "OK"),
                ["Elapsed"] = new(WorkflowTraceValueKind.Scalar, "System.Double", "12.43", 12.43)
            });

        Assert.Equal("[上料流程/子流程] 节点 拍照#3(第2圈) 完成，出口=OK，耗时 12.4ms", WorkflowOutlet.FormatLine("上料流程", runEvent, "拍照"));
        Assert.Equal(WorkflowLogLevel.Info, WorkflowOutlet.GetLevel(runEvent));
    }

    [Fact]
    public void FormatLine_ShowsCanvasPathOfSubCanvasEvents()
    {
        var identity = new WorkflowExecutionIdentity(Guid.NewGuid(), 1, Array.Empty<long>(), Array.Empty<long>(), 1);
        var runEvent = new WorkflowRunEvent(Guid.NewGuid(), 1, DateTimeOffset.UtcNow, WorkflowRunEventCategory.NodeExecution, "NodeStarted",
            WorkflowEventWriteMode.Buffered, "上料流程", "Shot", "Action", identity, null, null, 1, null,
            Parents: new[] { new WorkflowTraceParent("Loop", 3), new WorkflowTraceParent("Block") });

        Assert.Equal("[上料流程/Loop@3/Block] 节点 拍照#1 开始", WorkflowOutlet.FormatLine("上料流程", runEvent, "拍照"));
        Assert.Equal("$/Loop/Block", runEvent.CanvasPath);
    }

    private static WorkflowOutletEvent Item(long sequence) =>
        new("流程", new WorkflowRunEvent(Guid.Empty, sequence, DateTimeOffset.UtcNow, WorkflowRunEventCategory.Trace, "Step",
            WorkflowEventWriteMode.Buffered, null, null, null, null, null, null, 1, null), WorkflowLogLevel.Debug, "line");

    private static WorkflowDocument Increment(string name, string key, string defaultValue)
    {
        var document = Build(name, new SignalValueSetNodeModel
        {
            Id = "Add", Title = "加一", SignalKey = key, ValueType = E_SignalValueType.Int32,
            WriteMode = E_SignalValueWriteMode.Increment, Value = WorkflowInput<string>.FromLiteral("1")
        });
        document.PublicData.Add(WorkflowPublicDataDeclaration.Value(key, E_SignalValueType.Int32, defaultValue));
        return document;
    }

    private static WorkflowDocument Build(string name, IWorkflowNodeModel node)
    {
        var document = new WorkflowDocument { Name = name, EntryNodeId = node.Id };
        document.CanvasProjection.Nodes.Add(new WorkflowCanvasNode { Node = node });
        return document;
    }

    private sealed class CountingSink : IWorkflowRunEventSink
    {
        private int _count;
        public int Count => Volatile.Read(ref _count);

        public ValueTask<WorkflowRunEventWriteResult> WriteAsync(IReadOnlyList<WorkflowRunEvent> events, bool requestImmediateFlush, CancellationToken cancellationToken)
        {
            Interlocked.Add(ref _count, events.Count);
            return new ValueTask<WorkflowRunEventWriteResult>(WorkflowRunEventWriteResult.Success);
        }

        public ValueTask FlushAsync(CancellationToken cancellationToken) => default;
    }
}
