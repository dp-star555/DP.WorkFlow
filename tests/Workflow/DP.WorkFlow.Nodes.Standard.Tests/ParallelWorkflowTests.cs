namespace DP.WorkFlow.Tests;

public sealed class ParallelWorkflowTests
{
    [Fact]
    public async Task RunAsync_TwoBranches_ExecuteConcurrentlyAndMergeOnce()
    {
        var document = BuildParallelDocument();
        var canvas = document.CanvasProjection;
        var bothBranchesEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseBranches = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var activeBranches = 0;
        var maxActiveBranches = 0;
        var afterMergeCount = 0;
        async ValueTask<object?> Branch(IWorkflowNodeExecutionContext _, CancellationToken cancellationToken)
        {
            var active = Interlocked.Increment(ref activeBranches);
            UpdateMaximum(ref maxActiveBranches, active);
            if (active == 2)
                bothBranchesEntered.TrySetResult(true);
            await releaseBranches.Task.WaitAsync(cancellationToken);
            Interlocked.Decrement(ref activeBranches);
            return active;
        }

        var actions = new WorkflowActionRegistry()
            .Register("BranchA", Branch)
            .Register("BranchB", Branch)
            .Register("AfterMerge", (_, _) =>
            {
                Interlocked.Increment(ref afterMergeCount);
                return ValueTask.FromResult<object?>(null);
            });
        var services = new WorkflowServiceProvider().Add<IWorkflowActionRegistry>(actions);
        var engine = CreateEngine(document, services);

        var runTask = engine.RunAsync();
        await bothBranchesEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var runningSnapshot = engine.GetRuntimeSnapshot();
        releaseBranches.TrySetResult(true);
        var result = await runTask;
        var finalSnapshot = engine.GetRuntimeSnapshot();
        var outputs = engine.Context.ExportNodeOutputs();

        Assert.True(result.Success, result.Message);
        Assert.Equal(2, maxActiveBranches);
        Assert.Contains("BranchA", runningSnapshot.ActiveNodeIds);
        Assert.Contains("BranchB", runningSnapshot.ActiveNodeIds);
        Assert.Equal(2, runningSnapshot.ActiveTokens.Count);
        var branchOutputs = outputs.Where(output => output.NodeId is "BranchA" or "BranchB").ToArray();
        Assert.Equal(2, branchOutputs.Length);
        Assert.Equal(2, branchOutputs.Select(output => output.TokenId).Distinct().Count());
        Assert.All(branchOutputs, output => Assert.Single(output.ScopeIds));
        Assert.Equal(1, afterMergeCount);
        var scope = Assert.Single(finalSnapshot.ParallelScopes.Values);
        Assert.True(scope.IsCompleted);
        Assert.Equal(2, scope.CompletedBranches);
        Assert.Equal(E_NodeState.Completed, finalSnapshot.Nodes["Merge"].State);
    }

    [Fact]
    public async Task RunAsync_BranchOutputBecomesVisibleAfterMerge()
    {
        var parallel = new ParallelAllNodeModel { Id = "Parallel", Title = "并行" };
        var producer = new ActionNodeModel { Id = "Producer", Title = "生产", FunctionKey = "Produce" };
        var branchB = new ActionNodeModel { Id = "BranchB", Title = "分支B", FunctionKey = "BranchB" };
        var merge = new WaitAllInputsCompletedNodeModel { Id = "Merge", Title = "汇聚" };
        var decision = new DecisionNodeModel
        {
            Id = "Decision",
            Title = "汇聚后绑定",
            ConditionSource = E_DecisionConditionSource.Binding,
            Condition = WorkflowInput<bool>.FromBinding(new WorkflowBindingKey("Producer", "Value"))
        };
        var end = new EndNodeModel { Id = "End", Title = "结束" };
        var canvasDocument = new WorkflowDocument { Name = "汇聚后可见" };
        var canvas = canvasDocument.CanvasProjection;
        foreach (var node in new IWorkflowNodeModel[] { parallel, producer, branchB, merge, decision, end })
            canvas.Nodes.Add(new WorkflowCanvasNode { Node = node });
        canvas.Connections.Add(Connect("Parallel", WorkflowPorts.Branch, "Producer"));
        canvas.Connections.Add(Connect("Parallel", WorkflowPorts.Branch, "BranchB"));
        canvas.Connections.Add(Connect("Producer", WorkflowPorts.Success, "Merge"));
        canvas.Connections.Add(Connect("BranchB", WorkflowPorts.Success, "Merge"));
        canvas.Connections.Add(Connect("Merge", WorkflowPorts.Success, "Decision"));
        canvas.Connections.Add(Connect("Decision", WorkflowPorts.True, "End"));
        var actions = new WorkflowActionRegistry()
            .Register("Produce", (_, _) => ValueTask.FromResult<object?>(new BooleanOutput(true)))
            .Register("BranchB", (_, _) => ValueTask.FromResult<object?>(null));
        var services = new WorkflowServiceProvider().Add<IWorkflowActionRegistry>(actions);

        var result = await CreateEngine(canvasDocument, services).RunAsync();

        Assert.True(result.Success, result.Message);
    }

