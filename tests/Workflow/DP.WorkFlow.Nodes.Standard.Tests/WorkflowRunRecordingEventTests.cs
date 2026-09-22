using System.Collections;

namespace DP.WorkFlow.Tests;

/// <summary>
/// §20验收：引擎接入 Recorder 后的生命周期事件、统一序号、执行身份和数据血缘。
/// 事件必须能按执行身份区分并发运行，并且失败尝试不产生输出提交事实。
/// </summary>
public sealed class WorkflowRunRecordingEventTests
{
    [Fact]
    public async Task Run_RecordsLifecycleAndNodeEventsWithUniqueMonotonicSequence()
    {
        var sink = new CollectingSink();
        var engine = CreateEngine(BuildLinearDocument(), EmptyServices(), sink);

        var result = await engine.RunAsync();

        Assert.True(result.Success, result.Message);
        var events = sink.Events;
        Assert.Equal(events.Select(item => item.Sequence).Distinct().Count(), events.Count);
        Assert.All(
            events.Zip(events.Skip(1)),
            pair => Assert.True(pair.First.Sequence < pair.Second.Sequence));
        Assert.Equal("RunStarted", events[0].EventType);
        Assert.Contains(events, item => item.EventType == "NodeStarted" && item.NodeId == "A");
        Assert.Contains(events, item => item.EventType == "NodeCompleted" && item.NodeId == "B");
        Assert.Equal("RunCompleted", events[^1].EventType);
        // 每个节点事件都携带完整执行身份，供并发运行按执行身份区分。
        Assert.All(
            events.Where(item => item.Category == WorkflowRunEventCategory.NodeExecution),
            item => Assert.NotNull(item.ExecutionIdentity));
    }

    [Fact]
    public async Task ParallelBranchEvents_CarryTokenAndScope()
    {
        var sink = new CollectingSink();
        var engine = CreateEngine(BuildParallelDocument(), EmptyServices(), sink, startNodeId: "Parallel");

        var result = await engine.RunAsync();

        Assert.True(result.Success, result.Message);
        var branchEvents = sink.Events.Where(item => item.NodeId is "BranchA" or "BranchB").ToArray();
        Assert.NotEmpty(branchEvents);
        Assert.All(branchEvents, item =>
        {
            Assert.NotNull(item.ExecutionIdentity);
            Assert.Single(item.ExecutionIdentity!.ScopeIds);
        });
        // 两个分支属于不同 Token，事件因此可以按执行身份区分。
        Assert.Equal(2, branchEvents.Select(item => item.ExecutionIdentity!.TokenId).Distinct().Count());
    }

    [Fact]
    public async Task LoopEvents_CarryNodeExecutionCountAndLoopIteration()
    {
        var sink = new CollectingSink();
        var engine = CreateEngine(BuildLoopDocument(), EmptyServices(), sink, startNodeId: "Loop");

        var result = await engine.RunAsync();

        Assert.True(result.Success, result.Message);
        var loopEvents = sink.Events
            .Where(item => item.EventType == "NodeStarted" && item.NodeId == "Loop")
            .ToArray();
        var bodyEvents = sink.Events
            .Where(item => item.EventType == "NodeStarted" && item.NodeId == "Body")
            .ToArray();
        // 循环节点被进入 4 次（3 次循环 + 1 次完成出口），循环体执行 3 次。
        Assert.Equal(new[] { 1, 2, 3, 4 }, loopEvents.Select(item => item.ExecutionIdentity!.NodeExecutionCount));
        Assert.Equal(new int?[] { 1, 2, 3, 4 }, loopEvents.Select(item => item.ExecutionIdentity!.LoopIteration));
        Assert.Equal(new[] { 1, 2, 3 }, bodyEvents.Select(item => item.ExecutionIdentity!.NodeExecutionCount));
    }

    [Fact]
    public async Task InputResolved_RecordsSourceOutputSequence()
    {
        var sink = new CollectingSink();
        var services = new WorkflowServiceProvider().Add<IWorkflowActionRegistry>(
            new WorkflowActionRegistry().Register(
                "Produce",
                (_, _) => ValueTask.FromResult<object?>(new BooleanOutput(true))));
        var engine = CreateEngine(BuildBindingDocument(), services, sink, startNodeId: "Producer");

        var result = await engine.RunAsync();

        Assert.True(result.Success, result.Message);
        var producer = Assert.Single(engine.Context.ExportNodeOutputs(), item => item.NodeId == "Producer");
        var resolved = Assert.Single(sink.Events, item => item.EventType == "InputResolved");
        Assert.Equal("NodeOutput", resolved.Data!["SourceKind"].Text);
        Assert.Equal("Producer", resolved.Data["SourceNodeId"].Text);
        Assert.Equal(producer.ExecutionSequence, Convert.ToInt64(resolved.Data["SourceOutputSequence"].Scalar));
    }

