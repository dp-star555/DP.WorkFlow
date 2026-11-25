namespace DP.WorkFlow;

/// <summary>上报故障时目标流程怎么停。</summary>
public enum WorkflowFaultStopMode
{
    /// <summary>停住：正在执行的节点做完，在下一个节点之前停下。</summary>
    Hold = 0,

    /// <summary>中断：立即取消正在执行的节点，停在这些节点上（急停、外部故障）。</summary>
    Interrupt = 1
}

/// <summary>故障条件的句柄：上报方用它告诉处理流程"故障现在消除了没有"。</summary>
public interface IWorkflowFaultCondition
{
    /// <summary>故障条件是否已经消除。</summary>
    bool IsCleared { get; }
}

/// <summary>
/// 一次故障上报（业务系统调用 <see cref="WorkflowRunCenter.RaiseFault"/>，或流程里的节点故障）。
/// 框架不认识具体业务：上报方说清楚影响哪些流程、怎么停，其余信息放在负载里交给处理流程。
/// </summary>
public sealed record WorkflowFaultReport
{
    /// <summary>故障说明。</summary>
    public required string Message { get; init; }

    /// <summary>报警编号；没有时为 0。</summary>
    public int AlarmCode { get; init; }

    /// <summary>上报方（业务模块名，或出故障的流程名）。</summary>
    public string? Source { get; init; }

    /// <summary>受影响的流程名；为空时为全部正在运行的流程。</summary>
    public IReadOnlyList<string> TargetFlows { get; init; } = Array.Empty<string>();

    /// <summary>目标流程怎么停。</summary>
    public WorkflowFaultStopMode StopMode { get; init; } = WorkflowFaultStopMode.Hold;

    /// <summary>故障条件句柄；处理流程可以据此判断故障是否已消除。</summary>
    public IWorkflowFaultCondition? Condition { get; init; }

    /// <summary>交给处理流程的附加信息。</summary>
    public IReadOnlyDictionary<string, object?> Payload { get; init; } =
        new Dictionary<string, object?>(StringComparer.Ordinal);
}

/// <summary>故障会话的状态。</summary>
public enum WorkflowFaultSessionState
{
    /// <summary>正在等受影响的流程停下。</summary>
    Collecting = 0,

    /// <summary>处理流程正在运行（或兜底界面正在等选择）。</summary>
    Handling = 1,

    /// <summary>去向已经交给各流程，会话结束。</summary>
    Ended = 2
}

/// <summary>
/// 故障会话（统管层）：一次故障从各流程停下、运行处理流程、给出去向到一起放行的全过程。
/// 处理流程里的节点通过服务 <see cref="WorkflowFaultSession"/> 读取故障和停点、给出去向。
/// </summary>
public sealed class WorkflowFaultSession
{
    private readonly object _sync = new();
    private readonly List<WorkflowFaultReport> _faults = new();
    private readonly List<ParkedStop> _stops = new();
    private readonly Dictionary<string, WorkflowResumeDirective> _directives = new(StringComparer.Ordinal);
    private readonly HashSet<string> _expectedFlows = new(StringComparer.Ordinal);

    internal WorkflowFaultSession(WorkflowRunCenter center, string? originFlow)
    {
        Center = center;
        OriginFlow = originFlow;
        StartedAt = WorkflowClock.UtcNow;
    }

    /// <summary>会话身份。</summary>
    public Guid Id { get; } = Guid.NewGuid();

    /// <summary>所属流程管理。</summary>
    public WorkflowRunCenter Center { get; }

    /// <summary>最先出故障的流程；由业务系统上报时为空。</summary>
    public string? OriginFlow { get; }

    /// <summary>会话开始时间（UTC）。</summary>
    public DateTimeOffset StartedAt { get; }

    /// <summary>当前状态。</summary>
    public WorkflowFaultSessionState State { get; internal set; }

    /// <summary>处理流程已经运行的次数（含"重新运行处理流程"）；第一次运行时为 1。</summary>
    public int Attempt { get; internal set; }

    /// <summary>上一次处理没有完成的原因（处理流程出错、去向不被允许）；第一次运行时为空。</summary>
    public string? LastFailure { get; internal set; }

    /// <summary>本会话收到的故障上报，按到达顺序。</summary>
    public IReadOnlyList<WorkflowFaultReport> Faults
    {
        get
        {
            lock (_sync)
                return _faults.ToArray();
        }
    }

