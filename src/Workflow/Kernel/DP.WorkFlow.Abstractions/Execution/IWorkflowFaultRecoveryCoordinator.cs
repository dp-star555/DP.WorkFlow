namespace DP.WorkFlow;

/// <summary>故障恢复动作。</summary>
public enum WorkflowFaultRecoveryAction
{
    /// <summary>不恢复并保持流程故障。</summary>
    Stop = 0,
    /// <summary>重新执行发生故障的节点。</summary>
    RetryFaultedNode = 1,
    /// <summary>跳转到指定节点继续执行。</summary>
    JumpToNode = 2,
    /// <summary>继续原操作实例；要求Handler实现操作工厂契约。</summary>
    ContinueOperation = 3,
    /// <summary>验证并从命名入口重执行；不是任意节点跳转。</summary>
    RestartFromEntry = 4
}

/// <summary>描述一次节点故障恢复请求。</summary>
/// <param name="WorkflowName">发生故障的编译后工作流名称。</param>
/// <param name="FaultNodeId">发生故障的节点 ID。</param>
/// <param name="FaultNodeTitle">故障节点面向用户的标题。</param>
/// <param name="Message">节点返回或抛出的故障说明。</param>
/// <param name="AlarmCode">节点配置的恢复报警编码；零表示未指定。</param>
/// <param name="ExecutionIdentity">故障节点的运行、Token、Scope 和执行次数身份。</param>
/// <param name="Attempt">本次根运行中已经发起的恢复次数，从 1 开始。</param>
public sealed record WorkflowFaultRecoveryRequest(
    string WorkflowName,
    string FaultNodeId,
    string FaultNodeTitle,
    string Message,
    int AlarmCode,
    WorkflowExecutionIdentity? ExecutionIdentity,
    int Attempt)
{
    /// <summary>本次故障处理会话身份；一次处置失败重问时保持不变。</summary>
    public Guid CaseId { get; init; } = Guid.NewGuid();
    /// <summary>原操作身份；创建操作之前失败时为空。</summary>
    public Guid? OperationId { get; init; }
    /// <summary>操作实现是否允许继续；最终仍由引擎验证。</summary>
    public bool CanContinueOperation { get; init; }
    /// <summary>是否为协作阻断在节点边界产生的中断；不表示该节点发生了设备故障。</summary>
    public bool IsBoundaryInterruption { get; init; }
    /// <summary>当前Token持有的循环帧与迭代号，用于处理流程识别当前轮次。</summary>
    public IReadOnlyDictionary<string, int> LoopIterations { get; init; } = new System.Collections.ObjectModel.ReadOnlyDictionary<string, int>(new Dictionary<string, int>());
    /// <summary>原始异常类型；显式失败结果可以为空。</summary>
    public string? ExceptionType { get; init; }
    /// <summary>最近一次恢复请求被拒绝或处理失败的原因。</summary>
    public string? RecoveryFailure { get; init; }
    /// <summary>是否发生于输出提交或操作清理阶段；V1禁止自动重执行。</summary>
    public bool CommitStarted { get; init; }
}

/// <summary>表示宿主或告警子流程给出的恢复裁决。</summary>
/// <param name="Action">停止、重试故障节点或跳转节点的动作。</param>
/// <param name="TargetNodeId">跳转动作的目标节点 ID；其他动作应为空。</param>
/// <param name="Message">可选裁决说明，停止时可作为最终运行消息。</param>
public sealed record WorkflowFaultRecoveryDecision(WorkflowFaultRecoveryAction Action, string? TargetNodeId = null, string? Message = null)
{
    /// <summary>创建停止当前工作流的裁决。</summary>
    /// <param name="message">可选停止原因。</param>
    /// <returns>动作设置为 <see cref="WorkflowFaultRecoveryAction.Stop"/> 的裁决。</returns>
    public static WorkflowFaultRecoveryDecision Stop(string? message = null) => new(WorkflowFaultRecoveryAction.Stop, null, message);

    /// <summary>创建重新执行故障节点的裁决。</summary>
    /// <param name="message">可选重试说明。</param>
    /// <returns>动作设置为 <see cref="WorkflowFaultRecoveryAction.RetryFaultedNode"/> 的裁决。</returns>
    public static WorkflowFaultRecoveryDecision Retry(string? message = null) => new(WorkflowFaultRecoveryAction.RetryFaultedNode, null, message);

    /// <summary>创建从指定节点重新开始调度的裁决。</summary>
    /// <param name="targetNodeId">编译定义中必须存在的目标节点 ID。</param>
    /// <param name="message">可选跳转说明。</param>
    /// <returns>动作设置为 <see cref="WorkflowFaultRecoveryAction.JumpToNode"/> 的裁决。</returns>
    public static WorkflowFaultRecoveryDecision Jump(string targetNodeId, string? message = null) => new(WorkflowFaultRecoveryAction.JumpToNode, targetNodeId, message);

    /// <summary>命名恢复入口键，只用于RestartFromEntry。</summary>
    public string? RecoveryEntryKey { get; init; }

    /// <summary>请求继续原操作实例。</summary>
    /// <param name="message">处置说明。</param>
    /// <returns>由引擎验证的恢复请求。</returns>
    public static WorkflowFaultRecoveryDecision Continue(string? message = null) => new(WorkflowFaultRecoveryAction.ContinueOperation, Message: message);

    /// <summary>请求从命名入口重执行。</summary>
    /// <param name="entryKey">工程师配置的入口键。</param>
    /// <param name="message">处置说明。</param>
    /// <returns>由引擎生成计划并验证的请求。</returns>
    public static WorkflowFaultRecoveryDecision Restart(string entryKey, string? message = null) =>
        new(WorkflowFaultRecoveryAction.RestartFromEntry, Message: message) { RecoveryEntryKey = entryKey };
}

/// <summary>向恢复协调器暴露受控的工作流变量访问。</summary>
public interface IWorkflowFaultRecoveryContext
{
    /// <summary>尝试读取故障工作流上下文中的指定类型变量。</summary>
    /// <typeparam name="T">期望的变量类型。</typeparam>
    /// <param name="key">变量稳定键。</param>
    /// <param name="value">找到且类型兼容时返回变量值。</param>
    /// <returns>变量存在且类型兼容时返回 <see langword="true"/>。</returns>
    bool TryGetVariable<T>(string key, out T? value);

    /// <summary>写入非空变量，同键值会被覆盖。</summary>
    /// <param name="key">变量稳定键。</param>
    /// <param name="value">要保存的非空值。</param>
    void SetVariable(string key, object value);
    /// <summary>获取宿主服务。</summary>
    IServiceProvider Services { get; }
}

/// <summary>由宿主实现，用告警子流程或其他策略裁决节点故障。</summary>
public interface IWorkflowFaultRecoveryCoordinator
{
    /// <summary>异步处理节点故障并返回恢复动作。</summary>
    /// <param name="request">故障节点、报警编码、执行身份和当前尝试次数。</param>
    /// <param name="context">恢复策略可访问的变量及宿主服务接口。</param>
    /// <param name="cancellationToken">根工作流取消时触发的令牌。</param>
    /// <returns>停止、重试故障节点或跳转节点的非空裁决。</returns>
    ValueTask<WorkflowFaultRecoveryDecision> RecoverAsync(WorkflowFaultRecoveryRequest request, IWorkflowFaultRecoveryContext context, CancellationToken cancellationToken);
}
