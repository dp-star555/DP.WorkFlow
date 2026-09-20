namespace DP.WorkFlow;

/// <summary>
/// 一次逻辑操作。重复调用是完成原意图，不是重新解释输入或盲目重发命令。
/// 实现负责保存输入、进度及必要租约，核实外部完成状态；不得保存上一次尝试的执行上下文。
/// 成功输出须拥有独立生命周期：引擎在正式提交输出前释放操作自身资源。
/// </summary>
public interface IWorkflowNodeOperation : IAsyncDisposable
{
    /// <summary>是否允许核实并继续此操作；不能可靠核实的命令应返回false。</summary>
    bool CanContinue => true;

    /// <summary>尝试完成原操作；只有成功结果才提交本次上下文的暂存输出。</summary>
    /// <param name="context">本次尝试的新上下文；意图输入应在创建操作时固定。</param>
    /// <param name="cancellationToken">本次执行取消令牌；取消不证明设备已停止。</param>
    /// <returns>成功或故障结果；异常由引擎捕获。</returns>
    ValueTask<NodeExecutionResult> ExecuteAsync(IWorkflowNodeExecutionContext context, CancellationToken cancellationToken);
}

/// <summary>Handler的可选操作工厂能力。每次正常进入创建新操作，继续故障操作时复用原实例。</summary>
public interface IWorkflowNodeOperationFactory
{
    /// <summary>固定本次操作意图。创建阶段不得发送产生外部副作用的业务命令。</summary>
    /// <param name="operationId">引擎分配的逻辑操作身份；需要业务去重时仍应结合领域业务键。</param>
    /// <param name="node">本次节点配置。</param>
    /// <param name="context">用于解析输入与获取必要能力的上下文；不得保留该上下文。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>该次逻辑操作独占的非空实例；引擎负责释放。</returns>
    ValueTask<IWorkflowNodeOperation> CreateOperationAsync(
        Guid operationId, IWorkflowNodeModel node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken);
}
