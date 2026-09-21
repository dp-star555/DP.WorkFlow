namespace DP.WorkFlow;

/// <summary>
/// 根运行的运行级资源作用域所有者：在引擎执行首节点之前为本轮取得运行级资源，并在本轮结束时退役。
/// <para>
/// <b>只有根运行宿主解析本接口。</b> 嵌套运行（子流程、故障处置、联合参与者）在类型上拿不到它，
/// 因此无法重新取得所有权，也无法释放父运行仍在使用的资源——这是结构性保证，
/// 与 <see cref="IWorkflowRunResourceOwner"/> 采用同一原则：不依赖调用者传对枚举。
/// </para>
/// <para>
/// 与 <see cref="IWorkflowRunResourceOwner"/> 的分工：
/// 后者负责"上一轮资源退役"，故意延迟到下一次根运行开始，使上一轮结果在查看窗口内仍然有效；
/// 本接口负责"本轮作用域取得与退役"，必须在首节点之前生效、在本轮结束时归还。
/// 两者可以由不同实现承担，也可以同时注册。
/// </para>
/// <para>
/// 典型用途是外部回调缓冲的图像源：相机需要在采集节点之前就布防，否则"回调早于采集节点"无法成立；
/// 而"哪一根运行的帧"只能由根运行界定，嵌套运行不得重置它。
/// </para>
/// </summary>
public interface IWorkflowRunScopeOwner
{
    /// <summary>
    /// 在引擎执行首节点之前取得本轮根运行的运行级资源所有权。
    /// </summary>
    /// <param name="runId">宿主为本轮根运行分配的作用域身份；用于诊断与冲突报告。</param>
    /// <param name="cancellationToken">宿主取消本次运行时触发的令牌。</param>
    /// <returns>本轮所有权租约；释放它即退役本轮。</returns>
    /// <exception cref="InvalidOperationException">已有其他根运行持有同一资源的所有权。</exception>
    ValueTask<IWorkflowRunScopeLease> BeginRunAsync(Guid runId, CancellationToken cancellationToken);
}

/// <summary>
/// 一根根运行的运行级资源所有权租约。
/// <para>
/// 释放顺序由实现固定；释放必须等待已经进入的外部回调退出，之后不得再交付本轮的帧。
/// 释放动作发生在 <c>WorkflowEngine.RunAsync</c> 返回之后，因此本轮已经取得的图像必须与
/// 本租约解耦——节点输出持有的是独立租约，不因本租约释放而失效。
/// </para>
/// </summary>
public interface IWorkflowRunScopeLease : IAsyncDisposable
{
    /// <summary>本租约对应的根运行作用域身份。</summary>
    Guid RunId { get; }

    /// <summary>本轮已取得所有权的资源标识，按序数排序；用于运行制品与诊断。</summary>
    IReadOnlyList<string> OwnedResourceIds { get; }
}
