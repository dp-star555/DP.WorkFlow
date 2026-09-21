namespace DP.WorkFlow.Tests;

public sealed class WorkflowContextTests
{
    [Fact]
    public void SetVariable_WhenValueIsNull_ThrowsAndDoesNotCreateVariable()
    {
        var context = new WorkflowContext();

        Assert.Throws<ArgumentNullException>(() => context.SetVariable("Value", null!));
        Assert.False(context.ContainsVariable("Value"));
    }

    [Fact]
    public void RemoveVariable_UsesExplicitDeletionSemantics()
    {
        var context = new WorkflowContext();
        context.SetVariable("Value", 42);

        var removed = context.RemoveVariable("Value");

        Assert.True(removed);
        Assert.False(context.ContainsVariable("Value"));
    }

    [Fact]
    public void PublicData_IsSharedWithChildScopeButLocalVariablesAreIsolated()
    {
        var store = new WorkflowPublicDataStore();
        var parent = new WorkflowContext(publicData: store);
        parent.SetVariable("Local", 1);
        var child = parent.CreateChildScope();
        store.Apply(new WorkflowPublicDataChangeSet(
            new Dictionary<string, object> { ["Shared"] = 2 },
            Array.Empty<string>()));

        Assert.False(child.TryGetVariable<int>("Local", out _));
        Assert.True(child.PublicData.TryGet<int>("Shared", out var shared));
        Assert.Equal(2, shared);
    }

    [Fact]
    public async Task RunAsync_WhenCancellationIsRequested_ReturnsCanceledResult()
    {
        var canvasDocument = new WorkflowDocument { Name = "取消测试" };
        var canvas = canvasDocument.CanvasProjection;
        canvas.Nodes.Add(new WorkflowCanvasNode { Node = new BlockingNode { Id = "N1", Title = "等待" } });
        canvasDocument.EntryNodeId = "N1";
        var definition = new WorkflowCompiler().Compile(canvasDocument);
        var handlers = new WorkflowNodeHandlerCatalog().Register(new BlockingNodeHandler());
        var engine = new WorkflowEngine(definition, handlers);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        var result = await engine.RunAsync(cancellation.Token);

        Assert.False(result.Success);
        Assert.Equal(E_WorkflowExecutionState.Canceled, result.State);
    }

    [Fact]
    public async Task WorkflowSignals_AreSharedWithChildScopeAndUseAsyncNotification()
    {
        var parent = new WorkflowContext();
        var child = parent.CreateChildScope();
        var waiting = parent.WaitAllWorkflowSignalsAsync(new[] { "A", "B" }, TimeSpan.FromSeconds(1), CancellationToken.None).AsTask();

        child.RaiseWorkflowSignal("A");
        Assert.False(waiting.IsCompleted);
        child.RaiseWorkflowSignal("B");

        Assert.True(await waiting);
        Assert.True(parent.ContainsWorkflowSignal("A"));
    }

