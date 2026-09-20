namespace DP.WorkFlow;

/// <summary>机器人停止方式。</summary>
public enum E_StopMode { Normal = 0, Emergency = 1 }
/// <summary>机器人末端执行器。</summary>
public enum E_EndEffectorType { Unknown = 0, Arm1 = 1, Arm2 = 2 }
/// <summary>机器人目标字段来源。</summary>
public enum E_WaferRobotValueSource { Literal = 0, Binding = 1 }

/// <summary>机器人业务目标。</summary>
public sealed record WaferRobotTarget(string StationId, int? Slot, E_EndEffectorType Arm, bool WaitForCompletion);
/// <summary>机器人实时状态，字段与旧版 WaferRobotState 一致。</summary>
public sealed record WaferRobotStateResult(bool IsConnected, bool IsInitialized, bool IsBusy, bool IsPaused, bool HasAlarm, bool CanStartMotion, string? CurrentPositionNo, string? CurrentStationId, int? CurrentSlot, bool? Arm1HasWafer, bool? Arm2HasWafer, string? StatusCode, string? StatusText);
/// <summary>机器人报警。</summary>
public sealed record WaferRobotAlarm(string? Code, string? Message, string? Level);
/// <summary>机器人原始快照，字段与旧版 WaferRobotSnapshot 一致。</summary>
public sealed record WaferRobotSnapshotResult(DateTime Timestamp, WaferRobotStateResult State, bool? HasWafer, IReadOnlyList<WaferRobotAlarm> Alarms, string? ErrorCode, string? ErrorMessage);
/// <summary>设备命令返回。</summary>
public sealed record WaferRobotDeviceCommandResult(bool Success, string? Code = null, string? Message = null, string? RawRequest = null, string? RawResponse = null);
/// <summary>工作流机器人命令结果，字段与旧版一致。</summary>
public sealed record WaferRobotCommandNodeResult(string RobotKey, string OperationName, string? StationId, int? Slot, E_EndEffectorType Arm, bool Success, string? Code, string? Message, string? RawRequest, string? RawResponse, bool WaitForCompleted, bool WaitCompleted, bool IsConnected, bool IsInitialized, bool IsBusy, bool IsPaused, bool HasAlarm, bool CanStartMotion, string? StatusCode, string? StatusText);
/// <summary>机器人空闲等待结果。</summary>
public sealed record WaferRobotWaitIdleNodeResult(string RobotKey, bool Success, bool IsConnected, bool IsInitialized, bool IsBusy, bool IsPaused, bool HasAlarm, bool CanStartMotion, string? StatusCode, string? StatusText);

/// <summary>提供强类型晶圆机器人设备能力。</summary>
public interface IWorkflowWaferRobotService
{
    /// <summary>初始化机器人。</summary>
    ValueTask<WaferRobotDeviceCommandResult> InitializeAsync(string robotKey, CancellationToken cancellationToken);
    /// <summary>机器人回原。</summary>
    ValueTask<WaferRobotDeviceCommandResult> HomeAsync(string robotKey, CancellationToken cancellationToken);
    /// <summary>停止机器人。</summary>
    ValueTask<WaferRobotDeviceCommandResult> StopAsync(string robotKey, E_StopMode stopMode, CancellationToken cancellationToken);
    /// <summary>移动到目标位。</summary>
    ValueTask<WaferRobotDeviceCommandResult> MoveAsync(string robotKey, WaferRobotTarget target, CancellationToken cancellationToken);
    /// <summary>从目标位取片。</summary>
    ValueTask<WaferRobotDeviceCommandResult> PickAsync(string robotKey, WaferRobotTarget target, CancellationToken cancellationToken);
    /// <summary>向目标位放片。</summary>
    ValueTask<WaferRobotDeviceCommandResult> PlaceAsync(string robotKey, WaferRobotTarget target, CancellationToken cancellationToken);
    /// <summary>读取机器人快照。</summary>
    ValueTask<WaferRobotSnapshotResult> ReadSnapshotAsync(string robotKey, CancellationToken cancellationToken);
    /// <summary>刷新机器人状态。</summary>
    ValueTask<WaferRobotStateResult> RefreshStateAsync(string robotKey, CancellationToken cancellationToken);
}

