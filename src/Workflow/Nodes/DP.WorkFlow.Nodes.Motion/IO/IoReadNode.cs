namespace DP.WorkFlow;

/// <summary>按旧版 DriveId、Index 和 IOType 读取 IO 点位。</summary>
[WorkflowNode("IORead", DisplayName = "读 IO", Category = "4.Motion/IO")]
public sealed class IoReadNodeModel : WorkflowNodeModel
{
    /// <inheritdoc />
    public override string NodeType => "IORead";
    /// <summary>获取或设置 IO 类型。</summary>
    public WorkflowIoPointType IOType { get; set; } = WorkflowIoPointType.Input;
    /// <summary>获取或设置驱动 ID。</summary>
    public string DriveId { get; set; } = string.Empty;
    /// <summary>获取或设置兼容旧 JSON 的字符串索引。</summary>
    public string Index { get; set; } = "0";
}

/// <summary>执行 IO 读取节点。</summary>
public sealed class IoReadNodeHandler : WorkflowNodeHandler<IoReadNodeModel>
{
    /// <inheritdoc />
    protected override async ValueTask<NodeExecutionResult> ExecuteAsync(IoReadNodeModel node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(node.DriveId)) throw new InvalidOperationException("DriveId 为空。");
        if (!int.TryParse(node.Index, out var index) || index < 0) throw new InvalidOperationException($"Invalid IO Index: {node.Index}");
        var service = GetService(context);
        var output = await service.ReadAsync(new WorkflowIoAddress(node.DriveId.Trim(), index, node.IOType), cancellationToken).ConfigureAwait(false);
        if (!output.Success) throw new InvalidOperationException(output.Message ?? "IO read failed.");
        return NodeExecutionResult.Continue(output: output);
    }

    internal static IWorkflowIoService GetService(IWorkflowNodeExecutionContext context) =>
        context.GetRequiredCapability<IWorkflowIoService>();
}
