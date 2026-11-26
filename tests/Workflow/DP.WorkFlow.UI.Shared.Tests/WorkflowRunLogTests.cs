namespace DP.WorkFlow.Tests;

/// <summary>运行日志：按循环圈、并行分支建树，成功的层自动收起，连续成功的圈合并，失败的层展开。</summary>
public sealed class WorkflowRunLogTests
{
    [Fact]
    public async Task Run_BuildsTreeByCycleAndBranch_AndFoldsSuccessfulLayers()
    {
        var book = new WorkflowRunLogBook();
        using var center = CreateCenter(failOnCycle: 0);
        var flow = center.AddFlow(Document());
        book.Attach(center);

        Assert.True((await flow.RunAsync()).Success);
        Assert.True(flow.Outlet.WaitIdle(TimeSpan.FromSeconds(5)));

        var summary = Assert.Single(book.Runs("日志"));
        Assert.Equal(WorkflowRunLogState.Succeeded, summary.State);
        Assert.Equal(0, summary.Failures);
        book.Read("日志", 0, log =>
        {
            var root = log.Root;
            var loop = Assert.Single(root.Children, row => row.NodeId == "Loop");
            Assert.Equal(WorkflowRunLogRowKind.LoopBlock, loop.Kind);
            Assert.Equal("扫描循环", loop.Title);
            Assert.Equal(new[] { 1, 2, 3 }, loop.Children.Where(row => row.Kind == WorkflowRunLogRowKind.Cycle).Select(row => row.Cycle));
            Assert.All(loop.Children.Where(row => row.Kind == WorkflowRunLogRowKind.Cycle), cycle =>
            {
                Assert.Equal(WorkflowRunLogState.Succeeded, cycle.State);
                Assert.Contains(cycle.Children, row => row.NodeId == "Work" && row.State == WorkflowRunLogState.Succeeded);
            });
            var parallel = Assert.Single(root.Children, row => row.NodeId == "Parallel");
            Assert.Equal(WorkflowRunLogRowKind.Parallel, parallel.Kind);
            Assert.Equal(2, parallel.Children.Count(row => row.Kind == WorkflowRunLogRowKind.Branch));
            Assert.Contains(parallel.Children, branch => branch.Title.Contains("生产") && branch.Children.Any(row => row.NodeId == "Producer"));
            Assert.Contains(root.Children, row => row.NodeId == "End");

            var view = new WorkflowRunLogView();
            var lines = view.Build(log, DateTimeOffset.UtcNow);
            var loopLine = Assert.Single(lines, line => line.NodeId == "Loop" && line.Kind != WorkflowRunLogRowKind.Message);
            Assert.False(loopLine.Expanded);
            Assert.DoesNotContain(lines, line => line.NodeId == "Work");

            view.Toggle(loopLine);
            lines = view.Build(log, DateTimeOffset.UtcNow);
            var group = Assert.Single(lines, line => line.IsGroup);
            Assert.Equal("第 1–3 圈", group.Title);
            Assert.False(group.Expanded);
            return 0;
        });
    }

    [Fact]
    public async Task FailedCycle_StaysExpanded_AndFailureFilterKeepsOnlyItsPath()
    {
        var book = new WorkflowRunLogBook();
        using var center = CreateCenter(failOnCycle: 2);
        var flow = center.AddFlow(Document());
        book.Attach(center);

        Assert.False((await flow.RunAsync()).Success);
        Assert.True(flow.Outlet.WaitIdle(TimeSpan.FromSeconds(5)));

        var summary = Assert.Single(book.Runs("日志"));
        Assert.True(summary.Failures > 0);
        book.Read("日志", 0, log =>
        {
            var view = new WorkflowRunLogView();
            var lines = view.Build(log, DateTimeOffset.UtcNow);
            Assert.True(Assert.Single(lines, line => line.NodeId == "Loop" && line.Kind != WorkflowRunLogRowKind.Message).Expanded);
            // 第 1 圈成功、只有一圈，不合并；第 2 圈出故障，保持展开，失败的“扫描”可见。
            var second = Assert.Single(lines, line => line.Title == "第 2 圈");
            Assert.True(second.Expanded);
            Assert.True(second.HasFailure);
            var failed = Assert.Single(lines, line => line.NodeId == "Work" && line.HasFailure);
            Assert.Contains("第 2 次扫描坏了", failed.Detail);
            Assert.Equal("扫描循环 / 第 2 圈 / 扫描", failed.Path);

            view.Filter = WorkflowRunLogFilter.Failures;
            lines = view.Build(log, DateTimeOffset.UtcNow);
            Assert.DoesNotContain(lines, line => line.Title == "第 1 圈");
            Assert.DoesNotContain(lines, line => line.NodeId == "Init");
            Assert.Contains(lines, line => line.NodeId == "Work" && line.HasFailure);
            return 0;
        });
    }

