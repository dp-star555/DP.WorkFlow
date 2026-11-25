namespace DP.WorkFlow.Tests;

public sealed class AxisActionNodeTests
{
    [Fact]
    public async Task AxisAction_UsesStepKeyAndPreservesWaitAndTimeoutSemantics()
    {
        var node = new AxisActionNodeModel
        {
            Id = "Move",
            Title = "移动",
            StepKey = "LoadPosition",
            WaitForCompleted = true,
            OverrideTimeoutMs = 12000
        };
        var canvasDocument = new WorkflowDocument { Name = "轴" };
        var canvas = canvasDocument;
        canvas.Nodes.Add(new WorkflowCanvasNode { Node = node });
        var catalog = new WorkflowNodeCatalog().RegisterMotionNodes();
        canvasDocument.EntryNodeId = node.Id;
        var definition = new WorkflowCompiler(catalog).Compile(canvasDocument);
        var axis = new FakeAxisService();
        var services = new WorkflowServiceProvider().Add<IWorkflowAxisService>(axis);

        var result = await new WorkflowEngine(
            definition,
            new WorkflowNodeHandlerCatalog().RegisterMotionNodeHandlers(),
            new WorkflowContext(services)).RunAsync();

        Assert.True(result.Success);
        Assert.Equal("LoadPosition", axis.StepKey);
        Assert.True(axis.WaitForCompleted);
        Assert.Equal(12000, axis.OverrideTimeoutMs);
    }

    [Fact]
    public async Task RecoverableAxisFailure_StopsAtStopPointAndRedoesNode()
    {
        var node = new AxisServoNodeModel { Id = "Servo", Title = "伺服", DeviceId = "D1", AxisId = "0", Interrupt = new RecoverableInterruptOption { AlarmCode = 1001 } };
        var canvasDocument = new WorkflowDocument { Name = "可恢复轴" };
        var canvas = canvasDocument;
        canvas.Nodes.Add(new WorkflowCanvasNode { Node = node });
        var axis = new RetryAxisService();
        var coordinator = new ScriptedStopHandler(_ => WorkflowResumeDirective.Redo());
        var services = new WorkflowServiceProvider().Add<IWorkflowAxisService>(axis);
        canvasDocument.EntryNodeId = node.Id;
        var definition = new WorkflowCompiler(new WorkflowNodeCatalog().RegisterMotionNodes()).Compile(canvasDocument);

        var result = await new WorkflowEngine(definition, new WorkflowNodeHandlerCatalog().RegisterMotionNodeHandlers(), new WorkflowContext(services),
            new WorkflowExecutionOptions { StopPointHandler = coordinator }).RunAsync();

        Assert.True(result.Success, result.Message);
        Assert.Equal(2, axis.ServoCalls);
        Assert.Equal(1, coordinator.Calls);
        Assert.Equal(1001, coordinator.Points[0].AlarmCode);
        Assert.Equal(WorkflowRecoveryCategory.Redoable, coordinator.Points[0].Category);
    }

    [Fact]
    public async Task Servo_IsRedoableButHasNoVerifyAndComplete()
    {
        var node = new AxisServoNodeModel { Id = "Servo", DeviceId = "D1", AxisId = "0", Enabled = true };
        var document = new WorkflowDocument { EntryNodeId = node.Id };
        document.Nodes.Add(new WorkflowCanvasNode { Node = node });
        var axis = new RetryAxisService();
        var coordinator = new ScriptedStopHandler(point => point.Rejection is null ? WorkflowResumeDirective.Continue() : WorkflowResumeDirective.Redo());
        var context = new WorkflowContext(new WorkflowServiceProvider().Add<IWorkflowAxisService>(axis));
        var engine = new WorkflowEngine(new WorkflowCompiler(new WorkflowNodeCatalog().RegisterMotionNodes()).Compile(document),
            new WorkflowNodeHandlerCatalog().RegisterMotionNodeHandlers(), context, new WorkflowExecutionOptions { StopPointHandler = coordinator });
        var result = await engine.RunAsync();
        Assert.True(result.Success, result.Message);
        Assert.Equal(2, axis.ServoCalls);
        Assert.Equal(2, coordinator.Calls);
        Assert.False(coordinator.Points[0].CanContinueOperation);
        Assert.Contains("没有实现核实并补完", coordinator.Points[1].Rejection);
    }