    [Fact]
    public async Task FailedNode_DoesNotProduceOutputCommitted()
    {
        var sink = new CollectingSink();
        var document = new WorkflowDocument { Name = "失败不提交" };
        var failing = new ActionNodeModel { Id = "Failing", Title = "失败", FunctionKey = "Fail" };
        document.CanvasProjection.Nodes.Add(new WorkflowCanvasNode { Node = failing });
        var services = new WorkflowServiceProvider().Add<IWorkflowActionRegistry>(
            new WorkflowActionRegistry().Register(
                "Fail",
                (_, _) => throw new InvalidOperationException("设备未就绪。")));
        var engine = CreateEngine(document, services, sink, startNodeId: "Failing");

        var result = await engine.RunAsync();

        Assert.False(result.Success);
        Assert.Contains("设备未就绪", result.Message);
        // 失败尝试没有正式提交输出，因此不得产生 OutputCommitted 事实。
        Assert.DoesNotContain(sink.Events, item => item.EventType == "OutputCommitted");
        Assert.Contains(sink.Events, item => item.EventType == "NodeFailed" && item.NodeId == "Failing");
        Assert.Equal("RunFaulted", sink.Events[^1].EventType);
    }

    [Fact]
    public async Task Snapshot_DoesNotAccumulateHistoryAcrossExecutions()
    {
        var sink = new CollectingSink();
        var engine = CreateEngine(BuildLoopDocument(iterations: 50), EmptyServices(), sink, startNodeId: "Loop");

        var result = await engine.RunAsync();
        var snapshot = engine.GetRuntimeSnapshot();

        Assert.True(result.Success, result.Message);
        // 50 次循环后，快照仍只为每个节点保留一条实时状态，不随执行历史线性增长。
        Assert.Equal(3, snapshot.Nodes.Count);
        Assert.Equal(51, snapshot.Nodes["Loop"].ExecutionCount);
        Assert.Empty(snapshot.ActiveNodeIds);
        // 快照只表达当前状态，不携带输出、故障和恢复历史。
        Assert.Null(snapshot.CurrentFault);
        Assert.Null(snapshot.CurrentRecovery);
    }

    [Fact]
    public async Task TraceDataThatCannotBeEnumerated_DoesNotFaultTheRun()
    {
        var sink = new CollectingSink();
        var document = new WorkflowDocument { Name = "记录异常", EntryNodeId = "Trace" };
        var canvas = document.CanvasProjection;
        canvas.Nodes.Add(new WorkflowCanvasNode { Node = new ThrowingTraceNodeModel { Id = "Trace", Title = "抛异常跟踪" } });
        canvas.Nodes.Add(new WorkflowCanvasNode { Node = new EndNodeModel { Id = "End", Title = "结束" } });
        canvas.Connections.Add(Connect("Trace", WorkflowPorts.Success, "End"));
        var engine = CreateEngine(document, EmptyServices(), sink, startNodeId: "Trace");

        var result = await engine.RunAsync();

        // 记录链路必须完全 fail-open：调用方自定义跟踪数据自身无法枚举时，
        // 异常不得穿过 Recorder 把节点变成 Fault。
        Assert.True(result.Success, result.Message);
        Assert.DoesNotContain(sink.Events, item => item.EventType == "NodeFailed");
        Assert.Equal("RunCompleted", sink.Events[^1].EventType);
        // 失败被记为记录降级（可观测），而不是静默吞掉。
        var health = engine.RecordingHealth;
        Assert.Equal(E_WorkflowRecordingHealth.Degraded, health.State);
        Assert.True(health.DiagnosticCount > 0);
        Assert.NotNull(health.LastDiagnostic);
        // 降级不得伪装成 Sink 写入失败。
        Assert.Equal(0L, health.FailedWriteCount);
    }

    private static IServiceProvider EmptyServices() =>
        new WorkflowServiceProvider().Add<IWorkflowActionRegistry>(
            new WorkflowActionRegistry()
                .Register("A", (_, _) => ValueTask.FromResult<object?>(null))
                .Register("B", (_, _) => ValueTask.FromResult<object?>(null))
                .Register("BranchA", (_, _) => ValueTask.FromResult<object?>(null))
                .Register("BranchB", (_, _) => ValueTask.FromResult<object?>(null))
                .Register("Body", (_, _) => ValueTask.FromResult<object?>(null)));

