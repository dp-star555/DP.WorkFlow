namespace DP.WorkFlow;

/// <summary>控制气缸伸出、缩回或全部断电。</summary>
[WorkflowRecovery(WorkflowRecoveryCategory.VerifyThenContinue)]
[WorkflowNode("CylinderControl", DisplayName = "气缸控制节点", Category = "4.Motion/Pneumatic", OutputType = typeof(PneumaticNodeResult))]
public sealed class CylinderControlNodeModel : WorkflowNodeModel, IRecoverableWorkflowNode, IWorkflowNodeEnvironmentValidator
{
    /// <inheritdoc />
    public IReadOnlyList<string> ValidateEnvironment(IServiceProvider services) =>
        WorkflowMotionEnvironment.Check(services, catalog => new[] { WorkflowMotionEnvironment.Key(catalog, WorkflowMotionObjectKind.Cylinder, CylinderName, "气缸") });
    /// <inheritdoc />
    public RecoverableInterruptOption Interrupt { get; set; } = new();
    /// <inheritdoc />
    public override string NodeType => "CylinderControl";
    /// <summary>获取或设置气缸注册名称。</summary>
    [WorkflowPropertyEditor(WorkflowMotionEditorKeys.Cylinder)]
    public string CylinderName { get; set; } = string.Empty;
    /// <summary>获取或设置动作命令。</summary>
    public WorkflowCylinderCommand Command { get; set; } = WorkflowCylinderCommand.Extend;
    /// <summary>获取或设置是否等待目标状态。</summary>
    public bool WaitForTargetState { get; set; } = true;
    /// <summary>获取或设置超时毫秒数；0 表示不超时。</summary>
    public int TimeoutMs { get; set; } = 3000;
}

/// <summary>
/// 执行气缸控制节点。阀命令可能已经发出，故障后先核实：气缸阀是状态命令，再发同一命令并等到位即为补完。
/// 撤回动作为反向命令（伸出↔缩回）。
/// </summary>
public sealed class CylinderControlNodeHandler : WorkflowNodeHandler<CylinderControlNodeModel>,
    IWorkflowContinuableNodeHandler, IWorkflowUndoableNodeHandler
{
    /// <inheritdoc />
    protected override IEnumerable<WorkflowRuntimeCapabilityRequirement> GetRequiredCapabilities(CylinderControlNodeModel node) =>
        new[] { MotionCapabilities.PneumaticCapability };

    /// <inheritdoc />
    protected override async ValueTask<NodeExecutionResult> ExecuteAsync(CylinderControlNodeModel node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
    {
        var service = GetService(context, node.CylinderName);
        context.ReportProgress($"已发出气缸命令 {node.Command}");
        var output = await service.ControlCylinderAsync(node.CylinderName.Trim(), node.Command, node.WaitForTargetState, node.TimeoutMs, cancellationToken).ConfigureAwait(false);
        if (!output.Success) return WorkflowRecoverableNodeFailure.Create(node, output.Message ?? $"气缸 '{node.CylinderName}' 执行动作 '{node.Command}' 失败。");
        return NodeExecutionResult.Continue(output: output);
    }

    /// <inheritdoc />
    public async ValueTask<NodeExecutionResult> VerifyAndCompleteAsync(IWorkflowNodeModel node, IWorkflowNodeExecutionContext context,
        WorkflowNodeInterruption interruption, CancellationToken cancellationToken)
    {
        var model = (CylinderControlNodeModel)node;
        var service = GetService(context, model.CylinderName);
        var name = model.CylinderName.Trim();
        // 伸出/缩回有可核实的目标状态：补发同一阀命令并等到位；全部断电没有目标状态，补发即完成。
        var wait = model.Command is WorkflowCylinderCommand.Extend or WorkflowCylinderCommand.Retract;
        var output = await service.ControlCylinderAsync(name, model.Command, wait, model.TimeoutMs, cancellationToken).ConfigureAwait(false);
        return output.Success
            ? NodeExecutionResult.Continue(output: output)
            : WorkflowRecoverableNodeFailure.Create(model, output.Message ?? $"气缸 '{name}' 核实未到位。");
    }

    /// <inheritdoc />
    public async ValueTask UndoAsync(IWorkflowNodeModel node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
    {
        var model = (CylinderControlNodeModel)node;
        var reverse = model.Command switch
        {
            WorkflowCylinderCommand.Extend => WorkflowCylinderCommand.Retract,
            WorkflowCylinderCommand.Retract => WorkflowCylinderCommand.Extend,
            _ => throw new InvalidOperationException($"气缸 '{model.CylinderName}' 的全部断电没有可撤回的反向动作。")
        };
        var output = await GetService(context, model.CylinderName).ControlCylinderAsync(model.CylinderName.Trim(), reverse, true, model.TimeoutMs, cancellationToken).ConfigureAwait(false);
        if (!output.Success)
            throw new InvalidOperationException(output.Message ?? $"气缸 '{model.CylinderName}' 撤回到 {reverse} 失败。");
    }

    internal static IWorkflowPneumaticService GetService(IWorkflowNodeExecutionContext context, string name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("未配置执行器名称。");
        return context.GetRequiredCapability<IWorkflowPneumaticService>();
    }
}
