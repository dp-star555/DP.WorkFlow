namespace DP.WorkFlow;

/// <summary>工艺流程中的命名恢复入口；不是设备位置快照。</summary>
[WorkflowNode("RecoveryEntry", DisplayName = "恢复入口", Category = "8.Process/异常恢复")]
public sealed class RecoveryEntryNodeModel : WorkflowNodeModel, IWorkflowRecoveryEntryNode, IWorkflowNodeConfigurationValidator
{
    /// <inheritdoc />
    public override string NodeType => "RecoveryEntry";
    /// <summary>工程师配置的、文档内唯一的入口键。</summary>
    [WorkflowProperty("入口键", "文档内唯一名称；恢复时还需工位验证，不能直接跳转。", Category = "恢复配置")]
    public string EntryKey { get; set; } = string.Empty;
    /// <inheritdoc />
    public string RecoveryEntryKey => (EntryKey ?? string.Empty).Trim();
    /// <inheritdoc />
    public IReadOnlyList<string> ValidateConfiguration() => string.IsNullOrWhiteSpace(EntryKey)
        ? new[] { "恢复入口键不能为空。" } : Array.Empty<string>();
}

/// <summary>经过入口后，由引擎记录当前执行位置。</summary>
public sealed class RecoveryEntryNodeHandler : WorkflowNodeHandler<RecoveryEntryNodeModel>
{
    /// <inheritdoc />
    protected override ValueTask<NodeExecutionResult> ExecuteAsync(RecoveryEntryNodeModel node,
        IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(node.EntryKey)) throw new InvalidOperationException("恢复入口键不能为空。");
        return ValueTask.FromResult(NodeExecutionResult.Continue(output: node.RecoveryEntryKey));
    }
}

/// <summary>异常处理出口：请求继续故障操作实例。</summary>
[WorkflowNode("WarnContinueOperation", DisplayName = "继续原操作", Category = "8.Process/异常恢复")]
public sealed class ContinueOperationNodeModel : WorkflowNodeModel
{
    /// <inheritdoc />
    public override string NodeType => "WarnContinueOperation";
    /// <summary>工程师配置的处置说明。</summary>
    [WorkflowProperty("说明", "请求继续原意图；不重新创建操作。", Category = "恢复配置")]
    public string Note { get; set; } = string.Empty;
}

/// <summary>只产生请求，不直接执行设备命令或调度主流程。</summary>
public sealed class ContinueOperationNodeHandler : WorkflowNodeHandler<ContinueOperationNodeModel>
{
    /// <inheritdoc />
    protected override ValueTask<NodeExecutionResult> ExecuteAsync(ContinueOperationNodeModel node,
        IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var resolution = new WarningResolution { ExitAction = E_WarningExitAction.ContinueOperation, Note = node.Note };
        context.SetVariable(WorkflowRecoveryRuntimeKeys.WarningResolution, resolution);
        return ValueTask.FromResult(NodeExecutionResult.Complete(resolution));
    }
}

/// <summary>异常处理出口：请求从工程师指定的命名入口重执行。</summary>
[WorkflowNode("WarnRestartFromEntry", DisplayName = "从恢复入口重执行", Category = "8.Process/异常恢复")]
public sealed class RestartFromEntryNodeModel : WorkflowNodeModel, IWorkflowNodeConfigurationValidator
{
    /// <inheritdoc />
    public override string NodeType => "WarnRestartFromEntry";
    /// <inheritdoc />
    public IReadOnlyList<string> ValidateConfiguration() => string.IsNullOrWhiteSpace(EntryKey)
        ? new[] { "恢复目标入口键不能为空。" } : Array.Empty<string>();
    /// <summary>主流程中已声明的入口键，不是任意NodeId。</summary>
    [WorkflowProperty("入口键", "V1仅支持当前文档内已经经过的无环串行入口。", Category = "恢复配置")]
    public string EntryKey { get; set; } = string.Empty;
    /// <summary>处置说明。</summary>
    [WorkflowProperty("说明", "人工处置与验证完成后请求重执行。", Category = "恢复配置")]
    public string Note { get; set; } = string.Empty;
}

/// <summary>生成命名入口请求，由引擎验证并失效旧派生数据。</summary>
public sealed class RestartFromEntryNodeHandler : WorkflowNodeHandler<RestartFromEntryNodeModel>
{
    /// <inheritdoc />
    protected override ValueTask<NodeExecutionResult> ExecuteAsync(RestartFromEntryNodeModel node,
        IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(node.EntryKey)) throw new InvalidOperationException("恢复入口键不能为空。");
        var resolution = new WarningResolution { ExitAction = E_WarningExitAction.RestartFromEntry, RecoveryEntryKey = node.EntryKey.Trim(), Note = node.Note };
        context.SetVariable(WorkflowRecoveryRuntimeKeys.WarningResolution, resolution);
        return ValueTask.FromResult(NodeExecutionResult.Complete(resolution));
    }
}
