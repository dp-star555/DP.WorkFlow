namespace DP.WorkFlow;

/// <summary>
/// 循环退出（R3-c）的三种条件中，"退出循环节点"和"外部 bool 信号"的共用做法；循环节点与循环块都使用。
/// </summary>
/// <remarks>
/// 退出循环节点把请求记在本画布的流程数据中（保留键，不对使用者开放）：
/// 指定了循环节点时记给该循环节点，未指定时记给所在子画布的循环块。
/// 外部信号是公共数据中的 bool 信号，每圈结束时检查，为真则跑完当前这一圈后退出。
/// </remarks>
public static class WorkflowLoopExit
{
    /// <summary>循环块子画布内退出请求的保留键。</summary>
    public const string BlockRequestKey = "$LoopExit";

    /// <summary>画布内某个循环节点的退出请求保留键。</summary>
    /// <param name="loopNodeId">循环节点 ID。</param>
    public static string RequestKey(string loopNodeId) => BlockRequestKey + ":" + loopNodeId;

    /// <summary>记下退出请求。</summary>
    /// <param name="context">退出循环节点的执行上下文。</param>
    /// <param name="loopNodeId">要退出的循环节点；为空时退出所在子画布的循环块。</param>
    public static void Request(IWorkflowNodeExecutionContext context, string? loopNodeId)
    {
        Guard.NotNull(context);
        context.SetVariable(string.IsNullOrWhiteSpace(loopNodeId) ? BlockRequestKey : RequestKey(loopNodeId!.Trim()), true);
    }

    /// <summary>读取并清除循环节点的退出请求。</summary>
    /// <param name="context">循环节点的执行上下文。</param>
    /// <param name="loopNodeId">循环节点 ID。</param>
    /// <returns>有退出请求时返回 true。</returns>
    public static bool Consume(IWorkflowNodeExecutionContext context, string loopNodeId)
    {
        Guard.NotNull(context);
        var key = RequestKey(loopNodeId);
        if (!context.TryGetVariable<bool>(key, out var requested))
            return false;
        context.RemoveVariable(key);
        return requested;
    }

    /// <summary>检查外部退出信号；为真且要求复位时把信号置回 false。</summary>
    /// <param name="context">循环节点的执行上下文。</param>
    /// <param name="signalKey">公共数据 bool 信号键；为空时不检查。</param>
    /// <param name="reset">退出后是否把信号复位为 false。</param>
    /// <returns>信号为真时返回 true。</returns>
    public static bool CheckSignal(IWorkflowNodeExecutionContext context, string? signalKey, bool reset)
    {
        Guard.NotNull(context);
        if (string.IsNullOrWhiteSpace(signalKey))
            return false;
        var key = signalKey!.Trim();
        var signals = context.GetRequiredCapability<IWorkflowSignalService>();
        if (!signals.Read(key))
            return false;
        if (reset)
            signals.Write(key, false);
        return true;
    }
}
