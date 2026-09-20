namespace DP.WorkFlow;

/// <summary>由提供可编辑脚本正文的节点实现，设计器据此自动增加脚本页面。</summary>
public interface IWorkflowScriptNode : IWorkflowNodeModel
{
    /// <summary>获取或设置脚本实例的全局稳定标识；编辑保留，复制节点时重新生成。</summary>
    string ScriptId { get; set; }

    /// <summary>获取或设置脚本正文。</summary>
    string Script { get; set; }

    /// <summary>获取脚本语言的稳定标识。</summary>
    string ScriptLanguage { get; }
}

/// <summary>由支持显式 DLL 引用的脚本节点实现。</summary>
public interface IWorkflowScriptReferenceNode : IWorkflowScriptNode
{
    /// <summary>获取脚本使用的 DLL 路径集合；持久化时应优先保存相对路径。</summary>
    IList<string> ScriptReferencePaths { get; set; }
}
