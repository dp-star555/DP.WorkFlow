namespace DP.WorkFlow;

/// <summary>由工艺设计者声明的命名恢复入口；标记本身不构成现场安全证明。</summary>
public interface IWorkflowRecoveryEntryNode
{
    /// <summary>当前文档内唯一、非空的恢复入口键。</summary>
    string RecoveryEntryKey { get; }
}

/// <summary>V1串行无环路径的候选恢复计划。历史事实不回滚，公共数据和设备状态不自动还原。</summary>
/// <param name="EntryKey">工程师选择的命名入口。</param>
/// <param name="EntryNodeId">已经实际经过的入口节点。</param>
/// <param name="Fault">当前故障。</param>
/// <param name="AffectedNodeIds">入口以来实际执行或失败的节点；重跑时仍遵循图上的条件分支。</param>
/// <param name="InvalidatedVariableKeys">重跑前删除的、该段已成功暂存写入或删除的局部变量键。</param>
public sealed record WorkflowRecoveryEntryPlan(
    string EntryKey,
    string EntryNodeId,
    WorkflowFaultRecoveryRequest Fault,
    IReadOnlyList<string> AffectedNodeIds,
    IReadOnlyList<string> InvalidatedVariableKeys);

/// <summary>对恢复入口的最新现场验证结果。</summary>
/// <param name="Allowed">是否已核实允许重新进入。</param>
/// <param name="Message">拒绝原因或验证说明。</param>
public sealed record WorkflowRecoveryEntryValidation(bool Allowed, string? Message = null);

/// <summary>
/// 工位/工艺层的恢复入口验证能力，不由故障节点选择恢复策略。
/// 必须核实工件、设备、互锁、不可重复副作用及数据重建前提；缺少该能力时禁止重入。
/// </summary>
public interface IWorkflowRecoveryEntryGuard
{
    /// <summary>只验证最新条件，不执行移动、补偿或修改流程变量；处置应在处理子流程中完成。</summary>
    /// <param name="plan">运行时生成的候选计划。</param>
    /// <param name="context">故障流程上下文。</param>
    /// <param name="cancellationToken">运行取消令牌。</param>
    /// <returns>非空验证结果；异常或拒绝均不能推进流程。</returns>
    ValueTask<WorkflowRecoveryEntryValidation> ValidateAsync(
        WorkflowRecoveryEntryPlan plan, IWorkflowFaultRecoveryContext context, CancellationToken cancellationToken);
}
