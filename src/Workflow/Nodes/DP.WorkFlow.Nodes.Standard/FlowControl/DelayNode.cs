namespace DP.WorkFlow;

/// <summary>表示延时节点的标准输出。</summary>
public sealed record DelayNodeResult(TimeSpan RequestedDelay, TimeSpan Elapsed);

/// <summary>
/// 使用可取消的异步等待暂停当前执行路径。
/// </summary>
[WorkflowNode("Delay", DisplayName = "延时节点", Category = "3.Control/流程控制")]
public sealed class DelayNodeModel : WorkflowNodeModel
{
    /// <inheritdoc />
    public override string NodeType => "Delay";

    /// <summary>获取或设置延时毫秒数，必须大于等于 0。</summary>
    [WorkflowProperty("延迟时间", "当前执行路径暂停后再继续运行的时间。", Category = "等待与超时", Unit = "ms")]
    public int DelayMs { get; set; } = 100;
}

/// <summary>执行延时节点。</summary>
public sealed class DelayNodeHandler : WorkflowNodeHandler<DelayNodeModel>
{
    /// <inheritdoc />
    protected override async ValueTask<NodeExecutionResult> ExecuteAsync(
        DelayNodeModel node,
        IWorkflowNodeExecutionContext context,
        CancellationToken cancellationToken)
    {
        if (node.DelayMs < 0)
            throw new InvalidOperationException("DelayMs 不能小于 0。");

        var startedAt = DateTimeOffset.UtcNow;
        if (node.DelayMs > 0)
            await Task.Delay(TimeSpan.FromMilliseconds(node.DelayMs), cancellationToken).ConfigureAwait(false);
        var elapsed = DateTimeOffset.UtcNow - startedAt;
        return NodeExecutionResult.Continue(
            output: new DelayNodeResult(TimeSpan.FromMilliseconds(node.DelayMs), elapsed));
    }
}