/// <summary>机器人命令节点公共配置。</summary>
public abstract class WaferRobotCommandNodeModel : WorkflowNodeModel, IRecoverableWorkflowNode
{
    /// <inheritdoc />
    [WorkflowProperty("异常处理", "机器人命令失败时使用的报警编码和可恢复中断策略。", Category = "异常处理")]
    public RecoverableInterruptOption Interrupt { get; set; } = new();
    /// <summary>机器人注册键。</summary>
    [WorkflowProperty("机器人键", "宿主机器人服务中用于定位目标机器人的稳定键。", Category = "机器人")]
    public string RobotKey { get; set; } = string.Empty;
    /// <summary>结果变量键。</summary>
    [WorkflowProperty("结果变量键", "机器人命令结果写入流程变量时使用的键。", Category = "输出结果")]
    public string ResultVarKey { get; set; } = "WaferRobotCommandResult";
    /// <summary>是否等待命令实际完成。</summary>
    [WorkflowProperty("等待完成", "是否等待机器人动作实际完成后再继续流程。", Category = "等待与超时")]
    public bool WaitForCompleted { get; set; }
    /// <summary>等待超时毫秒数。</summary>
    [WorkflowProperty("等待超时时间", "等待机器人动作完成所允许的最长时间。", Category = "等待与超时", Unit = "ms")]
    public int WaitTimeoutMs { get; set; } = 120000;
    /// <summary>等待轮询间隔毫秒数。</summary>
    [WorkflowProperty("等待轮询间隔", "查询机器人动作完成状态的时间间隔。", Category = "等待与超时", Unit = "ms")]
    public int WaitPollIntervalMs { get; set; } = 100;
}

/// <summary>机器人目标命令公共配置。</summary>
public abstract class WaferRobotTargetCommandNodeModel : WaferRobotCommandNodeModel
{
    /// <summary>站位来源。</summary>
    [WorkflowProperty("站位来源", "指定目标站位使用固定值还是流程绑定值。", Category = "机器人目标")]
    public E_WaferRobotValueSource StationIdSource { get; set; }
    /// <summary>固定站位。</summary>
    [WorkflowProperty("目标站位", "机器人动作使用的固定目标站位标识。", Category = "机器人目标")]
    public string StationId { get; set; } = string.Empty;
    /// <summary>绑定站位。</summary>
    [WorkflowProperty("站位绑定", "从流程数据读取目标站位标识。", Category = "数据来源")]
    public WorkflowInput<string> StationIdBinding { get; set; } = WorkflowInput<string>.FromLiteral(string.Empty);
    /// <summary>槽位来源。</summary>
    [WorkflowProperty("槽位来源", "指定目标槽位使用固定值还是流程绑定值。", Category = "机器人目标")]
    public E_WaferRobotValueSource SlotSource { get; set; }
    /// <summary>固定槽位；小于等于零表示未指定。</summary>
    [WorkflowProperty("目标槽位", "机器人动作使用的固定槽位编号；小于等于零表示未指定。", Category = "机器人目标")]
    public int Slot { get; set; }
    /// <summary>绑定槽位。</summary>
    [WorkflowProperty("槽位绑定", "从流程数据读取目标槽位编号。", Category = "数据来源")]
    public WorkflowInput<int> SlotBinding { get; set; } = WorkflowInput<int>.FromLiteral(0);
    /// <summary>手臂来源。</summary>
    [WorkflowProperty("手臂来源", "指定末端执行器使用固定值还是流程绑定值。", Category = "机器人目标")]
    public E_WaferRobotValueSource ArmSource { get; set; }
    /// <summary>固定手臂。</summary>
    [WorkflowProperty("目标手臂", "机器人动作使用的固定末端执行器。", Category = "机器人目标")]
    public E_EndEffectorType Arm { get; set; }
    /// <summary>绑定手臂。</summary>
    [WorkflowProperty("手臂绑定", "从流程数据读取需要使用的末端执行器。", Category = "数据来源")]
    public WorkflowInput<E_EndEffectorType> ArmBinding { get; set; } = WorkflowInput<E_EndEffectorType>.FromLiteral(E_EndEffectorType.Unknown);
}

