namespace DP.WorkFlow;

/// <summary>节点停下的原因。</summary>
public enum WorkflowStopReason
{
    /// <summary>节点报告了故障。</summary>
    Fault = 0,

    /// <summary>节点正在执行时被中断（急停、外部故障）。</summary>
    Interrupted = 1,

    /// <summary>流程在两个节点之间被要求停下，这个节点还没有开始。</summary>
    Requested = 2
}

/// <summary>节点停下时的情况，交给节点的「就地恢复」使用。</summary>
/// <param name="Reason">停下的原因。</param>
/// <param name="Message">故障或中断说明。</param>
/// <param name="Progress">节点停下前报告的最后一步（例如"命令已发、未到位"、"已破真空"）；没有报告时为空。</param>
public sealed record WorkflowNodeInterruption(WorkflowStopReason Reason, string? Message, string? Progress);

/// <summary>
/// 节点的第一种恢复能力「就地恢复」（可选）：出事后由节点自己在原地解决，用于"当前节点自己能解决"的情况。
/// 节点自己看现场：已经做完就按成功往下走；没做完就补做或重做；解决不了就返回故障，回到处理流程。
/// 是否完成只由节点判断，框架不提供人工确认完成（PENG 2026-11-22）。没有实现时不能选"就地恢复"。
/// </summary>
/// <remarks>
/// 节点只定义两种恢复能力：就地恢复（本接口）和回退（<see cref="IWorkflowUndoableNodeHandler"/>）；
/// 出事后走哪一条由处理流程（异常处理界面）选择（PENG 2026-11-25，取代原来的恢复类别）。
/// 没有物理后果的节点（读 IO、拍照、计算）继承 <see cref="WorkflowSideEffectFreeNodeHandler{TNode}"/> 即同时具备两种能力。
/// </remarks>
public interface IWorkflowInPlaceRecoveryNodeHandler
{
    /// <summary>就地恢复：核实现场，补做或重做原操作。</summary>
    /// <param name="node">节点配置。</param>
    /// <param name="context">本次恢复的执行上下文；输入已重新取齐。</param>
    /// <param name="interruption">节点停下时的情况（原因、说明、做到哪一步）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>成功结果（出口、输出）表示原操作已完成；故障结果表示就地解决不了。</returns>
    ValueTask<NodeExecutionResult> RecoverInPlaceAsync(
        IWorkflowNodeModel node,
        IWorkflowNodeExecutionContext context,
        WorkflowNodeInterruption interruption,
        CancellationToken cancellationToken);
}

/// <summary>
/// 节点的第二种恢复能力「回退」（可选）：把该节点上一次执行做过的动作反着做（例如下降→抬起、夹紧→松爪）。
/// 处理流程选"回退到入口再继续"时，按全部流程真实执行的倒序调用。
/// 没有实现的节点，回退到它时停下交人确认处理好。
/// </summary>
public interface IWorkflowUndoableNodeHandler
{
    /// <summary>回退该节点上一次执行做过的动作。</summary>
    /// <param name="node">节点配置。</param>
    /// <param name="context">回退用的执行上下文。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    ValueTask UndoAsync(IWorkflowNodeModel node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken);
}

/// <summary>
/// 没有物理后果的节点（读 IO、等信号、拍照、判断、计算、绝对定位这类再做一次结果相同的节点）的处理器基类：
/// 就地恢复 = 再执行一次；回退 = 不用做任何事。
/// </summary>
/// <typeparam name="TNode">节点模型类型。</typeparam>
public abstract class WorkflowSideEffectFreeNodeHandler<TNode> : WorkflowNodeHandler<TNode>,
    IWorkflowInPlaceRecoveryNodeHandler, IWorkflowUndoableNodeHandler
    where TNode : class, IWorkflowNodeModel
{
    /// <inheritdoc />
    public ValueTask<NodeExecutionResult> RecoverInPlaceAsync(
        IWorkflowNodeModel node,
        IWorkflowNodeExecutionContext context,
        WorkflowNodeInterruption interruption,
        CancellationToken cancellationToken) =>
        ExecuteAsync(node, context, cancellationToken);

    /// <inheritdoc />
    public ValueTask UndoAsync(IWorkflowNodeModel node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken) =>
        default;
}

/// <summary>接收节点进度报告的执行上下文。</summary>
public interface IWorkflowNodeProgressSink
{
    /// <summary>报告节点当前做到哪一步；故障或中断时随停点交给处理流程和「就地恢复」。</summary>
    /// <param name="step">面向使用者的一步说明。</param>
    void ReportProgress(string step);
}

/// <summary>节点进度报告的入口。</summary>
public static class WorkflowNodeProgressExtensions
{
    /// <summary>报告节点当前做到哪一步（例如"命令已发"、"已破真空"）；上下文不支持时忽略。</summary>
    /// <param name="context">节点执行上下文。</param>
    /// <param name="step">一步说明。</param>
    public static void ReportProgress(this IWorkflowNodeExecutionContext context, string step)
    {
        Guard.NotNull(context);
        if (!string.IsNullOrWhiteSpace(step))
            (context as IWorkflowNodeProgressSink)?.ReportProgress(step.Trim());
    }
}

/// <summary>提供恢复轮次号的执行上下文。</summary>
public interface IWorkflowRecoveryRoundSource
{
    /// <summary>本流程的恢复轮次号：每次故障会话放行时加一；不在流程管理里运行时为 0。</summary>
    int RecoveryRound { get; }
}

/// <summary>恢复轮次号的入口。</summary>
public static class WorkflowRecoveryRoundExtensions
{
    /// <summary>
    /// 本流程当前的恢复轮次号。跨流程交接（握手）时带上它，对方就能认出恢复前留下的旧握手并丢掉。
    /// 上下文不支持时为 0。
    /// </summary>
    /// <param name="context">节点执行上下文。</param>
    public static int GetRecoveryRound(this IWorkflowNodeExecutionContext context)
    {
        Guard.NotNull(context);
        return (context as IWorkflowRecoveryRoundSource)?.RecoveryRound ?? 0;
    }
}

/// <summary>由工艺设计者标出的恢复入口（锚点）：处理流程"回退到入口 X 再继续"只能指向这些节点。</summary>
public interface IWorkflowRecoveryEntryNode
{
    /// <summary>当前文档内唯一、非空的恢复入口键。</summary>
    string RecoveryEntryKey { get; }
}

/// <summary>流程走过时可以丢弃之前合并执行记录的检查点节点（通常是交接起点）。</summary>
public interface IWorkflowCheckpointNode
{
}
