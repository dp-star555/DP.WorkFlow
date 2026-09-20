namespace DP.WorkFlow.Tests;

public sealed class JointRecoveryTests
{
    [Theory]
    [InlineData(2, false)]
    [InlineData(2, true)]
    [InlineData(3, true)]
    public async Task RelatedRuns_QuiesceTogether_TreatOnce_AndPrepareBeforeRelease(int count, bool restart)
    {
        using var rig = new Rig(count, restart);
        var oldStamp = rig.Group.Stamp;
        var running = rig.Group.RunAsync();
        await rig.SourceQuiescent.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(0, rig.Treatments);
        Assert.False(rig.Group.Accepts(oldStamp));
        Assert.All(rig.States.Values, state => Assert.Equal(0, state.Tails));
        rig.ReleasePeers.TrySetResult();
        var results = await running.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.All(results.Values, result => Assert.True(result.Success, result.Message));
        Assert.Equal(1, rig.Treatments);
        Assert.All(rig.States.Values, state => { Assert.Equal(1, state.Feeds); Assert.Equal(restart ? 2 : 1, state.Commands); Assert.Equal(1, state.Tails); });
        var timeline = rig.Group.Events.ToArray();
        var authorized = Array.FindIndex(timeline, e => e.Stage == "Authorized");
        Assert.Equal(count, timeline.Take(authorized).Count(e => e.Stage == "Prepared"));
        Assert.Single(timeline, e => e.Stage == "Treating");
        Assert.All(timeline, e => Assert.Equal(rig.Group.CaseId, e.CaseId));
        Assert.NotEqual(rig.Group.Id, rig.Group.CaseId);
        Assert.False(rig.Group.Accepts(oldStamp));
        Assert.False(rig.Group.Accepts(rig.Group.Stamp)); // 已结束的运行不接受晚响应。
        if (restart) Assert.NotEmpty(rig.Engines["A"].InvalidatedOutputSequences);
    }

    [Theory]
    [InlineData("guard")]
    [InlineData("treatment")]
    [InlineData("quiescence")]
    [InlineData("finalguard")]
    public async Task AnyPreparationOrTreatmentFailure_NeverReleasesAnyRole(string failure)
    {
        using var rig = new Rig(2, true) { Failure = failure };
        var running = rig.Group.RunAsync();
        await rig.SourceQuiescent.Task.WaitAsync(TimeSpan.FromSeconds(5));
        rig.ReleasePeers.TrySetResult();
        var results = await running.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.All(results.Values, result => Assert.False(result.Success));
        Assert.All(rig.States.Values, state => Assert.Equal(0, state.Tails));
        Assert.DoesNotContain(rig.Group.Events, e => e.Stage == "Authorized");
        Assert.Contains(rig.Group.Events, e => e.Stage == "Failed");
        Assert.True(rig.Treatments <= 1);
    }

    [Fact]
    public async Task ExplicitStopRun_CannotBeRevivedByJointRecovery()
    {
        using var rig = new Rig(2, true) { Failure = "hardstop" };
        var results = await rig.Group.RunAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.All(results.Values, r => Assert.False(r.Success));
        Assert.Equal(0, rig.Treatments);
        Assert.All(rig.States.Values, state => Assert.Equal(0, state.Tails));
    }