internal static class WaferRobotNodeRuntime
{
    internal static IWorkflowWaferRobotService GetService(IWorkflowNodeExecutionContext context) =>
        context.GetRequiredCapability<IWorkflowWaferRobotService>();
    internal static string RobotKey(WaferRobotCommandNodeModel node)
    {
        if (string.IsNullOrWhiteSpace(node.RobotKey)) throw new InvalidOperationException("RobotKey 为空。");
        return node.RobotKey.Trim();
    }
    internal static WaferRobotTarget ResolveTarget(WaferRobotTargetCommandNodeModel node, IWorkflowNodeExecutionContext context)
    {
        var station = node.StationIdSource == E_WaferRobotValueSource.Binding ? context.ResolveInput(node.StationIdBinding) : node.StationId;
        station = station?.Trim();
        if (string.IsNullOrWhiteSpace(station)) throw new InvalidOperationException("StationId 为空。");
        var slot = node.SlotSource == E_WaferRobotValueSource.Binding ? context.ResolveInput(node.SlotBinding) : node.Slot;
        var arm = node.ArmSource == E_WaferRobotValueSource.Binding ? context.ResolveInput(node.ArmBinding) : node.Arm;
        return new WaferRobotTarget(station, slot > 0 ? slot : null, arm, node.WaitForCompleted);
    }
    internal static async ValueTask<NodeExecutionResult> ExecuteCommandAsync(WaferRobotCommandNodeModel node, string operation, WaferRobotTarget? target, IWorkflowNodeExecutionContext context, Func<IWorkflowWaferRobotService, string, CancellationToken, ValueTask<WaferRobotDeviceCommandResult>> execute, bool supportsWait, CancellationToken cancellationToken)
    {
        var key = RobotKey(node);
        var service = GetService(context);
        var command = await execute(service, key, cancellationToken).ConfigureAwait(false);
        WaferRobotStateResult? state = null;
        var waitCompleted = false;
        if (command.Success && supportsWait && node.WaitForCompleted)
        {
            var started = System.Diagnostics.Stopwatch.StartNew();
            while (node.WaitTimeoutMs <= 0 || started.ElapsedMilliseconds < node.WaitTimeoutMs)
            {
                state = await service.RefreshStateAsync(key, cancellationToken).ConfigureAwait(false);
                if (state.CanStartMotion) { waitCompleted = true; break; }
                await Task.Delay(Math.Max(10, node.WaitPollIntervalMs), cancellationToken).ConfigureAwait(false);
            }
        }
        var success = command.Success && (!supportsWait || !node.WaitForCompleted || waitCompleted);
        var output = new WaferRobotCommandNodeResult(key, operation, target?.StationId, target?.Slot, target?.Arm ?? E_EndEffectorType.Unknown, success, command.Code, success ? command.Message : command.Message ?? operation + " wait completed timeout.", command.RawRequest, command.RawResponse, supportsWait && node.WaitForCompleted, waitCompleted, state?.IsConnected ?? false, state?.IsInitialized ?? false, state?.IsBusy ?? false, state?.IsPaused ?? false, state?.HasAlarm ?? false, state?.CanStartMotion ?? false, state?.StatusCode, state?.StatusText);
        if (!string.IsNullOrWhiteSpace(node.ResultVarKey)) context.SetVariable(node.ResultVarKey.Trim(), output);
        if (!success) return WorkflowRecoverableNodeFailure.Create(node, output.Message ?? operation + " failed.");
        return NodeExecutionResult.Continue(output: output);
    }
}