    private static WorkflowEngine CreateEngine(
        WorkflowDocument document,
        IServiceProvider services,
        IWorkflowRunEventSink sink,
        string startNodeId = "A")
    {
        var catalog = new WorkflowNodeCatalog()
            .RegisterStandardNodes()
            .Register(WorkflowNodeDescriptor.Create<BindingNodeModel>(
                ports: new[]
                {
                    WorkflowPortDescriptor.Input(maxConnections: int.MaxValue),
                    WorkflowPortDescriptor.Output()
                }))
            .Register(WorkflowNodeDescriptor.Create<ThrowingTraceNodeModel>(
                ports: new[]
                {
                    WorkflowPortDescriptor.Input(maxConnections: int.MaxValue),
                    WorkflowPortDescriptor.Output()
                }));
        document.EntryNodeId = startNodeId;
        var definition = new WorkflowCompiler(catalog).Compile(document);
        var handlers = new WorkflowNodeHandlerCatalog()
            .Register(new ActionNodeHandler())
            .Register(new EndNodeHandler())
            .Register(new LoopNodeHandler())
            .Register(new ParallelAllNodeHandler())
            .Register(new WaitAllInputsCompletedNodeHandler())
            .Register(new BindingNodeHandler())
            .Register(new ThrowingTraceNodeHandler());
        var options = new WorkflowExecutionOptions
        {
            Recording = new WorkflowRunRecordingOptions { Sink = sink }
        };
        return new WorkflowEngine(definition, handlers, new WorkflowContext(services), options);
    }

    private static WorkflowDocument BuildLinearDocument()
    {
        var document = new WorkflowDocument { Name = "线性事件" };
        var canvas = document.CanvasProjection;
        foreach (var node in new IWorkflowNodeModel[]
                 {
                     new ActionNodeModel { Id = "A", Title = "节点A", FunctionKey = "A" },
                     new ActionNodeModel { Id = "B", Title = "节点B", FunctionKey = "B" },
                     new EndNodeModel { Id = "End", Title = "结束" }
                 })
            canvas.Nodes.Add(new WorkflowCanvasNode { Node = node });
        canvas.Connections.Add(Connect("A", WorkflowPorts.Success, "B"));
        canvas.Connections.Add(Connect("B", WorkflowPorts.Success, "End"));
        return document;
    }

    private static WorkflowDocument BuildParallelDocument()
    {
        var document = new WorkflowDocument { Name = "并行事件" };
        var canvas = document.CanvasProjection;
        foreach (var node in new IWorkflowNodeModel[]
                 {
                     new ParallelAllNodeModel { Id = "Parallel", Title = "并行" },
                     new ActionNodeModel { Id = "BranchA", Title = "分支A", FunctionKey = "BranchA" },
                     new ActionNodeModel { Id = "BranchB", Title = "分支B", FunctionKey = "BranchB" },
                     new WaitAllInputsCompletedNodeModel { Id = "Merge", Title = "汇聚" },
                     new EndNodeModel { Id = "End", Title = "结束" }
                 })
            canvas.Nodes.Add(new WorkflowCanvasNode { Node = node });
        canvas.Connections.Add(Connect("Parallel", WorkflowPorts.Branch, "BranchA"));
        canvas.Connections.Add(Connect("Parallel", WorkflowPorts.Branch, "BranchB"));
        canvas.Connections.Add(Connect("BranchA", WorkflowPorts.Success, "Merge"));
        canvas.Connections.Add(Connect("BranchB", WorkflowPorts.Success, "Merge"));
        canvas.Connections.Add(Connect("Merge", WorkflowPorts.Success, "End"));
        return document;
    }

    private static WorkflowDocument BuildLoopDocument(int iterations = 3)
    {
        var document = new WorkflowDocument { Name = "循环事件" };
        var canvas = document.CanvasProjection;
        foreach (var node in new IWorkflowNodeModel[]
                 {
                     new LoopNodeModel { Id = "Loop", Title = "循环", Iterations = iterations },
                     new ActionNodeModel { Id = "Body", Title = "循环体", FunctionKey = "Body" },
                     new EndNodeModel { Id = "End", Title = "结束" }
                 })
            canvas.Nodes.Add(new WorkflowCanvasNode { Node = node });
        canvas.Connections.Add(Connect("Loop", WorkflowPorts.Loop, "Body"));
        canvas.Connections.Add(Connect("Body", WorkflowPorts.Success, "Loop"));
        canvas.Connections.Add(Connect("Loop", WorkflowPorts.Completed, "End"));
        return document;
    }

