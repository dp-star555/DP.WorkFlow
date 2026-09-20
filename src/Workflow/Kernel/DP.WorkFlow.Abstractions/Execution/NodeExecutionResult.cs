namespace DP.WorkFlow;

/// <summary>指定节点故障进入恢复协调还是直接终止本次运行。</summary>
public enum WorkflowFaultDisposition
{
    /// <summary>记录故障事实并终止本次运行。</summary>
    StopRun = 0,

    /// <summary>记录故障事实后请求宿主恢复协调器裁决。</summary>
    RequestRecovery = 1,

    /// <summary>普通节点故障交由所属运行的处理策略；无协调器时终止，不要求报警编码。</summary>
    HandleAtScope = 2
}

/// <summary>指定一次节点执行互斥的终止形态。</summary>
public enum NodeExecutionOutcome
{
    /// <summary>成功完成并沿一个控制出口继续。</summary>
    Continue,

    /// <summary>成功完成并正常结束当前控制路径。</summary>
    CompletePath,

    /// <summary>未能完成节点契约，进入独立故障处理。</summary>
    Fault
}

/// <summary>
/// 描述一次节点执行的互斥结果。节点只选择出口端口，不直接返回目标节点 ID。
/// </summary>
public sealed class NodeExecutionResult
{
    private NodeExecutionResult()
    {
    }

    /// <summary>获取本次执行互斥的终止形态。</summary>
    public NodeExecutionOutcome Outcome { get; private init; }

    /// <summary>获取当前执行是否成功完成节点契约。</summary>
    public bool Success => Outcome is not NodeExecutionOutcome.Fault;

    /// <summary>获取是否正常结束当前执行路径。</summary>
    public bool CompleteCurrentPath => Outcome == NodeExecutionOutcome.CompletePath;

    /// <summary>获取执行后选择的出口端口。</summary>
    public string? SelectedPortKey { get; private init; }

    /// <summary>获取节点产生的标准输出。</summary>
    public object? Output { get; private init; }

    /// <summary>获取失败消息。</summary>
    public string? Message { get; private init; }

    /// <summary>获取节点失败后的处理策略。</summary>
    public WorkflowFaultDisposition FaultDisposition { get; private init; }

    /// <summary>获取触发可恢复中断的报警编码；未配置时为 0。</summary>
    public int InterruptAlarmCode { get; private init; }

    /// <summary>创建沿指定输出端口继续调度的成功结果。</summary>
    /// <param name="portKey">当前节点选择的稳定输出端口键；运行时据此从编译定义查找后继节点。</param>
    /// <param name="output">节点产生的可选标准输出；运行时会按 Run、Token、Scope 和执行次数记录。</param>
    /// <returns>不会结束当前路径的成功结果。</returns>
    /// <exception cref="ArgumentException"><paramref name="portKey"/> 为空或仅包含空白字符。</exception>
    public static NodeExecutionResult Continue(string portKey = WorkflowPorts.Success, object? output = null)
    {
        if (string.IsNullOrWhiteSpace(portKey))
            throw new ArgumentException("出口端口不能为空。", nameof(portKey));

        return new NodeExecutionResult
        {
            Outcome = NodeExecutionOutcome.Continue,
            SelectedPortKey = portKey.Trim(),
            Output = output
        };
    }

    /// <summary>创建正常结束当前执行路径的成功结果。</summary>
    /// <param name="output">路径结束前由当前节点产生的可选标准输出。</param>
    /// <returns>将 <see cref="CompleteCurrentPath"/> 标记为 <see langword="true"/> 的成功结果。</returns>
    public static NodeExecutionResult Complete(object? output = null) => new()
    {
        Outcome = NodeExecutionOutcome.CompletePath,
        Output = output
    };

    /// <summary>创建节点执行失败结果，并声明运行时应采用的故障处理策略。</summary>
    /// <param name="message">面向运行监视和恢复协调器的失败说明；空白文本会替换为默认消息。</param>
    /// <param name="faultDisposition">终止本次运行或请求恢复协调的裁决入口。</param>
    /// <param name="interruptAlarmCode">可恢复中断使用的报警编码；小于等于零表示未配置。</param>
    /// <returns>不包含后继端口的失败结果。</returns>
    public static NodeExecutionResult Fail(
        string message,
        WorkflowFaultDisposition faultDisposition = WorkflowFaultDisposition.HandleAtScope,
        int interruptAlarmCode = 0) => new()
    {
        Outcome = NodeExecutionOutcome.Fault,
        Message = string.IsNullOrWhiteSpace(message) ? "节点执行失败。" : message,
        FaultDisposition = faultDisposition,
        InterruptAlarmCode = interruptAlarmCode > 0 ? interruptAlarmCode : 0
    };
}
