namespace DP.WorkFlow;

/// <summary>轴步骤键来源，沿用旧版配置语义。</summary>
public enum E_AxisStepValueSource
{
    /// <summary>使用节点中配置的 StepKey。</summary>
    Literal = 0,
    /// <summary>使用绑定解析出的 StepKey。</summary>
    Binding = 1
}

/// <summary>按 AxisStep 仓库中的 StepKey 执行完整轴步骤。</summary>
[WorkflowNode("AxisAction", DisplayName = "轴动作", Category = "4.Motion/Axis")]
public sealed class AxisActionNodeModel : WorkflowNodeModel, IRecoverableWorkflowNode
{
    /// <inheritdoc />
    public override string NodeType => "AxisAction";

    /// <inheritdoc />
    [WorkflowProperty("异常处理", "轴动作失败时使用的报警编码和可恢复中断策略。", Category = "异常处理")]
    public RecoverableInterruptOption Interrupt { get; set; } = new();

    /// <summary>获取或设置 StepKey 来源。</summary>
    [WorkflowProperty("步骤键来源", "指定轴步骤键使用节点固定值还是流程绑定值。", Category = "运动参数")]
    public E_AxisStepValueSource StepKeySource { get; set; }

    /// <summary>获取或设置固定 StepKey。</summary>
    [WorkflowProperty("步骤键", "轴步骤仓库中需要执行的固定步骤键。", Category = "运动参数")]
    public string StepKey { get; set; } = string.Empty;

    /// <summary>获取或设置绑定形式的 StepKey。</summary>
    [WorkflowProperty("步骤键绑定", "从流程变量或上游节点输出读取轴步骤键。", Category = "数据来源")]
    public WorkflowInput<string> StepKeyBinding { get; set; } = WorkflowInput<string>.FromLiteral(string.Empty);

    /// <summary>获取或设置是否等待步骤全部完成。</summary>
    [WorkflowProperty("等待完成", "是否等待轴步骤全部执行完成后再继续流程。", Category = "等待与超时")]
    public bool WaitForCompleted { get; set; }

    /// <summary>获取或设置覆盖步骤默认超时的毫秒数；0 表示使用步骤默认值。</summary>
    [WorkflowProperty("覆盖超时时间", "覆盖轴步骤自身配置的超时时间；零值表示使用步骤默认值。", Category = "等待与超时", Unit = "ms")]
    public int OverrideTimeoutMs { get; set; }

    /// <summary>获取或设置兼容旧流程变量输出的结果键。</summary>
    [WorkflowProperty("结果变量键", "轴动作执行结果写入流程变量时使用的键。", Category = "输出结果")]
    public string ResultVarKey { get; set; } = "AxisActionResult";
}

/// <summary>执行轴步骤节点。</summary>
public sealed class AxisActionNodeHandler : WorkflowNodeHandler<AxisActionNodeModel>
{
    /// <inheritdoc />
    protected override async ValueTask<NodeExecutionResult> ExecuteAsync(
        AxisActionNodeModel node,
        IWorkflowNodeExecutionContext context,
        CancellationToken cancellationToken)
    {
        if (node.OverrideTimeoutMs < 0)
            throw new InvalidOperationException("OverrideTimeoutMs 不能小于 0。");
        var stepKey = node.StepKeySource == E_AxisStepValueSource.Binding
            ? context.ResolveInput(node.StepKeyBinding)
            : node.StepKey;
        if (string.IsNullOrWhiteSpace(stepKey))
            throw new InvalidOperationException("AxisAction.StepKey 为空。");
        var service = AxisServoNodeHandler.GetService(context);
        var output = await service.ExecuteStepAsync(
            stepKey.Trim(), node.WaitForCompleted, node.OverrideTimeoutMs, cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(node.ResultVarKey))
            context.SetVariable(node.ResultVarKey.Trim(), output);
        context.Trace("AxisAction", "轴步骤执行完成。", new Dictionary<string, object?>
        {
            ["StepKey"] = stepKey.Trim(),
            ["WaitForCompleted"] = node.WaitForCompleted,
            ["OverrideTimeoutMs"] = node.OverrideTimeoutMs,
            ["Success"] = output.Success,
            ["Message"] = output.Message
        });
        if (!output.Success)
            return WorkflowRecoverableNodeFailure.Create(node, output.Message ?? "Axis action failed.");
        return NodeExecutionResult.Continue(output: output);
    }
}
