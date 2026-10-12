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

    /// <summary>
    /// 已提交的输出被同一节点的新输出取代、退出运行历史后调用；此后任何绑定都不会再读到它。
    /// 投影借此释放随输出一起持有的资源（例如图像帧租约），使环形流程的内存不随循环次数增长。
    /// 节点结束后仍在使用这些资源的异步操作（例如后台保存图像）必须自行持有独立租约。默认不做任何事。
    /// </summary>
    void Retire() { }
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
