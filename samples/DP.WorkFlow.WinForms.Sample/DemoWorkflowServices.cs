using DP.WorkFlow;

namespace WinFormsApp_test;

internal sealed class DemoProductFlowService : IWorkflowProductFlowService
{
    public ValueTask<ProductMoveStartNodeResult> StartMoveAsync(ProductMoveStartRequest request, CancellationToken cancellationToken) => Polyfill.FromResult(new ProductMoveStartNodeResult(Guid.NewGuid().ToString("N"), request.ProductId ?? Guid.NewGuid().ToString("N"), request.FromStationId, request.FromSlotId, request.ToStationId, request.ToSlotId, E_ProductFlowState.Active));
    public ValueTask<ProductMoveFinishNodeResult> CompleteMoveAsync(string sessionId, CancellationToken cancellationToken) => Polyfill.FromResult(new ProductMoveFinishNodeResult(sessionId, "DEMO", null, null, "DEMO", null, E_ProductFlowState.Succeeded, true, null));
    public ValueTask<ProductMoveFinishNodeResult> FailMoveAsync(string sessionId, string? failReason, CancellationToken cancellationToken) => Polyfill.FromResult(new ProductMoveFinishNodeResult(sessionId, "DEMO", null, null, "DEMO", null, E_ProductFlowState.Failed, false, failReason));
    public ValueTask<bool> CanReceiveAsync(string stationId, string? slotId, CancellationToken cancellationToken) => Polyfill.FromResult(true);
    public ValueTask<bool> CanSendAsync(string stationId, string? slotId, CancellationToken cancellationToken) => Polyfill.FromResult(true);
    public ValueTask<bool> SlotHasProductAsync(string stationId, string slotId, CancellationToken cancellationToken) => Polyfill.FromResult(true);
    public ValueTask MarkStationFinishedAsync(string stationId, string? slotId, CancellationToken cancellationToken) => default(ValueTask);
}

internal sealed class DemoWaferRobotService : IWorkflowWaferRobotService
{
    private static readonly WaferRobotDeviceCommandResult Success = new(true, "0", "OK");
    private static readonly WaferRobotStateResult Idle = new(true, true, false, false, false, true, null, null, null, null, null, "IDLE", "Idle");
    public ValueTask<WaferRobotDeviceCommandResult> InitializeAsync(string robotKey, CancellationToken cancellationToken) => Polyfill.FromResult(Success);
    public ValueTask<WaferRobotDeviceCommandResult> HomeAsync(string robotKey, CancellationToken cancellationToken) => Polyfill.FromResult(Success);
    public ValueTask<WaferRobotDeviceCommandResult> StopAsync(string robotKey, E_StopMode stopMode, CancellationToken cancellationToken) => Polyfill.FromResult(Success);
    public ValueTask<WaferRobotDeviceCommandResult> MoveAsync(string robotKey, WaferRobotTarget target, CancellationToken cancellationToken) => Polyfill.FromResult(Success);
    public ValueTask<WaferRobotDeviceCommandResult> PickAsync(string robotKey, WaferRobotTarget target, CancellationToken cancellationToken) => Polyfill.FromResult(Success);
    public ValueTask<WaferRobotDeviceCommandResult> PlaceAsync(string robotKey, WaferRobotTarget target, CancellationToken cancellationToken) => Polyfill.FromResult(Success);
    public ValueTask<WaferRobotSnapshotResult> ReadSnapshotAsync(string robotKey, CancellationToken cancellationToken) => Polyfill.FromResult(new WaferRobotSnapshotResult(DateTime.Now, Idle, null, Array.Empty<WaferRobotAlarm>(), null, null));
    public ValueTask<WaferRobotStateResult> RefreshStateAsync(string robotKey, CancellationToken cancellationToken) => Polyfill.FromResult(Idle);
}

internal sealed class DemoProcessService : IWorkflowProcessService
{
    public ValueTask<ProductCreateNodeResult> CreateProductAsync(ProductCreateRequest request, CancellationToken cancellationToken) => Polyfill.FromResult(new ProductCreateNodeResult(request.ProductId ?? Guid.NewGuid().ToString("N"), "DEMO-LOT", request.Recipe, request.StationId, request.SlotId, "Created", 1, 1));
}
