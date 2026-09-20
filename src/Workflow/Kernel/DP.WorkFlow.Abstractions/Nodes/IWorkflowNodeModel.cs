namespace DP.WorkFlow;

/// <summary>
/// 表示可持久化的工作流节点配置。
/// 节点模型不再负责执行；运行行为由对应的 <see cref="IWorkflowNodeHandler"/> 提供。
/// </summary>
public interface IWorkflowNodeModel
{
    /// <summary>获取或设置画布内唯一的节点标识。</summary>
    string Id { get; set; }

    /// <summary>获取或设置面向用户的节点标题。</summary>
    string Title { get; set; }

    /// <summary>获取稳定的节点类型键。</summary>
    string NodeType { get; }
}

/// <summary>
/// 提供节点标识和标题的基础实现。
/// </summary>
public abstract class WorkflowNodeModel : IWorkflowNodeModel
{
    /// <inheritdoc />
    public string Id { get; set; } = string.Empty;

    /// <inheritdoc />
    public string Title { get; set; } = string.Empty;

    /// <inheritdoc />
    public abstract string NodeType { get; }
}
