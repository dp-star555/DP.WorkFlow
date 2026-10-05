using System.ComponentModel;

namespace DP.WorkFlow;

/// <summary>提供与厂商板卡无关的数字 IO 访问能力。</summary>
public enum WorkflowIoPointType
{
    /// <summary>输入点。</summary>
    Input = 1,
    /// <summary>输出点。</summary>
    Output = 2
}

/// <summary>IO 写入命令，名称与旧版保持一致。</summary>
public enum WorkflowIoWriteCommand { Off = 0, On = 1, Toggle = 2, Pulse = 3, DelayOn = 4, DelayOff = 5, SetByValue = 6 }

/// <summary>标识旧版 DriveId/Index 形式的 IO 点。</summary>
public sealed record WorkflowIoAddress(string DriveId, int Index, WorkflowIoPointType PointType);

/// <summary>描述完整 IO 写入请求。</summary>
public sealed record WorkflowIoWriteRequest(WorkflowIoAddress Address, WorkflowIoWriteCommand Command, bool Value, bool WaitForSignal, int TimeoutMs, int ActionDelayMs);

/// <summary>IO 节点结构化结果。</summary>
public sealed record WorkflowIoNodeResult([property: DisplayName("驱动")] string DriveId, [property: DisplayName("索引")] int Index, [property: DisplayName("值")] object? Value, [property: DisplayName("是否成功")] bool Success, [property: DisplayName("消息")] string? Message = null);

/// <summary>IO 写入结构化结果。</summary>
public sealed record WorkflowIoWriteResult([property: DisplayName("地址")] WorkflowIoAddress Address, [property: DisplayName("命令")] WorkflowIoWriteCommand Command, [property: DisplayName("目标值")] bool TargetValue, [property: DisplayName("是否成功")] bool Success, [property: DisplayName("消息")] string? Message = null);

public interface IWorkflowIoService
{
    /// <summary>按旧版 DriveId/Index/IOType 读取点位值。</summary>
    ValueTask<WorkflowIoNodeResult> ReadAsync(WorkflowIoAddress address, CancellationToken cancellationToken);

    /// <summary>按旧版完整命令语义执行输出写入。</summary>
    ValueTask<WorkflowIoWriteResult> WriteAsync(WorkflowIoWriteRequest request, CancellationToken cancellationToken);

    /// <summary>兼容临时 PointKey 骨架的布尔读取；保真重写完成后删除。</summary>
    ValueTask<bool> ReadAsync(string pointKey, CancellationToken cancellationToken);

    /// <summary>异步写入指定稳定点位。</summary>
    ValueTask WriteAsync(string pointKey, bool value, CancellationToken cancellationToken);

    /// <summary>
    /// 等待点位达到期望状态；达到时返回 <see langword="true"/>，超时时返回
    /// <see langword="false"/>。
    /// </summary>
    ValueTask<bool> WaitAsync(
        string pointKey,
        bool expectedValue,
        TimeSpan timeout,
        CancellationToken cancellationToken);

    /// <summary>按旧版瞬时/保持模式等待单点条件。</summary>
    ValueTask<WorkflowIoNodeResult> WaitAsync(
        WorkflowIoExpectation expectation,
        WorkflowIoPassMode passMode,
        int holdMs,
        int timeoutMs,
        int pollIntervalMs,
        CancellationToken cancellationToken);

    /// <summary>立即读取并判断一组 IO 条件。</summary>
    ValueTask<IoMultiNodeResult> CheckManyAsync(
        IReadOnlyList<WorkflowIoExpectation> expectations,
        WorkflowIoMatchMode matchMode,
        CancellationToken cancellationToken);

    /// <summary>按旧版组合、保持、超时和轮询参数等待多点条件。</summary>
    ValueTask<IoMultiNodeResult> WaitManyAsync(
        IReadOnlyList<WorkflowIoExpectation> expectations,
        WorkflowIoMatchMode matchMode,
        WorkflowIoPassMode passMode,
        int holdMs,
        int timeoutMs,
        int pollIntervalMs,
        CancellationToken cancellationToken);
}
