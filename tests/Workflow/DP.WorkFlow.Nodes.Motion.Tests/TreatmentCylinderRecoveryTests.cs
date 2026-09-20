namespace DP.WorkFlow.Tests;

public sealed class TreatmentCylinderRecoveryTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public async Task CylinderBlockedDuringTreatment_ContinuesSameOperationWithoutNestingOrReissuing(int waits)
    {
        var rig = new Rig(waits);
        var result = await rig.Engine.RunAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(result.Success, result.Message);
        Assert.Equal(1, rig.Root.Created);
        Assert.Equal(2, rig.Root.Attempts);
        Assert.Equal(1, rig.Prepare.Calls);
        Assert.Equal(1, rig.Pneumatic.Commands);
        Assert.Equal(waits, rig.Pneumatic.Waits);
        Assert.Equal(waits, rig.Operator.Choices);
        Assert.All(rig.Policy.Ids, pair => Assert.Equal(pair.Original, pair.Step));
        Assert.Single(rig.Policy.OperationIds.Distinct());
        Assert.All(rig.Operator.Messages, message => { Assert.Contains("原故障", message); Assert.Contains("当前步骤", message); });
    }

    [Fact]
    public async Task UnverifiableCylinderCommand_IsNotOfferedContinue()
    {
        var rig = new Rig(1, WorkflowCylinderCommand.AllOff);
        var result = await rig.Engine.RunAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(result.Success);
        Assert.Equal(0, rig.Operator.Choices);
        Assert.Equal(1, rig.Prepare.Calls);
        Assert.Equal(0, rig.Pneumatic.Waits);
    }

    [Fact]
    public async Task OperatorDismissal_DoesNotReplayTreatment()
    {
        var rig = new Rig(1); rig.Operator.Dismiss = true;
        var result = await rig.Engine.RunAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(result.Success);
        Assert.Equal(1, rig.Prepare.Calls);
        Assert.Equal(1, rig.Pneumatic.Commands);
        Assert.Equal(0, rig.Pneumatic.Waits);
    }

    private sealed class Rig
    {
        public RootHandler Root { get; } = new();
        public PrepareHandler Prepare { get; } = new();
        public Pneumatic Pneumatic { get; }
        public Operator Operator { get; } = new();
        public Policy Policy { get; } = new();
        public WorkflowEngine Engine { get; }
        public Rig(int waits, WorkflowCylinderCommand command = WorkflowCylinderCommand.Retract)
        {
            Pneumatic = new Pneumatic(waits);
            var nodes = new WorkflowNodeCatalog().RegisterProcessNodes().RegisterMotionNodes()
                .Register(WorkflowNodeDescriptor.Create<RootNode>(ports: Ports()))
                .Register(WorkflowNodeDescriptor.Create<PrepareNode>(ports: Ports()));
            var handlers = new WorkflowNodeHandlerCatalog().RegisterProcessNodeHandlers().RegisterMotionNodeHandlers().Register(Root).Register(Prepare);
            var block = new WarningHandlerBlockNodeModel();
            block.SubDocument.CanvasProjection.Nodes.Clear(); block.SubDocument.CanvasProjection.Connections.Clear();
            block.SubDocument.EntryNodeId = "start";
            Add(block.SubDocument, new WarningHandlerStartNodeModel { Id = "start" }, new PrepareNode { Id = "prepare" },
                new CylinderControlNodeModel { Id = "cylinder", Title = "接收侧气缸退回", CylinderName = "Receiver", Command = command },
                new ContinueOperationNodeModel { Id = "continue" });
            Link(block.SubDocument, "start", "prepare"); Link(block.SubDocument, "prepare", "cylinder"); Link(block.SubDocument, "cylinder", "continue");
            var coordinator = new WorkflowWarningHandlerCoordinator(block, nodes, handlers, treatmentPolicy: Policy);
            var services = new WorkflowServiceProvider().Add<IWorkflowPneumaticService>(Pneumatic)
                .Add<IWorkflowOperatorService>(Operator).Add<IWorkflowFaultRecoveryCoordinator>(coordinator);
            var root = new WorkflowDocument { Name = "交接", EntryNodeId = "root" }; Add(root, new RootNode { Id = "root" });
            Engine = new WorkflowEngine(new WorkflowCompiler(nodes).Compile(root), handlers, new WorkflowContext(services));
        }
    }
    private static WorkflowPortDescriptor[] Ports() => new[] { WorkflowPortDescriptor.Input(), WorkflowPortDescriptor.Output() };
    private static void Add(WorkflowDocument document, params WorkflowNodeModel[] nodes)
    { foreach (var node in nodes) document.CanvasProjection.Nodes.Add(new WorkflowCanvasNode { Node = node }); }
    private static void Link(WorkflowDocument document, string from, string to) => document.CanvasProjection.Connections.Add(new WorkflowConnectionModel
        { FromNodeId = from, FromPort = WorkflowPorts.Success, ToNodeId = to });
    [WorkflowNode("TreatmentRoot")]
    private sealed class RootNode : WorkflowNodeModel { public override string NodeType => "TreatmentRoot"; }
    [WorkflowNode("TreatmentPrepare")]
    private sealed class PrepareNode : WorkflowNodeModel { public override string NodeType => "TreatmentPrepare"; }
    private sealed class PrepareHandler : WorkflowNodeHandler<PrepareNode>
    {
        public int Calls;
        protected override ValueTask<NodeExecutionResult> ExecuteAsync(PrepareNode node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
        { Calls++; return ValueTask.FromResult(NodeExecutionResult.Continue()); }
    }
    private sealed class RootHandler : WorkflowNodeHandler<RootNode>, IWorkflowNodeOperationFactory
    {
        public int Created, Attempts;
        protected override ValueTask<NodeExecutionResult> ExecuteAsync(RootNode node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<IWorkflowNodeOperation> CreateOperationAsync(Guid operationId, IWorkflowNodeModel node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
        { Created++; return ValueTask.FromResult<IWorkflowNodeOperation>(new Operation(this)); }
        private sealed class Operation(RootHandler owner) : IWorkflowNodeOperation
        {
            public ValueTask<NodeExecutionResult> ExecuteAsync(IWorkflowNodeExecutionContext context, CancellationToken cancellationToken) =>
                ValueTask.FromResult(++owner.Attempts == 1 ? NodeExecutionResult.Fail("物料交接响应丢失") : NodeExecutionResult.Continue());
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
    private sealed class Policy : IWorkflowTreatmentRecoveryPolicy
    {
        public List<(Guid Original, Guid Step)> Ids { get; } = new();
        public List<Guid?> OperationIds { get; } = new();
        public ValueTask<WorkflowFaultRecoveryDecision> RecoverStepAsync(WorkflowFaultRecoveryRequest original, WorkflowFaultRecoveryRequest step, IWorkflowFaultRecoveryContext context, CancellationToken cancellationToken)
        { Ids.Add((original.CaseId, step.CaseId)); OperationIds.Add(step.OperationId); return new WorkflowOperatorTreatmentRecoveryPolicy().RecoverStepAsync(original, step, context, cancellationToken); }
    }
    private sealed class Operator : IWorkflowOperatorService
    {
        public int Choices; public bool Dismiss;
        public List<string> Messages { get; } = new();
        public ValueTask<string> AskChoiceAsync(string title, string message, IReadOnlyList<OperatorChoiceOption> options, CancellationToken cancellationToken)
        {
            Choices++; Messages.Add(message);
            if (Dismiss) throw new WorkflowOperatorPromptDismissedException();
            return ValueTask.FromResult("Continue");
        }
        public ValueTask ConfirmStepAsync(string title, string message, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
    private sealed class Pneumatic(int waits) : IWorkflowPneumaticService
    {
        public int Commands, Waits;
        public ValueTask<PneumaticNodeResult> ControlCylinderAsync(string cylinderName, WorkflowCylinderCommand command, bool waitForTargetState, int timeoutMs, CancellationToken cancellationToken)
        { Commands++; return ValueTask.FromResult(new PneumaticNodeResult(cylinderName, false, "Blocked", "气缸卡住")); }
        public ValueTask<PneumaticNodeResult> WaitCylinderAsync(string cylinderName, WorkflowCylinderTargetState targetState, int timeoutMs, CancellationToken cancellationToken)
        { Assert.Equal(WorkflowCylinderTargetState.Retracted, targetState); return ValueTask.FromResult(new PneumaticNodeResult(cylinderName, ++Waits >= waits, "Feedback", "尚未到位")); }
        public ValueTask<PneumaticNodeResult> ControlVacuumAsync(string vacuumName, WorkflowVacuumCommand command, int blowOffDurationMs, bool waitForVacuumOk, int timeoutMs, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<PneumaticNodeResult> WaitVacuumAsync(string vacuumName, WorkflowVacuumTargetState targetState, int timeoutMs, int pollIntervalMs, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