    [Fact]
    public async Task RunAsync_SiblingBranchOutputIsHiddenUntilMerge()
    {
        var parallel = new ParallelAllNodeModel { Id = "Parallel", Title = "并行" };
        var producer = new ActionNodeModel { Id = "Producer", Title = "生产", FunctionKey = "Produce" };
        var waitProducer = new ActionNodeModel { Id = "WaitProducer", Title = "等待生产", FunctionKey = "WaitProducer" };
        var decision = new DecisionNodeModel
        {
            Id = "Decision",
            Title = "错误跨分支绑定",
            ConditionSource = E_DecisionConditionSource.Binding,
            Condition = WorkflowInput<bool>.FromBinding(new WorkflowBindingKey("Producer", "Value"))
        };
        var merge = new WaitAllInputsCompletedNodeModel { Id = "Merge", Title = "汇聚" };
        var canvasDocument = new WorkflowDocument { Name = "分支隔离" };
        var canvas = canvasDocument.CanvasProjection;
        foreach (var node in new IWorkflowNodeModel[] { parallel, producer, waitProducer, decision, merge })
            canvas.Nodes.Add(new WorkflowCanvasNode { Node = node });
        canvas.Connections.Add(Connect("Parallel", WorkflowPorts.Branch, "Producer"));
        canvas.Connections.Add(Connect("Parallel", WorkflowPorts.Branch, "WaitProducer"));
        canvas.Connections.Add(Connect("Producer", WorkflowPorts.Success, "Merge"));
        canvas.Connections.Add(Connect("WaitProducer", WorkflowPorts.Success, "Decision"));
        canvas.Connections.Add(Connect("Decision", WorkflowPorts.True, "Merge"));
        canvas.Connections.Add(Connect("Decision", WorkflowPorts.False, "Merge"));
        var producerCompleted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var actions = new WorkflowActionRegistry()
            .Register("Produce", (_, _) => ValueTask.FromResult<object?>(new BooleanOutput(true)))
            .Register("WaitProducer", async (_, cancellationToken) =>
            {
                await producerCompleted.Task.WaitAsync(cancellationToken);
                return null;
            });
        var services = new WorkflowServiceProvider().Add<IWorkflowActionRegistry>(actions);
        var engine = CreateEngine(canvasDocument, services, validateBindings: false);
        engine.NodeCompleted += node =>
        {
            if (node.Id == "Producer")
                producerCompleted.TrySetResult(true);
        };

        var result = await engine.RunAsync();

        Assert.False(result.Success);
        Assert.Contains("当前 Token/Scope 中没有可见输出", result.Message);
    }

    [Fact]
    public async Task RunAsync_NestedParallel_CompletesBothRuntimeScopes()
    {
        var outer = new ParallelAllNodeModel { Id = "Outer", Title = "外层并行" };
        var inner = new ParallelAllNodeModel { Id = "Inner", Title = "内层并行" };
        var innerA = new ActionNodeModel { Id = "InnerA", Title = "内A", FunctionKey = "InnerA" };
        var innerB = new ActionNodeModel { Id = "InnerB", Title = "内B", FunctionKey = "InnerB" };
        var outerB = new ActionNodeModel { Id = "OuterB", Title = "外B", FunctionKey = "OuterB" };
        var innerMerge = new WaitAllInputsCompletedNodeModel { Id = "InnerMerge", Title = "内汇聚" };
        var outerMerge = new WaitAllInputsCompletedNodeModel { Id = "OuterMerge", Title = "外汇聚" };
        var end = new EndNodeModel { Id = "End", Title = "结束" };
        var canvasDocument = new WorkflowDocument { Name = "嵌套并行" };
        var canvas = canvasDocument.CanvasProjection;
        foreach (var node in new IWorkflowNodeModel[] { outer, inner, innerA, innerB, outerB, innerMerge, outerMerge, end })
            canvas.Nodes.Add(new WorkflowCanvasNode { Node = node });
        canvas.Connections.Add(Connect("Outer", WorkflowPorts.Branch, "Inner"));
        canvas.Connections.Add(Connect("Outer", WorkflowPorts.Branch, "OuterB"));
        canvas.Connections.Add(Connect("Inner", WorkflowPorts.Branch, "InnerA"));
        canvas.Connections.Add(Connect("Inner", WorkflowPorts.Branch, "InnerB"));
        canvas.Connections.Add(Connect("InnerA", WorkflowPorts.Success, "InnerMerge"));
        canvas.Connections.Add(Connect("InnerB", WorkflowPorts.Success, "InnerMerge"));
        canvas.Connections.Add(Connect("InnerMerge", WorkflowPorts.Success, "OuterMerge"));
        canvas.Connections.Add(Connect("OuterB", WorkflowPorts.Success, "OuterMerge"));
        canvas.Connections.Add(Connect("OuterMerge", WorkflowPorts.Success, "End"));
        var executed = new List<string>();
        var actions = new WorkflowActionRegistry();
        foreach (var key in new[] { "InnerA", "InnerB", "OuterB" })
        {
            var capturedKey = key;
            actions.Register(key, (_, _) =>
            {
                lock (executed)
                    executed.Add(capturedKey);
                return ValueTask.FromResult<object?>(null);
            });
        }
        var services = new WorkflowServiceProvider().Add<IWorkflowActionRegistry>(actions);

        var engine = CreateEngine(canvasDocument, services, "Outer");
        var result = await engine.RunAsync();
        var snapshot = engine.GetRuntimeSnapshot();

        Assert.True(result.Success, result.Message);
        Assert.Equal(3, executed.Count);
        Assert.Equal(2, snapshot.ParallelScopes.Count);
        Assert.All(snapshot.ParallelScopes.Values, scope => Assert.True(scope.IsCompleted));
    }

