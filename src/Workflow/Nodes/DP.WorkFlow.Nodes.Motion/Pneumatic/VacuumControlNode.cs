namespace DP.WorkFlow;

/// <summary>控制真空打开、关闭和破真空时序。</summary>
[WorkflowRecovery(WorkflowRecoveryCategory.VerifyThenContinue)]
[WorkflowNode("VacuumControl", DisplayName = "真空控制", Category = "4.Motion/Pneumatic", OutputType = typeof(PneumaticNodeResult), Icon = "vacuum")]
public sealed class VacuumControlNodeModel : WorkflowNodeModel, IRecoverableWorkflowNode, IWorkflowNodeEnvironmentValidator
{
    /// <inheritdoc />
    public IReadOnlyList<string> ValidateEnvironment(IServiceProvider services) =>
        WorkflowMotionEnvironment.Check(services, catalog => new[] { WorkflowMotionEnvironment.Key(catalog, WorkflowMotionObjectKind.Vacuum, VacuumName, "真空") });
    /// <inheritdoc />
    public RecoverableInterruptOption Interrupt { get; set; } = new();
    /// <inheritdoc />
    public override string NodeType => "VacuumControl";
    /// <summary>获取或设置真空注册名称。</summary>
    [WorkflowPropertyEditor(WorkflowMotionEditorKeys.Vacuum)]
    public string VacuumName { get; set; } = string.Empty;
    /// <summary>获取或设置命令。</summary>
    public WorkflowVacuumCommand Command { get; set; } = WorkflowVacuumCommand.VacuumOn;
    /// <summary>获取或设置破真空时长毫秒数。</summary>
    public int BlowOffDurationMs { get; set; } = 200;
    /// <summary>获取或设置 VacuumOn 后是否等待真空到位。</summary>
    public bool WaitForVacuumOk { get; set; } = true;
    /// <summary>获取或设置超时毫秒数。</summary>
    public int TimeoutMs { get; set; } = 3000;
}

/// <summary>
/// 执行真空控制节点。命令可能已经发出，故障后先核实：
/// 打开真空补发并等真空到位；关闭 / 破真空先看真空是否已经释放，没有释放再补发。
/// 撤回动作只对"打开真空"提供（关闭真空）；其余撤回交人确认。
/// </summary>
public sealed class VacuumControlNodeHandler : WorkflowNodeHandler<VacuumControlNodeModel>,
    IWorkflowContinuableNodeHandler, IWorkflowUndoableNodeHandler
{
    private const int VerifyPollMs = 20;

    /// <inheritdoc />
    protected override IEnumerable<WorkflowRuntimeCapabilityRequirement> GetRequiredCapabilities(VacuumControlNodeModel node) =>
        new[] { MotionCapabilities.PneumaticCapability };

    /// <inheritdoc />
    protected override async ValueTask<NodeExecutionResult> ExecuteAsync(VacuumControlNodeModel node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
    {
        if (node.BlowOffDurationMs < 0) throw new InvalidOperationException("BlowOffDurationMs 不能小于 0。");
        context.ReportProgress($"已发出真空命令 {node.Command}");
        var output = await CylinderControlNodeHandler.GetService(context, node.VacuumName).ControlVacuumAsync(node.VacuumName.Trim(), node.Command, node.BlowOffDurationMs, node.WaitForVacuumOk, node.TimeoutMs, cancellationToken).ConfigureAwait(false);
        if (!output.Success) return WorkflowRecoverableNodeFailure.Create(node, output.Message ?? $"真空 '{node.VacuumName}' 控制失败。");
        return NodeExecutionResult.Continue(output: output);
    }

    /// <inheritdoc />
    public async ValueTask<NodeExecutionResult> VerifyAndCompleteAsync(IWorkflowNodeModel node, IWorkflowNodeExecutionContext context,
        WorkflowNodeInterruption interruption, CancellationToken cancellationToken)
    {
        var model = (VacuumControlNodeModel)node;
        var service = CylinderControlNodeHandler.GetService(context, model.VacuumName);
        var name = model.VacuumName.Trim();
        PneumaticNodeResult output;
        if (model.Command == WorkflowVacuumCommand.VacuumOn)
        {
            // 打开真空是状态命令：补发并等真空到位。
            output = await service.ControlVacuumAsync(name, model.Command, model.BlowOffDurationMs, true, model.TimeoutMs, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            // 关闭 / 破真空：真空已经释放就算做完；没有释放时，状态命令补发，单次破真空脉冲不补发。
            output = await service.WaitVacuumAsync(name, WorkflowVacuumTargetState.VacuumNotOk, model.TimeoutMs, VerifyPollMs, cancellationToken).ConfigureAwait(false);
            if (!output.Success && model.Command != WorkflowVacuumCommand.BlowOffPulse)
                output = await service.ControlVacuumAsync(name, model.Command, model.BlowOffDurationMs, false, model.TimeoutMs, cancellationToken).ConfigureAwait(false);
        }
        return output.Success
            ? NodeExecutionResult.Continue(output: output)
            : WorkflowRecoverableNodeFailure.Create(model, output.Message ?? $"真空 '{name}' 核实未完成。");
    }

    /// <inheritdoc />
    public async ValueTask UndoAsync(IWorkflowNodeModel node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
    {
        var model = (VacuumControlNodeModel)node;
        if (model.Command != WorkflowVacuumCommand.VacuumOn)
            throw new InvalidOperationException($"真空 '{model.VacuumName}' 的 {model.Command} 没有可自动撤回的动作，请人工确认。");
        var output = await CylinderControlNodeHandler.GetService(context, model.VacuumName)
            .ControlVacuumAsync(model.VacuumName.Trim(), WorkflowVacuumCommand.VacuumOff, model.BlowOffDurationMs, false, model.TimeoutMs, cancellationToken).ConfigureAwait(false);
        if (!output.Success)
            throw new InvalidOperationException(output.Message ?? $"真空 '{model.VacuumName}' 撤回（关闭真空）失败。");
    }
}