    /// <summary>第一条故障上报。</summary>
    public WorkflowFaultReport PrimaryFault
    {
        get
        {
            lock (_sync)
                return _faults[0];
        }
    }

    /// <summary>受影响的流程名（等它们停下或已经不在运行）。</summary>
    public IReadOnlyList<string> AffectedFlows
    {
        get
        {
            lock (_sync)
                return _expectedFlows.ToArray();
        }
    }

    /// <summary>各流程停下的位置，按到达顺序（会话结束后仍保留，供事后查看）。</summary>
    public IReadOnlyList<WorkflowStopPoint> StopPoints
    {
        get
        {
            lock (_sync)
                return _stops.Select(stop => stop.Point).ToArray();
        }
    }

    /// <summary>某个流程的停点；没有停下时为空。</summary>
    /// <param name="flowName">流程名。</param>
    public IReadOnlyList<WorkflowStopPoint> GetStopPoints(string flowName)
    {
        lock (_sync)
            return _stops.Where(stop => string.Equals(stop.FlowName, flowName, StringComparison.Ordinal))
                .Select(stop => stop.Point).ToArray();
    }

    /// <summary>
    /// 给一个流程的去向。流程名为空时给出故障的流程（没有时为唯一停下的流程）。
    /// "停止流程"作用于该流程所有停点；其他去向作用于故障 / 中断的停点（只有被要求停下的停点时作用于第一个），
    /// 其余被要求停下的停点照常继续。去向不被允许时抛出异常，处理流程因此出错并进入兜底。
    /// "回退再继续"要先回退，请用 <see cref="RollBackAsync"/>。
    /// </summary>
    /// <param name="flowName">流程名；为空时为出故障的流程。</param>
    /// <param name="directive">去向。</param>
    /// <exception cref="InvalidOperationException">流程没有停在本会话里，或去向不被允许。</exception>
    public void SetDirective(string? flowName, WorkflowResumeDirective directive)
    {
        Guard.NotNull(directive);
        if (directive.Kind == WorkflowResumeKind.RollBack)
            throw new InvalidOperationException("回退再继续要先把动作回退，请用 RollBackAsync。");
        lock (_sync)
        {
            var name = ResolveFlow(flowName);
            foreach (var stop in StopsFor(name, directive))
            {
                if (!stop.Point.Allows(directive, out var reason))
                    throw new InvalidOperationException($"流程 {name}：{reason}");
            }
            _directives[name] = directive;
        }
    }

    /// <summary>
    /// 回退再继续：把各目标流程从现在逆序回退到恢复入口（入口本身不回退），全部目标合在一起按真实执行的倒序回退
    /// （例如先抬起 Z 再松爪）；回退完成后给这些流程"回退到入口再继续"的去向。
    /// 支持回退的节点自动回退；不支持的节点交人确认处理好（<paramref name="confirmManual"/>），没有人确认时停下并返回原因。
    /// 已经回退过的动作不会再回退（重新运行处理流程时）。
    /// </summary>
    /// <param name="targets">回退目标：流程（为空时为出故障的流程）和恢复入口键。</param>
    /// <param name="confirmManual">不支持回退的节点交人确认；返回 true 表示已处理好。</param>
    /// <param name="message">说明，记入运行记录。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <exception cref="InvalidOperationException">流程没有停在本会话里，或入口不在停点可以回去的入口里。</exception>
    public async Task<WorkflowUndoReport> RollBackAsync(
        IReadOnlyList<WorkflowRollBackTarget> targets,
        Func<WorkflowExecutionRecordEntry, CancellationToken, Task<bool>>? confirmManual,
        string? message,
        CancellationToken cancellationToken)
    {
        Guard.NotNull(targets);
        if (targets.Count == 0)
            throw new InvalidOperationException("没有回退目标。");
        var plan = new List<(string Flow, WorkflowResumeDirective Directive, WorkflowUndoTarget Undo)>();
        lock (_sync)
        {
            foreach (var target in targets)
            {
                var name = ResolveFlow(target.FlowName);
                var directive = WorkflowResumeDirective.RollBack(target.EntryKey, message);
                var stops = StopsFor(name, directive);
                foreach (var stop in stops)
                {
                    if (!stop.Point.Allows(directive, out var reason))
                        throw new InvalidOperationException($"流程 {name}：{reason}");
                }
                var entry = stops[0].Point.Entries.First(item => string.Equals(item.Key, directive.EntryKey, StringComparison.Ordinal));
                plan.Add((name, directive, new WorkflowUndoTarget(name, entry.Position.NodeId)));
            }
        }
        var report = await Center.ExecutionRecord.UndoAsync(plan.Select(item => item.Undo).ToArray(), confirmManual, cancellationToken)
            .ConfigureAwait(false);
        if (report.Success)
        {
            lock (_sync)
            {
                foreach (var item in plan)
                    _directives[item.Flow] = item.Directive;
            }
        }
        return report;
    }