    [Fact]
    public async Task SuccessfulNode_CommitsStagedPublicData()
    {
        var node = new PublishNode { Id = "Publish" };
        var canvasDocument = new WorkflowDocument();
        var canvas = canvasDocument.CanvasProjection;
        canvas.Nodes.Add(new WorkflowCanvasNode { Node = node });
        var context = new WorkflowContext();
        canvasDocument.EntryNodeId = node.Id;
        var plan = new WorkflowCompiler().Compile(canvasDocument);

        var result = await new WorkflowEngine(
            plan,
            new WorkflowNodeHandlerCatalog().Register(new PublishNodeHandler()),
            context).RunAsync();

        Assert.True(result.Success, result.Message);
        Assert.True(context.PublicData.TryGet<int>("Published", out var value));
        Assert.Equal(42, value);
        Assert.False(context.ContainsVariable("Published"));
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 0)]
    public async Task NodeFailure_UsesScopePolicyUnlessExplicitlyTerminal(bool stopImmediately, int recoveryCalls)
    {
        var node = new FailNode { Id = "Fail", Title = "失败", StopImmediately = stopImmediately };
        var canvasDocument = new WorkflowDocument { Name = "不可恢复" };
        var canvas = canvasDocument.CanvasProjection;
        canvas.Nodes.Add(new WorkflowCanvasNode { Node = node });
        var coordinator = new CountingRecoveryCoordinator();
        var context = new WorkflowContext(new WorkflowServiceProvider().Add<IWorkflowFaultRecoveryCoordinator>(coordinator));
        canvasDocument.EntryNodeId = node.Id;
        var definition = new WorkflowCompiler().Compile(canvasDocument);
        var engine = new WorkflowEngine(definition, new WorkflowNodeHandlerCatalog().Register(new FailNodeHandler()), context);
        var result = await engine.RunAsync();

        Assert.False(result.Success);
        Assert.Equal(recoveryCalls, coordinator.CallCount);
        Assert.Empty(context.ExportNodeOutputs());
        Assert.False(context.ContainsVariable("Uncommitted"));
        Assert.False(context.PublicData.TryGet<int>("UncommittedPublic", out _));
        var fault = Assert.Single(engine.RunState.Faults);
        Assert.Equal(node.Id, fault.NodeId);
        Assert.Equal("fatal", fault.Message);
        Assert.NotNull(engine.GetRuntimeSnapshot().CurrentFault);
    }

    [Fact]
    public async Task RecoverableFailure_StopsAfterConfiguredAttemptLimit()
    {
        var node = new RecoverableFailNode { Id = "RecoverableFail", Title = "持续失败" };
        var canvasDocument = new WorkflowDocument { Name = "恢复上限" };
        var canvas = canvasDocument.CanvasProjection;
        canvas.Nodes.Add(new WorkflowCanvasNode { Node = node });
        var handler = new RecoverableFailNodeHandler();
        var coordinator = new CountingRecoveryCoordinator();
        var context = new WorkflowContext(new WorkflowServiceProvider().Add<IWorkflowFaultRecoveryCoordinator>(coordinator));
        canvasDocument.EntryNodeId = node.Id;
        var definition = new WorkflowCompiler().Compile(canvasDocument);
        var options = new WorkflowExecutionOptions { MaxRecoveryAttempts = 2 };

        var result = await new WorkflowEngine(definition, new WorkflowNodeHandlerCatalog().Register(handler), context, options).RunAsync();

        Assert.False(result.Success);
        Assert.Contains("超过上限 2", result.Message, StringComparison.Ordinal);
        Assert.Equal(3, handler.ExecutionCount);
        Assert.Equal(2, coordinator.CallCount);
    }

    [Fact]
    public async Task RetryWithoutExplicitSafetyDeclaration_IsRejected()
    {
        var node = new UnsafeRecoverableNode { Id = "Unsafe", Title = "非幂等" };
        var canvasDocument = new WorkflowDocument();
        var canvas = canvasDocument.CanvasProjection;
        canvas.Nodes.Add(new WorkflowCanvasNode { Node = node });
        var coordinator = new CountingRecoveryCoordinator();
        var context = new WorkflowContext(new WorkflowServiceProvider().Add<IWorkflowFaultRecoveryCoordinator>(coordinator));
        canvasDocument.EntryNodeId = node.Id;
        var plan = new WorkflowCompiler().Compile(canvasDocument);

        var result = await new WorkflowEngine(
            plan,
            new WorkflowNodeHandlerCatalog().Register(new UnsafeRecoverableNodeHandler()),
            context).RunAsync();

        Assert.False(result.Success);
        Assert.Contains("禁止重试", result.Message, StringComparison.Ordinal);
        Assert.Equal(1, coordinator.CallCount);
    }

    [Fact]
    public async Task CancelDuringRecovery_RemovesRecoveryHoldAndCancelsRun()
    {
        var node = new RecoverableFailNode { Id = "RecoverableFail", Title = "持续失败" };
        var canvasDocument = new WorkflowDocument { Name = "取消恢复" };
        var canvas = canvasDocument.CanvasProjection;
        canvas.Nodes.Add(new WorkflowCanvasNode { Node = node });
        var coordinator = new BlockingRecoveryCoordinator();
        var context = new WorkflowContext(new WorkflowServiceProvider().Add<IWorkflowFaultRecoveryCoordinator>(coordinator));
        canvasDocument.EntryNodeId = node.Id;
        var definition = new WorkflowCompiler().Compile(canvasDocument);
        var engine = new WorkflowEngine(definition, new WorkflowNodeHandlerCatalog().Register(new RecoverableFailNodeHandler()), context);
        using var cts = new CancellationTokenSource();
        var runTask = engine.RunAsync(cts.Token);
        await coordinator.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Contains(engine.ExternalHoldReasons, reason => reason.StartsWith("Recovery:", StringComparison.Ordinal)
            && Guid.TryParse(reason["Recovery:".Length..], out _));
        cts.Cancel();
        var result = await runTask;

        Assert.Equal(E_WorkflowExecutionState.Canceled, result.State);
        Assert.Empty(engine.ExternalHoldReasons);
    }

    private sealed class PublishNode : WorkflowNodeModel { public override string NodeType => "Publish"; }
    private sealed class PublishNodeHandler : WorkflowNodeHandler<PublishNode>
    {
        protected override ValueTask<NodeExecutionResult> ExecuteAsync(
            PublishNode node,
            IWorkflowNodeExecutionContext context,
            CancellationToken cancellationToken)
        {
            context.PublishData("Published", 42);
            return ValueTask.FromResult(NodeExecutionResult.Complete());
        }
    }

    private sealed class FailNode : WorkflowNodeModel
    {
        public override string NodeType => "Fail";
        public bool StopImmediately { get; set; }
    }
    private sealed class FailNodeHandler : WorkflowNodeHandler<FailNode>
    {
        protected override ValueTask<NodeExecutionResult> ExecuteAsync(FailNode node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
        {
            context.SetVariable("Uncommitted", 42);
            context.PublishData("UncommittedPublic", 43);
            return ValueTask.FromResult(NodeExecutionResult.Fail("fatal", node.StopImmediately
                ? WorkflowFaultDisposition.StopRun : WorkflowFaultDisposition.HandleAtScope));
        }
    }
    private sealed class CountingRecoveryCoordinator : IWorkflowFaultRecoveryCoordinator
    {
        public int CallCount { get; private set; }
        public ValueTask<WorkflowFaultRecoveryDecision> RecoverAsync(WorkflowFaultRecoveryRequest request, IWorkflowFaultRecoveryContext context, CancellationToken cancellationToken) { CallCount++; return ValueTask.FromResult(WorkflowFaultRecoveryDecision.Retry()); }
    }
    private sealed class RecoverableFailNode : WorkflowNodeModel, IWorkflowRetrySafetyNode
    {
        public override string NodeType => "RecoverableFail";
        public WorkflowRetrySafety RetrySafety => WorkflowRetrySafety.Idempotent;
    }
    private sealed class RecoverableFailNodeHandler : WorkflowNodeHandler<RecoverableFailNode>
    {
        public int ExecutionCount { get; private set; }
        protected override ValueTask<NodeExecutionResult> ExecuteAsync(RecoverableFailNode node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken) { ExecutionCount++; return ValueTask.FromResult(NodeExecutionResult.Fail("recoverable", WorkflowFaultDisposition.RequestRecovery, 3001)); }
    }
    private sealed class UnsafeRecoverableNode : WorkflowNodeModel
    {
        public override string NodeType => "UnsafeRecoverable";
    }
    private sealed class UnsafeRecoverableNodeHandler : WorkflowNodeHandler<UnsafeRecoverableNode>
    {
        protected override ValueTask<NodeExecutionResult> ExecuteAsync(UnsafeRecoverableNode node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken) =>
            ValueTask.FromResult(NodeExecutionResult.Fail("unsafe", WorkflowFaultDisposition.RequestRecovery, 3002));
    }
    private sealed class BlockingRecoveryCoordinator : IWorkflowFaultRecoveryCoordinator
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async ValueTask<WorkflowFaultRecoveryDecision> RecoverAsync(WorkflowFaultRecoveryRequest request, IWorkflowFaultRecoveryContext context, CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return WorkflowFaultRecoveryDecision.Retry();
        }
    }

    private sealed class BlockingNode : WorkflowNodeModel
    {
        public override string NodeType => "Blocking";
    }

    private sealed class BlockingNodeHandler : WorkflowNodeHandler<BlockingNode>
    {
        protected override async ValueTask<NodeExecutionResult> ExecuteAsync(
            BlockingNode node,
            IWorkflowNodeExecutionContext context,
            CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return NodeExecutionResult.Complete();
        }
    }
}
