namespace DP.WorkFlow;

/// <summary>
/// 定义 UI 和设备宿主控制工作流生命周期所需的统一接口。
/// </summary>
public interface IWorkflowRuntimeHost
{
    /// <summary>运行快照变化时发生。</summary>
    event Action<WorkflowRuntimeSnapshot>? SnapshotChanged;

    /// <summary>获取当前引擎。</summary>
    WorkflowEngine? Engine { get; }

    /// <summary>获取当前状态。</summary>
    E_WorkflowExecutionState State { get; }

    /// <summary>获取最近一次运行结果。</summary>
    WorkflowRunResult? LastRunResult { get; }

    /// <summary>编译正式文档并配置下一次运行。</summary>
    /// <param name="document">包含显式入口、语义图和布局的工作流文档。</param>
    /// <param name="context">可选的宿主上下文；为空时创建默认上下文。</param>
    /// <exception cref="InvalidOperationException">当前流程仍在运行。</exception>
    void Configure(WorkflowDocument document, WorkflowContext? context = null);

    /// <summary>启动新引擎，或在运行期间返回同一个运行任务。</summary>
    /// <param name="cancellationToken">与宿主内部取消源链接的调用方令牌。</param>
    /// <returns>工作流进入 Completed、Faulted 或 Canceled 后的结果。</returns>
    /// <exception cref="InvalidOperationException">尚未调用 <see cref="Configure"/>。</exception>
    Task<WorkflowRunResult> RunAsync(CancellationToken cancellationToken = default);

    /// <summary>请求人工暂停。</summary>
    void Pause();

    /// <summary>清除人工暂停。</summary>
    void Resume();

    /// <summary>添加一个阻止继续调度的外部 Hold 原因。</summary>
    /// <param name="reason">会被裁剪并按序号字符串比较去重的原因。</param>
    void AddExternalHold(string reason);

    /// <summary>移除一个外部 Hold；人工暂停或其他原因仍存在时不会恢复。</summary>
    /// <param name="reason">要清除的 Hold 原因；空白值被忽略。</param>
    void RemoveExternalHold(string reason);

    /// <summary>请求取消当前运行。</summary>
    void Cancel();

    /// <summary>获取当前运行快照。</summary>
    /// <returns>当前引擎快照；尚未运行时返回基于已配置定义的 Idle 快照。</returns>
    WorkflowRuntimeSnapshot GetSnapshot();
}