    [Fact]
    public async Task CancellationDuringInFlightExit_DoesNotStartTreatment()
    {
        using var rig = new Rig(2, true);
        using var cancel = new CancellationTokenSource();
        var running = rig.Group.RunAsync(cancel.Token);
        await rig.SourceQuiescent.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancel.Cancel();
        var results = await running.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(0, rig.Treatments);
        Assert.All(results.Values, result => Assert.False(result.Success));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IndependentHold_IsNotReleasedByJointAuthorization(bool heldBeforeQuiescence)
    {
        using var rig = new Rig(2, true);
        var oldStamp = rig.Group.Stamp;
        if (!heldBeforeQuiescence) rig.OnTreat = () => rig.Engines["A"].AddExternalHold("DoorOpen");
        var running = rig.Group.RunAsync();
        await rig.SourceQuiescent.Task.WaitAsync(TimeSpan.FromSeconds(5));
        if (heldBeforeQuiescence) rig.Engines["A"].AddExternalHold("DoorOpen");
        rig.ReleasePeers.TrySetResult();
        await rig.BTail.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(0, rig.States["A"].Tails);
        Assert.False(running.IsCompleted);
        var messages = 0;
        Assert.False(rig.Group.TryApply(oldStamp, () => messages++));
        Assert.True(rig.Group.TryApply(rig.Group.Stamp, () => messages++));
        Assert.Equal(1, messages);
        rig.Engines["A"].RemoveExternalHold("DoorOpen");
        Assert.All((await running.WaitAsync(TimeSpan.FromSeconds(5))).Values, result => Assert.True(result.Success));
    }

    [Fact]
    public async Task WaitOnlyRole_PreservesItsOperationAndOutputs()
    {
        using var rig = new Rig(3, true, true);
        var running = rig.Group.RunAsync();
        await rig.SourceQuiescent.Task.WaitAsync(TimeSpan.FromSeconds(5));
        rig.ReleasePeers.TrySetResult();
        Assert.All((await running.WaitAsync(TimeSpan.FromSeconds(5))).Values, r => Assert.True(r.Success, r.Message));
        Assert.Equal(2, rig.States["A"].Commands);
        Assert.Equal(2, rig.States["B"].Commands);
        Assert.Equal(1, rig.States["C"].Commands);
        Assert.Empty(rig.Engines["C"].InvalidatedOutputSequences);
    }

    [Fact]
    public async Task JointTreatmentBlockedStep_StaysInOriginalCase_WithoutReplayingPriorSteps()
    {
        using var rig = new Rig(2, true) { UseAuthoredTreatment = true };
        var running = rig.Group.RunAsync();
        await rig.SourceQuiescent.Task.WaitAsync(TimeSpan.FromSeconds(5));
        rig.ReleasePeers.TrySetResult();
        var results = await running.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.All(results.Values, result => Assert.True(result.Success, result.Message));
        Assert.Equal(1, rig.Treatments);
        Assert.Equal(1, rig.TreatmentPreparations);
        Assert.Equal(1, rig.TreatmentCommands);
        Assert.Equal(1, rig.StepPrompts);
        Assert.Single(rig.Group.Events, e => e.Stage == "Treating");
    }

    private sealed class Rig : IWorkflowFaultRecoveryCoordinator, IWorkflowJointRecoverySafety, IWorkflowRecoveryEntryGuard, IDisposable
    {
        public readonly Dictionary<string, State> States = new(StringComparer.Ordinal);
        public readonly Dictionary<string, WorkflowJointRecoveryParticipant> Engines = new(StringComparer.Ordinal);
        public readonly TaskCompletionSource SourceQuiescent = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource ReleasePeers = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource PeersEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource BTail = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public WorkflowJointRecoveryGroup Group { get; }
        public int Treatments, PeersRemaining, Validations;
        public string? Failure;
        public Action? OnTreat;
        public bool UseAuthoredTreatment;
        public int TreatmentPreparations, TreatmentCommands, StepPrompts;
        private readonly WorkflowWarningHandlerCoordinator _authored;
        private readonly bool _restart;
        public Rig(int count, bool restart, bool waitOnlyLast = false)
        {
            _restart = restart; PeersRemaining = count - 1;
            Group = new WorkflowJointRecoveryGroup(this, this);
            var nodes = new WorkflowNodeCatalog().RegisterProcessNodes().Register(WorkflowNodeDescriptor.Create<Step>(ports:
                new[] { WorkflowPortDescriptor.Input(), WorkflowPortDescriptor.Output() }));
            var handlers = new WorkflowNodeHandlerCatalog().RegisterProcessNodeHandlers().Register(new Handler(this));
            var treatment = new WarningHandlerBlockNodeModel();
            treatment.SubDocument.CanvasProjection.Nodes.Clear(); treatment.SubDocument.CanvasProjection.Connections.Clear();
            treatment.SubDocument.EntryNodeId = "start";
            WorkflowNodeModel[] treatmentNodes = { new WarningHandlerStartNodeModel { Id = "start" },
                new Step { Id = "prepare", Stage = "treatmentPrepare" }, new Step { Id = "repair", Stage = "treatmentAction", Title = "气缸退回" },
                new RestartFromEntryNodeModel { Id = "exit", EntryKey = "TransferReset" } };
            foreach (var node in treatmentNodes) treatment.SubDocument.CanvasProjection.Nodes.Add(new WorkflowCanvasNode { Node = node });
            for (var j = 1; j < treatmentNodes.Length; j++) treatment.SubDocument.CanvasProjection.Connections.Add(new WorkflowConnectionModel
                { FromNodeId = treatmentNodes[j - 1].Id, FromPort = WorkflowPorts.Success, ToNodeId = treatmentNodes[j].Id });
            _authored = new WorkflowWarningHandlerCoordinator(treatment, nodes, handlers);
            var entries = new Dictionary<string, string?>();
            for (var i = 0; i < count; i++)
            {
                var role = ((char)('A' + i)).ToString(); States.Add(role, new State()); entries.Add(role, waitOnlyLast && i == count - 1 ? null : "Restart" + role);
                var doc = new WorkflowDocument { Name = role, EntryNodeId = "feed" };
                WorkflowNodeModel[] models = { new Step { Id = "feed", Role = role, Stage = "feed" },
                    new RecoveryEntryNodeModel { Id = "entry", EntryKey = "Restart" + role },
                    new Step { Id = "move", Role = role, Stage = "move" }, new Step { Id = "tail", Role = role, Stage = "tail" } };
                foreach (var model in models) doc.CanvasProjection.Nodes.Add(new WorkflowCanvasNode { Node = model });
                for (var j = 1; j < models.Length; j++) doc.CanvasProjection.Connections.Add(new WorkflowConnectionModel
                    { FromNodeId = models[j - 1].Id, FromPort = WorkflowPorts.Success, ToNodeId = models[j].Id });
                var plan = new WorkflowRuntimeBinder(handlers).Bind(new WorkflowCompiler(nodes).Compile(doc));
                Engines.Add(role, Group.AddParticipant(role, plan, new WorkflowContext(new WorkflowServiceProvider().Add<IWorkflowRecoveryEntryGuard>(this).Add<IWorkflowOperatorService>(new StepOperator(this)))));
            }
            Group.AddRestartPlan("TransferReset", entries);
        }
        public async ValueTask<WorkflowFaultRecoveryDecision> RecoverAsync(WorkflowFaultRecoveryRequest request, IWorkflowFaultRecoveryContext context, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Treatments); OnTreat?.Invoke();
            if (Failure == "treatment") throw new InvalidOperationException("处置气缸故障");
            if (UseAuthoredTreatment) return await _authored.RecoverAsync(request, context, cancellationToken);
            return _restart ? WorkflowFaultRecoveryDecision.Restart("TransferReset") : WorkflowFaultRecoveryDecision.Continue();
        }
        private sealed class StepOperator(Rig rig) : IWorkflowOperatorService
        {
            public ValueTask<string> AskChoiceAsync(string title, string message, IReadOnlyList<OperatorChoiceOption> options, CancellationToken cancellationToken)
            { rig.StepPrompts++; Assert.Contains("原故障", message); return ValueTask.FromResult("Continue"); }
            public ValueTask ConfirmStepAsync(string title, string message, CancellationToken cancellationToken) => throw new NotSupportedException();
        }
        public ValueTask EnsureQuiescentAsync(string role, WorkflowFaultRecoveryRequest request, CancellationToken cancellationToken)
        {
            if (role == "B")
            {
                SourceQuiescent.TrySetResult();
                if (Failure == "quiescence") throw new InvalidOperationException("设备未退出");
            }
            return ValueTask.CompletedTask;
        }
        public ValueTask ValidateRestoreAsync(WorkflowCollaborationStamp stamp, IReadOnlyDictionary<string, WorkflowFaultRecoveryDecision> decisions, CancellationToken cancellationToken)
        {
            if (++Validations == 2 && Failure == "finalguard") throw new InvalidOperationException("准备后共同条件已改变");
            return ValueTask.CompletedTask;
        }
        public ValueTask<WorkflowRecoveryEntryValidation> ValidateAsync(WorkflowRecoveryEntryPlan plan, IWorkflowFaultRecoveryContext context, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new WorkflowRecoveryEntryValidation(!(Failure == "guard" && plan.EntryKey == "RestartB"), "现场前提被拒绝"));
        public void Dispose() => Group.Dispose();
    }
    private sealed class State { public int Feeds, Commands, Tails; }
    [WorkflowNode("JointTestStep")]
    private sealed class Step : WorkflowNodeModel
    {
        public override string NodeType => "JointTestStep";
        public string Role { get; set; } = "";
        public string Stage { get; set; } = "";
    }
    private sealed class Handler(Rig rig) : WorkflowNodeHandler<Step>, IWorkflowNodeOperationFactory
    {
        protected override ValueTask<NodeExecutionResult> ExecuteAsync(Step node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<IWorkflowNodeOperation> CreateOperationAsync(Guid operationId, IWorkflowNodeModel node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IWorkflowNodeOperation>(new Operation(rig, ((Step)node).Role, ((Step)node).Stage));
        private sealed class Operation(Rig rig, string role, string stage) : IWorkflowNodeOperation
        {
            private bool _sent;
            public async ValueTask<NodeExecutionResult> ExecuteAsync(IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
            {
                if (stage == "treatmentPrepare") { rig.TreatmentPreparations++; return NodeExecutionResult.Continue(); }
                if (stage == "treatmentAction")
                {
                    if (!_sent) { _sent = true; rig.TreatmentCommands++; return NodeExecutionResult.Fail("气缸卡住"); }
                    return NodeExecutionResult.Continue();
                }
                var state = rig.States[role];
                if (stage == "feed") state.Feeds++;
                else if (stage == "tail") { state.Tails++; if (role == "B") rig.BTail.TrySetResult(); }
                else if (!_sent)
                {
                    _sent = true; state.Commands++;
                    if (state.Commands == 1)
                    {
                        if (role == "B")
                        {
                            await rig.PeersEntered.Task.WaitAsync(cancellationToken);
                            return NodeExecutionResult.Fail("交接完成响应丢失", rig.Failure == "hardstop"
                                ? WorkflowFaultDisposition.StopRun : WorkflowFaultDisposition.HandleAtScope);
                        }
                        if (Interlocked.Decrement(ref rig.PeersRemaining) == 0) rig.PeersEntered.TrySetResult();
                        await rig.ReleasePeers.Task.WaitAsync(cancellationToken);
                    }
                }
                return NodeExecutionResult.Continue(output: stage);
            }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