    [Fact]
    public async Task RecoverableNodeConfigurationError_StopsButCannotBlindlyRedo()
    {
        var node = new AxisActionNodeModel
        {
            Id = "InvalidAction",
            Title = "无效轴动作",
            StepKey = string.Empty,
            Interrupt = new RecoverableInterruptOption { AlarmCode = 1002 }
        };
        var canvasDocument = new WorkflowDocument { Name = "配置错误" };
        var canvas = canvasDocument;
        canvas.Nodes.Add(new WorkflowCanvasNode { Node = node });
        var coordinator = new ScriptedStopHandler(point => point.Rejection is null ? WorkflowResumeDirective.Redo() : WorkflowResumeDirective.Stop("停止"));
        var services = new WorkflowServiceProvider().Add<IWorkflowAxisService>(new FakeAxisService());
        canvasDocument.EntryNodeId = node.Id;
        var definition = new WorkflowCompiler(new WorkflowNodeCatalog().RegisterMotionNodes()).Compile(canvasDocument);

        var result = await new WorkflowEngine(definition, new WorkflowNodeHandlerCatalog().RegisterMotionNodeHandlers(), new WorkflowContext(services),
            new WorkflowExecutionOptions { StopPointHandler = coordinator }).RunAsync();

        Assert.False(result.Success);
        Assert.Equal(2, coordinator.Calls);
        Assert.Contains("不能直接重做", coordinator.Points[1].Rejection, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AxisWaitTimeout_UsesTimeoutPortOrRecoverableFailureAccordingToConfiguration()
    {
        var node = new AxisWaitNodeModel { Id = "Wait", Title = "等待", DeviceId = "D1", AxisId = "0", TimeoutAsFalseBranch = true };
        var canvasDocument = new WorkflowDocument { Name = "等待超时" };
        var canvas = canvasDocument;
        canvas.Nodes.Add(new WorkflowCanvasNode { Node = node });
        var catalog = new WorkflowNodeCatalog().RegisterMotionNodes();
        canvasDocument.EntryNodeId = node.Id;
        var definition = new WorkflowCompiler(catalog).Compile(canvasDocument);
        var coordinator = new ScriptedStopHandler(_ => WorkflowResumeDirective.Stop("stop"));
        var services = new WorkflowServiceProvider().Add<IWorkflowAxisService>(new TimeoutAxisService());
        var options = new WorkflowExecutionOptions { StopPointHandler = coordinator };

        var timeoutBranch = await new WorkflowEngine(definition, new WorkflowNodeHandlerCatalog().RegisterMotionNodeHandlers(), new WorkflowContext(services), options).RunAsync();
        Assert.True(timeoutBranch.Success, timeoutBranch.Message);
        Assert.Equal(0, coordinator.Calls);

        node.TimeoutAsFalseBranch = false;
        node.Interrupt = new RecoverableInterruptOption { AlarmCode = 1201 };
        canvasDocument.EntryNodeId = node.Id;
        definition = new WorkflowCompiler(catalog).Compile(canvasDocument);
        var recoverable = await new WorkflowEngine(definition, new WorkflowNodeHandlerCatalog().RegisterMotionNodeHandlers(), new WorkflowContext(services), options).RunAsync();
        Assert.False(recoverable.Success);
        Assert.Equal(1, coordinator.Calls);
    }

    [Fact]
    public async Task CancelDuringAxisServiceCall_ProducesCanceledRun()
    {
        var node = new AxisServoNodeModel { Id = "Servo", Title = "伺服", DeviceId = "D1", AxisId = "0" };
        var canvasDocument = new WorkflowDocument { Name = "取消设备调用" };
        var canvas = canvasDocument;
        canvas.Nodes.Add(new WorkflowCanvasNode { Node = node });
        var service = new BlockingAxisService();
        canvasDocument.EntryNodeId = node.Id;
        var definition = new WorkflowCompiler(new WorkflowNodeCatalog().RegisterMotionNodes()).Compile(canvasDocument);
        var engine = new WorkflowEngine(definition, new WorkflowNodeHandlerCatalog().RegisterMotionNodeHandlers(), new WorkflowContext(new WorkflowServiceProvider().Add<IWorkflowAxisService>(service)));
        using var cts = new CancellationTokenSource();
        var task = engine.RunAsync(cts.Token);
        await service.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cts.Cancel();

        var result = await task;
        Assert.Equal(E_WorkflowExecutionState.Canceled, result.State);
    }

    private sealed class ScriptedStopHandler(Func<WorkflowStopPoint, WorkflowResumeDirective> decide) : IWorkflowStopPointHandler
    {
        public List<WorkflowStopPoint> Points { get; } = new();
        public int Calls => Points.Count;
        public Task<WorkflowResumeDirective> OnStoppedAsync(WorkflowEngine engine, WorkflowStopPoint stopPoint, CancellationToken cancellationToken)
        {
            Points.Add(stopPoint);
            return Task.FromResult(decide(stopPoint));
        }
    }

    private sealed class RetryAxisService : IWorkflowAxisService
    {
        public int ServoCalls { get; private set; }
        public ValueTask<WorkflowAxisStepResult> ExecuteStepAsync(string stepKey, bool waitForCompleted, int overrideTimeoutMs, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<AxisServoNodeResult> SetServoAsync(WorkflowAxisAddress axis, bool enabled, bool waitForCompleted, int timeoutMs, int pollIntervalMs, CancellationToken cancellationToken) { ServoCalls++; return ValueTask.FromResult(new AxisServoNodeResult(axis.DeviceId, axis.AxisId, enabled, enabled, ServoCalls > 1, waitForCompleted)); }
        public ValueTask<AxisStopNodeResult> StopAsync(WorkflowAxisAddress axis, bool waitForCompleted, int timeoutMs, int pollIntervalMs, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<AxisWaitNodeResult> WaitAsync(WorkflowAxisWaitRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
    }


    private class FakeAxisService : IWorkflowAxisService
    {
        public string? StepKey { get; private set; }
        public bool WaitForCompleted { get; private set; }
        public int OverrideTimeoutMs { get; private set; }
        public virtual ValueTask<WorkflowAxisStepResult> ExecuteStepAsync(string stepKey, bool waitForCompleted, int overrideTimeoutMs, CancellationToken cancellationToken)
        {
            StepKey = stepKey;
            WaitForCompleted = waitForCompleted;
            OverrideTimeoutMs = overrideTimeoutMs;
            return ValueTask.FromResult(new WorkflowAxisStepResult(true, null, TimeSpan.Zero, Array.Empty<string>()));
        }
        public virtual ValueTask<AxisServoNodeResult> SetServoAsync(WorkflowAxisAddress axis, bool enabled, bool waitForCompleted, int timeoutMs, int pollIntervalMs, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new AxisServoNodeResult(axis.DeviceId, axis.AxisId, enabled, enabled, true, waitForCompleted));
        public virtual ValueTask<AxisStopNodeResult> StopAsync(WorkflowAxisAddress axis, bool waitForCompleted, int timeoutMs, int pollIntervalMs, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new AxisStopNodeResult(axis.DeviceId, axis.AxisId, waitForCompleted, true, 0, true));
        public virtual ValueTask<AxisWaitNodeResult> WaitAsync(WorkflowAxisWaitRequest request, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new AxisWaitNodeResult(request.Axis.DeviceId, request.Axis.AxisId, request.Condition, 0, request.TargetPosition, 0, request.PositionTolerance, true));
    }

    private sealed class TimeoutAxisService : FakeAxisService
    {
        public override ValueTask<AxisWaitNodeResult> WaitAsync(WorkflowAxisWaitRequest request, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new AxisWaitNodeResult(request.Axis.DeviceId, request.Axis.AxisId, request.Condition, 0, request.TargetPosition, 0, request.PositionTolerance, false));
    }

    private sealed class BlockingAxisService : FakeAxisService
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<AxisServoNodeResult> SetServoAsync(WorkflowAxisAddress axis, bool enabled, bool waitForCompleted, int timeoutMs, int pollIntervalMs, CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new AxisServoNodeResult(axis.DeviceId, axis.AxisId, enabled, enabled, true, waitForCompleted);
        }
    }
}