    [Fact]
    public async Task LoopNode_GroupsEachPassAsCycle_AndFoldsLikeLoopBlock()
    {
        var book = new WorkflowRunLogBook();
        using var center = CreateCenter(failOnCycle: 3);
        var flow = center.AddFlow(LoopNodeDocument());
        book.Attach(center);

        Assert.False((await flow.RunAsync()).Success);
        Assert.True(flow.Outlet.WaitIdle(TimeSpan.FromSeconds(5)));

        book.Read("循环节点", 0, log =>
        {
            var loop = Assert.Single(log.Root.Children, row => row.NodeId == "Loop");
            Assert.Equal(WorkflowRunLogRowKind.LoopBlock, loop.Kind);
            // 每经过一次循环节点算一圈，循环体节点挂在圈下，不平铺在根下。
            var cycles = loop.Children.Where(row => row.Kind == WorkflowRunLogRowKind.Cycle).ToArray();
            Assert.Equal(new[] { 1, 2, 3 }, cycles.Select(row => row.Cycle));
            Assert.All(cycles, cycle => Assert.Contains(cycle.Children, row => row.NodeId == "Work"));
            Assert.DoesNotContain(log.Root.Children, row => row.NodeId == "Work");
            Assert.DoesNotContain(loop.Children, row => row.Kind == WorkflowRunLogRowKind.Message);
            Assert.Equal(WorkflowRunLogState.Succeeded, cycles[0].State);
            Assert.True(cycles[2].HasFailure);

            var lines = new WorkflowRunLogView().Build(log, DateTimeOffset.UtcNow);
            // 第 1、2 圈成功合并成一行；第 3 圈出错保持展开。
            Assert.Equal("第 1–2 圈", Assert.Single(lines, line => line.IsGroup).Title);
            Assert.True(Assert.Single(lines, line => line.Title == "第 3 圈").Expanded);
            Assert.Equal("循环生产 / 第 3 圈 / 扫描", Assert.Single(lines, line => line.NodeId == "Work" && line.Title == "扫描").Path);
            return 0;
        });
    }

