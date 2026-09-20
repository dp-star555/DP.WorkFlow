namespace DP.WorkFlow.Tests;

public sealed class RecoveryV1Tests
{
    [Theory]
    [InlineData("Continue", 1, 1, 110)]
    [InlineData("restart", 2, 2, 10)]
    public async Task EngineerAuthoredChoices_ContinueOrRestartWithoutRepeatingFeed(
        string choice, int creations, int commands, int target)
    {
        var rig = new Rig(choice);
        var result = await rig.Engine.RunAsync();

        Assert.True(result.Success, result.Message);
        Assert.Equal(1, rig.Feed.Count);
        Assert.True(rig.Context.PublicData.TryGet<int>("recorded-feed", out var feedRecord));
        Assert.Equal(1, feedRecord);
        Assert.Equal(creations, rig.Motion.Created);
        Assert.Equal(commands, rig.Axis.Commands);
        Assert.Equal(target, rig.Axis.Position);
        Assert.Equal(creations, rig.Motion.Disposed);
        Assert.False(rig.Context.ContainsVariable("failed-attempt-only"));
        Assert.Equal(1, rig.Operator.ChoiceCalls);
        Assert.Equal(creations - 1, rig.Operator.ConfirmCalls);
        Assert.NotEmpty(rig.Engine.RunState.RecoveryEvents);
        Assert.Contains(rig.Engine.GetRuntimeSnapshot().ChildWorkflows.Values,
            child => child.Snapshot.ExecutionState == E_WorkflowExecutionState.Completed);
        if (creations == 2)
        {
            Assert.Equal(2, rig.Prepare.Count);
            Assert.False(rig.Prepare.SawStaleValue);
            Assert.False(rig.Prepare.SawStaleOutput);
            Assert.NotNull(rig.Guard.Plan);
            Assert.DoesNotContain("feed", rig.Guard.Plan!.AffectedNodeIds);
            Assert.Contains("derived", rig.Guard.Plan.InvalidatedVariableKeys);
            Assert.NotEmpty(rig.Engine.RunState.InvalidatedOutputSequences);
            Assert.Equal(new[] { 110, 10 }, rig.Motion.Targets);
            Assert.Equal(2, rig.Engine.RunState.NodeOutputs.Count(output => output.NodeId == "prepare"));
        }
        else Assert.Empty(rig.Engine.RunState.InvalidatedOutputSequences);
    }

