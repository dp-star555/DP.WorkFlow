namespace DP.WorkFlow.Persistence.Json;

/// <summary>读取后的工作流文档，以及读取时的提示（例如未注册的节点类型）。</summary>
/// <param name="Document">包含入口、语义图和布局的工作流文档。</param>
/// <param name="Warnings">读取提示。</param>
public sealed record WorkflowLoadResult(WorkflowDocument Document, IReadOnlyList<string> Warnings);
