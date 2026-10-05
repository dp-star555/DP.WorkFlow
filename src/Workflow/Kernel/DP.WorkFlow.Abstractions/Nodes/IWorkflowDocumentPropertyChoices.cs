namespace DP.WorkFlow;

/// <summary>
/// 节点按所在文档提供属性下拉候选，例如本文档已有的坐标系。
/// 属性需声明 <see cref="WorkflowPropertyEditorKeys.DocumentChoice"/>；属性面板把同文档节点交给节点，节点自己决定候选。
/// </summary>
public interface IWorkflowDocumentPropertyChoices
{
    /// <summary>返回指定属性的候选；键为显示文字，值为写入属性的对象。</summary>
    /// <param name="propertyName">属性名。</param>
    /// <param name="documentNodes">同文档全部节点，包含本节点。</param>
    /// <returns>候选；没有时返回空集合。</returns>
    IReadOnlyList<KeyValuePair<string, object?>> GetPropertyChoices(string propertyName, IReadOnlyList<IWorkflowNodeModel> documentNodes);
}