    [Fact]
    public async Task Continue_DoesNotRereadChangedIntentInputs()
    {
        var rig = new Rig("Continue");
        rig.Context.SetVariable("delta", 10);
        rig.Operator.OnChoice = () => rig.Context.SetVariable("delta", 50);
        var result = await rig.Engine.RunAsync();
        Assert.True(result.Success, result.Message);
        Assert.Equal(new[] { 110 }, rig.Motion.Targets);
        Assert.Equal(1, rig.Axis.Commands);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RecoverySubflow_ObeysIndependentPauseAndHold(bool manual)
    {
        var rig = new Rig("Continue");
        rig.Operator.OnChoice = () => { if (manual) rig.Engine.Pause(); else rig.Engine.AddExternalHold("door"); };
        var running = rig.Engine.RunAsync();
        await rig.Operator.ChoiceEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(running.IsCompleted);
        Assert.DoesNotContain(rig.Engine.RunState.RecoveryEvents, item => item.Stage == "Applied");
        if (manual) rig.Engine.Resume(); else rig.Engine.RemoveExternalHold("door");
        var result = await running.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(result.Success, result.Message);
        Assert.Equal(1, rig.Axis.Commands);
    }

    [Fact]
    public async Task GuardRejection_ReturnsToSameCaseWithoutReexecutingBusiness()
    {
        var rig = new Rig("Restart", "Stop");
        rig.Guard.Allow = false;
        var result = await rig.Engine.RunAsync();

        Assert.False(result.Success);
        Assert.Equal(1, rig.Axis.Commands);
        Assert.Equal(1, rig.Feed.Count);
        Assert.Equal(1, rig.Prepare.Count);
        Assert.Empty(rig.Engine.RunState.InvalidatedOutputSequences);
        Assert.Single(rig.Engine.RunState.RecoveryEvents.Select(item => item.CaseId).Distinct());
        Assert.Contains(rig.Engine.RunState.RecoveryEvents, item => item.Stage == "Rejected" && item.Message!.Contains("夹具"));
        Assert.Equal(2, rig.Operator.ChoiceCalls);
    }

    [Fact]
    public async Task TreatmentException_PreservesCaseAndStopsWithoutReplayingTreatment()
    {
        var rig = new Rig("Restart", "Stop");
        rig.Operator.FailConfirmation = true;
        var result = await rig.Engine.RunAsync();

        Assert.False(result.Success);
        Assert.Equal(1, rig.Axis.Commands);
        Assert.Equal(1, rig.Operator.ChoiceCalls);
        Assert.Single(rig.Engine.RunState.RecoveryEvents.Select(item => item.CaseId).Distinct());
        Assert.Contains(rig.Engine.RunState.RecoveryEvents, item => item.Stage == "Stopped" && item.Message!.Contains("人工处置失败"));
        Assert.Contains(rig.Engine.RunState.Faults, item => item.NodeId == "motion");
    }

    [Fact]
    public async Task CancelDuringChoice_IgnoresLateContinueAndDisposesOriginalOperation()
    {
        using var cancellation = new CancellationTokenSource();
        var rig = new Rig("Continue");
        rig.Operator.OnChoice = cancellation.Cancel;
        var result = await rig.Engine.RunAsync(cancellation.Token);

        Assert.Equal(E_WorkflowExecutionState.Canceled, result.State);
        Assert.Equal(1, rig.Axis.Commands);
        Assert.Equal(1, rig.Motion.Attempts);
        Assert.Equal(1, rig.Motion.Disposed);
        Assert.DoesNotContain(rig.Engine.RunState.RecoveryEvents, item => item.Stage == "Applied");
    }

    [Fact]
    public async Task CancelDuringOperationCreation_DoesNotExecuteLateCreatedOperation()
    {
        using var cancellation = new CancellationTokenSource();
        var rig = new Rig("Continue");
        rig.Motion.OnCreate = cancellation.Cancel;
        var result = await rig.Engine.RunAsync(cancellation.Token);
        Assert.Equal(E_WorkflowExecutionState.Canceled, result.State);
        Assert.Equal(0, rig.Axis.Commands);
        Assert.Equal(0, rig.Motion.Attempts);
        Assert.Equal(1, rig.Motion.Disposed);
        Assert.Equal(0, rig.Operator.ChoiceCalls);
    }

    [Fact]
    public async Task OperationCleanupFailure_DoesNotPublishSuccessfulAttemptOrReplayIt()
    {
        var rig = new Rig("Continue", "Stop");
        rig.Motion.FailDisposal = true;
        var result = await rig.Engine.RunAsync();
        Assert.False(result.Success);
        Assert.Equal(1, rig.Axis.Commands);
        Assert.Equal(2, rig.Motion.Attempts);
        Assert.Equal(1, rig.Motion.Disposed);
        Assert.False(rig.Context.TryGetNodeOutput("motion", out _));
        Assert.Contains(rig.Engine.RunState.Faults, fault => fault.Message.Contains("清理失败"));
    }

    [Fact]
    public async Task MissingRecoveryCapability_IsDetectedBeforeOperatorPrompt()
    {
        var rig = new Rig("Continue");
        var coordinator = rig.Coordinator;
        var context = new WorkflowContext(new WorkflowServiceProvider().Add<IWorkflowOperatorService>(rig.Operator));
        var request = new WorkflowFaultRecoveryRequest("test", "motion", "轴", "故障", 0, null, 1);

        await Assert.ThrowsAsync<WorkflowRuntimeCapabilityException>(() => coordinator.RecoverAsync(request, context, default).AsTask());
        Assert.Equal(0, rig.Operator.ChoiceCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PlainCodeException_EntersRecovery_AndUnrelatedCancellationIsNotRunCancellation(bool unrelatedCancellation)
    {
        var catalog = new WorkflowNodeCatalog().Register(WorkflowNodeDescriptor.Create<ThrowNode>());
        var document = Document(new ThrowNode { Id = "throw" });
        var handler = new ThrowHandler(unrelatedCancellation);
        var coordinator = new DelegateCoordinator((_, _) => WorkflowFaultRecoveryDecision.Stop());
        var engine = new WorkflowEngine(new WorkflowCompiler(catalog).Compile(document),
            new WorkflowNodeHandlerCatalog().Register(handler),
            new WorkflowContext(new WorkflowServiceProvider().Add<IWorkflowFaultRecoveryCoordinator>(coordinator)));

        var result = await engine.RunAsync();

        Assert.Equal(E_WorkflowExecutionState.Faulted, result.State);
        Assert.Single(coordinator.Requests);
        Assert.Contains(unrelatedCancellation ? "OperationCanceledException" : "InvalidOperationException", coordinator.Requests[0].ExceptionType);
    }

    [Fact]
    public async Task ContinueWithoutOperation_IsRejectedRatherThanCallingHandlerAgain()
    {
        var catalog = new WorkflowNodeCatalog().Register(WorkflowNodeDescriptor.Create<ThrowNode>());
        var handler = new ThrowHandler(false);
        var coordinator = new DelegateCoordinator((_, index) => index == 1
            ? WorkflowFaultRecoveryDecision.Continue() : WorkflowFaultRecoveryDecision.Stop());
        var engine = new WorkflowEngine(new WorkflowCompiler(catalog).Compile(Document(new ThrowNode { Id = "throw" })),
            new WorkflowNodeHandlerCatalog().Register(handler),
            new WorkflowContext(new WorkflowServiceProvider().Add<IWorkflowFaultRecoveryCoordinator>(coordinator)));

        var result = await engine.RunAsync();

        Assert.False(result.Success);
        Assert.Equal(1, handler.Calls);
        Assert.Equal(2, coordinator.Requests.Count);
        Assert.Equal(coordinator.Requests[0].CaseId, coordinator.Requests[1].CaseId);
        Assert.Contains("没有保留的操作", coordinator.Requests[1].RecoveryFailure);
    }

    [Fact]
    public async Task LoopBodyContinue_PreservesIterationAndOperation()
    {
        var axis = new Axis { FailOnCommand = 7 };
        var motion = new MotionHandler(axis);
        var loop = new LoopNode { Id = "loop", Iterations = 8 };
        var body = new MotionNode { Id = "motion" };
        var document = Document(loop, body);
        Connect(document, "loop", "motion", WorkflowPorts.Loop);
        Connect(document, "motion", "loop");
        var catalog = new WorkflowNodeCatalog()
            .Register(WorkflowNodeDescriptor.Create<LoopNode>(ports: new[] {
                WorkflowPortDescriptor.Input(maxConnections: int.MaxValue), WorkflowPortDescriptor.Output(WorkflowPorts.Loop), WorkflowPortDescriptor.Output(WorkflowPorts.Completed) }))
            .Register(WorkflowNodeDescriptor.Create<MotionNode>(ports: Ports()));
        var coordinator = new DelegateCoordinator((_, _) => WorkflowFaultRecoveryDecision.Continue());
        var engine = new WorkflowEngine(new WorkflowCompiler(catalog).Compile(document),
            new WorkflowNodeHandlerCatalog().Register(new LoopHandler()).Register(motion),
            new WorkflowContext(new WorkflowServiceProvider().Add<IWorkflowFaultRecoveryCoordinator>(coordinator)));

        var result = await engine.RunAsync();

        Assert.True(result.Success, result.Message);
        Assert.Equal(8, motion.Created);
        Assert.Equal(9, motion.Attempts);
        Assert.Equal(8, axis.Commands);
        Assert.Equal(180, axis.Position);
        Assert.Equal(8, motion.Disposed);
        Assert.Equal(7, Assert.Single(coordinator.Requests).LoopIterations["loop"]);
    }

    [Fact]
    public async Task CyclicPlanEntryRestart_IsRejectedBeforeGuardOrReexecution()
    {
        var rig = new Rig("Restart", "Stop", cyclic: true);
        var result = await rig.Engine.RunAsync();

        Assert.False(result.Success);
        Assert.DoesNotContain(rig.Engine.RunState.RecoveryEvents, item => item.Stage == "Applied");
        Assert.Null(rig.Guard.Plan);
        Assert.Equal(1, rig.Feed.Count);
        Assert.Contains(rig.Engine.RunState.RecoveryEvents, item => item.Message?.Contains("无环串行") == true);
    }

    [Theory]
    [InlineData("Missing")]
    [InlineData("Unvisited")]
    public async Task MissingOrUnvisitedEntry_CannotMoveProgramCounter(string target)
    {
        var rig = new Rig("Restart", "Stop", targetEntry: target);
        var result = await rig.Engine.RunAsync();
        Assert.False(result.Success);
        Assert.Null(rig.Guard.Plan);
        Assert.Equal(1, rig.Axis.Commands);
        Assert.Empty(rig.Engine.RunState.InvalidatedOutputSequences);
        Assert.Contains(rig.Engine.RunState.RecoveryEvents, item => item.Stage == "Rejected" && item.Message!.Contains("实际经过"));
    }

    [Fact]
    public void DuplicateRecoveryEntry_IsRejectedAtCompilation()
    {
        var document = Document(new RecoveryEntryNodeModel { Id = "a", EntryKey = "same" },
            new RecoveryEntryNodeModel { Id = "b", EntryKey = "same" });
        Connect(document, "a", "b");
        var error = Assert.Throws<WorkflowCompilationException>(() =>
            new WorkflowCompiler(new WorkflowNodeCatalog().RegisterProcessNodes()).Compile(document));
        Assert.Contains("入口键", error.Message);
    }

    private sealed class Rig
    {
        public Rig(string choice, string? secondChoice = null, bool cyclic = false, string targetEntry = "OperationStart")
        {
            Operator = new Operator(Axis, new[] { choice, secondChoice ?? "Stop" });
            Guard = new Guard(Axis);
            var catalog = new WorkflowNodeCatalog().RegisterProcessNodes()
                .Register(WorkflowNodeDescriptor.Create<FeedNode>(ports: Ports()))
                .Register(WorkflowNodeDescriptor.Create<PrepareNode>(ports: Ports()))
                .Register(WorkflowNodeDescriptor.Create<MotionNode>(ports: Ports()));
            Motion = new MotionHandler(Axis);
            var handlers = new WorkflowNodeHandlerCatalog().RegisterProcessNodeHandlers().Register(Feed).Register(Prepare).Register(Motion);
            var block = new WarningHandlerBlockNodeModel();
            block.SubDocument.CanvasProjection.Nodes.Clear();
            block.SubDocument.CanvasProjection.Connections.Clear();
            Add(block.SubDocument, new WarningHandlerStartNodeModel { Id = "start" },
                new OperatorChoiceNodeModel { Id = "choice", OptionsText = "继续原操作=Continue;人工回初始点=Restart;停止=Stop" },
                new ContinueOperationNodeModel { Id = "continue" },
                new OperatorStepConfirmNodeModel { Id = "confirm" },
                new RestartFromEntryNodeModel { Id = "restart", EntryKey = targetEntry },
                new StopCurrentStationNodeModel { Id = "stop" });
            block.SubDocument.EntryNodeId = "start";
            Connect(block.SubDocument, "start", "choice");
            Connect(block.SubDocument, "choice", "continue", "Continue");
            Connect(block.SubDocument, "choice", "confirm", "Restart");
            Connect(block.SubDocument, "confirm", "restart");
            Connect(block.SubDocument, "choice", "stop", "Stop");
            Coordinator = new WorkflowWarningHandlerCoordinator(block, catalog, handlers);
            var services = new WorkflowServiceProvider().Add<IWorkflowOperatorService>(Operator)
                .Add<IWorkflowRecoveryEntryGuard>(Guard).Add<IWorkflowFaultRecoveryCoordinator>(Coordinator);
            Context = new WorkflowContext(services);
            var document = Document(new FeedNode { Id = "feed" },
                new RecoveryEntryNodeModel { Id = "entry", EntryKey = "OperationStart" },
                new PrepareNode { Id = "prepare" }, new MotionNode { Id = "motion" });
            Connect(document, "feed", "entry");
            Connect(document, "entry", "prepare");
            Connect(document, "prepare", "motion");
            Add(document, new RecoveryEntryNodeModel { Id = "unvisited", EntryKey = "Unvisited" });
            if (cyclic) Connect(document, "motion", "entry");
            Engine = new WorkflowEngine(new WorkflowCompiler(catalog).Compile(document), handlers, Context,
                new WorkflowExecutionOptions { MaxRecoveryAttempts = 4 });
        }
        public Axis Axis { get; } = new();
        public FeedHandler Feed { get; } = new();
        public PrepareHandler Prepare { get; } = new();
        public MotionHandler Motion { get; }
        public Guard Guard { get; }
        public Operator Operator { get; }
        public WorkflowWarningHandlerCoordinator Coordinator { get; }
        public WorkflowContext Context { get; }
        public WorkflowEngine Engine { get; }
    }

    private static WorkflowPortDescriptor[] Ports() => new[] { WorkflowPortDescriptor.Input(maxConnections: int.MaxValue), WorkflowPortDescriptor.Output() };
    private static WorkflowDocument Document(params WorkflowNodeModel[] nodes)
    {
        var document = new WorkflowDocument { Name = "RecoveryV1", EntryNodeId = nodes[0].Id };
        Add(document, nodes);
        return document;
    }
    private static void Add(WorkflowDocument document, params WorkflowNodeModel[] nodes)
    {
        foreach (var node in nodes) document.CanvasProjection.Nodes.Add(new WorkflowCanvasNode { Node = node });
    }
    private static void Connect(WorkflowDocument document, string from, string to, string port = WorkflowPorts.Success) =>
        document.CanvasProjection.Connections.Add(new WorkflowConnectionModel { FromNodeId = from, FromPort = port, ToNodeId = to });

    private sealed class Axis { public int Position = 100; public int Commands; public int FailOnCommand = 1; }
    [WorkflowNode("TestV1Feed")]
    private sealed class FeedNode : WorkflowNodeModel { public override string NodeType => "TestV1Feed"; }
    private sealed class FeedHandler : WorkflowNodeHandler<FeedNode>
    {
        public int Count;
        protected override ValueTask<NodeExecutionResult> ExecuteAsync(FeedNode node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
        { Count++; context.PublishData("recorded-feed", Count); return ValueTask.FromResult(NodeExecutionResult.Continue(output: "P007")); }
    }
    [WorkflowNode("TestV1Prepare")]
    private sealed class PrepareNode : WorkflowNodeModel { public override string NodeType => "TestV1Prepare"; }
    private sealed class PrepareHandler : WorkflowNodeHandler<PrepareNode>
    {
        public int Count;
        public bool SawStaleValue;
        public bool SawStaleOutput;
        protected override ValueTask<NodeExecutionResult> ExecuteAsync(PrepareNode node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
        {
            Count++;
            SawStaleValue |= context.TryGetVariable<int>("derived", out _);
            if (Count > 1)
            {
                try
                {
                    context.ResolveInput(WorkflowInput<int>.FromBinding(new WorkflowBindingKey(node.Id, "$")));
                    SawStaleOutput = true;
                }
                catch (WorkflowBindingException) { }
            }
            context.SetVariable("derived", Count);
            return ValueTask.FromResult(NodeExecutionResult.Continue(output: Count));
        }
    }
    [WorkflowNode("TestV1Motion")]
    private sealed class MotionNode : WorkflowNodeModel { public override string NodeType => "TestV1Motion"; }
    private sealed class MotionHandler(Axis axis) : WorkflowNodeHandler<MotionNode>, IWorkflowNodeOperationFactory
    {
        public int Created;
        public int Attempts;
        public int Disposed;
        public Action? OnCreate;
        public bool FailDisposal;
        public List<int> Targets { get; } = new();
        protected override ValueTask<NodeExecutionResult> ExecuteAsync(MotionNode node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("引擎必须走操作工厂。");
        public ValueTask<IWorkflowNodeOperation> CreateOperationAsync(Guid operationId, IWorkflowNodeModel node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
        {
            Created++;
            var target = axis.Position + (context.TryGetVariable<int>("delta", out var delta) ? delta : 10);
            Targets.Add(target);
            OnCreate?.Invoke();
            return ValueTask.FromResult<IWorkflowNodeOperation>(new Move(this, axis, target));
        }
        private sealed class Move(MotionHandler owner, Axis axis, int target) : IWorkflowNodeOperation
        {
            private bool _sent;
            public ValueTask<NodeExecutionResult> ExecuteAsync(IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
            {
                owner.Attempts++;
                if (!_sent)
                {
                    _sent = true;
                    axis.Commands++;
                    axis.Position = target;
                    if (axis.Commands == axis.FailOnCommand)
                    {
                        context.SetVariable("failed-attempt-only", true);
                        return ValueTask.FromResult(NodeExecutionResult.Fail("完成响应丢失"));
                    }
                }
                if (axis.Position != target) throw new InvalidOperationException("原目标尚未满足。");
                return ValueTask.FromResult(NodeExecutionResult.Continue(output: target));
            }
            public ValueTask DisposeAsync()
            {
                owner.Disposed++;
                if (owner.FailDisposal) throw new InvalidOperationException("操作清理失败");
                return ValueTask.CompletedTask;
            }
        }
    }
    private sealed class Guard(Axis axis) : IWorkflowRecoveryEntryGuard
    {
        public bool Allow = true;
        public WorkflowRecoveryEntryPlan? Plan;
        public ValueTask<WorkflowRecoveryEntryValidation> ValidateAsync(WorkflowRecoveryEntryPlan plan, IWorkflowFaultRecoveryContext context, CancellationToken cancellationToken)
        { Plan = plan; return ValueTask.FromResult(new WorkflowRecoveryEntryValidation(Allow && axis.Position == 0, Allow ? "需核实初始位置" : "夹具未就绪")); }
    }
    private sealed class Operator(Axis axis, IEnumerable<string> choices) : IWorkflowOperatorService
    {
        private readonly Queue<string> _choices = new(choices);
        public int ChoiceCalls;
        public int ConfirmCalls;
        public bool FailConfirmation;
        public Action? OnChoice;
        public TaskCompletionSource ChoiceEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ValueTask<string> AskChoiceAsync(string title, string message, IReadOnlyList<OperatorChoiceOption> options, CancellationToken cancellationToken)
        {
            ChoiceCalls++;
            OnChoice?.Invoke();
            ChoiceEntered.TrySetResult();
            return ValueTask.FromResult(_choices.Count > 0 ? _choices.Dequeue() : "Stop");
        }
        public ValueTask ConfirmStepAsync(string title, string message, CancellationToken cancellationToken)
        {
            ConfirmCalls++;
            if (FailConfirmation) throw new InvalidOperationException("人工处置失败");
            axis.Position = 0;
            return ValueTask.CompletedTask;
        }
    }
    private sealed class DelegateCoordinator(Func<WorkflowFaultRecoveryRequest, int, WorkflowFaultRecoveryDecision> decide) : IWorkflowFaultRecoveryCoordinator
    {
        public List<WorkflowFaultRecoveryRequest> Requests { get; } = new();
        public ValueTask<WorkflowFaultRecoveryDecision> RecoverAsync(WorkflowFaultRecoveryRequest request, IWorkflowFaultRecoveryContext context, CancellationToken cancellationToken)
        { Requests.Add(request); return ValueTask.FromResult(decide(request, Requests.Count)); }
    }
    [WorkflowNode("TestV1Throw")]
    private sealed class ThrowNode : WorkflowNodeModel { public override string NodeType => "TestV1Throw"; }
    private sealed class ThrowHandler(bool unrelatedCancellation) : WorkflowNodeHandler<ThrowNode>
    {
        public int Calls;
        protected override ValueTask<NodeExecutionResult> ExecuteAsync(ThrowNode node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
        { Calls++; throw unrelatedCancellation ? new OperationCanceledException("其他令牌") : new InvalidOperationException("代码异常"); }
    }
    [WorkflowNode("TestV1Loop")]
    private sealed class LoopNode : WorkflowNodeModel, IWorkflowLoopNode
    { public override string NodeType => "TestV1Loop"; public int Iterations { get; set; } }
    private sealed class LoopHandler : WorkflowNodeHandler<LoopNode>
    {
        protected override ValueTask<NodeExecutionResult> ExecuteAsync(LoopNode node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken) =>
            ValueTask.FromResult(NodeExecutionResult.Continue(context.ExecutionIdentity.LoopIteration <= node.Iterations ? WorkflowPorts.Loop : WorkflowPorts.Completed));
    }
}
