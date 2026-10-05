namespace DP.WorkFlow;

/// <summary>准备请求所属的运行作用域。</summary>
public enum WorkflowRunScopeKind
{
    /// <summary>
    /// 根运行：真正开始新一轮。只有根运行宿主持有
    /// <see cref="IWorkflowRunResourceOwner"/>，因此只有它能退役上一轮资源。
    /// </summary>
    Root,

    /// <summary>
    /// 根运行内部的嵌套运行（子流程、故障处置、联合参与者）：只做校验与绑定准备。
    /// 其调用点在类型上拿不到 <see cref="IWorkflowRunResourceOwner"/>，既有资源保持被根运行引用。
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
/// <param name="PositionedNodes">包含稳定子计划路径的节点快照；旧调用方可为空。</param>
/// <param name="BindingScopeId">本次准备的绑定作用域身份。</param>
public sealed record WorkflowRunPreparationContext(
    IReadOnlyList<IWorkflowNodeModel> Nodes,
    WorkflowRunScopeKind ScopeKind,
    string? ParentNodeId = null,
    IServiceProvider? Services = null,
    IReadOnlyList<WorkflowPreparationNode>? PositionedNodes = null,
    Guid BindingScopeId = default)
{
    /// <summary>保留原有四参数构造入口，未提供位置的旧准备服务仍可运行。</summary>
    public WorkflowRunPreparationContext(IReadOnlyList<IWorkflowNodeModel> nodes, WorkflowRunScopeKind scopeKind,
        string? parentNodeId, IServiceProvider? services) : this(nodes, scopeKind, parentNodeId, services, null, default) { }

    /// <summary>保留原有四字段解构入口。</summary>
    public void Deconstruct(out IReadOnlyList<IWorkflowNodeModel> nodes, out WorkflowRunScopeKind scopeKind,
        out string? parentNodeId, out IServiceProvider? services)
    { nodes = Nodes; scopeKind = ScopeKind; parentNodeId = ParentNodeId; services = Services; }
}

/// <summary>准备阶段的稳定计划位置，不以节点ID跨子文档唯一为前提。</summary>
public sealed record WorkflowPreparationNode(string PlanPath, IWorkflowNodeModel Node);

/// <summary>提供节点作用域能力的宿主入口；准备阶段仍必须验证与建立实际绑定。</summary>
public interface IWorkflowNodeCapabilityProvider
{
    /// <summary>此节点的能力是否由该作用域提供者在准备阶段绑定。</summary>
    bool Provides(IWorkflowNodeModel node, Type capabilityType);
}

/// <summary>支持候选准备、提交与回滚的运行准备服务。</summary>
public interface IWorkflowTransactionalRunPreparationService : IWorkflowRunPreparationService
{
    /// <summary>准备候选；失败清理自身资源，成功由调用方提交并在运行结束归还。</summary>
    ValueTask<IWorkflowPreparedRun> PrepareRunAsync(WorkflowRunPreparationContext context, CancellationToken cancellationToken);
}

/// <summary>候选运行租约；未提交时Dispose回滚，提交后Dispose退役该运行。</summary>
public interface IWorkflowPreparedRun : IAsyncDisposable
{
    /// <summary>发布完整候选绑定；不进行可能失败的资源创建。</summary>
    void Commit();
}

/// <summary>
/// 由运行宿主在根运行开始时、以及任何嵌套运行开始时调用，用于校验运行前提并准备计划节点绑定。
/// <para>
/// 本接口**只做校验与绑定准备，不得释放任何既有资源**：嵌套运行仍在引用它们。
/// 释放上一轮资源是 <see cref="IWorkflowRunResourceOwner.ReleasePreviousRunAsync"/> 的职责，
/// 而嵌套调用点在类型上拿不到那个接口。
/// </para>
/// <para>
/// <see cref="WorkflowRunPreparationContext.ScopeKind"/> 仅供实现者做诊断与断言；
/// 破坏性行为**不应**再依赖它——依赖"调用者传对枚举"拦不住误用，AR-01 阶段 1 之前正是那样。
/// </para>
/// </summary>
public interface IWorkflowRunPreparationService
{
    /// <summary>在新建引擎开始执行首节点前校验运行前提并准备全部计划节点绑定。</summary>
    /// <param name="context">本次已编译计划的递归节点快照与作用域声明。</param>
    /// <param name="cancellationToken">宿主取消本次运行时触发的令牌。</param>
    /// <returns>验证和绑定准备完成时结束的异步操作。</returns>
    ValueTask PrepareAsync(
        WorkflowRunPreparationContext context,
        CancellationToken cancellationToken);
}

/// <summary>
/// 释放上一轮运行留下的运行级资源。**只有运行的所有者可以调用**：
/// 根运行宿主在完成 <see cref="IWorkflowRunPreparationService.PrepareAsync"/> 之后、新引擎执行首节点之前调用一次。
/// <para>
/// 之所以拆成独立接口而不是给准备请求再加一个标志位：嵌套调用点（子流程、故障处置、联合参与者）
/// 在类型上就**拿不到**本接口，因此"清空了父运行仍在引用的资源"从运行期缺陷变成编译期错误。
/// 依赖调用者自觉传对枚举是拦不住的——AR-01 阶段1 之前正是那样。
/// </para>
/// </summary>
public interface IWorkflowRunResourceOwner
{
    /// <summary>
    /// 退役上一轮资源并为新一轮让路。调用时机必须在
    /// <see cref="IWorkflowRunPreparationService.PrepareAsync"/> 之后：准备阶段产出的候选状态（例如
    /// 冻结后的文件夹清单）在这里才启用，因此"先准备、后释放"是契约的一部分。
    /// </summary>
    /// <param name="cancellationToken">宿主取消本次运行时触发的令牌。</param>
    /// <returns>上一轮资源退役完成时结束的异步操作。</returns>
    ValueTask ReleasePreviousRunAsync(CancellationToken cancellationToken);
}
