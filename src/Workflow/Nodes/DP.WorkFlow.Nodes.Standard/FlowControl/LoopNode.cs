namespace DP.WorkFlow;

/// <summary>表示循环节点的标准输出。</summary>
public sealed record LoopNodeResult(int CurrentIteration, int TotalIterations, bool Completed);

/// <summary>
/// 按固定次数选择 Loop 或 Completed 出口。
/// 循环体末端应通过普通连接返回本节点。
/// </summary>
[WorkflowNode("Loop", DisplayName = "循环节点", Category = "3.Control/流程控制")]
public sealed class LoopNodeModel : WorkflowNodeModel, IWorkflowLoopNode
{
    /// <inheritdoc />
    public override string NodeType => "Loop";

    /// <summary>获取或设置循环体执行次数，必须大于等于 0。</summary>
    public int Iterations { get; set; } = 1;
}

/// <summary>执行固定次数循环节点。</summary>
public sealed class LoopNodeHandler : WorkflowNodeHandler<LoopNodeModel>
{
    /// <inheritdoc />
    protected override ValueTask<NodeExecutionResult> ExecuteAsync(
        LoopNodeModel node,
        IWorkflowNodeExecutionContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (node.Iterations < 0)
            throw new InvalidOperationException("Iterations 不能小于 0。");

        var currentIteration = context.ExecutionIdentity.LoopIteration
            ?? throw new InvalidOperationException("循环节点缺少当前执行令牌的 LoopFrame 状态。");
        var shouldLoop = currentIteration <= node.Iterations;
        var output = new LoopNodeResult(
            Math.Min(currentIteration, node.Iterations),
            node.Iterations,
            !shouldLoop);
        var port = shouldLoop ? WorkflowPorts.Loop : WorkflowPorts.Completed;
        context.Trace("Loop", shouldLoop
            ? $"进入第 {currentIteration}/{node.Iterations} 次循环。"
            : $"循环已完成，共执行 {node.Iterations} 次。",
            new Dictionary<string, object?>
            {
                ["CurrentIteration"] = currentIteration,
                ["TotalIterations"] = node.Iterations,
                ["Port"] = port
            });
        return ValueTask.FromResult(NodeExecutionResult.Continue(port, output));
    }
}
