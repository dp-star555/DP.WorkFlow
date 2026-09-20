namespace DP.WorkFlow;

/// <summary>记录当前流程最近一次安全点。</summary>
[WorkflowNode("SafePoint", DisplayName = "安全点节点", Category = "8.Process/异常恢复")]
public sealed class SafePointNodeModel : WorkflowNodeModel, IWorkflowRecoveryTargetNode
{
    /// <inheritdoc />
    public override string NodeType => "SafePoint";
    /// <summary>安全点键；为空时使用节点 ID。</summary>
    public string SafePointKey { get; set; } = string.Empty;
}

/// <summary>执行安全点记录。</summary>
public sealed class SafePointNodeHandler : WorkflowNodeHandler<SafePointNodeModel>
{
    /// <inheritdoc />
    protected override async ValueTask<NodeExecutionResult> ExecuteAsync(SafePointNodeModel node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
    {
        var key = string.IsNullOrWhiteSpace(node.SafePointKey) ? node.Id : node.SafePointKey.Trim();
        var output = new WorkflowSafePointContext { SafePointKey = key, NodeId = node.Id, Title = node.Title };
        context.SetVariable(WorkflowRecoveryRuntimeKeys.CurrentSafePoint, output);
        if (context.Services.GetService(typeof(IWorkflowRecoveryService)) is IWorkflowRecoveryService service)
            await service.RegisterSafePointAsync(key, context.ExecutionIdentity, cancellationToken).ConfigureAwait(false);
        context.Trace("更新安全点", "更新当前安全点。", new Dictionary<string, object?> { ["SafePointKey"] = key, ["NodeId"] = node.Id });
        return NodeExecutionResult.Continue(output: output);
    }

    internal static IWorkflowRecoveryService GetService(IWorkflowNodeExecutionContext context) =>
        context.GetRequiredCapability<IWorkflowRecoveryService>();
}
