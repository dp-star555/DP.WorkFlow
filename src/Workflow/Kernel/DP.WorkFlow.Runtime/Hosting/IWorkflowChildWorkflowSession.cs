namespace DP.WorkFlow;

/// <summary>
/// 在父引擎监管下反复运行同一子计划的会话，供循环块等节点使用。子引擎只创建一次，每圈是一次独立的子运行：
/// 运行状态、执行次数上限和恢复次数每圈重新开始，每圈有自己的运行身份和记录。
/// 暂停、Hold 和快照在整个会话期间关联到父运行；取消由传入的取消令牌立即生效。
/// </summary>
public interface IWorkflowChildWorkflowSession : IAsyncDisposable
{
    /// <summary>运行一圈子流程。上一圈剩余输出的资源（例如图像帧租约）在本圈开始前释放。</summary>
    /// <param name="cancellationToken">父节点执行的取消令牌。</param>
    /// <returns>本圈子运行的结果。</returns>
    Task<WorkflowRunResult> RunCycleAsync(CancellationToken cancellationToken);

    /// <summary>
    /// 保留最后一圈的剩余输出资源，随父节点本次输出的生命周期释放；输出映射引用子流程输出时，在正常退出循环后调用。
    /// 未调用时，会话释放时一并释放最后一圈的资源。
    /// </summary>
    void KeepLastCycleOutputs();
}