    [Fact]
    public async Task OnlyTheLatestFailure_IsExpandedAutomatically()
    {
        // 开始 → 循环节点（5 圈）→ 检查（第 2、4 次失败，走失败出口回到循环）→ 回到循环节点。
        var document = new WorkflowDocument { Name = "多次失败", EntryNodeId = "Start" };
        IWorkflowNodeModel[] nodes =
        {
            new StartNodeModel { Id = "Start", Title = "开始" },
            new LoopNodeModel { Id = "Loop", Title = "循环生产", Iterations = 5 },
            new CheckNode { Id = "Check", Title = "检查" },
            new EndNodeModel { Id = "End", Title = "结束" }
        };
        foreach (var node in nodes)
            document.Nodes.Add(new WorkflowCanvasNode { Node = node });
        document.Connections.Add(Connect("Start", WorkflowPorts.Success, "Loop"));
        document.Connections.Add(Connect("Loop", WorkflowPorts.Loop, "Check"));
        document.Connections.Add(Connect("Check", WorkflowPorts.Success, "Loop"));
        document.Connections.Add(Connect("Check", WorkflowPorts.Failed, "Loop"));
        document.Connections.Add(Connect("Loop", WorkflowPorts.Completed, "End"));
        using var center = new WorkflowRunCenter(
            new WorkflowNodeCatalog().RegisterStandardNodes().RegisterCompositeNodes()
                .Register<CheckNode>(1, WorkflowPortDescriptor.Input(maxConnections: int.MaxValue), WorkflowPortDescriptor.Output(), WorkflowPortDescriptor.Failure()),
            new WorkflowNodeHandlerCatalog().RegisterStandardNodeHandlers().RegisterCompositeNodeHandlers().Register(new CheckHandler()),
            new WorkflowRunCenterOptions { FlowOutlet = WorkflowRunLogBook.OutletOptions() },
            new WorkflowServiceProvider());
        var book = new WorkflowRunLogBook();
        var flow = center.AddFlow(document);
        book.Attach(center);

        Assert.True((await flow.RunAsync()).Success);
        Assert.True(flow.Outlet.WaitIdle(TimeSpan.FromSeconds(5)));

        book.Read("多次失败", 0, log =>
        {
            var lines = new WorkflowRunLogView().Build(log, DateTimeOffset.UtcNow);
            // 第 2、4 圈都出过错：只有最近的第 4 圈自动展开，第 2 圈收起但仍标着失败。
            var second = Assert.Single(lines, line => line.Title == "第 2 圈");
            Assert.True(second.HasFailure);
            Assert.False(second.Expanded);
            Assert.True(Assert.Single(lines, line => line.Title == "第 4 圈").Expanded);
            Assert.Single(lines, line => line.NodeId == "Check" && line.HasFailure);
            return 0;
        });
    }

    [WorkflowNode("RunLogCheck")]
    private sealed class CheckNode : WorkflowNodeModel
    {
        public override string NodeType => "RunLogCheck";
    }

    private sealed class CheckHandler : WorkflowNodeHandler<CheckNode>
    {
        private int _count;

        protected override ValueTask<NodeExecutionResult> ExecuteAsync(CheckNode node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
        {
            var count = Interlocked.Increment(ref _count);
            if (count is 2 or 4) throw new InvalidOperationException($"第 {count} 次检查不合格");
            return ValueTask.FromResult(NodeExecutionResult.Continue());
        }
    }

    [Fact]
    public async Task NestedStructures_BelongToTheirCycleSubflowAndBranch()
    {
        var book = new WorkflowRunLogBook();
        using var center = CreateCenter(failOnCycle: 0);
        var flow = center.AddFlow(NestedDocument());
        book.Attach(center);

        Assert.True((await flow.RunAsync()).Success);
        Assert.True(flow.Outlet.WaitIdle(TimeSpan.FromSeconds(5)));

        book.Read("嵌套", 0, log =>
        {
            // 根下只有：开始、外层循环、结束；其余都归到各自的圈、子流程和分支下。
            Assert.Equal(new[] { "Start", "Loop", "End" }, log.Root.Children.Select(row => row.NodeId));
            var loop = log.Root.Children[1];
            var cycles = loop.Children.Where(row => row.Kind == WorkflowRunLogRowKind.Cycle).ToArray();
            Assert.Equal(new[] { 1, 2 }, cycles.Select(row => row.Cycle));
            Assert.All(cycles, cycle =>
            {
                Assert.Equal(WorkflowRunLogState.Succeeded, cycle.State);
                var block = Assert.Single(cycle.Children, row => row.NodeId == "Station");
                Assert.Equal(WorkflowRunLogRowKind.SubFlow, block.Kind);
                var inner = Assert.Single(block.Children, row => row.NodeId == "InnerLoop");
                Assert.Equal(WorkflowRunLogRowKind.LoopBlock, inner.Kind);
                var innerCycles = inner.Children.Where(row => row.Kind == WorkflowRunLogRowKind.Cycle).ToArray();
                Assert.Equal(new[] { 1, 2 }, innerCycles.Select(row => row.Cycle));
                Assert.All(innerCycles, innerCycle => Assert.Contains(innerCycle.Children, row => row.NodeId == "Work"));
                var parallel = Assert.Single(cycle.Children, row => row.NodeId == "Parallel");
                Assert.Contains(parallel.Children, branch => branch.Children.Any(row => row.NodeId == "Producer"));
                Assert.Contains(cycle.Children, row => row.NodeId == "Merge");
            });

            // 全部成功：外层循环收起，展开后两圈合并成一行。
            var view = new WorkflowRunLogView();
            var lines = view.Build(log, DateTimeOffset.UtcNow);
            Assert.DoesNotContain(lines, line => line.NodeId is "Work" or "Producer" or "Station");
            view.Toggle(Assert.Single(lines, line => line.NodeId == "Loop"));
            Assert.Equal("第 1–2 圈", Assert.Single(view.Build(log, DateTimeOffset.UtcNow), line => line.IsGroup).Title);
            return 0;
        });
    }