    private static WorkflowDocument BuildBindingDocument()
    {
        var document = new WorkflowDocument { Name = "数据血缘" };
        var canvas = document.CanvasProjection;
        foreach (var node in new IWorkflowNodeModel[]
                 {
                     new ActionNodeModel { Id = "Producer", Title = "生产", FunctionKey = "Produce" },
                     new BindingNodeModel
                     {
                         Id = "Consumer",
                         Title = "消费",
                         Source = WorkflowInput<bool>.FromBinding(new WorkflowBindingKey("Producer", "Value"))
                     },
                     new EndNodeModel { Id = "End", Title = "结束" }
                 })
            canvas.Nodes.Add(new WorkflowCanvasNode { Node = node });
        canvas.Connections.Add(Connect("Producer", WorkflowPorts.Success, "Consumer"));
        canvas.Connections.Add(Connect("Consumer", WorkflowPorts.Success, "End"));
        return document;
    }

    private static WorkflowConnectionModel Connect(string from, string port, string to) => new()
    {
        FromNodeId = from,
        FromPort = port,
        ToNodeId = to
    };

    private sealed record BooleanOutput(bool Value);

    [WorkflowNode("Test.RecordingBinding")]
    private sealed class BindingNodeModel : WorkflowNodeModel
    {
        public override string NodeType => "Test.RecordingBinding";

        /// <summary>绑定到上游输出的命名输入；用于验证 InputResolved 记录来源身份。</summary>
        public WorkflowInput<bool> Source { get; set; } = WorkflowInput<bool>.FromLiteral(false);
    }

    private sealed class BindingNodeHandler : WorkflowNodeHandler<BindingNodeModel>
    {
        protected override ValueTask<NodeExecutionResult> ExecuteAsync(
            BindingNodeModel node,
            IWorkflowNodeExecutionContext context,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // 普通 ResolveInput 由绑定阶段冻结的输入槽元数据自动识别 InputKey，Handler 不手写输入名。
            var value = context.ResolveInput(node.Source);
            return ValueTask.FromResult(NodeExecutionResult.Continue(WorkflowPorts.Success, value));
        }
    }

    [WorkflowNode("Test.RecordingThrowingTrace")]
    private sealed class ThrowingTraceNodeModel : WorkflowNodeModel
    {
        public override string NodeType => "Test.RecordingThrowingTrace";
    }

    private sealed class ThrowingTraceNodeHandler : WorkflowNodeHandler<ThrowingTraceNodeModel>
    {
        protected override ValueTask<NodeExecutionResult> ExecuteAsync(
            ThrowingTraceNodeModel node,
            IWorkflowNodeExecutionContext context,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // 节点完全正常：只是它提供的跟踪数据在记录阶段无法被枚举。
            context.Trace("Step", "跟踪数据无法枚举。", new ThrowingDictionary());
            return ValueTask.FromResult(NodeExecutionResult.Continue(WorkflowPorts.Success));
        }
    }

    /// <summary>枚举器直接抛异常的只读字典；模拟调用方自定义数据结构在记录时失效。</summary>
    private sealed class ThrowingDictionary : IReadOnlyDictionary<string, object?>
    {
        public object? this[string key] => throw new NotSupportedException();

        public IEnumerable<string> Keys => throw new NotSupportedException();

        public IEnumerable<object?> Values => throw new NotSupportedException();

        public int Count => 1;

        public bool ContainsKey(string key) => throw new NotSupportedException();

        public bool TryGetValue(string key, out object? value) => throw new NotSupportedException();

        public IEnumerator<KeyValuePair<string, object?>> GetEnumerator() =>
            throw new InvalidOperationException("跟踪数据无法枚举。");

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class CollectingSink : IWorkflowRunEventSink
    {
        private readonly object _sync = new();
        private readonly List<WorkflowRunEvent> _events = new();

        public IReadOnlyList<WorkflowRunEvent> Events
        {
            get
            {
                lock (_sync)
                    return _events.OrderBy(item => item.Sequence).ToArray();
            }
        }

        public ValueTask<WorkflowRunEventWriteResult> WriteAsync(
            IReadOnlyList<WorkflowRunEvent> events,
            bool requestImmediateFlush,
            CancellationToken cancellationToken)
        {
            lock (_sync)
                _events.AddRange(events);
            return ValueTask.FromResult(WorkflowRunEventWriteResult.Success);
        }

        public ValueTask FlushAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }
}
