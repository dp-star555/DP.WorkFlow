namespace DP.WorkFlow;

/// <summary>
/// 提供节点执行期间可使用的上下文。
/// </summary>
public interface IWorkflowNodeExecutionContext
{
    /// <summary>获取当前节点。</summary>
    IWorkflowNodeModel Node { get; }

    /// <summary>获取当前节点执行的 Token、Scope 和运行身份。</summary>
    WorkflowExecutionIdentity ExecutionIdentity { get; }

    /// <summary>获取当前节点在本次运行中的执行次数，从 1 开始。</summary>
    int NodeExecutionCount { get; }

    /// <summary>获取兼容旧节点和受信任脚本使用的宿主服务容器。</summary>
    IServiceProvider Services { get; }

    /// <summary>获取已经在运行准备阶段预检过的必需宿主能力。</summary>
    /// <typeparam name="TCapability">节点要求的强类型运行能力。</typeparam>
    /// <returns>当前宿主注册的能力实例。</returns>
    /// <exception cref="InvalidOperationException">宿主没有注册要求的能力。</exception>
    TCapability GetRequiredCapability<TCapability>() where TCapability : class;

    /// <summary>尝试读取指定类型的流程作用域局部变量。</summary>
    /// <typeparam name="T">调用者期望的变量类型；存储值类型不兼容时按未找到处理。</typeparam>
    /// <param name="key">流程局部变量稳定键。</param>
    /// <param name="value">找到且类型兼容时返回变量值，否则返回默认值。</param>
    /// <returns>变量存在且能够直接转换为 <typeparamref name="T"/> 时返回 <see langword="true"/>。</returns>
    bool TryGetVariable<T>(string key, out T? value);

    /// <summary>暂存非空流程局部变量，同键值会被覆盖。</summary>
    /// <param name="key">流程局部变量稳定键。</param>
    /// <param name="value">要保存的非空值；删除变量应调用 <see cref="RemoveVariable"/>。</param>
    void SetVariable(string key, object value);

    /// <summary>删除流程局部变量。</summary>
    /// <param name="key">要删除的流程局部变量键。</param>
    /// <returns>删除了现有变量时返回 <see langword="true"/>。</returns>
    bool RemoveVariable(string key);

    /// <summary>尝试读取独立公共数据仓中的指定值。</summary>
    /// <typeparam name="T">要求的运行时类型。</typeparam>
    /// <param name="key">公共数据稳定键。</param>
    /// <param name="value">找到且类型兼容时返回公开值。</param>
    /// <returns>键存在且值类型兼容时返回 <see langword="true"/>。</returns>
    bool TryGetPublicData<T>(string key, out T? value);

    /// <summary>暂存一个公共数据写入；仅在节点成功后提交。</summary>
    /// <param name="key">公共数据稳定键。</param>
    /// <param name="value">待公开的非空值。</param>
    void PublishData(string key, object value);

    /// <summary>暂存删除公共数据；仅在节点成功后提交。</summary>
    /// <param name="key">公共数据稳定键。</param>
    /// <returns>当前执行视图中该键存在时返回 <see langword="true"/>。</returns>
    bool RemovePublicData(string key);

    /// <summary>解析固定值、公共数据或当前执行身份可见的节点输出绑定。</summary>
    /// <typeparam name="T">节点属性要求的输入类型。</typeparam>
    /// <param name="input">包含固定值或绑定来源的输入配置。</param>
    /// <returns>解析及类型转换后的值；可空输入允许返回 <see langword="null"/>。</returns>
    T? ResolveInput<T>(WorkflowInput<T> input);

    /// <summary>解析带稳定输入名称的绑定，并记录可查询的数据血缘事件。</summary>
    /// <typeparam name="T">节点属性要求的输入类型。</typeparam>
    /// <param name="inputName">节点作者可见的稳定输入名称；用于关联来源身份和诊断。</param>
    /// <param name="input">包含固定值或绑定来源的输入配置。</param>
    /// <returns>解析及类型转换后的值；可空输入允许返回 <see langword="null"/>。</returns>
    /// <remarks>内置节点应优先使用命名重载，以便外部分析确认消费者实际读取了哪一次来源输出。</remarks>
    T? ResolveInput<T>(string inputName, WorkflowInput<T> input);

    /// <summary>触发当前工作流上下文中的一个幂等信号。</summary>
    /// <param name="signalKey">信号稳定键；同一运行中重复触发不会重复累积。</param>
    void RaiseWorkflowSignal(string signalKey);

