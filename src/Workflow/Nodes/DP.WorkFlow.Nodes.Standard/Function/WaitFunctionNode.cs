using System.Diagnostics;

namespace DP.WorkFlow;

/// <summary>表示函数等待节点的标准输出。</summary>
public sealed record WaitFunctionNodeResult(bool Matched, bool TimedOut, TimeSpan Elapsed);

/// <summary>
/// 周期调用宿主条件，满足时走 Success，超时时走 Timeout。
/// </summary>
[WorkflowNode("WaitFunction", DisplayName = "函数等待节点", Category = "2.Function/等待")]
public sealed class WaitFunctionNodeModel : WorkflowNodeModel
{
    /// <inheritdoc />
    public override string NodeType => "WaitFunction";

    /// <summary>获取或设置条件函数键。</summary>
    public string FunctionKey { get; set; } = string.Empty;

    /// <summary>获取或设置超时毫秒数；小于等于 0 表示无限等待。</summary>
    public int TimeoutMs { get; set; } = 5000;

    /// <summary>获取或设置轮询间隔毫秒数，运行时最小按 5ms 处理。</summary>
    public int PollIntervalMs { get; set; } = 50;

    /// <summary>获取或设置超时后是否选择 Timeout 端口；否则节点失败。</summary>
    public bool TimeoutAsFalseBranch { get; set; } = true;
}

/// <summary>执行函数等待节点。</summary>
public sealed class WaitFunctionNodeHandler : WorkflowNodeHandler<WaitFunctionNodeModel>
{
    /// <inheritdoc />
    protected override async ValueTask<NodeExecutionResult> ExecuteAsync(
        WaitFunctionNodeModel node,
        IWorkflowNodeExecutionContext context,
        CancellationToken cancellationToken)
    {
        var registry = context.GetRequiredCapability<IWorkflowConditionRegistry>();
        var condition = registry.GetOrThrow(node.FunctionKey);
        var timeout = node.TimeoutMs > 0 ? TimeSpan.FromMilliseconds(node.TimeoutMs) : Timeout.InfiniteTimeSpan;
        var pollInterval = TimeSpan.FromMilliseconds(Math.Max(5, node.PollIntervalMs));
        var stopwatch = Stopwatch.StartNew();

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await condition(context, cancellationToken).ConfigureAwait(false))
            {
                var output = new WaitFunctionNodeResult(true, false, stopwatch.Elapsed);
                context.Trace("Matched", "等待条件已经满足。", new Dictionary<string, object?>
                {
                    ["ElapsedMs"] = stopwatch.Elapsed.TotalMilliseconds
                });
                return NodeExecutionResult.Continue(WorkflowPorts.Success, output);
            }

            if (timeout != Timeout.InfiniteTimeSpan && stopwatch.Elapsed >= timeout)
            {
                var output = new WaitFunctionNodeResult(false, true, stopwatch.Elapsed);
                context.Trace("Timeout", "等待条件超时。", new Dictionary<string, object?>
                {
                    ["TimeoutMs"] = node.TimeoutMs,
                    ["ElapsedMs"] = stopwatch.Elapsed.TotalMilliseconds
                });
                return node.TimeoutAsFalseBranch
                    ? NodeExecutionResult.Continue(WorkflowPorts.Timeout, output)
                    : NodeExecutionResult.Fail($"等待条件 {node.FunctionKey} 超时。", WorkflowFaultDisposition.StopRun);
            }

            var delay = pollInterval;
            if (timeout != Timeout.InfiniteTimeSpan)
            {
                var remaining = timeout - stopwatch.Elapsed;
                if (remaining < delay)
                    delay = remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
            }
            if (delay > TimeSpan.Zero)
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }
    }
}
