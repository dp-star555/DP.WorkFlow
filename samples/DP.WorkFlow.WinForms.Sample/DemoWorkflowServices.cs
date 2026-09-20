using System.Collections.Concurrent;
using DP.WorkFlow;

namespace WinFormsApp_test;

/// <summary>线程安全的示例 IO 服务。</summary>
internal sealed class DemoIoService : IWorkflowIoService
{
    private readonly ConcurrentDictionary<string, bool> _values = new(StringComparer.Ordinal);

    public ValueTask<WorkflowIoNodeResult> ReadAsync(WorkflowIoAddress address, CancellationToken cancellationToken) =>
        ValueTask.FromResult(new WorkflowIoNodeResult(address.DriveId, address.Index, _values.GetValueOrDefault($"{address.DriveId}:{address.Index}"), true));

    public ValueTask<WorkflowIoWriteResult> WriteAsync(WorkflowIoWriteRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var key = $"{request.Address.DriveId}:{request.Address.Index}";
        var target = request.Command == WorkflowIoWriteCommand.Toggle
            ? !_values.GetValueOrDefault(key)
            : request.Command == WorkflowIoWriteCommand.Off
                ? false
                : request.Command == WorkflowIoWriteCommand.On || request.Value;
        _values[key] = target;
        return ValueTask.FromResult(new WorkflowIoWriteResult(request.Address, request.Command, target, true));
    }

    public ValueTask<bool> ReadAsync(string pointKey, CancellationToken cancellationToken) =>
        ValueTask.FromResult(_values.GetValueOrDefault(pointKey));

    public ValueTask WriteAsync(string pointKey, bool value, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _values[pointKey] = value;
        return ValueTask.CompletedTask;
    }

    public ValueTask<bool> WaitAsync(string pointKey, bool expectedValue, TimeSpan timeout, CancellationToken cancellationToken) =>
        ValueTask.FromResult(_values.GetValueOrDefault(pointKey) == expectedValue);

    public ValueTask<WorkflowIoNodeResult> WaitAsync(WorkflowIoExpectation expectation, WorkflowIoPassMode passMode, int holdMs, int timeoutMs, int pollIntervalMs, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var address = expectation.ToAddress();
        var value = _values.GetValueOrDefault($"{address.DriveId}:{address.Index}");
        return ValueTask.FromResult(new WorkflowIoNodeResult(address.DriveId, address.Index, value, value == expectation.ExpectedValue));
    }

    public ValueTask<IoMultiNodeResult> CheckManyAsync(IReadOnlyList<WorkflowIoExpectation> expectations, WorkflowIoMatchMode matchMode, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Evaluate(expectations, matchMode, cancellationToken));

    public ValueTask<IoMultiNodeResult> WaitManyAsync(IReadOnlyList<WorkflowIoExpectation> expectations, WorkflowIoMatchMode matchMode, WorkflowIoPassMode passMode, int holdMs, int timeoutMs, int pollIntervalMs, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Evaluate(expectations, matchMode, cancellationToken));

    private IoMultiNodeResult Evaluate(IReadOnlyList<WorkflowIoExpectation> expectations, WorkflowIoMatchMode mode, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var items = expectations.Select(item =>
        {
            var address = item.ToAddress();
            var value = _values.GetValueOrDefault($"{address.DriveId}:{address.Index}");
            return new WorkflowIoNodeResult(address.DriveId, address.Index, value, value == item.ExpectedValue);
        }).ToArray();
        return new IoMultiNodeResult(mode == WorkflowIoMatchMode.All ? items.All(item => item.Success) : items.Any(item => item.Success), mode, items);
    }
}

