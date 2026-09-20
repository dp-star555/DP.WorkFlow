using ModernUI.Localization;
using DP.WorkFlow;

namespace DP.WorkFlow.UI.WinForms;

/// <summary>把稳定运行时状态和系统 Trace 步骤适配为当前语言的用户文本。</summary>
public static class WorkflowRuntimeText
{
    public static string ExecutionState(ILocalizationContext context, E_WorkflowExecutionState state)
    {
        ArgumentNullException.ThrowIfNull(context);
        var key = state switch
        {
            E_WorkflowExecutionState.Idle => WorkflowUiTextKeys.ExecutionStateIdle,
            E_WorkflowExecutionState.Running => WorkflowUiTextKeys.ExecutionStateRunning,
            E_WorkflowExecutionState.Paused => WorkflowUiTextKeys.ExecutionStatePaused,
            E_WorkflowExecutionState.Completed => WorkflowUiTextKeys.ExecutionStateCompleted,
            E_WorkflowExecutionState.Faulted => WorkflowUiTextKeys.ExecutionStateFaulted,
            E_WorkflowExecutionState.Canceled => WorkflowUiTextKeys.ExecutionStateCanceled,
            _ => throw new ArgumentOutOfRangeException(nameof(state), state, "Unknown workflow execution state.")
        };
        return context.Text(key);
    }

    public static string NodeState(ILocalizationContext context, E_NodeState state)
    {
        ArgumentNullException.ThrowIfNull(context);
        var key = state switch
        {
            E_NodeState.Idle => WorkflowUiTextKeys.NodeStateIdle,
            E_NodeState.Running => WorkflowUiTextKeys.NodeStateRunning,
            E_NodeState.Completed => WorkflowUiTextKeys.NodeStateCompleted,
            E_NodeState.Failed => WorkflowUiTextKeys.NodeStateFailed,
            E_NodeState.Canceled => WorkflowUiTextKeys.NodeStateCanceled,
            _ => throw new ArgumentOutOfRangeException(nameof(state), state, "Unknown workflow node state.")
        };
        return context.Text(key);
    }

    /// <summary>只翻译引擎拥有的稳定步骤；节点自定义步骤保持原始业务文本。</summary>
    public static string TraceStep(ILocalizationContext context, string step)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(step);
        var key = step switch
        {
            "NodeStarted" => WorkflowUiTextKeys.TraceStepNodeStarted,
            "NodeCompleted" => WorkflowUiTextKeys.TraceStepNodeCompleted,
            "NodeFailed" => WorkflowUiTextKeys.TraceStepNodeFailed,
            "ParallelMerged" => WorkflowUiTextKeys.TraceStepParallelMerged,
            "ScriptOutput" => WorkflowUiTextKeys.TraceStepScriptOutput,
            _ => (TextKey?)null
        };
        return key is { } known ? context.Text(known) : step;
    }
}