    [Fact]
    public async Task RunAsync_WhenOneBranchFails_CancelsSiblingAndReturnsOriginalFailure()
    {
        var document = BuildParallelDocument();
        var canvas = document.CanvasProjection;
        var siblingCanceled = false;
        var actions = new WorkflowActionRegistry()
            .Register("BranchA", (_, _) => throw new InvalidOperationException("Branch A failed."))
            .Register("BranchB", async (_, cancellationToken) =>
            {
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                    return null;
                }
                catch (OperationCanceledException)
                {
                    siblingCanceled = true;
                    throw;
                }
            })
            .Register("AfterMerge", (_, _) => ValueTask.FromResult<object?>(null));
        var services = new WorkflowServiceProvider().Add<IWorkflowActionRegistry>(actions);

        var result = await CreateEngine(document, services).RunAsync();

        Assert.False(result.Success);
        Assert.Contains("Branch A failed", result.Message);
        Assert.True(siblingCanceled);
    }

    private static WorkflowEngine CreateEngine(
        WorkflowDocument document,
        IServiceProvider services,
        string startNodeId = "Parallel",
        bool validateBindings = true)
    {
        var catalog = new WorkflowNodeCatalog().RegisterStandardNodes();
        document.EntryNodeId = startNodeId;
        var definition = new WorkflowCompiler(catalog, validateBindings).Compile(document);
        var handlers = new WorkflowNodeHandlerCatalog()
            .Register(new ParallelAllNodeHandler())
            .Register(new ActionNodeHandler())
            .Register(new DecisionNodeHandler())
            .Register(new WaitAllInputsCompletedNodeHandler())
            .Register(new EndNodeHandler());
        return new WorkflowEngine(definition, handlers, new WorkflowContext(services));
    }

    private static WorkflowDocument BuildParallelDocument()
    {
        var parallel = new ParallelAllNodeModel { Id = "Parallel", Title = "并行" };
        var branchA = new ActionNodeModel { Id = "BranchA", Title = "分支A", FunctionKey = "BranchA" };
        var branchB = new ActionNodeModel { Id = "BranchB", Title = "分支B", FunctionKey = "BranchB" };
        var merge = new WaitAllInputsCompletedNodeModel { Id = "Merge", Title = "汇聚" };
        var after = new ActionNodeModel { Id = "After", Title = "汇聚后", FunctionKey = "AfterMerge" };
        var end = new EndNodeModel { Id = "End", Title = "结束" };
        var canvasDocument = new WorkflowDocument { Name = "并行运行" };
        var canvas = canvasDocument.CanvasProjection;
        foreach (var node in new IWorkflowNodeModel[] { parallel, branchA, branchB, merge, after, end })
            canvas.Nodes.Add(new WorkflowCanvasNode { Node = node });
        // 先调度 B，故障测试可确认 A 失败后会取消已经运行的兄弟分支。
        canvas.Connections.Add(Connect("Parallel", WorkflowPorts.Branch, "BranchB"));
        canvas.Connections.Add(Connect("Parallel", WorkflowPorts.Branch, "BranchA"));
        canvas.Connections.Add(Connect("BranchA", WorkflowPorts.Success, "Merge"));
        canvas.Connections.Add(Connect("BranchB", WorkflowPorts.Success, "Merge"));
        canvas.Connections.Add(Connect("Merge", WorkflowPorts.Success, "After"));
        canvas.Connections.Add(Connect("After", WorkflowPorts.Success, "End"));
        return canvasDocument;
    }

    private static WorkflowConnectionModel Connect(string from, string port, string to) => new()
    {
        FromNodeId = from,
        FromPort = port,
        ToNodeId = to
    };

    private sealed record BooleanOutput(bool Value);

    private static void UpdateMaximum(ref int maximum, int candidate)
    {
        while (true)
        {
            var current = Volatile.Read(ref maximum);
            if (candidate <= current || Interlocked.CompareExchange(ref maximum, candidate, current) == current)
                return;
        }
    }
}
