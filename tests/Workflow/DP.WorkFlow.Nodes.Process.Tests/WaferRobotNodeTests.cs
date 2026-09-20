namespace DP.WorkFlow.Tests;

public sealed class WaferRobotNodeTests
{
    [Fact]
    public async Task Move_PreservesTargetAndWaitsForIdle()
    {
        var node = new WaferRobotMoveNodeModel
        {
            Id = "Move",
            Title = "移动",
            RobotKey = "Robot1",
            StationId = "LoadPort",
            Slot = 3,
            Arm = E_EndEffectorType.Arm2,
            WaitForCompleted = true,
            WaitTimeoutMs = 1000,
            WaitPollIntervalMs = 10
        };
        var service = new FakeRobotService();

        var result = await RunAsync(node, service);

        Assert.True(result.Success);
        Assert.Equal(new WaferRobotTarget("LoadPort", 3, E_EndEffectorType.Arm2, true), service.LastTarget);
        Assert.Equal(1, service.RefreshCount);
    }

    [Fact]
    public async Task WaitIdle_UsesSuccessPortWhenRobotCanStartMotion()
    {
        var node = new WaferRobotWaitIdleNodeModel { Id = "Wait", Title = "等待", RobotKey = "Robot1", TimeoutMs = 1000, PollIntervalMs = 10 };
        var service = new FakeRobotService();

        var result = await RunAsync(node, service);

        Assert.True(result.Success);
        Assert.Equal(1, service.RefreshCount);
    }

    private static async Task<WorkflowRunResult> RunAsync(IWorkflowNodeModel node, IWorkflowWaferRobotService service)
    {
        var canvasDocument = new WorkflowDocument { Name = "机器人" };
        var canvas = canvasDocument.CanvasProjection;
        canvas.Nodes.Add(new WorkflowCanvasNode { Node = node });
        canvasDocument.EntryNodeId = node.Id;
        var definition = new WorkflowCompiler(new WorkflowNodeCatalog().RegisterProcessNodes()).Compile(canvasDocument);
        var context = new WorkflowContext(new WorkflowServiceProvider().Add<IWorkflowWaferRobotService>(service));
        return await new WorkflowEngine(definition, new WorkflowNodeHandlerCatalog().RegisterProcessNodeHandlers(), context).RunAsync();
    }

    private sealed class FakeRobotService : IWorkflowWaferRobotService
    {
        public WaferRobotTarget? LastTarget { get; private set; }
        public int RefreshCount { get; private set; }
        private static WaferRobotDeviceCommandResult Ok => new(true, "0", "OK", "REQ", "RESP");
        public ValueTask<WaferRobotDeviceCommandResult> InitializeAsync(string robotKey, CancellationToken cancellationToken) => ValueTask.FromResult(Ok);
        public ValueTask<WaferRobotDeviceCommandResult> HomeAsync(string robotKey, CancellationToken cancellationToken) => ValueTask.FromResult(Ok);
        public ValueTask<WaferRobotDeviceCommandResult> StopAsync(string robotKey, E_StopMode stopMode, CancellationToken cancellationToken) => ValueTask.FromResult(Ok);
        public ValueTask<WaferRobotDeviceCommandResult> MoveAsync(string robotKey, WaferRobotTarget target, CancellationToken cancellationToken) { LastTarget = target; return ValueTask.FromResult(Ok); }
        public ValueTask<WaferRobotDeviceCommandResult> PickAsync(string robotKey, WaferRobotTarget target, CancellationToken cancellationToken) => ValueTask.FromResult(Ok);
        public ValueTask<WaferRobotDeviceCommandResult> PlaceAsync(string robotKey, WaferRobotTarget target, CancellationToken cancellationToken) => ValueTask.FromResult(Ok);
        public ValueTask<WaferRobotSnapshotResult> ReadSnapshotAsync(string robotKey, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<WaferRobotStateResult> RefreshStateAsync(string robotKey, CancellationToken cancellationToken)
        {
            RefreshCount++;
            return ValueTask.FromResult(new WaferRobotStateResult(true, true, false, false, false, true, null, null, null, null, null, "IDLE", "Idle"));
        }
    }
}
