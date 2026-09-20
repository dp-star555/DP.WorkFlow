namespace DP.WorkFlow;

/// <summary>准备请求所属的运行作用域。</summary>
public enum WorkflowRunScopeKind
{
    /// <summary>根运行：真正开始新一轮，允许释放上一轮运行留下的运行级资源。</summary>
    Root,

    /// <summary>
    /// 根运行内部的嵌套运行（子流程、故障处置、联合参与者）：
    /// 只做校验与绑定准备，不得释放任何既有资源——这些资源仍被根运行引用。
    /// </summary>
    Nested
}

/// <summary>提供运行准备阶段可检查的全部根计划和子计划节点配置快照。</summary>
/// <param name="Nodes">按根计划优先的稳定顺序递归展开的节点配置。</param>
/// <param name="ScopeKind">本次准备请求的作用域；调用者必须明确声明。</param>
/// <param name="ParentNodeId">嵌套运行时承载它的父节点 ID；根运行为空。</param>
/// <param name="Services">
/// 本次运行的宿主服务容器；准备实现可据此读取自己需要的装配能力
/// （例如已发布的逻辑源目录）。为空表示调用者未提供，实现必须按能力缺失处理而不是假定存在。
/// </param>
public sealed record WorkflowRunPreparationContext(
    IReadOnlyList<IWorkflowNodeModel> Nodes,
    WorkflowRunScopeKind ScopeKind,
    string? ParentNodeId = null,
    IServiceProvider? Services = null);

/// <summary>
/// 由运行宿主在根运行开始时、以及任何嵌套运行开始时调用。
/// 实现者必须依据 <see cref="WorkflowRunPreparationContext.ScopeKind"/> 判断：
/// 只有 <see cref="WorkflowRunScopeKind.Root"/> 才允许释放上一轮资源；
/// <see cref="WorkflowRunScopeKind.Nested"/> 必须保持既有资源不变。
/// </summary>
public interface IWorkflowRunPreparationService
{
    /// <summary>在新建引擎开始执行首节点前准备运行级资源和全部计划节点绑定。</summary>
    /// <param name="context">本次已编译计划的递归节点快照与作用域声明。</param>
    /// <param name="cancellationToken">宿主取消本次运行时触发的令牌。</param>
    /// <returns>资源释放、验证和绑定准备完成时结束的异步操作。</returns>
    ValueTask PrepareAsync(
        WorkflowRunPreparationContext context,
        CancellationToken cancellationToken);
}
