namespace DP.WorkFlow;

/// <summary>可恢复中断配置。</summary>
[System.Text.Json.Serialization.JsonConverter(typeof(RecoverableInterruptOptionJsonConverter))]
public sealed class RecoverableInterruptOption
{
    /// <summary>可选报警编号；普通故障无需编号也可进入所属运行的处理策略。</summary>
    public int AlarmCode { get; set; }
    /// <summary>返回是否已配置有效报警编号。</summary>
    public bool IsConfigured => AlarmCode > 0;
    /// <inheritdoc />
    public override string ToString() => IsConfigured ? AlarmCode.ToString(System.Globalization.CultureInfo.InvariantCulture) : "(未配置)";
}

/// <summary>由支持可恢复故障路由的节点模型实现。</summary>
public interface IRecoverableWorkflowNode
{
    /// <summary>获取可恢复中断配置。</summary>
    RecoverableInterruptOption Interrupt { get; }
}

/// <summary>创建显式节点故障结果，避免用异常表达预期中的设备失败或等待超时。</summary>
public static class WorkflowRecoverableNodeFailure
{
    /// <summary>按节点中断配置创建故障结果；未配置报警时仍可交给作用域处理策略。</summary>
    /// <param name="node">提供报警编码和恢复配置的节点模型。</param>
    /// <param name="message">设备失败、等待超时或节点未成功完成执行的说明。</param>
    /// <returns>带可选报警编码的故障结果；无协调器时终止运行。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="node"/> 为空。</exception>
    public static NodeExecutionResult Create(IRecoverableWorkflowNode node, string message)
    {
        ArgumentNullException.ThrowIfNull(node);
        var interrupt = node.Interrupt;
        return NodeExecutionResult.Fail(
            message,
            interrupt?.IsConfigured == true
                ? WorkflowFaultDisposition.RequestRecovery
                : WorkflowFaultDisposition.HandleAtScope,
            interrupt?.AlarmCode ?? 0);
    }
}
