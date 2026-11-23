namespace DP.WorkFlow;

/// <summary>关闭扫码设备连接。</summary>
[WorkflowRecovery(WorkflowRecoveryCategory.Redoable)]
[WorkflowNode("CodeReaderClose", DisplayName = "关闭扫码枪", Category = "4.Motion/CodeReader", OutputType = typeof(string))]
public sealed class CodeReaderCloseNodeModel : WorkflowNodeModel, IRecoverableWorkflowNode, IWorkflowNodeEnvironmentValidator
{
    /// <inheritdoc />
    public IReadOnlyList<string> ValidateEnvironment(IServiceProvider services) =>
        WorkflowMotionEnvironment.Check(services, catalog => new[] { WorkflowMotionEnvironment.Key(catalog, WorkflowMotionObjectKind.CodeReader, ReaderKey, "读码器") });
    /// <inheritdoc />
    public RecoverableInterruptOption Interrupt { get; set; } = new();
    /// <inheritdoc />
    public override string NodeType => "CodeReaderClose";
    /// <summary>获取或设置设备键。</summary>
    [WorkflowPropertyEditor(WorkflowMotionEditorKeys.CodeReader)]
    public string ReaderKey { get; set; } = string.Empty;
}

/// <summary>执行关闭扫码设备节点。</summary>
public sealed class CodeReaderCloseNodeHandler : WorkflowNodeHandler<CodeReaderCloseNodeModel>
{
    /// <inheritdoc />
    protected override IEnumerable<WorkflowRuntimeCapabilityRequirement> GetRequiredCapabilities(CodeReaderCloseNodeModel node) =>
        new[] { MotionCapabilities.CodeReaderCapability };

    /// <inheritdoc />
    protected override async ValueTask<NodeExecutionResult> ExecuteAsync(CodeReaderCloseNodeModel node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
    {
        var service = CodeReaderOpenNodeHandler.GetService(context, node.ReaderKey);
        var success = await service.CloseAsync(node.ReaderKey.Trim(), cancellationToken).ConfigureAwait(false);
        if (!success) return WorkflowRecoverableNodeFailure.Create(node, "关闭扫码枪失败: " + node.ReaderKey.Trim());
        return NodeExecutionResult.Continue(output: node.ReaderKey.Trim());
    }
}
