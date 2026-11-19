using System.Diagnostics;

namespace DP.WorkFlow;

/// <summary>等待的结果。</summary>
public enum WorkflowNodeWaitOutcome
{
    /// <summary>等到了。</summary>
    Satisfied,

    /// <summary>超时。</summary>
    TimedOut,

    /// <summary>等待被取消或因异常中断。</summary>
    Interrupted
}

/// <summary>
/// 节点的一次等待（R4-a 出口事件 NodeWaitStarted / NodeWaitEnded）：开始时记下"等什么、最长等多久"，
/// 结束时记下"等到、超时还是被中断"以及等了多久。用 <c>using</c> 包住等待，
/// 没有调用 <see cref="End(bool)"/> 就离开（取消、异常）时记为 <see cref="WorkflowNodeWaitOutcome.Interrupted"/>。
/// </summary>
/// <example>
/// <code>
/// using var wait = context.BeginWait($"信号 {key}", timeout);
/// var matched = await service.WaitAsync(key, expected, timeout, cancellationToken);
/// wait.End(matched);
/// </code>
/// </example>
public sealed class WorkflowNodeWait : IDisposable
{
    /// <summary>NodeWaitStarted 事件键。</summary>
    public const string StartedEvent = "NodeWaitStarted";

    /// <summary>NodeWaitEnded 事件键。</summary>
    public const string EndedEvent = "NodeWaitEnded";

    private readonly IWorkflowNodeExecutionContext _context;
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    private int _ended;

    internal WorkflowNodeWait(IWorkflowNodeExecutionContext context, string target, TimeSpan? timeout)
    {
        _context = context;
        Target = target;
        _context.Trace(StartedEvent, $"开始等待：{target}。", new Dictionary<string, object?>
        {
            ["Target"] = target,
            ["TimeoutMs"] = timeout is { } value && value > TimeSpan.Zero && value != Timeout.InfiniteTimeSpan
                ? (long)value.TotalMilliseconds
                : null
        });
    }

    /// <summary>等待的对象说明。</summary>
    public string Target { get; }

    /// <summary>按"是否等到"结束等待：<see langword="true"/> 为等到，<see langword="false"/> 为超时。</summary>
    /// <param name="satisfied">是否等到。</param>
    public void End(bool satisfied) =>
        End(satisfied ? WorkflowNodeWaitOutcome.Satisfied : WorkflowNodeWaitOutcome.TimedOut);

    /// <summary>结束等待并记录结果；只记录第一次。</summary>
    /// <param name="outcome">等待结果。</param>
    public void End(WorkflowNodeWaitOutcome outcome)
    {
        if (Interlocked.Exchange(ref _ended, 1) != 0)
            return;
        var message = outcome switch
        {
            WorkflowNodeWaitOutcome.Satisfied => $"等待结束：{Target} 已满足。",
            WorkflowNodeWaitOutcome.TimedOut => $"等待超时：{Target}。",
            _ => $"等待中断：{Target}。"
        };
        _context.Trace(EndedEvent, message, new Dictionary<string, object?>
        {
            ["Target"] = Target,
            ["Outcome"] = outcome.ToString(),
            ["WaitElapsedMs"] = _stopwatch.ElapsedMilliseconds
        });
    }

    /// <summary>没有结束过的等待记为中断。</summary>
    public void Dispose() => End(WorkflowNodeWaitOutcome.Interrupted);
}

/// <summary>节点等待的入口。</summary>
public static class WorkflowNodeWaitExtensions
{
    /// <summary>开始一次需要记录的等待；返回值用 <c>using</c> 包住等待过程，并在等到或超时时调用 <see cref="WorkflowNodeWait.End(bool)"/>。</summary>
    /// <param name="context">节点执行上下文。</param>
    /// <param name="target">等什么，例如"信号 Ready"、"轴 X 到位"。</param>
    /// <param name="timeout">最长等待时间；不限时为空、零或无限。</param>
    /// <returns>本次等待。</returns>
    public static WorkflowNodeWait BeginWait(this IWorkflowNodeExecutionContext context, string target, TimeSpan? timeout = null)
    {
        if (context is null)
            throw new ArgumentNullException(nameof(context));
        return new WorkflowNodeWait(context, string.IsNullOrWhiteSpace(target) ? "条件" : target.Trim(), timeout);
    }

    /// <summary>按毫秒超时开始一次等待；小于等于 0 表示不限时。</summary>
    /// <param name="context">节点执行上下文。</param>
    /// <param name="target">等什么。</param>
    /// <param name="timeoutMs">最长等待毫秒数。</param>
    /// <returns>本次等待。</returns>
    public static WorkflowNodeWait BeginWait(this IWorkflowNodeExecutionContext context, string target, int timeoutMs) =>
        context.BeginWait(target, timeoutMs > 0 ? TimeSpan.FromMilliseconds(timeoutMs) : null);
}
