namespace DP.WorkFlow;

/// <summary>
/// 节点的恢复类别（第 8 类）：节点出故障或被中断后，处理流程可以给它哪些去向。
/// 节点只声明类别，不决定弹不弹框、停不停机；去向由统管层的故障会话决定。
/// </summary>
public enum WorkflowRecoveryCategory
{
    /// <summary>
    /// 不可重做（默认，最保守）：例如取料、放料、记账、上报 MES、相对移动。
    /// 允许的去向：继续原操作（节点实现了核实并补完时）、回到入口、结束本圈、停止。
    /// </summary>
    NotRedoable = 0,

    /// <summary>
    /// 可直接重做：例如读 IO、等待信号、拍照、判断、绝对定位。重新执行一次不会造成额外影响。
    /// 允许的去向：重做本节点、继续原操作（实现了核实并补完时）、回到入口、结束本圈、停止。
    /// </summary>
    Redoable = 1,

    /// <summary>
    /// 需核实后继续：例如轴到位、气缸、真空、机器人移动。命令可能已经发出，要先看现场再补完。
    /// 允许的去向：继续原操作（核实并补完）、回到入口、结束本圈、停止。
    /// </summary>
    VerifyThenContinue = 2
}

/// <summary>声明节点类型的恢复类别；没有声明的节点按 <see cref="WorkflowRecoveryCategory.NotRedoable"/> 处理。</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = true, AllowMultiple = false)]
public sealed class WorkflowRecoveryAttribute : Attribute
{
    /// <summary>声明恢复类别。</summary>
    /// <param name="category">恢复类别。</param>
    public WorkflowRecoveryAttribute(WorkflowRecoveryCategory category) => Category = category;

    /// <summary>恢复类别。</summary>
    public WorkflowRecoveryCategory Category { get; }
}

/// <summary>查询节点的恢复类别。</summary>
public static class WorkflowRecoveryCategories
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, WorkflowRecoveryCategory> Cache = new();

    /// <summary>节点模型类型上声明的恢复类别；没有声明时为不可重做。</summary>
    /// <param name="node">节点模型。</param>
    public static WorkflowRecoveryCategory Of(IWorkflowNodeModel node)
    {
        Guard.NotNull(node);
        return Cache.GetOrAdd(node.GetType(), static type =>
            type.GetCustomAttributes(typeof(WorkflowRecoveryAttribute), inherit: true) is [WorkflowRecoveryAttribute attribute, ..]
                ? attribute.Category
                : WorkflowRecoveryCategory.NotRedoable);
    }

    /// <summary>显示用名称。</summary>
    /// <param name="category">恢复类别。</param>
    public static string DisplayName(WorkflowRecoveryCategory category) => category switch
    {
        WorkflowRecoveryCategory.Redoable => "可直接重做",
        WorkflowRecoveryCategory.VerifyThenContinue => "需核实后继续",
        _ => "不可重做"
    };
}

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

/// <summary>节点停下时的情况，交给节点的「核实并补完」使用。</summary>
/// <param name="Reason">停下的原因。</param>
/// <param name="Message">故障或中断说明。</param>
/// <param name="Progress">节点停下前报告的最后一步（例如"命令已发、未到位"、"已破真空"）；没有报告时为空。</param>
public sealed record WorkflowNodeInterruption(WorkflowStopReason Reason, string? Message, string? Progress);

/// <summary>
/// 节点处理器可选实现的「核实并补完」：故障或中断后选"继续原操作"时调用。
/// 节点自己看现场：已经做完就按成功往下走；没做完而且能补就补完；判断不了就返回故障，回到处理流程。
/// 是否完成只由节点判断，框架不提供人工确认完成（PENG 2026-11-22）。
/// </summary>
public interface IWorkflowContinuableNodeHandler
{
    /// <summary>核实现场并补完原操作。</summary>
    /// <param name="node">节点配置。</param>
    /// <param name="context">本次核实的执行上下文；输入已重新取齐。</param>
    /// <param name="interruption">节点停下时的情况。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>成功结果（出口、输出）表示原操作已完成；故障结果表示核实不通过。</returns>
    ValueTask<NodeExecutionResult> VerifyAndCompleteAsync(
        IWorkflowNodeModel node,
        IWorkflowNodeExecutionContext context,
        WorkflowNodeInterruption interruption,
        CancellationToken cancellationToken);
}

/// <summary>
/// 节点处理器可选实现的「撤回动作」：联合逆序撤回时按真实执行的倒序调用（例如下降→抬起、夹紧→松爪）。
/// 没有声明撤回动作的节点，撤回到它时停下交人确认。
/// </summary>
public interface IWorkflowUndoableNodeHandler
{
    /// <summary>撤回该节点上一次执行做过的动作。</summary>
    /// <param name="node">节点配置。</param>
    /// <param name="context">撤回用的执行上下文。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    ValueTask UndoAsync(IWorkflowNodeModel node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken);
}

/// <summary>接收节点进度报告的执行上下文。</summary>
public interface IWorkflowNodeProgressSink
{
    /// <summary>报告节点当前做到哪一步；故障或中断时随停点交给处理流程和「核实并补完」。</summary>
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

/// <summary>由工艺设计者标出的恢复入口（锚点）：处理流程的"回到入口 X"只能指向这些节点。</summary>
public interface IWorkflowRecoveryEntryNode
{
    /// <summary>当前文档内唯一、非空的恢复入口键。</summary>
    string RecoveryEntryKey { get; }
}

/// <summary>流程走过时可以丢弃之前合并执行记录的检查点节点（通常是交接起点）。</summary>
public interface IWorkflowCheckpointNode
{
}
