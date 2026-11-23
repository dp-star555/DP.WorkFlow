namespace DP.WorkFlow.UI;

/// <summary>运行总览里的一个流程。</summary>
/// <param name="Name">流程名。</param>
/// <param name="State">运行状态。</param>
/// <param name="RecoveryRound">恢复轮次号（故障会话每放行一次加一）。</param>
/// <param name="NoticeCount">提示条数。</param>
/// <param name="Recording">运行记录健康；还没运行过时为空。</param>
/// <param name="InFault">是否在当前故障会话里。</param>
public sealed record WorkflowRunCenterFlowRow(
    string Name,
    E_WorkflowExecutionState State,
    int RecoveryRound,
    int NoticeCount,
    WorkflowRecordingHealth? Recording,
    bool InFault)
{
    /// <summary>状态文字。</summary>
    public string StateText => InFault ? "故障处理中" : State switch
    {
        E_WorkflowExecutionState.Running => "运行中",
        E_WorkflowExecutionState.Paused => "已暂停",
        E_WorkflowExecutionState.Completed => "已完成",
        E_WorkflowExecutionState.Faulted => "失败",
        E_WorkflowExecutionState.Canceled => "已停止",
        _ => "未运行"
    };

    /// <summary>记录状态文字：正常时为空，否则说明丢了多少、写失败多少。</summary>
    public string RecordingText => Recording is not { } health || health.State == E_WorkflowRecordingHealth.Healthy
        ? string.Empty
        : $"记录{(health.DroppedCount > 0 ? $"丢弃 {health.DroppedCount} 条" : string.Empty)}{(health.FailedWriteCount > 0 ? $" 写入失败 {health.FailedWriteCount} 次" : string.Empty)}".Trim();
}

/// <summary>
/// 运行总览的模型：流程管理里每个流程的状态、恢复轮次、提示条数、记录健康和是否在故障处理中，
/// 以及启动、停止。按拉取方式读取，不订阅引擎事件；界面按刷新节拍调用 <see cref="GetRows"/>。
/// </summary>
public sealed class WorkflowRunCenterOverviewModel
{
    /// <summary>初始化模型。</summary>
    /// <param name="center">流程管理。</param>
    public WorkflowRunCenterOverviewModel(WorkflowRunCenter center) =>
        Center = center ?? throw new ArgumentNullException(nameof(center));

    /// <summary>流程管理。</summary>
    public WorkflowRunCenter Center { get; }

    /// <summary>当前各流程的一行。</summary>
    public IReadOnlyList<WorkflowRunCenterFlowRow> GetRows()
    {
        var inFault = new HashSet<string>(Center.CurrentFaultSession?.AffectedFlows ?? Array.Empty<string>(), StringComparer.Ordinal);
        return Center.Flows.Select(flow => new WorkflowRunCenterFlowRow(
                flow.Name,
                flow.State,
                flow.RecoveryRound,
                flow.Host.Notices.GetAll().Count,
                flow.Host.Engine?.RecordingHealth,
                inFault.Contains(flow.Name)))
            .ToArray();
    }

    /// <summary>取一个流程的提示板；流程不存在时为空。</summary>
    /// <param name="name">流程名。</param>
    public WorkflowNoticeBoard? GetNotices(string name) => Center.GetFlow(name)?.Host.Notices;

    /// <summary>启动一个流程（已在运行时不重复启动）；运行结果在后台完成。</summary>
    /// <param name="name">流程名。</param>
    /// <exception cref="InvalidOperationException">流程不存在。</exception>
    public Task<WorkflowRunResult> Start(string name) =>
        (Center.GetFlow(name) ?? throw new InvalidOperationException($"流程 {name} 不存在。")).RunAsync();

    /// <summary>停止一个流程。</summary>
    /// <param name="name">流程名。</param>
    /// <exception cref="InvalidOperationException">流程不存在。</exception>
    public Task Stop(string name) =>
        (Center.GetFlow(name) ?? throw new InvalidOperationException($"流程 {name} 不存在。")).StopAsync();

    /// <summary>启动全部流程。</summary>
    public IReadOnlyList<Task<WorkflowRunResult>> StartAll() => Center.StartAll();

    /// <summary>停止全部流程。</summary>
    public Task StopAll() => Center.StopAllAsync();
}
