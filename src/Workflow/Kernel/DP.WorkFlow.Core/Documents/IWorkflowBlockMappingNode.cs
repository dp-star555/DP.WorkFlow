namespace DP.WorkFlow;

/// <summary>由通过显式输入/输出映射隔离父子数据作用域的复合节点实现。</summary>
public interface IWorkflowBlockMappingNode : IWorkflowSubDocumentNode
{
    /// <summary>获取或设置父作用域到子作用域的输入映射。</summary>
    IList<BlockInputMapping> InputMappings { get; set; }

    /// <summary>获取或设置子作用域到父作用域的输出映射。</summary>
    IList<BlockOutputMapping> OutputMappings { get; set; }
}
