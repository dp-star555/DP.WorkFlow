namespace DP.WorkFlow;

/// <summary>联合恢复的本地准备屏障。引擎完成本地验证、清理和数据失效后，尚未调度下一节点时调用。</summary>
public interface IWorkflowRecoveryBarrier
{
    /// <summary>等待其他必要参与者准备就绪；失败或取消不得放行。</summary>
    /// <param name="request">本地故障或安全边界中断身份。</param>
    /// <param name="cancellationToken">运行取消令牌。</param>
    /// <returns>联合放行完成；不保证物理设备同时执行。</returns>
    ValueTask PreparedAsync(WorkflowFaultRecoveryRequest request, CancellationToken cancellationToken);
    /// <summary>拒绝本轮恢复并阻断其他参与者；不得抛出异常。</summary>
    /// <param name="request">本地恢复身份。</param>
    /// <param name="message">拒绝原因。</param>
    void Reject(WorkflowFaultRecoveryRequest request, string message);
}
