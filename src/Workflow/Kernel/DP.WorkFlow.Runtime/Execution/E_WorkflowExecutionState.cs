namespace DP.WorkFlow;

/// <summary>
/// 工作流引擎执行状态。名称沿用旧版以降低宿主迁移成本。
/// </summary>
public enum E_WorkflowExecutionState
{
    /// <summary>尚未运行或已经重置。</summary>
    Idle,

    /// <summary>正在运行。</summary>
    Running,

    /// <summary>因人工请求或外部条件暂时挂起。</summary>
    Paused,

    /// <summary>已经成功完成。</summary>
    Completed,

    /// <summary>因故障结束。</summary>
    Faulted,

    /// <summary>因取消请求结束。</summary>
    Canceled
}
