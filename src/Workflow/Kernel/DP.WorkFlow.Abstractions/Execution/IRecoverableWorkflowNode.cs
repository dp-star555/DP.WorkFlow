namespace DP.WorkFlow;

/// <summary>节点的报警配置：故障时带上的报警编号，故障会话据此选处理流程。</summary>
public sealed class RecoverableInterruptOption
{
    /// <summary>可选报警编号；没有编号的故障也会进入故障会话，按默认处理流程处理。</summary>
    public int AlarmCode { get; set; }
    /// <summary>返回是否已配置有效报警编号。</summary>
    public bool IsConfigured => AlarmCode > 0;
    /// <inheritdoc />
    public override string ToString() => IsConfigured ? AlarmCode.ToString(System.Globalization.CultureInfo.InvariantCulture) : "(未配置)";
}

/// <summary>由带报警配置的节点模型实现。</summary>
public interface IRecoverableWorkflowNode
{
    /// <summary>获取报警配置。</summary>
    RecoverableInterruptOption Interrupt { get; }
}

/// <summary>创建显式节点故障结果，避免用异常表达预期中的设备失败或等待超时。</summary>
public static class WorkflowRecoverableNodeFailure
{
    /// <summary>按节点的报警配置创建故障结果，交给故障会话处理。</summary>
    /// <param name="node">提供报警编号的节点模型。</param>
    /// <param name="message">设备失败、等待超时或节点未成功完成执行的说明。</param>
    /// <returns>带报警编号（未配置时为 0）的故障结果。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="node"/> 为空。</exception>
    public static NodeExecutionResult Create(IRecoverableWorkflowNode node, string message)
    {
        Guard.NotNull(node);
        return NodeExecutionResult.Fail(message, WorkflowFaultDisposition.ReportFault, node.Interrupt?.AlarmCode ?? 0);
    }
}