internal sealed class DemoAxisService : IWorkflowAxisService
{
    public ValueTask<WorkflowAxisStepResult> ExecuteStepAsync(string stepKey, bool waitForCompleted, int overrideTimeoutMs, CancellationToken cancellationToken) =>
        ValueTask.FromResult(new WorkflowAxisStepResult(true, null, TimeSpan.Zero, Array.Empty<string>()));
    public ValueTask<AxisServoNodeResult> SetServoAsync(WorkflowAxisAddress axis, bool enabled, bool waitForCompleted, int timeoutMs, int pollIntervalMs, CancellationToken cancellationToken) =>
        ValueTask.FromResult(new AxisServoNodeResult(axis.DeviceId, axis.AxisId, enabled, enabled, true, waitForCompleted));
    public ValueTask<AxisStopNodeResult> StopAsync(WorkflowAxisAddress axis, bool waitForCompleted, int timeoutMs, int pollIntervalMs, CancellationToken cancellationToken) =>
        ValueTask.FromResult(new AxisStopNodeResult(axis.DeviceId, axis.AxisId, waitForCompleted, true, 0, true));
    public ValueTask<AxisWaitNodeResult> WaitAsync(WorkflowAxisWaitRequest request, CancellationToken cancellationToken) =>
        ValueTask.FromResult(new AxisWaitNodeResult(request.Axis.DeviceId, request.Axis.AxisId, request.Condition, 0, request.TargetPosition, request.TargetPosition ?? 0, request.PositionTolerance, true));
}

internal sealed class DemoPneumaticService : IWorkflowPneumaticService
{
    public ValueTask<PneumaticNodeResult> ControlCylinderAsync(string cylinderName, WorkflowCylinderCommand command, bool waitForTargetState, int timeoutMs, CancellationToken cancellationToken) => ValueTask.FromResult(new PneumaticNodeResult(cylinderName, true, command.ToString()));
    public ValueTask<PneumaticNodeResult> WaitCylinderAsync(string cylinderName, WorkflowCylinderTargetState targetState, int timeoutMs, CancellationToken cancellationToken) => ValueTask.FromResult(new PneumaticNodeResult(cylinderName, true, targetState.ToString()));
    public ValueTask<PneumaticNodeResult> ControlVacuumAsync(string vacuumName, WorkflowVacuumCommand command, int blowOffDurationMs, bool waitForVacuumOk, int timeoutMs, CancellationToken cancellationToken) => ValueTask.FromResult(new PneumaticNodeResult(vacuumName, true, command.ToString()));
    public ValueTask<PneumaticNodeResult> WaitVacuumAsync(string vacuumName, WorkflowVacuumTargetState targetState, int timeoutMs, int pollIntervalMs, CancellationToken cancellationToken) => ValueTask.FromResult(new PneumaticNodeResult(vacuumName, true, targetState.ToString()));
}

internal sealed class DemoCodeReaderService : IWorkflowCodeReaderService
{
    public ValueTask<bool> OpenAsync(string readerKey, CancellationToken cancellationToken) => ValueTask.FromResult(true);
    public ValueTask<bool> CloseAsync(string readerKey, CancellationToken cancellationToken) => ValueTask.FromResult(true);
    public ValueTask<bool> TriggerAsync(string readerKey, bool autoOpenWhenClosed, CancellationToken cancellationToken) => ValueTask.FromResult(true);
    public ValueTask<CodeReaderScanResult?> WaitScanAsync(CodeReaderWaitRequest request, CancellationToken cancellationToken) => ValueTask.FromResult<CodeReaderScanResult?>(new("DEMO-CODE", null, DateTime.Now, request.ReaderKey));
}

