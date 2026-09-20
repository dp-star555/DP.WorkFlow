namespace DP.WorkFlow;

/// <summary>处置步骤受阻策略。只处理当前步骤，不重新运行原始工艺处置图。</summary>
public interface IWorkflowTreatmentRecoveryPolicy
{
    /// <summary>在原会话内请求继续当前操作或终止；返回其他动作会被拒绝。</summary>
    /// <param name="original">触发工艺处置的原故障。</param>
    /// <param name="step">当前受阻步骤。</param>
    /// <param name="context">当前处置运行上下文。</param>
    /// <param name="cancellationToken">所属运行取消令牌。</param>
    /// <returns>受限恢复裁决，人工确认不代表设备到位。</returns>
    ValueTask<WorkflowFaultRecoveryDecision> RecoverStepAsync(WorkflowFaultRecoveryRequest original,
        WorkflowFaultRecoveryRequest step, IWorkflowFaultRecoveryContext context, CancellationToken cancellationToken);
}

/// <summary>使用现有非模态人工交互呈现原故障和当前受阻步骤，不递归创建处理流程。</summary>
public sealed class WorkflowOperatorTreatmentRecoveryPolicy : IWorkflowTreatmentRecoveryPolicy
{
    /// <inheritdoc />
    public async ValueTask<WorkflowFaultRecoveryDecision> RecoverStepAsync(WorkflowFaultRecoveryRequest original,
        WorkflowFaultRecoveryRequest step, IWorkflowFaultRecoveryContext context, CancellationToken cancellationToken)
    {
        if (!step.CanContinueOperation || step.OperationId is null || step.CommitStarted)
            return WorkflowFaultRecoveryDecision.Stop("处置步骤没有可继续的操作，或已进入提交/清理阶段；禁止自动重放。");
        if (context.Services.GetService(typeof(IWorkflowOperatorService)) is not IWorkflowOperatorService interaction)
            return WorkflowFaultRecoveryDecision.Stop("处置步骤受阻，缺少人工交互能力。");
        var selection = await interaction.AskChoiceAsync("异常处置受阻：" + step.FaultNodeTitle,
            $"原故障：{original.Message}\n当前步骤：{step.FaultNodeTitle}\n当前问题：{step.Message}\n" +
            (step.RecoveryFailure is null ? "" : $"上次核实被拒绝：{step.RecoveryFailure}\n") +
            "排除问题后请求核实原操作。此操作不重放前序处置，也不直接批准生产恢复。",
            new[] { new OperatorChoiceOption("Continue", "核实并继续当前操作"), new OperatorChoiceOption("Stop", "终止处置") },
            cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return string.Equals(selection, "Continue", StringComparison.OrdinalIgnoreCase)
            ? WorkflowFaultRecoveryDecision.Continue("人员请求核实并继续当前处置操作。")
            : WorkflowFaultRecoveryDecision.Stop("处置未完成，禁止恢复生产。");
    }
}