    /// <summary>判断当前工作流上下文信号是否已触发。</summary>
    /// <param name="signalKey">要检查的信号稳定键。</param>
    /// <returns>该信号已在当前上下文触发时返回 <see langword="true"/>。</returns>
    bool ContainsWorkflowSignal(string signalKey);

    /// <summary>异步等待当前工作流上下文中的全部指定信号。</summary>
    /// <param name="signalKeys">需要全部触发的信号键；空集合立即成功。</param>
    /// <param name="timeout">最长等待时间；为空表示只受取消令牌限制。</param>
    /// <param name="cancellationToken">由工作流取消或宿主停止操作触发的取消令牌。</param>
    /// <returns>全部信号到达时返回 <see langword="true"/>；仅因配置超时到期时返回 <see langword="false"/>。</returns>
    ValueTask<bool> WaitAllWorkflowSignalsAsync(IReadOnlyList<string> signalKeys, TimeSpan? timeout, CancellationToken cancellationToken);

    /// <summary>写入与当前节点和执行身份关联的结构化跟踪记录。</summary>
    /// <param name="step">稳定的跟踪步骤键，供监视器筛选和聚合。</param>
    /// <param name="message">面向使用者的可选说明。</param>
    /// <param name="data">可选结构化数据；调用方不应在提交后修改其中内容。</param>
    void Trace(string step, string? message = null, IReadOnlyDictionary<string, object?>? data = null);

    /// <summary>按指定推送优先级写入与当前节点和执行身份关联的结构化跟踪记录。</summary>
    /// <param name="step">稳定的跟踪步骤键，供监视器筛选和聚合。</param>
    /// <param name="message">面向使用者的可选说明。</param>
    /// <param name="data">可选结构化数据；调用方不应在提交后修改其中内容。</param>
    /// <param name="writeMode">推送优先级；Durable 只请求立即调度，不阻塞节点执行。</param>
    void Trace(
        string step,
        string? message,
        IReadOnlyDictionary<string, object?>? data,
        WorkflowEventWriteMode writeMode);
}

/// <summary>
/// 执行一种或多种节点模型的运行处理器。
/// </summary>
public interface IWorkflowNodeHandler
{
    /// <summary>判断当前处理器是否可以执行指定节点模型。</summary>
    /// <param name="node">待匹配的节点实例。</param>
    /// <returns>当前处理器支持该模型类型时返回 <see langword="true"/>。</returns>
    bool CanHandle(IWorkflowNodeModel node);

    /// <summary>异步执行节点，并以端口选择或路径完成结果描述控制流。</summary>
    /// <param name="node">本次执行的节点配置实例。</param>
    /// <param name="context">当前运行、Token、Scope、变量、绑定和宿主服务的访问接口。</param>
    /// <param name="cancellationToken">工作流取消、并行分支失败或宿主停止时触发的令牌。</param>
    /// <returns>节点的成功/失败状态、所选出口端口及可选标准输出。</returns>
    ValueTask<NodeExecutionResult> ExecuteAsync(
        IWorkflowNodeModel node,
        IWorkflowNodeExecutionContext context,
        CancellationToken cancellationToken);
}

/// <summary>
/// 为强类型节点处理器提供模型匹配和安全转换。
/// </summary>
/// <typeparam name="TNode">当前处理器支持的节点模型类型。</typeparam>
public abstract class WorkflowNodeHandler<TNode> : IWorkflowNodeHandler where TNode : class, IWorkflowNodeModel
{
    /// <inheritdoc />
    public bool CanHandle(IWorkflowNodeModel node) => node is TNode;

    /// <inheritdoc />
    /// <exception cref="ArgumentException"><paramref name="node"/> 不是 <typeparamref name="TNode"/> 实例。</exception>
    public async ValueTask<NodeExecutionResult> ExecuteAsync(
        IWorkflowNodeModel node,
        IWorkflowNodeExecutionContext context,
        CancellationToken cancellationToken)
    {
        if (node is not TNode typedNode)
            throw new ArgumentException($"处理器不支持节点类型 {node.GetType().FullName}。", nameof(node));

        return await ExecuteAsync(typedNode, context, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>执行已经完成类型转换的节点模型。</summary>
    /// <param name="node">强类型节点配置实例。</param>
    /// <param name="context">当前节点执行上下文。</param>
    /// <param name="cancellationToken">工作流及并行作用域传播的取消令牌。</param>
    /// <returns>控制后继端口、路径结束或故障处理的执行结果。</returns>
    protected abstract ValueTask<NodeExecutionResult> ExecuteAsync(
        TNode node,
        IWorkflowNodeExecutionContext context,
        CancellationToken cancellationToken);
}
