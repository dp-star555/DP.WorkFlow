namespace DP.WorkFlow;

/// <summary>
/// 宿主登记在流程服务表里的属性候选来源，例如“本机已配置的轴”。
/// 节点属性用 <see cref="WorkflowPropertyEditorKeys.HostChoicePrefix"/> 开头的编辑器键声明要哪一类候选，
/// 由提供该类对象的一方回答；节点和属性面板都不需要知道对象从哪里来。
/// </summary>
public interface IWorkflowPropertyChoiceSource
{
    /// <summary>返回候选；不认识这个编辑器键时返回 <see langword="null"/>，交给下一个来源。</summary>
    /// <param name="editorKey">属性声明的编辑器键。</param>
    /// <param name="propertyName">属性名。</param>
    /// <returns>键为显示文字、值为写入属性的对象。</returns>
    IReadOnlyList<KeyValuePair<string, object?>>? GetChoices(string editorKey, string propertyName);
}