    [Fact]
    public async Task Details_ShowDataNames_AndLinesCarryTheirFlow()
    {
        var book = new WorkflowRunLogBook();
        using var center = CreateCenter(failOnCycle: 3);
        var flow = center.AddFlow(LoopNodeDocument());
        book.Attach(center);

        await flow.RunAsync();
        Assert.True(flow.Outlet.WaitIdle(TimeSpan.FromSeconds(5)));

        var loopType = new LoopNodeModel().NodeType;
        Assert.Equal("循环次数", book.DataLabel("循环节点", loopType, "Iterations"));
        Assert.Equal("耗时 (ms)", book.DataLabel("循环节点", loopType, "Elapsed"));
        Assert.Equal("输出 · 循环次数", book.DataLabel("循环节点", loopType, "OutputValue.Iterations"));
        Assert.Equal("没有名称的键", book.DataLabel("循环节点", loopType, "没有名称的键"));

        var lines = book.Read("循环节点", 0, log => new WorkflowRunLogView { ExpandAll = true }.Build(log, DateTimeOffset.UtcNow))!;
        Assert.All(lines, line => Assert.Equal("循环节点", line.FlowName));
        Assert.All(lines, line => Assert.StartsWith("循环节点/", line.Key, StringComparison.Ordinal));

        var line = lines[0] with
        {
            Data = new Dictionary<string, WorkflowTraceValue>
            {
                ["CanRecoverInPlace"] = new(WorkflowTraceValueKind.Scalar, "Boolean", "True", true),
                ["Custom"] = new(WorkflowTraceValueKind.Scalar, "Int32", "7", 7)
            }
        };
        var rows = WorkflowRunLogText.DetailRows(line, (type, key) => book.DataLabel(line.FlowName, type, key));
        Assert.Equal("循环节点", rows[0].Value);
        Assert.Contains((string.Empty, "可就地恢复 = 是"), rows);
        Assert.Contains((string.Empty, "Custom = 7"), rows);
    }

    [Fact]
    public async Task Sink_RecordsHostsOutsideTheRunCenter_WithNodeTitles()
    {
        var book = new WorkflowRunLogBook();
        WorkflowRuntimeHost? host = null;
        var name = "试运行 · 上料";
        var options = new WorkflowExecutionOptions();
        options.Recording.Sink = book.CreateSink(() => name, () => host?.Plan);
        host = new WorkflowRuntimeHost(
            new WorkflowNodeCatalog().RegisterStandardNodes().RegisterCompositeNodes(),
            new WorkflowNodeHandlerCatalog().RegisterStandardNodeHandlers().RegisterCompositeNodeHandlers(),
            options);
        using var owned = host;
        var actions = new WorkflowActionRegistry().Register("Work", (_, _) => ValueTask.FromResult<object?>(null));
        var document = LoopNodeDocument();
        ((LoopNodeModel)document.Nodes.Single(item => item.Node.Id == "Loop").Node).Iterations = 2;
        host.Configure(document, new WorkflowContext(new WorkflowServiceProvider().Add<IWorkflowActionRegistry>(actions)));

        // 名字每次启动时取：两次记在“上料”下，换了流程后记在“下料”下。
        Assert.True((await host.RunAsync()).Success);
        Assert.True((await host.RunAsync()).Success);
        await WaitEnded(book, "试运行 · 上料", 2);
        name = "试运行 · 下料";
        Assert.True((await host.RunAsync()).Success);
        await WaitEnded(book, "试运行 · 下料", 1);

        Assert.Equal(2, book.Runs("试运行 · 上料").Count);
        Assert.Single(book.Runs("试运行 · 下料"));
        var lines = book.Read("试运行 · 上料", 1, log => new WorkflowRunLogView { ExpandAll = true }.Build(log, DateTimeOffset.UtcNow))!;
        Assert.Contains(lines, line => line.NodeId == "Work" && line.Title == "扫描");
        Assert.DoesNotContain(lines, line => line.FlowName != "试运行 · 上料");
    }

