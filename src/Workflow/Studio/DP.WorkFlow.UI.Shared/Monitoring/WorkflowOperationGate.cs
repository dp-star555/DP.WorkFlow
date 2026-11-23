namespace DP.WorkFlow.UI;

/// <summary>界面上会改变流程运行的操作。</summary>
public enum WorkflowOperationKind
{
    /// <summary>单独启动一个流程。</summary>
    StartFlow,

    /// <summary>单独停止一个流程。</summary>
    StopFlow,

    /// <summary>在运行总览里全部启动。</summary>
    StartAll,

    /// <summary>在运行总览里全部停止。</summary>
    StopAll,

    /// <summary>故障工作台：按所选去向放行。</summary>
    ReleaseFault,

    /// <summary>故障工作台：重新运行处理流程。</summary>
    RerunFaultHandler,

    /// <summary>故障工作台：停止全部受影响的流程。</summary>
    StopFaultedFlows,

    /// <summary>设计页试运行（含运行当前子流程、单节点运行）。</summary>
    TrialRun,

    /// <summary>设计页停止试运行。</summary>
    StopTrialRun,

    /// <summary>发布草稿：替换正式流程文件并记一版。</summary>
    Publish,

    /// <summary>放弃草稿，回到已发布的版本。</summary>
    DiscardDraft
}

/// <summary>一次界面操作。</summary>
/// <param name="Kind">操作。</param>
/// <param name="Target">对象：流程名，或全部流程时为 *。</param>
/// <param name="Detail">补充说明，例如各流程选的去向；不含敏感值。</param>
public sealed record WorkflowOperation(WorkflowOperationKind Kind, string Target, string? Detail = null)
{
    /// <summary>稳定的操作键，宿主记审计时用，例如 workflow.flow.start。</summary>
    public string Key => Kind switch
    {
        WorkflowOperationKind.StartFlow => "workflow.flow.start",
        WorkflowOperationKind.StopFlow => "workflow.flow.stop",
        WorkflowOperationKind.StartAll => "workflow.flow.startAll",
        WorkflowOperationKind.StopAll => "workflow.flow.stopAll",
        WorkflowOperationKind.ReleaseFault => "workflow.fault.release",
        WorkflowOperationKind.RerunFaultHandler => "workflow.fault.rerun",
        WorkflowOperationKind.StopFaultedFlows => "workflow.fault.stopAll",
        WorkflowOperationKind.TrialRun => "workflow.design.run",
        WorkflowOperationKind.Publish => "workflow.design.publish",
        WorkflowOperationKind.DiscardDraft => "workflow.design.discard",
        _ => "workflow.design.stop"
    };

    /// <summary>审计里的对象文字：对象加补充说明。</summary>
    public string Describe() => string.IsNullOrEmpty(Detail) ? Target : Target + " " + Detail;
}

/// <summary>
/// 宿主对界面上流程操作的准入与记录，例如 DP.Foundation 用它做鉴权和审计。
/// 页面把每个操作包成一个动作交给它；它决定是否执行，并记录结果。
/// </summary>
public interface IWorkflowOperationGate
{
    /// <summary>执行一个操作。拒绝时抛出异常，页面把异常消息显示出来。</summary>
    /// <param name="operation">操作。</param>
    /// <param name="action">真正的动作：发出命令即返回，不等流程跑完。</param>
    Task RunAsync(WorkflowOperation operation, Func<Task> action);
}

/// <summary>常用的操作准入。</summary>
public static class WorkflowOperationGates
{
    /// <summary>直接执行，不鉴权也不记录（单独使用 Workflow 时的默认）。</summary>
    public static IWorkflowOperationGate Direct { get; } = new DirectGate();

    private sealed class DirectGate : IWorkflowOperationGate
    {
        public Task RunAsync(WorkflowOperation operation, Func<Task> action) => action();
    }
}
