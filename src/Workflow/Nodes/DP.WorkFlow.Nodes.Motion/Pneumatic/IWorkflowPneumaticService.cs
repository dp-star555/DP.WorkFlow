namespace DP.WorkFlow;

/// <summary>气缸控制命令。</summary>
public enum WorkflowCylinderCommand { Extend = 0, Retract = 1, AllOff = 2 }
/// <summary>气缸等待目标。</summary>
public enum WorkflowCylinderTargetState { Extended = 0, Retracted = 1 }
/// <summary>真空控制命令。</summary>
public enum WorkflowVacuumCommand { VacuumOn = 0, VacuumOff = 1, VacuumOffWithBlowOff = 2, BlowOffPulse = 3 }
/// <summary>真空等待目标。</summary>
public enum WorkflowVacuumTargetState { VacuumOk = 0, VacuumNotOk = 1 }

/// <summary>气动节点执行结果。</summary>
public sealed record PneumaticNodeResult(string ActuatorName, bool Success, string State, string? Message = null);

/// <summary>提供与阀岛无关但保留旧版气缸/真空命令语义的服务。</summary>
public interface IWorkflowPneumaticService
{
    /// <summary>执行气缸命令，并可等待目标到位。</summary>
    ValueTask<PneumaticNodeResult> ControlCylinderAsync(string cylinderName, WorkflowCylinderCommand command, bool waitForTargetState, int timeoutMs, CancellationToken cancellationToken);
    /// <summary>等待气缸状态。</summary>
    ValueTask<PneumaticNodeResult> WaitCylinderAsync(string cylinderName, WorkflowCylinderTargetState targetState, int timeoutMs, CancellationToken cancellationToken);
    /// <summary>执行真空命令，包括破真空时序。</summary>
    ValueTask<PneumaticNodeResult> ControlVacuumAsync(string vacuumName, WorkflowVacuumCommand command, int blowOffDurationMs, bool waitForVacuumOk, int timeoutMs, CancellationToken cancellationToken);
    /// <summary>等待真空状态。</summary>
    ValueTask<PneumaticNodeResult> WaitVacuumAsync(string vacuumName, WorkflowVacuumTargetState targetState, int timeoutMs, int pollIntervalMs, CancellationToken cancellationToken);
}
