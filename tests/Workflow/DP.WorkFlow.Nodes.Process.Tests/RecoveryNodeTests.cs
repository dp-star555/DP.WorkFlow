namespace DP.WorkFlow.Tests;

public sealed class RecoveryNodeTests
{
    [Fact]
    public async Task SafePoint_RegistersExecutionIdentityAndContinues()
    {
        var node = new SafePointNodeModel
        {
            Id = "Safe1",
            Title = "安全点",
            SafePointKey = "BeforePick"
        };
        var canvasDocument = new WorkflowDocument { Name = "恢复" };
        var canvas = canvasDocument.CanvasProjection;
        canvas.Nodes.Add(new WorkflowCanvasNode { Node = node });
        var catalog = new WorkflowNodeCatalog().RegisterProcessNodes();
        canvasDocument.EntryNodeId = node.Id;
        var definition = new WorkflowCompiler(catalog).Compile(canvasDocument);
        var recovery = new FakeRecoveryService();
        var services = new WorkflowServiceProvider().Add<IWorkflowRecoveryService>(recovery);

        var result = await new WorkflowEngine(
            definition,
            new WorkflowNodeHandlerCatalog().RegisterProcessNodeHandlers(),
            new WorkflowContext(services)).RunAsync();

        Assert.True(result.Success);
        Assert.Equal("BeforePick", recovery.SafePointKey);
        Assert.NotNull(recovery.Identity);
    }

    [Fact]
    public void WarningHandlerBlock_OwnsDedicatedDisconnectedSubDocument()
    {
        var block = new WarningHandlerBlockNodeModel { Id = "WarningBlock", Title = "告警处理" };
        var catalog = new WorkflowNodeCatalog().RegisterProcessNodes();
        var descriptor = catalog.GetOrThrow(block.NodeType);

        Assert.IsAssignableFrom<IWorkflowSubDocumentNode>(block);
        Assert.Equal("WarningHandlerStart", block.SubDocument.EntryNodeId);
        Assert.Contains(block.SubDocument.CanvasProjection.Nodes, item => item.Node is WarningHandlerStartNodeModel);
        Assert.DoesNotContain(descriptor.Ports, port => port.Direction == WorkflowPortDirection.Input || port.Direction == WorkflowPortDirection.Output);
    }

    [Fact]
    public async Task RetryCurrent_WritesWarningResolutionAndUsesLegacyNodeType()
    {
        var node = new RetryCurrentNodeModel { Id = "Retry", Title = "重试", Note = "人工确认" };
        var canvasDocument = new WorkflowDocument { Name = "恢复" };
        var canvas = canvasDocument.CanvasProjection;
        canvas.Nodes.Add(new WorkflowCanvasNode { Node = node });
        canvasDocument.EntryNodeId = node.Id;
        var definition = new WorkflowCompiler(new WorkflowNodeCatalog().RegisterProcessNodes()).Compile(canvasDocument);
        var context = new WorkflowContext();

        var run = await new WorkflowEngine(definition, new WorkflowNodeHandlerCatalog().RegisterProcessNodeHandlers(), context).RunAsync();

        Assert.True(run.Success);
        Assert.Equal("WarnRetryCurrentNode", node.NodeType);
        Assert.True(context.TryGetVariable<WarningResolution>(WorkflowRecoveryRuntimeKeys.WarningResolution, out var resolution));
        Assert.Equal(E_WarningExitAction.RetryCurrentNode, resolution!.ExitAction);
    }

    [Fact]
    public void JumpToNode_UsesLegacyStableNodeType()
    {
        var node = new JumpToNodeNodeModel();
        Assert.Equal("WarnJumpToNode", node.NodeType);
        Assert.NotNull(new WorkflowNodeCatalog().RegisterProcessNodes().GetOrThrow(node.NodeType));
    }

