namespace DP.WorkFlow;

/// <summary>流程中断来源。</summary>
public enum E_WorkflowInterruptSource { Unknown = 0, Node = 1, ExternalMonitor = 2, StationManager = 3, Robot = 4, Manual = 5 }
/// <summary>流程故障严重度。</summary>
public enum E_WorkflowFaultSeverity { Recoverable = 0, Fatal = 1 }

/// <summary>最近安全点上下文。</summary>
public sealed class WorkflowSafePointContext
{
    /// <summary>安全点键。</summary>
    public string SafePointKey { get; set; } = string.Empty;
    /// <summary>节点 ID。</summary>
    public string NodeId { get; set; } = string.Empty;
    /// <summary>节点标题。</summary>
    public string Title { get; set; } = string.Empty;
    /// <summary>记录时间。</summary>
    public DateTime Time { get; set; } = DateTime.Now;
}

/// <summary>注入告警处理子流程的完整中断上下文。</summary>
public sealed class WorkflowInterruptContext
{
    /// <summary>中断来源。</summary>
    public E_WorkflowInterruptSource Source { get; set; }
    /// <summary>严重度。</summary>
    public E_WorkflowFaultSeverity Severity { get; set; }
    /// <summary>报警编码。</summary>
    public int AlarmCode { get; set; }
    /// <summary>报警名称。</summary>
    public string AlarmName { get; set; } = string.Empty;
    /// <summary>流程名称。</summary>
    public string WorkflowName { get; set; } = string.Empty;
    /// <summary>异常分类。</summary>
    public string Category { get; set; } = string.Empty;
    /// <summary>业务模块。</summary>
    public string Module { get; set; } = string.Empty;
    /// <summary>原因编码。</summary>
    public string ReasonCode { get; set; } = string.Empty;
    /// <summary>恢复指引。</summary>
    public string RecoveryGuide { get; set; } = string.Empty;
    /// <summary>建议检查项。</summary>
    public List<string> SuggestedChecks { get; set; } = new();
    /// <summary>扩展负载。</summary>
    public Dictionary<string, object> Payload { get; set; } = new(StringComparer.Ordinal);
    /// <summary>故障节点 ID。</summary>
    public string FaultNodeId { get; set; } = string.Empty;
    /// <summary>故障节点标题。</summary>
    public string FaultNodeTitle { get; set; } = string.Empty;
    /// <summary>人类可读描述。</summary>
    public string Message { get; set; } = string.Empty;
    /// <summary>工站 ID。</summary>
    public string StationId { get; set; } = string.Empty;
    /// <summary>产品 ID。</summary>
    public string ProductId { get; set; } = string.Empty;
    /// <summary>运输会话 ID。</summary>
    public string SessionId { get; set; } = string.Empty;
    /// <summary>来源槽位。</summary>
    public string FromSlotId { get; set; } = string.Empty;
    /// <summary>目标工站。</summary>
    public string ToStationId { get; set; } = string.Empty;
    /// <summary>目标槽位。</summary>
    public string ToSlotId { get; set; } = string.Empty;
    /// <summary>发生时间。</summary>
    public DateTime Time { get; set; } = DateTime.Now;
    /// <summary>最近安全点。</summary>
    public WorkflowSafePointContext? SafePoint { get; set; }
}

/// <summary>告警处理子图的恢复出口动作。</summary>
public enum E_WarningExitAction
{
    /// <summary>旧版按安全声明重新调用Handler。</summary>
    RetryCurrentNode = 0,
    /// <summary>旧任意跳转；新引擎拒绝该动作。</summary>
    JumpToNode = 1,
    /// <summary>停止本次运行。</summary>
    StopCurrentStation = 2,
    /// <summary>继续保留的原操作实例。</summary>
    ContinueOperation = 3,
    /// <summary>从命名恢复入口重执行。</summary>
    RestartFromEntry = 4
}
/// <summary>告警处理裁决结果。</summary>
public sealed class WarningResolution
{
    /// <summary>恢复动作。</summary>
    public E_WarningExitAction ExitAction { get; set; }
    /// <summary>跳转目标节点。</summary>
    public string? TargetNodeId { get; set; }
    /// <summary>命名恢复入口键。</summary>
    public string? RecoveryEntryKey { get; set; }
    /// <summary>操作员标识。</summary>
    public string? OperatorId { get; set; }
    /// <summary>操作员选择。</summary>
    public string? OperatorChoice { get; set; }
    /// <summary>裁决时间。</summary>
    public DateTime DecidedAt { get; set; } = DateTime.Now;
    /// <summary>备注。</summary>
    public string? Note { get; set; }
}

/// <summary>恢复运行时稳定变量键。</summary>
public static class WorkflowRecoveryRuntimeKeys
{
    /// <summary>当前中断上下文。</summary>
    public const string InterruptContext = "DP.WorkFlow.InterruptContext";
    /// <summary>当前不可变故障请求，包含会话身份和上次处置失败原因。</summary>
    public const string FaultRequest = "DP.WorkFlow.FaultRequest";
    /// <summary>最近安全点。</summary>
    public const string CurrentSafePoint = "$CurrentSafePoint";
    /// <summary>告警处理裁决。</summary>
    public const string WarningResolution = "DP.WorkFlow.WarningResolution";
}

/// <summary>提供工艺流程安全点登记和工站停止请求能力。</summary>
public interface IWorkflowRecoveryService
{
    /// <summary>登记当前执行身份对应的安全恢复点。</summary>
    ValueTask RegisterSafePointAsync(string safePointKey, WorkflowExecutionIdentity executionIdentity, CancellationToken cancellationToken);
    /// <summary>向宿主请求停止当前工站。</summary>
    ValueTask RequestStationStopAsync(string reason, WorkflowExecutionIdentity executionIdentity, CancellationToken cancellationToken);
    /// <summary>请求宿主重试发生故障的节点。</summary>
    ValueTask RequestRetryAsync(WorkflowExecutionIdentity executionIdentity, CancellationToken cancellationToken);
    /// <summary>请求宿主将流程恢复到指定安全点。</summary>
    ValueTask ReturnToSafePointAsync(string safePointKey, WorkflowExecutionIdentity executionIdentity, CancellationToken cancellationToken);
}

/// <summary>表示安全点节点输出。</summary>
public sealed record SafePointNodeResult(string SafePointKey, WorkflowExecutionIdentity ExecutionIdentity);
