namespace DP.WorkFlow;

/// <summary>联合域中的角色监控与阻断句柄，不暴露独立启动、重配置或任意跳转。</summary>
public sealed class WorkflowJointRecoveryParticipant
{
    private readonly WorkflowEngine _engine;
    internal WorkflowJointRecoveryParticipant(string role, WorkflowEngine engine) { Role = role; _engine = engine; }
    /// <summary>工艺角色键。</summary>
    public string Role { get; }
    /// <summary>本地不可变运行快照，包括受监管的处理子流程。</summary>
    public WorkflowRuntimeSnapshot Snapshot => _engine.GetRuntimeSnapshot();
    /// <summary>本地恢复事件快照。</summary>
    public IReadOnlyList<WorkflowRecoveryEvent> RecoveryEvents => Array.AsReadOnly(_engine.RunState.RecoveryEvents.ToArray());
    /// <summary>本地已失效输出序号快照；历史事实不会被删除。</summary>
    public IReadOnlyList<long> InvalidatedOutputSequences => Array.AsReadOnly(_engine.RunState.InvalidatedOutputSequences.ToArray());
    /// <summary>请求本地人工暂停，不代表设备安全停止。</summary>
    public void Pause() => _engine.Pause();
    /// <summary>仅解除本地人工暂停，不解除外部Hold。</summary>
    public void Resume() => _engine.Resume();
    /// <summary>增加独立调度阻断，不会替代设备退出核实。</summary>
    /// <param name="reason">由阻断所有者管理的非空稳定键。</param>
    public void AddExternalHold(string reason) => _engine.AddExternalHold(reason);
    /// <summary>仅解除调用方拥有的指定阻断；不得代其他所有者解除。</summary>
    /// <param name="reason">调用方持有的阻断键。</param>
    public void RemoveExternalHold(string reason) => _engine.RemoveExternalHold(reason);
}