    [Fact]
    public async Task History_SavedAsJsonLines_ReplaysExactlyLikeTheLiveLog()
    {
        var book = new WorkflowRunLogBook();
        WorkflowRuntimeHost? host = null;
        var saved = new List<string>();
        var options = new WorkflowExecutionOptions();
        options.Recording.Sink = new Tee(book.CreateSink("上料", () => host?.Plan), saved);
        host = new WorkflowRuntimeHost(
            new WorkflowNodeCatalog().RegisterStandardNodes().RegisterCompositeNodes(),
            new WorkflowNodeHandlerCatalog().RegisterStandardNodeHandlers().RegisterCompositeNodeHandlers(),
            options);
        using var owned = host;
        var actions = new WorkflowActionRegistry().Register("Work", (_, _) => ValueTask.FromResult<object?>(null));
        var document = LoopNodeDocument();
        ((LoopNodeModel)document.Nodes.Single(item => item.Node.Id == "Loop").Node).Iterations = 3;
        host.Configure(document, new WorkflowContext(new WorkflowServiceProvider().Add<IWorkflowActionRegistry>(actions)));
        Assert.True((await host.RunAsync()).Success);
        await WaitEnded(book, "上料", 1);

        // 外部存的是原始事件（这里一行一条 JSON）；读回来重建，显示和实时记录的一样。
        var replayed = book.Replay("上料", saved.Select(WorkflowRunEventJson.Read));
        var view = new WorkflowRunLogView { ExpandAll = true, ControlOnly = false };
        var now = DateTimeOffset.UtcNow;
        var live = book.Read("上料", 0, log => view.Build(log, now))!;
        var restored = view.Build(replayed, now);
        object Shape(WorkflowRunLogLine line) => (line.Key, line.Depth, line.Kind, line.Title, line.Detail, line.Started, line.Ended, line.Elapsed,
            line.State, line.NodeId, line.Path, string.Join(";", (line.Data ?? new Dictionary<string, WorkflowTraceValue>())
                .Select(pair => pair.Key + "=" + pair.Value.Text + "|" + pair.Value.ToObject())));
        Assert.True(live.Count > 5);
        Assert.Equal(live.Select(Shape), restored.Select(Shape));
        Assert.Equal(WorkflowRunLogState.Succeeded, replayed.Root.State);
    }

    private sealed class Tee(IWorkflowRunEventSink inner, List<string> saved) : IWorkflowRunEventSink
    {
        public ValueTask<WorkflowRunEventWriteResult> WriteAsync(IReadOnlyList<WorkflowRunEvent> events, bool requestImmediateFlush, CancellationToken cancellationToken)
        {
            lock (saved) saved.AddRange(events.Select(WorkflowRunEventJson.Write));
            return inner.WriteAsync(events, requestImmediateFlush, cancellationToken);
        }

        public ValueTask FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);
    }

