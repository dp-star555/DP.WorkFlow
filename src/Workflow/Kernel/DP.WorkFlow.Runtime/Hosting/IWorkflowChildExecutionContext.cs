namespace DP.WorkFlow;

/// <summary>
/// 允许复合节点在父引擎监管下运行子流程。
/// </summary>
public interface IWorkflowChildExecutionContext
{
    /// <summary>获取父定义中已经编译的当前复合节点子流程。</summary>
    /// <returns>以当前节点 ID 索引的子画布运行定义。</returns>
    /// <exception cref="InvalidOperationException">当前节点没有编译后的子定义。</exception>
    WorkflowExecutionPlan GetChildWorkflowExecutionPlan();

    /// <summary>创建复制父变量、共享信号域但隔离节点输出的子上下文。</summary>
    /// <returns>供一次子流程运行使用的新上下文。</returns>
    WorkflowContext CreateChildScope();

    /// <summary>运行子定义，并将暂停、Hold、取消和快照关联到父运行。</summary>
    /// <param name="definition">当前复合节点的编译后子定义。</param>
    /// <param name="childContext">通过 <see cref="CreateChildScope"/> 创建并可预先写入输入变量的上下文。</param>
    /// <param name="cancellationToken">父节点执行的取消令牌。</param>
    /// <returns>子引擎到达终态后的运行结果。</returns>
    Task<WorkflowRunResult> RunChildWorkflowAsync(
        WorkflowExecutionPlan definition,
        WorkflowContext childContext,
        CancellationToken cancellationToken);

    /// <summary>打开可反复运行当前复合节点子定义的会话；会话只创建一个子引擎，每圈调用一次。</summary>
    /// <param name="definition">当前复合节点的编译后子定义。</param>
    /// <param name="childContext">通过 <see cref="CreateChildScope"/> 创建、在整个会话期间复用的上下文。</param>
    /// <returns>调用方负责释放的会话。</returns>
    IWorkflowChildWorkflowSession OpenChildWorkflowSession(
        WorkflowExecutionPlan definition,
        WorkflowContext childContext);
}
