namespace DP.WorkFlow;

/// <summary>
/// 反复运行同一子画布的会话，供循环块等节点使用。每圈在同一运行内开一次画布作用域（R3-a）：
/// 子画布的数据和节点状态每圈重新开始，事件写入同一份运行记录并标明圈序号。
/// 暂停、Hold 直接使用父运行的；取消由传入的取消令牌立即生效。
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
