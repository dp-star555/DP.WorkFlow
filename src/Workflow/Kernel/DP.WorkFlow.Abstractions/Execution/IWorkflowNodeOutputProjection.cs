namespace DP.WorkFlow;

/// <summary>
/// 一次节点执行登记的派生投影。投影只在输出正式提交后发布；未提交、取消或提交失败的执行不得发布投影，
/// 否则界面会看到调度从未承认的结果。
/// </summary>
public interface IWorkflowNodeOutputProjection
{
    /// <summary>输出已正式写入运行状态后发布投影。</summary>
    /// <param name="executionSequence">本次提交在运行输出序列中的序号，用于后续失效同步。</param>
    void Commit(long executionSequence);
}

/// <summary>
/// 领域侧的投影接收方。运行状态使一批输出失效时（例如恢复重跑），必须同步撤销对应的派生投影，
/// 不能让界面通过"仍能显示"推断运行输出仍然有效。
/// </summary>
public interface IWorkflowNodeOutputProjectionSink
{
    /// <summary>从指定执行序号起的输出已失效。</summary>
    /// <param name="executionSequence">失效区间的起始序号，含该序号本身。</param>
    void InvalidateFrom(long executionSequence);
}
