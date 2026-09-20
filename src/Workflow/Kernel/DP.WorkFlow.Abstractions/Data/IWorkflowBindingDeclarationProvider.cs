namespace DP.WorkFlow;

/// <summary>指定声明绑定相对于复合节点所在的图。</summary>
public enum WorkflowBindingGraphScope
{
    /// <summary>绑定来源位于声明节点所在的当前画布。</summary>
    CurrentCanvas = 0,

    /// <summary>绑定来源位于复合节点持有的子画布。</summary>
    SubDocument = 1
}

/// <summary>表示节点配置显式声明的一个绑定使用点。</summary>
/// <param name="Binding">来源节点或公共数据及其成员路径。</param>
/// <param name="ExpectedType">配置要求的目标值类型。</param>
/// <param name="ConfigurationPath">绑定在节点配置中的路径。</param>
/// <param name="Scope">解析来源节点时应使用当前画布还是复合节点子画布。</param>
public sealed record WorkflowDeclaredBinding(
    WorkflowBindingKey Binding,
    Type ExpectedType,
    string ConfigurationPath,
    WorkflowBindingGraphScope Scope = WorkflowBindingGraphScope.CurrentCanvas);

/// <summary>由包含非 WorkflowInput 绑定结构的节点实现。</summary>
public interface IWorkflowBindingDeclarationProvider
{
    /// <summary>枚举无法通过普通 <see cref="WorkflowInput{T}"/> 反射发现的绑定使用点。</summary>
    /// <returns>供编译期可达性、成员路径和类型分析使用的声明集合。</returns>
    IEnumerable<WorkflowDeclaredBinding> GetDeclaredBindings();
}
