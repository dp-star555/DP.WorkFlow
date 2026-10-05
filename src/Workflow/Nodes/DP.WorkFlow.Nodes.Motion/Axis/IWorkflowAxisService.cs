using System.ComponentModel;

namespace DP.WorkFlow;

/// <summary>轴参数取值来源，沿用旧版语义。</summary>
public enum E_AxisValueSource
{
    /// <summary>使用节点固定值。</summary>
    Literal = 0,
    /// <summary>使用运行时绑定值。</summary>
    Binding = 1
}

/// <summary>轴等待条件，名称与旧版保持一致。</summary>
public enum AxisWaitCondition
{
    /// <summary>等待 InPos 信号关闭。</summary>
    InPosOff = 0,
    /// <summary>等待回原完成。</summary>
    Homed = 1,
    /// <summary>等待伺服开启。</summary>
    ServoOn = 2,
    /// <summary>等待报警清除。</summary>
    AlarmOff = 3,
    /// <summary>等待到达目标位置。</summary>
    PositionReached = 4,
    /// <summary>等待越过目标位置。</summary>
    PositionPassed = 5
}

/// <summary>标识一个具体轴。</summary>
public sealed record WorkflowAxisAddress(string DeviceId, int AxisId);

/// <summary>轴步骤执行结果。</summary>
public sealed record WorkflowAxisStepResult([property: DisplayName("是否成功")] bool Success, [property: DisplayName("消息")] string? Message, [property: DisplayName("耗时")] TimeSpan Elapsed, [property: DisplayName("失败轴")] IReadOnlyList<string> FailedAxes);

/// <summary>轴伺服节点结果，字段与旧版保持一致。</summary>
public sealed record AxisServoNodeResult([property: DisplayName("设备")] string DeviceId, [property: DisplayName("轴号")] int AxisId, [property: DisplayName("请求使能")] bool RequestedEnabled, [property: DisplayName("实际使能")] bool ActualEnabled, [property: DisplayName("是否成功")] bool Success, [property: DisplayName("等待完成")] bool WaitForCompleted);

/// <summary>轴停止节点结果，字段与旧版保持一致。</summary>
public sealed record AxisStopNodeResult([property: DisplayName("设备")] string DeviceId, [property: DisplayName("轴号")] int AxisId, [property: DisplayName("等待完成")] bool WaitForCompleted, [property: DisplayName("已到位")] bool InPosition, [property: DisplayName("实际速度")] double ActualSpeed, [property: DisplayName("是否成功")] bool Success);

/// <summary>轴等待节点结果。</summary>
public sealed record AxisWaitNodeResult([property: DisplayName("设备")] string DeviceId, [property: DisplayName("轴号")] int AxisId, [property: DisplayName("条件")] AxisWaitCondition Condition, [property: DisplayName("起始位置")] double StartPosition, [property: DisplayName("目标位置")] double? TargetPosition, [property: DisplayName("实际位置")] double ActualPosition, [property: DisplayName("位置容差")] double PositionTolerance, [property: DisplayName("是否成功")] bool Success);

/// <summary>描述轴等待请求。</summary>
public sealed record WorkflowAxisWaitRequest(WorkflowAxisAddress Axis, AxisWaitCondition Condition, double? TargetPosition, double PositionTolerance, int TimeoutMs, int PollIntervalMs);

/// <summary>提供与厂商控制卡无关、但保留旧版轴节点语义的服务。</summary>
public interface IWorkflowAxisService
{
    /// <summary>按旧版 AxisStep 仓库中的稳定 StepKey 执行完整多轴步骤。</summary>
    ValueTask<WorkflowAxisStepResult> ExecuteStepAsync(string stepKey, bool waitForCompleted, int overrideTimeoutMs, CancellationToken cancellationToken);

    /// <summary>设置伺服并按配置等待实际状态。</summary>
    ValueTask<AxisServoNodeResult> SetServoAsync(WorkflowAxisAddress axis, bool enabled, bool waitForCompleted, int timeoutMs, int pollIntervalMs, CancellationToken cancellationToken);

    /// <summary>停止轴并按配置等待停止状态。</summary>
    ValueTask<AxisStopNodeResult> StopAsync(WorkflowAxisAddress axis, bool waitForCompleted, int timeoutMs, int pollIntervalMs, CancellationToken cancellationToken);

    /// <summary>等待轴满足指定旧版条件。</summary>
    ValueTask<AxisWaitNodeResult> WaitAsync(WorkflowAxisWaitRequest request, CancellationToken cancellationToken);
}

/// <summary>轴节点共享的设备和轴号配置。</summary>
public abstract class WorkflowAxisNodeModel : WorkflowNodeModel, IRecoverableWorkflowNode
{
    /// <inheritdoc />
    public RecoverableInterruptOption Interrupt { get; set; } = new();
    /// <summary>获取或设置设备 ID 来源。</summary>
    public E_AxisValueSource DeviceIdSource { get; set; }
    /// <summary>获取或设置固定设备 ID。</summary>
    public string DeviceId { get; set; } = string.Empty;
    /// <summary>获取或设置绑定形式的设备 ID。</summary>
    public WorkflowInput<string> DeviceIdBinding { get; set; } = WorkflowInput<string>.FromLiteral(string.Empty);
    /// <summary>获取或设置轴号来源。</summary>
    public E_AxisValueSource AxisIdSource { get; set; }
    /// <summary>获取或设置固定轴号。</summary>
    public string AxisId { get; set; } = "0";
    /// <summary>获取或设置绑定形式的轴号。</summary>
    public WorkflowInput<int> AxisIdBinding { get; set; } = WorkflowInput<int>.FromLiteral(0);

    internal WorkflowAxisAddress ResolveAxis(IWorkflowNodeExecutionContext context) =>
        WorkflowAxisNodeResolver.Resolve(context, DeviceIdSource, DeviceId, DeviceIdBinding, AxisIdSource, AxisId, AxisIdBinding);
}

/// <summary>为轴节点解析固定值或绑定形式的轴地址。</summary>
internal static class WorkflowAxisNodeResolver
{
    public static WorkflowAxisAddress Resolve(
        IWorkflowNodeExecutionContext context,
        E_AxisValueSource deviceSource,
        string deviceId,
        WorkflowInput<string> deviceBinding,
        E_AxisValueSource axisSource,
        string axisId,
        WorkflowInput<int> axisBinding)
    {
        var resolvedDevice = deviceSource == E_AxisValueSource.Binding ? context.ResolveInput(deviceBinding) : deviceId;
        var resolvedAxis = axisSource == E_AxisValueSource.Binding
            ? context.ResolveInput(axisBinding)
            : int.TryParse(axisId, out var parsed) ? parsed : -1;
        if (string.IsNullOrWhiteSpace(resolvedDevice)) throw new InvalidOperationException("DeviceId 为空。");
        if (resolvedAxis < 0) throw new InvalidOperationException($"Invalid Axis Id: {axisId}");
        return new WorkflowAxisAddress(resolvedDevice.Trim(), resolvedAxis);
    }
}