    /// <summary>已经给出的去向，按流程名。</summary>
    public IReadOnlyDictionary<string, WorkflowResumeDirective> Directives
    {
        get
        {
            lock (_sync)
                return new Dictionary<string, WorkflowResumeDirective>(_directives, StringComparer.Ordinal);
        }
    }

    /// <summary>所有故障条件是否都已消除；没有句柄的上报视为已消除。</summary>
    public bool ConditionsCleared
    {
        get
        {
            lock (_sync)
                return _faults.All(fault => fault.Condition?.IsCleared ?? true);
        }
    }

    internal void AddFault(WorkflowFaultReport report)
    {
        lock (_sync)
            _faults.Add(report);
    }

    internal void Expect(string flowName)
    {
        lock (_sync)
            _expectedFlows.Add(flowName);
    }

    internal ParkedStop Park(string flowName, WorkflowStopPoint point)
    {
        var stop = new ParkedStop(flowName, point);
        lock (_sync)
        {
            _expectedFlows.Add(flowName);
            _stops.Add(stop);
        }
        return stop;
    }

    internal bool Unpark(ParkedStop stop)
    {
        lock (_sync)
            return _stops.Remove(stop);
    }

    internal bool IsParked(string flowName)
    {
        lock (_sync)
            return _stops.Any(stop => !stop.IsResolved && string.Equals(stop.FlowName, flowName, StringComparison.Ordinal));
    }

    internal bool HasStops
    {
        get
        {
            lock (_sync)
                return _stops.Any(stop => !stop.IsResolved);
        }
    }

    internal void ClearDirectives()
    {
        lock (_sync)
            _directives.Clear();
    }

    /// <summary>按已给出的去向分配到每个停点；缺去向的流程返回其名称。</summary>
    internal IReadOnlyList<string> Assign(out List<(ParkedStop Stop, WorkflowResumeDirective Directive)> assignment)
    {
        assignment = new List<(ParkedStop, WorkflowResumeDirective)>();
        var missing = new List<string>();
        lock (_sync)
        {
            var pending = _stops.Where(stop => !stop.IsResolved).ToArray();
            foreach (var flow in pending.Select(stop => stop.FlowName).Distinct(StringComparer.Ordinal).ToArray())
            {
                var flowStops = pending.Where(stop => string.Equals(stop.FlowName, flow, StringComparison.Ordinal)).ToArray();
                if (!_directives.TryGetValue(flow, out var directive))
                {
                    // 只是被要求停下的流程没有给去向时照常继续；出故障或被中断的流程必须有去向。
                    if (flowStops.Any(stop => stop.Point.Reason != WorkflowStopReason.Requested))
                    {
                        missing.Add(flow);
                        continue;
                    }
                    directive = WorkflowResumeDirective.RecoverInPlace("处理流程没有给去向，照常继续。");
                }
                var targets = StopsFor(flow, directive);
                foreach (var stop in flowStops)
                    assignment.Add((stop, targets.Contains(stop) ? directive : WorkflowResumeDirective.RecoverInPlace()));
            }
        }
        return missing;
    }

    internal IReadOnlyList<ParkedStop> TakeStops()
    {
        // 停点留在会话里供事后查看；只返回还在等去向的。
        lock (_sync)
            return _stops.Where(stop => !stop.IsResolved).ToArray();
    }

    private string ResolveFlow(string? flowName)
    {
        if (!string.IsNullOrWhiteSpace(flowName))
        {
            var name = flowName!.Trim();
            if (!_stops.Any(stop => string.Equals(stop.FlowName, name, StringComparison.Ordinal)))
                throw new InvalidOperationException($"流程 {name} 没有停在本次故障处理里。");
            return name;
        }
        if (OriginFlow is not null && _stops.Any(stop => string.Equals(stop.FlowName, OriginFlow, StringComparison.Ordinal)))
            return OriginFlow;
        var flows = _stops.Select(stop => stop.FlowName).Distinct(StringComparer.Ordinal).ToArray();
        return flows.Length == 1
            ? flows[0]
            : throw new InvalidOperationException("本次故障处理涉及多个流程，去向必须写明流程名。");
    }