    [Fact]
    public async Task WarningCoordinator_RetriesFaultedNodeAfterRecoverySubflow()
    {
        var block = new WarningHandlerBlockNodeModel { Id = "Warning", Title = "告警" };
        block.SubDocument.CanvasProjection.Nodes.Clear();
        block.SubDocument.CanvasProjection.Connections.Clear();
        var start = new WarningHandlerStartNodeModel { Id = "HandlerStart", Title = "入口" };
        var capture = new CaptureInterruptNode { Id = "Capture", Title = "采集中断" };
        var retry = new RetryCurrentNodeModel { Id = "Retry", Title = "重试" };
        block.SubDocument.CanvasProjection.Nodes.Add(new WorkflowCanvasNode { Node = start });
        block.SubDocument.CanvasProjection.Nodes.Add(new WorkflowCanvasNode { Node = capture });
        block.SubDocument.CanvasProjection.Nodes.Add(new WorkflowCanvasNode { Node = retry });
        block.SubDocument.CanvasProjection.Connections.Add(new WorkflowConnectionModel { FromNodeId = start.Id, FromPort = WorkflowPorts.Success, ToNodeId = capture.Id });
        block.SubDocument.CanvasProjection.Connections.Add(new WorkflowConnectionModel { FromNodeId = capture.Id, FromPort = WorkflowPorts.Success, ToNodeId = retry.Id });
        block.SubDocument.EntryNodeId = start.Id;

        var flaky = new FlakyNode { Id = "Flaky", Title = "故障一次" };
        var canvasDocument = new WorkflowDocument { Name = "可恢复流程" };
        var canvas = canvasDocument.CanvasProjection;
        canvas.Nodes.Add(new WorkflowCanvasNode { Node = flaky });
        var catalog = new WorkflowNodeCatalog()
            .RegisterProcessNodes()
            .Register(WorkflowNodeDescriptor.Create<FlakyNode>(ports: new[] { WorkflowPortDescriptor.Output() }))
            .Register(WorkflowNodeDescriptor.Create<CaptureInterruptNode>(ports: new[] { WorkflowPortDescriptor.Input(), WorkflowPortDescriptor.Output() }));
        var flakyHandler = new FlakyNodeHandler();
        var captureHandler = new CaptureInterruptNodeHandler();
        var handlers = new WorkflowNodeHandlerCatalog().RegisterProcessNodeHandlers().Register(flakyHandler).Register(captureHandler);
        var coordinator = new WorkflowWarningHandlerCoordinator(block, catalog, handlers);
        var services = new WorkflowServiceProvider().Add<IWorkflowFaultRecoveryCoordinator>(coordinator);
        canvasDocument.EntryNodeId = flaky.Id;
        var definition = new WorkflowCompiler(catalog).Compile(canvasDocument);

        var result = await new WorkflowEngine(definition, handlers, new WorkflowContext(services)).RunAsync();

        Assert.True(result.Success, result.Message);
        Assert.Equal(2, flakyHandler.ExecutionCount);
        Assert.NotNull(captureHandler.Interrupt);
        Assert.Equal(1001, captureHandler.Interrupt!.AlarmCode);
        Assert.Equal(flaky.Id, captureHandler.Interrupt.FaultNodeId);
        Assert.Equal(flaky.Title, captureHandler.Interrupt.FaultNodeTitle);
        Assert.Equal(1, captureHandler.Interrupt.Payload["RecoveryAttempt"]);
    }

