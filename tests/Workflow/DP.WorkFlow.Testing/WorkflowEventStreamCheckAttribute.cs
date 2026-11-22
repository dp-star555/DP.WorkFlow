using System.Collections.Concurrent;
using System.Reflection;
using System.Text;
using Xunit.Sdk;

namespace DP.WorkFlow.Testing;

/// <summary>
/// R5-a：程序集级特性。每个测试开始时开始收集该测试中产生的运行事件，测试结束后按
/// <see cref="WorkflowEventStreamInvariants"/> 核对；有违反时让该测试失败并列出违反的规则和事件。
/// 由 tests/Workflow/Directory.Build.targets 自动加到每个 Workflow 测试工程，不需要逐个测试声明。
/// </summary>
[AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Class | AttributeTargets.Method)]
public sealed class WorkflowEventStreamCheckAttribute : BeforeAfterTestAttribute
{
    private static readonly AsyncLocal<WorkflowEventStreamCollector?> Current = new();

    /// <inheritdoc />
    public override void Before(MethodInfo methodUnderTest)
    {
        WorkflowEventStreamHub.EnsureInstalled();
        Current.Value = IsSkipped(methodUnderTest) ? null : new WorkflowEventStreamCollector();
    }

    /// <inheritdoc />
    public override void After(MethodInfo methodUnderTest)
    {
        var collector = Current.Value;
        Current.Value = null;
        if (collector is null)
            return;
        var report = collector.Verify();
        if (report is not null)
            throw new XunitException(report);
    }

    internal static WorkflowEventStreamCollector? CurrentCollector => Current.Value;

    /// <summary>当前测试是否正在核对事件流；用于确认程序集级特性确实生效。</summary>
    public static bool IsActiveForCurrentTest => Current.Value is not null;

    private static bool IsSkipped(MethodInfo method) =>
        method.GetCustomAttribute<SkipWorkflowEventStreamCheckAttribute>() is not null
        || method.DeclaringType?.GetCustomAttribute<SkipWorkflowEventStreamCheckAttribute>() is not null;
}

/// <summary>
/// 让个别测试或测试类不做事件流核对；必须写明原因（例如测试本身故意构造不合规的事件）。
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class SkipWorkflowEventStreamCheckAttribute : Attribute
{
    public SkipWorkflowEventStreamCheckAttribute(string reason) => Reason = reason;

    /// <summary>不核对的原因。</summary>
    public string Reason { get; }
}

/// <summary>
/// 全局事件汇集：通过 Runtime 的测试探针接收所有记录器事件，按 RunId 分组。
/// 测试上下文（AsyncLocal）决定一个运行属于哪个测试；已知运行之后的事件即使来自不传递上下文的线程也照常收集。
/// </summary>
internal static class WorkflowEventStreamHub
{
    private static readonly ConcurrentDictionary<Guid, RunLog> Runs = new();
    private static int _installed;

    internal static void EnsureInstalled()
    {
        if (Interlocked.Exchange(ref _installed, 1) == 0)
            WorkflowRunEventProbe.Observer = OnEvent;
    }

    internal static IReadOnlyList<WorkflowRunEvent>? Find(Guid runId) =>
        Runs.TryGetValue(runId, out var log) ? log.Snapshot() : null;

    internal static void Forget(Guid runId) => Runs.TryRemove(runId, out _);

    private static void OnEvent(WorkflowRunEvent @event)
    {
        var collector = WorkflowEventStreamCheckAttribute.CurrentCollector;
        RunLog? log;
        if (collector is not null)
        {
            log = Runs.GetOrAdd(@event.RunId, _ => new RunLog());
            collector.Track(@event.RunId);
        }
        else if (!Runs.TryGetValue(@event.RunId, out log))
        {
            return; // 不属于任何正在核对的测试。
        }
        log.Add(@event);
    }

    private sealed class RunLog
    {
        private readonly List<WorkflowRunEvent> _events = new();

        public void Add(WorkflowRunEvent @event)
        {
            lock (_events)
                _events.Add(@event);
        }

        public IReadOnlyList<WorkflowRunEvent> Snapshot()
        {
            lock (_events)
                return _events.ToArray();
        }
    }
}

/// <summary>一个测试中涉及的运行。</summary>
internal sealed class WorkflowEventStreamCollector
{
    private const int MaxListedEvents = 60;
    private readonly ConcurrentDictionary<Guid, byte> _runIds = new();

    public void Track(Guid runId) => _runIds.TryAdd(runId, 0);

    /// <summary>核对本测试中的全部运行；全部满足时返回 null，否则返回失败说明。</summary>
    public string? Verify()
    {
        var runIds = _runIds.Keys.ToArray();
        StringBuilder? report = null;
        foreach (var runId in runIds)
        {
            var events = WorkflowEventStreamHub.Find(runId);
            if (events is null)
                continue;
            var violations = WorkflowEventStreamInvariants.Check(events, WorkflowEventStreamHub.Find);
            if (violations.Count == 0)
                continue;
            report ??= new StringBuilder("事件流规则核对失败（R5-a，见 DP.WorkFlow.Testing/WorkflowEventStreamInvariants.cs）：");
            var ordered = events.OrderBy(item => item.Sequence).ToArray();
            report.AppendLine().Append("运行 ").Append(runId)
                .Append(" [").Append(ordered[0].WorkflowName).Append(']');
            if (ordered[0].ParentRunId is { } parentId)
                report.Append(" 父运行 ").Append(parentId);
            report.AppendLine();
            foreach (var violation in violations)
                report.Append("  ✗ ").AppendLine(violation);
            report.AppendLine("  事件：");
            foreach (var item in ordered.Take(MaxListedEvents))
                report.Append("    #").Append(item.Sequence).Append(' ').Append(item.EventType)
                    .Append(item.Parents is { Count: > 0 } ? " " + item.CanvasPath : string.Empty)
                    .Append(item.NodeId is null ? string.Empty : " " + item.NodeId)
                    .Append(item.ExecutionIdentity is null ? string.Empty : "#" + item.ExecutionIdentity.NodeExecutionCount)
                    .AppendLine();
            if (ordered.Length > MaxListedEvents)
                report.Append("    …共 ").Append(ordered.Length).AppendLine(" 条");
        }
        foreach (var runId in runIds)
            WorkflowEventStreamHub.Forget(runId);
        return report?.ToString();
    }
}
