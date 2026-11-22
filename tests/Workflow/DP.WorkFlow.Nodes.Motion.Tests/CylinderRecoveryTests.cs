namespace DP.WorkFlow.Tests;

/// <summary>第 8 类：气缸是"需核实后继续"，继续原操作时补发同一阀命令并等到位。</summary>
public sealed class CylinderRecoveryTests
{
    [Fact]
    public async Task CylinderBlocked_ContinueVerifiesAndCompletes()
    {
        var pneumatic = new Pneumatic(failCommands: 1);
        var handler = new Handler(_ => WorkflowResumeDirective.Continue());

        var result = await Run(WorkflowCylinderCommand.Retract, pneumatic, handler);

        Assert.True(result.Success, result.Message);
        var point = Assert.Single(handler.Points);
        Assert.Equal(WorkflowRecoveryCategory.VerifyThenContinue, point.Category);
        Assert.True(point.CanContinueOperation);
        Assert.Equal(4101, point.AlarmCode);
        Assert.Contains("Retract", point.Progress);
        Assert.Equal(new[] { (WorkflowCylinderCommand.Retract, true), (WorkflowCylinderCommand.Retract, true) }, pneumatic.Commands);
    }

    [Fact]
    public async Task CylinderStillBlocked_VerifyFailureReturnsToStopPoint()
    {
        var pneumatic = new Pneumatic(failCommands: 2);
        var calls = 0;
        var handler = new Handler(_ => ++calls == 1 ? WorkflowResumeDirective.Continue() : WorkflowResumeDirective.Stop("仍卡住"));

        var result = await Run(WorkflowCylinderCommand.Extend, pneumatic, handler);

        Assert.False(result.Success);
        Assert.Equal(2, handler.Points.Count);
        Assert.Equal(2, pneumatic.Commands.Count);
    }

    [Fact]
    public async Task CylinderIsNotRedoable()
    {
        var handler = new Handler(point => point.Rejection is null ? WorkflowResumeDirective.Redo() : WorkflowResumeDirective.Stop());

        var result = await Run(WorkflowCylinderCommand.Extend, new Pneumatic(failCommands: 1), handler);

        Assert.False(result.Success);
        Assert.Contains("需核实后继续", handler.Points[1].Rejection);
    }

    private static Task<WorkflowRunResult> Run(WorkflowCylinderCommand command, Pneumatic pneumatic, IWorkflowStopPointHandler handler)
    {
        var node = new CylinderControlNodeModel
        {
            Id = "Cyl", Title = "夹爪气缸", CylinderName = "Clamp", Command = command,
            Interrupt = new RecoverableInterruptOption { AlarmCode = 4101 }
        };
        var document = new WorkflowDocument { Name = "气缸", EntryNodeId = node.Id };
        document.CanvasProjection.Nodes.Add(new WorkflowCanvasNode { Node = node });
        var engine = new WorkflowEngine(new WorkflowCompiler(new WorkflowNodeCatalog().RegisterMotionNodes()).Compile(document),
            new WorkflowNodeHandlerCatalog().RegisterMotionNodeHandlers(),
            new WorkflowContext(new WorkflowServiceProvider().Add<IWorkflowPneumaticService>(pneumatic)),
            new WorkflowExecutionOptions { StopPointHandler = handler });
        return engine.RunAsync().WaitAsync(TimeSpan.FromSeconds(5));
    }

    private sealed class Handler(Func<WorkflowStopPoint, WorkflowResumeDirective> decide) : IWorkflowStopPointHandler
    {
        public List<WorkflowStopPoint> Points { get; } = new();
        public Task<WorkflowResumeDirective> OnStoppedAsync(WorkflowEngine engine, WorkflowStopPoint stopPoint, CancellationToken cancellationToken)
        {
            Points.Add(stopPoint);
            return Task.FromResult(decide(stopPoint));
        }
    }

    private sealed class Pneumatic(int failCommands) : IWorkflowPneumaticService
    {
        public List<(WorkflowCylinderCommand Command, bool Wait)> Commands { get; } = new();
        public ValueTask<PneumaticNodeResult> ControlCylinderAsync(string cylinderName, WorkflowCylinderCommand command, bool waitForTargetState, int timeoutMs, CancellationToken cancellationToken)
        {
            Commands.Add((command, waitForTargetState));
            return ValueTask.FromResult(Commands.Count <= failCommands
                ? new PneumaticNodeResult(cylinderName, false, "Blocked", "气缸卡住")
                : new PneumaticNodeResult(cylinderName, true, command.ToString()));
        }
        public ValueTask<PneumaticNodeResult> WaitCylinderAsync(string cylinderName, WorkflowCylinderTargetState targetState, int timeoutMs, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<PneumaticNodeResult> ControlVacuumAsync(string vacuumName, WorkflowVacuumCommand command, int blowOffDurationMs, bool waitForVacuumOk, int timeoutMs, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<PneumaticNodeResult> WaitVacuumAsync(string vacuumName, WorkflowVacuumTargetState targetState, int timeoutMs, int pollIntervalMs, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