    [Fact]
    public async Task WarningCoordinator_RejectsLegacyJumpWithoutExecutingTarget()
    {
        var block = CreateWarningBlock(new JumpToNodeNodeModel { Id = "Jump", Title = "跳转", TargetNodeId = "Target" });
        var fault = new AlwaysFaultNode { Id = "Fault", Title = "持续故障" };
        var target = new MarkerNode { Id = "Target", Title = "恢复目标" };
        var canvasDocument = new WorkflowDocument { Name = "跳转恢复" };
        var canvas = canvasDocument.CanvasProjection;
        canvas.Nodes.Add(new WorkflowCanvasNode { Node = fault });
        canvas.Nodes.Add(new WorkflowCanvasNode { Node = target });
        var catalog = CreateTestCatalog();
        var faultHandler = new AlwaysFaultNodeHandler();
        var markerHandler = new MarkerNodeHandler();
        var handlers = new WorkflowNodeHandlerCatalog().RegisterProcessNodeHandlers().Register(faultHandler).Register(markerHandler);
        var coordinator = new WorkflowWarningHandlerCoordinator(block, catalog, handlers);
        var context = new WorkflowContext(new WorkflowServiceProvider().Add<IWorkflowFaultRecoveryCoordinator>(coordinator));
        canvasDocument.EntryNodeId = fault.Id;
        var definition = new WorkflowCompiler(catalog).Compile(canvasDocument);

        var result = await new WorkflowEngine(definition, handlers, context).RunAsync();

        Assert.False(result.Success);
        Assert.Contains("旧Jump恢复已禁用", result.Message);
        Assert.Equal(1, faultHandler.ExecutionCount);
        Assert.False(markerHandler.Executed);
    }

    [Fact]
    public async Task WarningCoordinator_StopResolution_StopsStationAndFaultsMainWorkflow()
    {
        var block = CreateWarningBlock(new StopCurrentStationNodeModel { Id = "Stop", Title = "停机", Reason = "人工停机" });
        var fault = new AlwaysFaultNode { Id = "Fault", Title = "持续故障" };
        var canvasDocument = new WorkflowDocument { Name = "停止恢复" };
        var canvas = canvasDocument.CanvasProjection;
        canvas.Nodes.Add(new WorkflowCanvasNode { Node = fault });
        var catalog = CreateTestCatalog();
        var handlers = new WorkflowNodeHandlerCatalog().RegisterProcessNodeHandlers().Register(new AlwaysFaultNodeHandler()).Register(new MarkerNodeHandler());
        var recovery = new FakeRecoveryService();
        var services = new WorkflowServiceProvider().Add<IWorkflowRecoveryService>(recovery);
        var coordinator = new WorkflowWarningHandlerCoordinator(block, catalog, handlers);
        services.Add<IWorkflowFaultRecoveryCoordinator>(coordinator);
        canvasDocument.EntryNodeId = fault.Id;
        var definition = new WorkflowCompiler(catalog).Compile(canvasDocument);

        var result = await new WorkflowEngine(definition, handlers, new WorkflowContext(services)).RunAsync();

        Assert.False(result.Success);
        Assert.Equal(E_WorkflowExecutionState.Faulted, result.State);
        Assert.Equal("人工停机", recovery.StopReason);
    }

    private static WarningHandlerBlockNodeModel CreateWarningBlock(WorkflowNodeModel exit)
    {
        var block = new WarningHandlerBlockNodeModel { Id = "Warning", Title = "告警" };
        block.SubDocument.CanvasProjection.Nodes.Clear();
        block.SubDocument.CanvasProjection.Connections.Clear();
        var start = new WarningHandlerStartNodeModel { Id = "HandlerStart", Title = "入口" };
        block.SubDocument.CanvasProjection.Nodes.Add(new WorkflowCanvasNode { Node = start });
        block.SubDocument.CanvasProjection.Nodes.Add(new WorkflowCanvasNode { Node = exit });
        block.SubDocument.CanvasProjection.Connections.Add(new WorkflowConnectionModel { FromNodeId = start.Id, FromPort = WorkflowPorts.Success, ToNodeId = exit.Id });
        block.SubDocument.EntryNodeId = start.Id;
        return block;
    }

    private static WorkflowNodeCatalog CreateTestCatalog() => new WorkflowNodeCatalog()
        .RegisterProcessNodes()
        .Register(WorkflowNodeDescriptor.Create<AlwaysFaultNode>(ports: new[] { WorkflowPortDescriptor.Output() }))
        .Register(WorkflowNodeDescriptor.Create<MarkerNode>(ports: new[] { WorkflowPortDescriptor.Input() }));

