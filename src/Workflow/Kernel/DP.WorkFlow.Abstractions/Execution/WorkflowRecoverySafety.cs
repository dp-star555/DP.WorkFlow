namespace DP.WorkFlow;

/// <summary>Declares whether repeating a faulted node execution is safe.</summary>
public enum WorkflowRetrySafety
{
    /// <summary>The node may have partially completed side effects and must not be retried automatically.</summary>
    NotRetryable,

    /// <summary>Repeating the operation with the same inputs is explicitly idempotent.</summary>
    Idempotent,

    /// <summary>The handler understands partial completion and resumes from durable device or domain state.</summary>
    ResumeAware
}

/// <summary>Implemented by node models that explicitly declare retry behavior.</summary>
public interface IWorkflowRetrySafetyNode
{
    /// <summary>Gets the retry safety guaranteed by the node and its handler.</summary>
    WorkflowRetrySafety RetrySafety { get; }
}

/// <summary>旧版恢复目标标记，仅保留文档/扩展类型兼容；不再授权Jump。新恢复使用命名入口与工位验证。</summary>
public interface IWorkflowRecoveryTargetNode
{
}
