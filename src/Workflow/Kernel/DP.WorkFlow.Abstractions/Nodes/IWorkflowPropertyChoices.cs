namespace DP.WorkFlow;

/// <summary>
/// 节点自己提供属性下拉候选（例如本文档已有的坐标系、宿主里配置的轴名）。
/// 属性需声明 <see cref="WorkflowPropertyEditorKeys.NodeChoice"/>；属性面板把上下文（同文档节点、服务表）交给节点，
/// 节点自己决定候选，基座不需要为新节点登记任何东西。
/// </summary>
public interface IWorkflowPropertyChoices
{
    /// <summary>返回指定属性的候选；键为显示文字，值为写入属性的对象。</summary>
    /// <param name="propertyName">属性名。</param>
    /// <param name="context">同文档节点和服务表。</param>
    /// <returns>候选；没有时返回空集合，属性退回文本输入。</returns>
    IReadOnlyList<KeyValuePair<string, object?>> GetPropertyChoices(string propertyName, WorkflowPropertyChoiceContext context);
}

/// <summary>节点提供属性候选时可用的上下文。</summary>
/// <param name="DocumentNodes">同文档全部节点，包含本节点。</param>
/// <param name="Services">宿主交给流程的服务表；设计器未接宿主时为 null。</param>
public sealed record WorkflowPropertyChoiceContext(IReadOnlyList<IWorkflowNodeModel> DocumentNodes, IServiceProvider? Services);