    [WorkflowNode("TestAlwaysFault")]
    private sealed class AlwaysFaultNode : WorkflowNodeModel { public override string NodeType => "TestAlwaysFault"; }
    private sealed class AlwaysFaultNodeHandler : WorkflowNodeHandler<AlwaysFaultNode>
    {
        public int ExecutionCount { get; private set; }
        protected override ValueTask<NodeExecutionResult> ExecuteAsync(AlwaysFaultNode node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
        {
            ExecutionCount++;
            return ValueTask.FromResult(NodeExecutionResult.Fail("持续故障", WorkflowFaultDisposition.RequestRecovery, 2001));
        }
    }

    [WorkflowNode("TestMarker")]
    private sealed class MarkerNode : WorkflowNodeModel, IWorkflowRecoveryTargetNode { public override string NodeType => "TestMarker"; }
    private sealed class MarkerNodeHandler : WorkflowNodeHandler<MarkerNode>
    {
        public bool Executed { get; private set; }
        protected override ValueTask<NodeExecutionResult> ExecuteAsync(MarkerNode node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken) { Executed = true; return ValueTask.FromResult(NodeExecutionResult.Complete()); }
    }

    [WorkflowNode("TestFlaky")]
    private sealed class FlakyNode : WorkflowNodeModel, IWorkflowRetrySafetyNode
    {
        public override string NodeType => "TestFlaky";
        public WorkflowRetrySafety RetrySafety => WorkflowRetrySafety.Idempotent;
    }

    private sealed class FlakyNodeHandler : WorkflowNodeHandler<FlakyNode>
    {
        public int ExecutionCount { get; private set; }
        protected override ValueTask<NodeExecutionResult> ExecuteAsync(FlakyNode node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
        {
            ExecutionCount++;
            return ValueTask.FromResult(ExecutionCount == 1 ? NodeExecutionResult.Fail("一次性故障", WorkflowFaultDisposition.RequestRecovery, 1001) : NodeExecutionResult.Complete());
        }
    }

    [WorkflowNode("TestCaptureInterrupt")]
    private sealed class CaptureInterruptNode : WorkflowNodeModel { public override string NodeType => "TestCaptureInterrupt"; }

    private sealed class CaptureInterruptNodeHandler : WorkflowNodeHandler<CaptureInterruptNode>
    {
        public WorkflowInterruptContext? Interrupt { get; private set; }
        protected override ValueTask<NodeExecutionResult> ExecuteAsync(CaptureInterruptNode node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
        {
            context.TryGetVariable<WorkflowInterruptContext>(WorkflowRecoveryRuntimeKeys.InterruptContext, out var interrupt);
            Interrupt = interrupt;
            return ValueTask.FromResult(NodeExecutionResult.Continue());
        }
    }

    private sealed class FakeRecoveryService : IWorkflowRecoveryService
    {
        public string? SafePointKey { get; private set; }

        public WorkflowExecutionIdentity? Identity { get; private set; }

        public string? StopReason { get; private set; }

        public ValueTask RegisterSafePointAsync(
            string safePointKey,
            WorkflowExecutionIdentity executionIdentity,
            CancellationToken cancellationToken)
        {
            SafePointKey = safePointKey;
            Identity = executionIdentity;
            return ValueTask.CompletedTask;
        }

        public ValueTask RequestStationStopAsync(
            string reason,
            WorkflowExecutionIdentity executionIdentity,
            CancellationToken cancellationToken)
        {
            StopReason = reason;
            return ValueTask.CompletedTask;
        }
        public ValueTask RequestRetryAsync(WorkflowExecutionIdentity executionIdentity, CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask ReturnToSafePointAsync(string safePointKey, WorkflowExecutionIdentity executionIdentity, CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }
}
