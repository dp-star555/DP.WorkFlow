namespace DP.WorkFlow;

/// <summary>
/// 测试专用的事件探针：只有测试支持库（DP.WorkFlow.Testing）会设置 <see cref="Observer"/>，
/// 用于在每个测试结束后核对事件流规则（R5-a）。现场程序中它始终为空，记录路径只多一次空判断。
/// </summary>
internal static class WorkflowRunEventProbe
{
    /// <summary>每条事件分配序号后立即回调；在记录器锁内调用，同一运行的回调顺序与序号一致。</summary>
    internal static volatile Action<WorkflowRunEvent>? Observer;

    internal static void Publish(WorkflowRunEvent @event)
    {
        var observer = Observer;
        if (observer is null)
            return;
        try
        {
            observer(@event);
        }
        catch
        {
            // 探针只服务于测试核对，绝不能影响记录或执行。
        }
    }
}
