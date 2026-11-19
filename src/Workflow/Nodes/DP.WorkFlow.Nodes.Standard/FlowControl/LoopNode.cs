using System.ComponentModel;

namespace DP.WorkFlow;

/// <summary>表示循环节点的标准输出。</summary>
public sealed record LoopNodeResult(
    [property: DisplayName("当前次数")] int CurrentIteration,
    [property: DisplayName("总次数")] int TotalIterations,
    [property: DisplayName("是否完成")] bool Completed,
    [property: DisplayName("退出原因")] string ExitReason = "");

/// <summary>
/// 画布回边循环：选择 Loop 或 Completed 出口，循环体末端通过普通连接返回本节点。
/// 退出条件（R3-c，可组合）：到达次数（0 = 不限）；循环体中走到指向本节点的"退出循环"节点；外部 bool 信号为真。
/// 后两者在每圈结束（回到本节点）时检查，跑完当前这一圈后退出。
/// </summary>
[WorkflowNode("Loop", DisplayName = "循环节点", Category = "3.Control/流程控制")]
public sealed class LoopNodeModel : WorkflowNodeModel, IWorkflowLoopNode, IWorkflowPublicDataUser
{
    /// <inheritdoc />
    public override string NodeType => "Loop";

    /// <summary>获取或设置循环体执行次数；0 表示不限次数，必须大于等于 0。</summary>
    [WorkflowProperty("循环次数", "循环体执行的次数；0 表示不限次数，直到退出循环节点、外部退出信号或流程停止。", Category = "循环")]
    public int Iterations { get; set; } = 1;

    /// <summary>外部退出信号：公共数据 bool 信号，每圈结束时为真则退出。为空时不检查。</summary>
    [WorkflowProperty("退出信号", "公共数据 bool 信号；外部程序、界面或其他流程置为真后，跑完当前这一圈退出。为空时不检查。", Category = "循环")]
    [WorkflowPropertyEditor(WorkflowPropertyEditorKeys.PublicDataSignal)]
    public string ExitSignalKey { get; set; } = string.Empty;

    /// <summary>因退出信号退出后是否把信号复位为 false。</summary>
    [WorkflowProperty("退出后复位信号", "因退出信号退出后把信号置回假，下次进入循环不会立刻退出。", Category = "循环")]
    public bool ResetExitSignal { get; set; } = true;

    /// <inheritdoc />
    public IEnumerable<WorkflowPublicDataDeclaration> GetPublicDataUses() => PublicDataUses.Signal(ExitSignalKey);
}

/// <summary>执行循环节点。</summary>
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
        // 第一次进入时清掉上次使用留下的退出请求；之后每次回到本节点即一圈结束，检查退出条件。
        var exitRequested = WorkflowLoopExit.Consume(context, node.Id) && currentIteration > 1;
        string? exitReason = null;
        if (exitRequested)
            exitReason = "退出循环节点";
        else if (currentIteration > 1 && WorkflowLoopExit.CheckSignal(context, node.ExitSignalKey, node.ResetExitSignal))
            exitReason = "外部退出信号";
        else if (node.Iterations > 0 && currentIteration > node.Iterations)
            exitReason = "达到循环次数";

        var shouldLoop = exitReason is null;
        var output = new LoopNodeResult(
            shouldLoop ? currentIteration : currentIteration - 1,
            node.Iterations,
            !shouldLoop,
            exitReason ?? string.Empty);
        var port = shouldLoop ? WorkflowPorts.Loop : WorkflowPorts.Completed;
        var total = node.Iterations > 0 ? node.Iterations.ToString(System.Globalization.CultureInfo.InvariantCulture) : "不限";
        context.Trace("Loop", shouldLoop
            ? $"进入第 {currentIteration}/{total} 次循环。"
            : $"循环结束：{exitReason}，共执行 {currentIteration - 1} 次。",
            new Dictionary<string, object?>
            {
                ["CurrentIteration"] = currentIteration,
                ["TotalIterations"] = node.Iterations,
                ["Port"] = port,
                ["ExitReason"] = exitReason
            });
        return Polyfill.FromResult(NodeExecutionResult.Continue(port, output));
    }
}

/// <summary>
/// 退出循环节点（R3-c）：在循环体中走到它，循环在本圈结束时退出。
/// 在画布回边循环中指定要退出的循环节点，并把出口连回该循环节点；在循环块的子画布中留空，走到即结束本圈并退出循环块。
/// </summary>
[WorkflowNode("ExitLoop", DisplayName = "退出循环", Category = "3.Control/流程控制")]
public sealed class ExitLoopNodeModel : WorkflowNodeModel
{
    /// <inheritdoc />
    public override string NodeType => "ExitLoop";

    /// <summary>要退出的循环节点 ID；为空时退出所在子画布的循环块。</summary>
    [WorkflowProperty("循环节点", "要退出的循环节点 ID；在循环块子画布中留空。", Category = "循环")]
    public string LoopNodeId { get; set; } = string.Empty;
}

/// <summary>执行退出循环节点。</summary>
public sealed class ExitLoopNodeHandler : WorkflowNodeHandler<ExitLoopNodeModel>
{
    /// <inheritdoc />
    protected override ValueTask<NodeExecutionResult> ExecuteAsync(
        ExitLoopNodeModel node,
        IWorkflowNodeExecutionContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        WorkflowLoopExit.Request(context, node.LoopNodeId);
        var target = string.IsNullOrWhiteSpace(node.LoopNodeId) ? "所在循环块" : $"循环节点 {node.LoopNodeId.Trim()}";
        context.Trace("ExitLoop", $"请求退出{target}，本圈结束后退出。");
        return Polyfill.FromResult(NodeExecutionResult.Continue());
    }
}
