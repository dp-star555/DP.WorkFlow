namespace DP.WorkFlow;

/// <summary>
/// 使用公共数据的节点（R2-d）：列出自己用到的公共数据（键、形态、值类型）。
/// 编译时据此核对流程的公共数据清单，打开旧流程文件时据此推断清单。
/// </summary>
public interface IWorkflowPublicDataUser : IWorkflowNodeModel
{
    /// <summary>本节点用到的公共数据；键为空的项不返回。初始化类节点可以带上默认值。</summary>
    IEnumerable<WorkflowPublicDataDeclaration> GetPublicDataUses();
}
