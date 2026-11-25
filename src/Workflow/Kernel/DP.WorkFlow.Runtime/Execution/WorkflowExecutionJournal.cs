namespace DP.WorkFlow;

/// <summary>
/// 一次成功执行的节点，交给合并执行记录（第 8 类）。回退再继续按它倒序回退。
/// </summary>
/// <param name="NodeId">节点 ID。</param>
/// <param name="NodeTitle">节点显示名。</param>
/// <param name="NodeType">节点类型。</param>
/// <param name="Position">执行位置（画布路径 / 节点 @ 圈次 [分支]）。</param>
/// <param name="IsCheckpoint">是否检查点节点：走过它时，本流程之前的记录不再回退。</param>
/// <param name="Undo">回退动作；节点不支持回退时为空。</param>
public sealed record WorkflowJournalEntry(
    string NodeId,
    string NodeTitle,
    string NodeType,
    string Position,
    bool IsCheckpoint,
    Func<CancellationToken, Task>? Undo);

/// <summary>
/// 一个流程的执行记录入口（由流程管理提供）：节点成功执行后记一笔；同时给出本流程当前的恢复轮次号。
/// </summary>
public interface IWorkflowExecutionJournal
{
    /// <summary>本流程当前的恢复轮次号：每次故障会话放行时加一。</summary>
    int RecoveryRound { get; }

    /// <summary>节点成功执行（输出已提交）后调用。</summary>
    /// <param name="entry">执行记录。</param>
    void NodeCompleted(WorkflowJournalEntry entry);
}