    private static async Task WaitEnded(WorkflowRunLogBook book, string flow, int count)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (book.Runs(flow).Count(run => run.Ended is not null) < count && DateTime.UtcNow < deadline) await Task.Delay(20);
    }

    [Fact]
    public void Book_KeepsOnlyTheLatestRuns()
    {
        var book = new WorkflowRunLogBook(maxRuns: 2);
        for (var index = 0; index < 3; index++)
        {
            var run = Guid.NewGuid();
            book.Add("甲", Event(run, "RunStarted"));
            book.Add("甲", Event(run, "RunCompleted"));
        }

        var runs = book.Runs("甲");
        Assert.Equal(2, runs.Count);
        Assert.All(runs, run => Assert.Equal(WorkflowRunLogState.Succeeded, run.State));
    }

    private static WorkflowRunEvent Event(Guid run, string type) =>
        new(run, 1, DateTimeOffset.UtcNow, WorkflowRunEventCategory.Lifecycle, type, WorkflowEventWriteMode.Buffered, "甲", null, null, null, null, null, 1, null);

    private static WorkflowRunCenter CreateCenter(int failOnCycle)
    {
        var count = 0;
        var actions = new WorkflowActionRegistry()
            .Register("Init", (_, _) => ValueTask.FromResult<object?>(null))
            .Register("Work", (_, _) =>
            {
                if (Interlocked.Increment(ref count) == failOnCycle)
                    throw new InvalidOperationException($"第 {failOnCycle} 次扫描坏了");
                return ValueTask.FromResult<object?>(null);
            })
            .Register("Produce", (_, _) => ValueTask.FromResult<object?>(null))
            .Register("BranchB", (_, _) => ValueTask.FromResult<object?>(null));
        var options = new WorkflowRunCenterOptions { FlowOutlet = WorkflowRunLogBook.OutletOptions() };
        return new WorkflowRunCenter(
            new WorkflowNodeCatalog().RegisterStandardNodes().RegisterCompositeNodes(),
            new WorkflowNodeHandlerCatalog().RegisterStandardNodeHandlers().RegisterCompositeNodeHandlers(),
            options,
            new WorkflowServiceProvider().Add<IWorkflowActionRegistry>(actions));
    }

    /// <summary>初始化 → 扫描循环（3 圈：开始 → 扫描）→ 并行（生产、分支B）→ 汇聚 → 结束。</summary>
    private static WorkflowDocument Document()
    {
        var child = new WorkflowDocument { Name = "循环体", EntryNodeId = "ChildStart" };
        child.Nodes.Add(new WorkflowCanvasNode { Node = new StartNodeModel { Id = "ChildStart", Title = "开始" } });
        child.Nodes.Add(new WorkflowCanvasNode { Node = new ActionNodeModel { Id = "Work", Title = "扫描", FunctionKey = "Work" } });
        child.Connections.Add(Connect("ChildStart", WorkflowPorts.Success, "Work"));

        var document = new WorkflowDocument { Name = "日志", EntryNodeId = "Init" };
        IWorkflowNodeModel[] nodes =
        {
            new ActionNodeModel { Id = "Init", Title = "初始化", FunctionKey = "Init" },
            new LoopBlockNodeModel { Id = "Loop", Title = "扫描循环", SubDocument = child, Iterations = 3 },
            new ParallelAllNodeModel { Id = "Parallel", Title = "放料与扫码" },
            new ActionNodeModel { Id = "Producer", Title = "生产", FunctionKey = "Produce" },
            new ActionNodeModel { Id = "BranchB", Title = "分支B", FunctionKey = "BranchB" },
            new WaitAllInputsCompletedNodeModel { Id = "Merge", Title = "汇聚" },
            new EndNodeModel { Id = "End", Title = "结束" }
        };
        foreach (var node in nodes)
            document.Nodes.Add(new WorkflowCanvasNode { Node = node });
        document.Connections.Add(Connect("Init", WorkflowPorts.Success, "Loop"));
        document.Connections.Add(Connect("Loop", WorkflowPorts.Success, "Parallel"));
        document.Connections.Add(Connect("Parallel", WorkflowPorts.Branch, "Producer"));
        document.Connections.Add(Connect("Parallel", WorkflowPorts.Branch, "BranchB"));
        document.Connections.Add(Connect("Producer", WorkflowPorts.Success, "Merge"));
        document.Connections.Add(Connect("BranchB", WorkflowPorts.Success, "Merge"));
        document.Connections.Add(Connect("Merge", WorkflowPorts.Success, "End"));
        return document;
    }

    /// <summary>开始 → 循环节点（不限次数）→ 扫描 → 回到循环节点；扫描第 3 次出错。</summary>
    private static WorkflowDocument LoopNodeDocument()
    {
        var document = new WorkflowDocument { Name = "循环节点", EntryNodeId = "Start" };
        IWorkflowNodeModel[] nodes =
        {
            new StartNodeModel { Id = "Start", Title = "开始" },
            new LoopNodeModel { Id = "Loop", Title = "循环生产", Iterations = 0 },
            new ActionNodeModel { Id = "Work", Title = "扫描", FunctionKey = "Work" },
            new EndNodeModel { Id = "End", Title = "结束" }
        };
        foreach (var node in nodes)
            document.Nodes.Add(new WorkflowCanvasNode { Node = node });
        document.Connections.Add(Connect("Start", WorkflowPorts.Success, "Loop"));
        document.Connections.Add(Connect("Loop", WorkflowPorts.Loop, "Work"));
        document.Connections.Add(Connect("Work", WorkflowPorts.Success, "Loop"));
        document.Connections.Add(Connect("Loop", WorkflowPorts.Completed, "End"));
        return document;
    }

    /// <summary>
    /// 开始 → 外层循环节点（2 圈）：子流程“工位”（里面是 2 圈的循环节点 → 扫描）→ 并行（生产、分支B）→ 汇聚 → 回到外层循环；完成 → 结束。
    /// </summary>
    private static WorkflowDocument NestedDocument()
    {
        var station = new WorkflowDocument { Name = "工位", EntryNodeId = "ChildStart" };
        IWorkflowNodeModel[] inner =
        {
            new StartNodeModel { Id = "ChildStart", Title = "开始" },
            new LoopNodeModel { Id = "InnerLoop", Title = "逐个扫描", Iterations = 2 },
            new ActionNodeModel { Id = "Work", Title = "扫描", FunctionKey = "Work" }
        };
        foreach (var node in inner)
            station.Nodes.Add(new WorkflowCanvasNode { Node = node });
        station.Connections.Add(Connect("ChildStart", WorkflowPorts.Success, "InnerLoop"));
        station.Connections.Add(Connect("InnerLoop", WorkflowPorts.Loop, "Work"));
        station.Connections.Add(Connect("Work", WorkflowPorts.Success, "InnerLoop"));

        var document = new WorkflowDocument { Name = "嵌套", EntryNodeId = "Start" };
        IWorkflowNodeModel[] nodes =
        {
            new StartNodeModel { Id = "Start", Title = "开始" },
            new LoopNodeModel { Id = "Loop", Title = "循环生产", Iterations = 2 },
            new BlockNodeModel { Id = "Station", Title = "工位", SubDocument = station },
            new ParallelAllNodeModel { Id = "Parallel", Title = "放料与扫码" },
            new ActionNodeModel { Id = "Producer", Title = "生产", FunctionKey = "Produce" },
            new ActionNodeModel { Id = "BranchB", Title = "分支B", FunctionKey = "BranchB" },
            new WaitAllInputsCompletedNodeModel { Id = "Merge", Title = "汇聚" },
            new EndNodeModel { Id = "End", Title = "结束" }
        };
        foreach (var node in nodes)
            document.Nodes.Add(new WorkflowCanvasNode { Node = node });
        document.Connections.Add(Connect("Start", WorkflowPorts.Success, "Loop"));
        document.Connections.Add(Connect("Loop", WorkflowPorts.Loop, "Station"));
        document.Connections.Add(Connect("Station", WorkflowPorts.Success, "Parallel"));
        document.Connections.Add(Connect("Parallel", WorkflowPorts.Branch, "Producer"));
        document.Connections.Add(Connect("Parallel", WorkflowPorts.Branch, "BranchB"));
        document.Connections.Add(Connect("Producer", WorkflowPorts.Success, "Merge"));
        document.Connections.Add(Connect("BranchB", WorkflowPorts.Success, "Merge"));
        document.Connections.Add(Connect("Merge", WorkflowPorts.Success, "Loop"));
        document.Connections.Add(Connect("Loop", WorkflowPorts.Completed, "End"));
        return document;
    }

    private static WorkflowConnectionModel Connect(string from, string port, string to) => new() { FromNodeId = from, FromPort = port, ToNodeId = to };
}
