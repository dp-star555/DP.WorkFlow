namespace DP.WorkFlow;

/// <summary>打开扫码设备连接。</summary>
[WorkflowRecovery(WorkflowRecoveryCategory.Redoable)]
[WorkflowNode("CodeReaderOpen", DisplayName = "打开扫码枪", Category = "4.Motion/CodeReader", OutputType = typeof(string))]
public sealed class CodeReaderOpenNodeModel : WorkflowNodeModel, IRecoverableWorkflowNode, IWorkflowNodeEnvironmentValidator
{
    /// <inheritdoc />
    public IReadOnlyList<string> ValidateEnvironment(IServiceProvider services) =>
        WorkflowMotionEnvironment.Check(services, catalog => new[] { WorkflowMotionEnvironment.Key(catalog, WorkflowMotionObjectKind.CodeReader, ReaderKey, "读码器") });
    /// <inheritdoc />
    public RecoverableInterruptOption Interrupt { get; set; } = new();
    /// <inheritdoc />
    public override string NodeType => "CodeReaderOpen";
    /// <summary>获取或设置设备键。</summary>
    [WorkflowPropertyEditor(WorkflowMotionEditorKeys.CodeReader)]
    public string ReaderKey { get; set; } = string.Empty;
}

/// <summary>执行打开扫码设备节点。</summary>
public sealed class CodeReaderOpenNodeHandler : WorkflowNodeHandler<CodeReaderOpenNodeModel>
{
    /// <inheritdoc />
    protected override IEnumerable<WorkflowRuntimeCapabilityRequirement> GetRequiredCapabilities(CodeReaderOpenNodeModel node) =>
        new[] { MotionCapabilities.CodeReaderCapability };

    /// <inheritdoc />
    protected override async ValueTask<NodeExecutionResult> ExecuteAsync(CodeReaderOpenNodeModel node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
    {
        var service = GetService(context, node.ReaderKey);
        var success = await service.OpenAsync(node.ReaderKey.Trim(), cancellationToken).ConfigureAwait(false);
        if (!success) return WorkflowRecoverableNodeFailure.Create(node, "打开扫码枪失败: " + node.ReaderKey.Trim());
        return NodeExecutionResult.Continue(output: node.ReaderKey.Trim());
    }

    internal static IWorkflowCodeReaderService GetService(IWorkflowNodeExecutionContext context, string key)
    {
        if (string.IsNullOrWhiteSpace(key)) throw new InvalidOperationException("扫码节点没有配置 ReaderKey。");
        return context.GetRequiredCapability<IWorkflowCodeReaderService>();
    }
}