internal sealed class DemoProductFlowService : IWorkflowProductFlowService
{
    public ValueTask<ProductMoveStartNodeResult> StartMoveAsync(ProductMoveStartRequest request, CancellationToken cancellationToken) => ValueTask.FromResult(new ProductMoveStartNodeResult(Guid.NewGuid().ToString("N"), request.ProductId ?? Guid.NewGuid().ToString("N"), request.FromStationId, request.FromSlotId, request.ToStationId, request.ToSlotId, E_ProductFlowState.Active));
    public ValueTask<ProductMoveFinishNodeResult> CompleteMoveAsync(string sessionId, CancellationToken cancellationToken) => ValueTask.FromResult(new ProductMoveFinishNodeResult(sessionId, "DEMO", null, null, "DEMO", null, E_ProductFlowState.Succeeded, true, null));
    public ValueTask<ProductMoveFinishNodeResult> FailMoveAsync(string sessionId, string? failReason, CancellationToken cancellationToken) => ValueTask.FromResult(new ProductMoveFinishNodeResult(sessionId, "DEMO", null, null, "DEMO", null, E_ProductFlowState.Failed, false, failReason));
    public ValueTask<bool> CanReceiveAsync(string stationId, string? slotId, CancellationToken cancellationToken) => ValueTask.FromResult(true);
    public ValueTask<bool> CanSendAsync(string stationId, string? slotId, CancellationToken cancellationToken) => ValueTask.FromResult(true);
    public ValueTask<bool> SlotHasProductAsync(string stationId, string slotId, CancellationToken cancellationToken) => ValueTask.FromResult(true);
    public ValueTask MarkStationFinishedAsync(string stationId, string? slotId, CancellationToken cancellationToken) => ValueTask.CompletedTask;
}

internal sealed class DemoWaferRobotService : IWorkflowWaferRobotService
{
    private static readonly WaferRobotDeviceCommandResult Success = new(true, "0", "OK");
    private static readonly WaferRobotStateResult Idle = new(true, true, false, false, false, true, null, null, null, null, null, "IDLE", "Idle");
    public ValueTask<WaferRobotDeviceCommandResult> InitializeAsync(string robotKey, CancellationToken cancellationToken) => ValueTask.FromResult(Success);
    public ValueTask<WaferRobotDeviceCommandResult> HomeAsync(string robotKey, CancellationToken cancellationToken) => ValueTask.FromResult(Success);
    public ValueTask<WaferRobotDeviceCommandResult> StopAsync(string robotKey, E_StopMode stopMode, CancellationToken cancellationToken) => ValueTask.FromResult(Success);
    public ValueTask<WaferRobotDeviceCommandResult> MoveAsync(string robotKey, WaferRobotTarget target, CancellationToken cancellationToken) => ValueTask.FromResult(Success);
    public ValueTask<WaferRobotDeviceCommandResult> PickAsync(string robotKey, WaferRobotTarget target, CancellationToken cancellationToken) => ValueTask.FromResult(Success);
    public ValueTask<WaferRobotDeviceCommandResult> PlaceAsync(string robotKey, WaferRobotTarget target, CancellationToken cancellationToken) => ValueTask.FromResult(Success);
    public ValueTask<WaferRobotSnapshotResult> ReadSnapshotAsync(string robotKey, CancellationToken cancellationToken) => ValueTask.FromResult(new WaferRobotSnapshotResult(DateTime.Now, Idle, null, Array.Empty<WaferRobotAlarm>(), null, null));
    public ValueTask<WaferRobotStateResult> RefreshStateAsync(string robotKey, CancellationToken cancellationToken) => ValueTask.FromResult(Idle);
}

internal sealed class DemoProcessService : IWorkflowProcessService
{
    public ValueTask<ProductCreateNodeResult> CreateProductAsync(ProductCreateRequest request, CancellationToken cancellationToken) => ValueTask.FromResult(new ProductCreateNodeResult(request.ProductId ?? Guid.NewGuid().ToString("N"), "DEMO-LOT", request.Recipe, request.StationId, request.SlotId, "Created", 1, 1));
}

internal sealed class DemoRecoveryService : IWorkflowRecoveryService
{
    public ValueTask RegisterSafePointAsync(string safePointKey, WorkflowExecutionIdentity executionIdentity, CancellationToken cancellationToken) => ValueTask.CompletedTask;
    public ValueTask RequestStationStopAsync(string reason, WorkflowExecutionIdentity executionIdentity, CancellationToken cancellationToken) => ValueTask.CompletedTask;
    public ValueTask RequestRetryAsync(WorkflowExecutionIdentity executionIdentity, CancellationToken cancellationToken) => ValueTask.CompletedTask;
    public ValueTask ReturnToSafePointAsync(string safePointKey, WorkflowExecutionIdentity executionIdentity, CancellationToken cancellationToken) => ValueTask.CompletedTask;
}
