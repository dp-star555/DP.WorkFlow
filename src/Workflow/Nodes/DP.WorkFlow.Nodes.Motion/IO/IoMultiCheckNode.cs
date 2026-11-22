namespace DP.WorkFlow;

/// <summary>立即读取并判断多条 IO 条件。</summary>
[WorkflowRecovery(WorkflowRecoveryCategory.Redoable)]
[WorkflowNode("IOMultiCheck", DisplayName = "判断多 IO 条件", Category = "4.Motion/IO", OutputType = typeof(IoMultiNodeResult))]
[WorkflowOutputPorts(WorkflowPorts.True, WorkflowPorts.False)]
public sealed class IoMultiCheckNodeModel : WorkflowNodeModel
{
    /// <inheritdoc />
    public override string NodeType => "IOMultiCheck";
    /// <summary>获取或设置 All/Any 组合模式。</summary>
    public WorkflowIoMatchMode Mode { get; set; }
    /// <summary>获取条件列表。</summary>
    public List<WorkflowIoExpectation> Conditions { get; } = new();
}

/// <summary>执行多 IO 判断节点。</summary>
public sealed class IoMultiCheckNodeHandler : WorkflowNodeHandler<IoMultiCheckNodeModel>
{
    /// <inheritdoc />
    protected override IEnumerable<WorkflowRuntimeCapabilityRequirement> GetRequiredCapabilities(IoMultiCheckNodeModel node) =>
        new[] { MotionCapabilities.IoCapability };

    /// <inheritdoc />
    protected override async ValueTask<NodeExecutionResult> ExecuteAsync(IoMultiCheckNodeModel node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
    {
        if (node.Conditions.Count == 0) throw new InvalidOperationException("条件列表为空。");
        foreach (var item in node.Conditions) _ = item.ToAddress();
        var output = await IoReadNodeHandler.GetService(context).CheckManyAsync(node.Conditions, node.Mode, cancellationToken).ConfigureAwait(false);
        return NodeExecutionResult.Continue(output.Matched ? WorkflowPorts.True : WorkflowPorts.False, output);
    }
}