    private List<ParkedStop> StopsFor(string flowName, WorkflowResumeDirective directive)
    {
        var flowStops = _stops.Where(stop => string.Equals(stop.FlowName, flowName, StringComparison.Ordinal)).ToList();
        if (directive.Kind == WorkflowResumeKind.Stop)
            return flowStops;
        var faulted = flowStops.Where(stop => stop.Point.Reason != WorkflowStopReason.Requested).ToList();
        return faulted.Count > 0 ? faulted : flowStops.Take(1).ToList();
    }

    /// <summary>一个停在会话里的停点，等去向。</summary>
    internal sealed class ParkedStop(string flowName, WorkflowStopPoint point)
    {
        public string FlowName { get; } = flowName;
        public WorkflowStopPoint Point { get; } = point;
        public TaskCompletionSource<WorkflowResumeDirective> Directive { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool IsResolved => Directive.Task.IsCompleted;
    }
}

/// <summary>回退再继续的一个目标。</summary>
/// <param name="FlowName">流程名；为空时为出故障的流程。</param>
/// <param name="EntryKey">回到的恢复入口键。</param>
public sealed record WorkflowRollBackTarget(string? FlowName, string EntryKey);

/// <summary>路由表的一项：满足条件的故障交给这个处理流程。</summary>
/// <param name="Name">显示用名称。</param>
/// <param name="Matches">判断故障会话是否交给本项处理。</param>
/// <param name="Handler">处理流程文档。</param>
public sealed record WorkflowFaultRoute(string Name, Func<WorkflowFaultSession, bool> Matches, WorkflowDocument Handler);

/// <summary>兜底界面的选择。</summary>
public enum WorkflowFaultFallbackChoice
{
    /// <summary>重新运行处理流程（已经回退过的动作不会再回退一次）。</summary>
    RerunHandler = 0,

    /// <summary>直接给出各流程的去向。</summary>
    UseDirectives = 1,

    /// <summary>停止全部受影响的流程。</summary>
    StopAll = 2
}

/// <summary>兜底界面的输入：会话和处理没完成的原因。</summary>
/// <param name="Session">故障会话。</param>
/// <param name="Failure">处理流程出错或去向不被允许的原因。</param>
/// <param name="HasHandler">是否有处理流程可以重新运行。</param>
public sealed record WorkflowFaultFallbackRequest(WorkflowFaultSession Session, string Failure, bool HasHandler);

/// <summary>兜底界面的选择结果。</summary>
/// <param name="Choice">选择。</param>
/// <param name="Directives">选"直接给出去向"时各流程的去向（流程名 → 去向）；"回退再继续"由统管层先回退再放行。</param>
/// <param name="ConfirmManual">回退时不支持回退的节点交人确认；为空时遇到这类节点即停下。</param>
public sealed record WorkflowFaultFallbackDecision(
    WorkflowFaultFallbackChoice Choice,
    IReadOnlyDictionary<string, WorkflowResumeDirective>? Directives = null,
    Func<WorkflowExecutionRecordEntry, CancellationToken, Task<bool>>? ConfirmManual = null)
{
    /// <summary>重新运行处理流程。</summary>
    public static WorkflowFaultFallbackDecision Rerun { get; } = new(WorkflowFaultFallbackChoice.RerunHandler);

    /// <summary>停止全部受影响的流程。</summary>
    public static WorkflowFaultFallbackDecision StopAll { get; } = new(WorkflowFaultFallbackChoice.StopAll);
}

/// <summary>
/// 内置兜底界面：处理流程出错、没有给出去向或去向不被允许时询问操作员。
/// 界面层实现它（例如 WinForms 弹窗）；没有提供时停止全部受影响的流程。
/// </summary>
public interface IWorkflowFaultFallback
{
    /// <summary>询问怎么办。</summary>
    /// <param name="request">会话和失败原因。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<WorkflowFaultFallbackDecision> DecideAsync(WorkflowFaultFallbackRequest request, CancellationToken cancellationToken);
}

/// <summary>标出处理流程块：流程自带的故障处理流程（优先于路由表）。</summary>
public interface IWorkflowFaultHandlerNode : IWorkflowSubDocumentNode
{
}
