namespace DP.WorkFlow;

/// <summary>控制真空打开、关闭和破真空时序。</summary>
[WorkflowNode("VacuumControl", DisplayName = "真空控制", Category = "4.Motion/Pneumatic")]
public sealed class VacuumControlNodeModel : WorkflowNodeModel, IRecoverableWorkflowNode
{
    /// <inheritdoc />
    public RecoverableInterruptOption Interrupt { get; set; } = new();
    /// <inheritdoc />
    public override string NodeType => "VacuumControl";
    /// <summary>获取或设置真空注册名称。</summary>
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

/// <summary>执行真空控制节点。</summary>
public sealed class VacuumControlNodeHandler : WorkflowNodeHandler<VacuumControlNodeModel>
{
    /// <inheritdoc />
    protected override async ValueTask<NodeExecutionResult> ExecuteAsync(VacuumControlNodeModel node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
    {
        if (node.BlowOffDurationMs < 0) throw new InvalidOperationException("BlowOffDurationMs 不能小于 0。");
        var output = await CylinderControlNodeHandler.GetService(context, node.VacuumName).ControlVacuumAsync(node.VacuumName.Trim(), node.Command, node.BlowOffDurationMs, node.WaitForVacuumOk, node.TimeoutMs, cancellationToken).ConfigureAwait(false);
        if (!output.Success) return WorkflowRecoverableNodeFailure.Create(node, output.Message ?? $"真空 '{node.VacuumName}' 控制失败。");
        return NodeExecutionResult.Continue(output: output);
    }
}
