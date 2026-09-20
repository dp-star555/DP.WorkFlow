namespace DP.WorkFlow;

/// <summary>控制气缸伸出、缩回或全部断电。</summary>
[WorkflowNode("CylinderControl", DisplayName = "气缸控制节点", Category = "4.Motion/Pneumatic")]
public sealed class CylinderControlNodeModel : WorkflowNodeModel, IRecoverableWorkflowNode
{
    /// <inheritdoc />
    public RecoverableInterruptOption Interrupt { get; set; } = new();
    /// <inheritdoc />
    public override string NodeType => "CylinderControl";
    /// <summary>获取或设置气缸注册名称。</summary>
    public string CylinderName { get; set; } = string.Empty;
    /// <summary>获取或设置动作命令。</summary>
    public WorkflowCylinderCommand Command { get; set; } = WorkflowCylinderCommand.Extend;
    /// <summary>获取或设置是否等待目标状态。</summary>
    public bool WaitForTargetState { get; set; } = true;
    /// <summary>获取或设置超时毫秒数；0 表示不超时。</summary>
    public int TimeoutMs { get; set; } = 3000;
}

/// <summary>执行气缸控制节点。</summary>
public sealed class CylinderControlNodeHandler : WorkflowNodeHandler<CylinderControlNodeModel>, IWorkflowNodeOperationFactory
{
    /// <inheritdoc />
    public ValueTask<IWorkflowNodeOperation> CreateOperationAsync(Guid operationId, IWorkflowNodeModel node,
        IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var model = (CylinderControlNodeModel)node;
        return ValueTask.FromResult<IWorkflowNodeOperation>(new CylinderOperation(GetService(context, model.CylinderName),
            model.CylinderName.Trim(), model.Command, model.WaitForTargetState, model.TimeoutMs, model.Interrupt?.AlarmCode ?? 0));
    }

    private sealed class CylinderOperation(IWorkflowPneumaticService service, string name,
        WorkflowCylinderCommand command, bool wait, int timeout, int alarm) : IWorkflowNodeOperation
    {
        private bool _issued;
        public bool CanContinue => wait && command is WorkflowCylinderCommand.Extend or WorkflowCylinderCommand.Retract;
        public async ValueTask<NodeExecutionResult> ExecuteAsync(IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PneumaticNodeResult result;
            if (!_issued)
            {
                // 调用前标记：即便响应丢失，也不盲目重发阀命令。
                _issued = true;
                result = await service.ControlCylinderAsync(name, command, wait, timeout, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                if (!CanContinue) throw new InvalidOperationException("此气缸命令没有可核实的目标状态，禁止继续。");
                result = await service.WaitCylinderAsync(name,
                    command == WorkflowCylinderCommand.Extend ? WorkflowCylinderTargetState.Extended : WorkflowCylinderTargetState.Retracted,
                    timeout, cancellationToken).ConfigureAwait(false);
            }
            return result.Success ? NodeExecutionResult.Continue(output: result)
                : NodeExecutionResult.Fail(result.Message ?? $"气缸 '{name}' 未到达目标；保持原意图等待核实。",
                    alarm > 0 ? WorkflowFaultDisposition.RequestRecovery : WorkflowFaultDisposition.HandleAtScope, alarm);
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    protected override async ValueTask<NodeExecutionResult> ExecuteAsync(CylinderControlNodeModel node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
    {
        var service = GetService(context, node.CylinderName);
        var output = await service.ControlCylinderAsync(node.CylinderName.Trim(), node.Command, node.WaitForTargetState, node.TimeoutMs, cancellationToken).ConfigureAwait(false);
        if (!output.Success) return WorkflowRecoverableNodeFailure.Create(node, output.Message ?? $"气缸 '{node.CylinderName}' 执行动作 '{node.Command}' 失败。");
        return NodeExecutionResult.Continue(output: output);
    }
    internal static IWorkflowPneumaticService GetService(IWorkflowNodeExecutionContext context, string name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("未配置执行器名称。");
        return context.GetRequiredCapability<IWorkflowPneumaticService>();
    }
}
