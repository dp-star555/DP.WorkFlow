namespace DP.WorkFlow.UI;

/// <summary>工作台里一个可选的去向，以及它对这个流程是否允许。</summary>
/// <param name="Directive">去向。</param>
/// <param name="Allowed">对这个流程要处理的停点是否全部允许。</param>
/// <param name="Reason">不允许的原因。</param>
public sealed record WorkflowFaultDirectiveOption(WorkflowResumeDirective Directive, bool Allowed, string? Reason)
{
    /// <summary>显示文字。</summary>
    public string Text => Directive.DisplayName;
}

/// <summary>工作台里一个停下的流程：它的停点、可选去向、已经给出的去向。</summary>
/// <param name="FlowName">流程名。</param>
/// <param name="StopPoints">停点（按到达顺序）。</param>
/// <param name="Options">可选去向。</param>
/// <param name="Directive">已经给出的去向；没有时为空。</param>
/// <param name="NeedsDirective">是否必须给去向（有故障或被中断的停点）；只被要求停下的流程不给时照常继续。</param>
public sealed record WorkflowFaultFlowView(
    string FlowName,
    IReadOnlyList<WorkflowStopPoint> StopPoints,
    IReadOnlyList<WorkflowFaultDirectiveOption> Options,
    WorkflowResumeDirective? Directive,
    bool NeedsDirective);

/// <summary>故障工作台显示的一次会话。</summary>
/// <param name="Session">会话。</param>
/// <param name="Flows">停下的流程。</param>
/// <param name="Fallback">兜底询问（处理流程没处理完，等操作员选择）；没有时为空。</param>
public sealed record WorkflowFaultSessionView(
    WorkflowFaultSession Session,
    IReadOnlyList<WorkflowFaultFlowView> Flows,
    WorkflowFaultFallbackRequest? Fallback)
{
    /// <summary>状态文字。</summary>
    public string StateText => Fallback is not null ? "等待操作员选择"
        : Session.State switch
        {
            WorkflowFaultSessionState.Collecting => "正在等流程停下",
            WorkflowFaultSessionState.Handling => Session.Attempt > 1 ? $"处理流程运行中（第 {Session.Attempt} 次）" : "处理流程运行中",
            _ => "已放行"
        };
}

/// <summary>
/// 故障工作台的模型（第 8 类）：显示当前故障会话（上报、受影响的流程、每个流程停在哪里、可选去向）和最近结束的会话；
/// 同时作为兜底界面（<see cref="IWorkflowFaultFallback"/>）：处理流程出错、没有给去向或去向不被允许时，
/// 在工作台里让操作员逐个流程选去向、重新运行处理流程或全部停止。
/// </summary>
/// <remarks>
/// 不改变故障处理规则：去向是否允许由停点判断（<see cref="WorkflowStopPoint.Allows"/>），放行由统管层负责。
/// 事件可能在任意线程触发，界面自己切回界面线程。
/// </remarks>
public sealed class WorkflowFaultWorkbenchModel : IWorkflowFaultFallback, IDisposable
{
    private readonly object _sync = new();
    private readonly WorkflowRunCenter _center;
    private readonly List<WorkflowFaultSession> _history = new();
    private readonly WorkflowRunCenterOptions? _options;
    private readonly IWorkflowFaultFallback? _previousFallback;
    private PendingFallback? _pending;
    private bool _disposed;

    /// <summary>
    /// 接到流程管理上。给出创建流程管理时用的 <paramref name="options"/> 时，同时接管兜底界面
    /// （<see cref="WorkflowRunCenterOptions.FaultFallback"/>，释放时还原）；不给时只显示，兜底仍由原来的设置处理。
    /// </summary>
    /// <param name="center">流程管理。</param>
    /// <param name="options">创建 <paramref name="center"/> 时传入的设置；为空时不接管兜底界面。</param>
    /// <param name="historyLimit">保留最近结束的会话数。</param>
    public WorkflowFaultWorkbenchModel(WorkflowRunCenter center, WorkflowRunCenterOptions? options = null, int historyLimit = 20)
    {
        _center = center ?? throw new ArgumentNullException(nameof(center));
        HistoryLimit = Math.Max(1, historyLimit);
        _center.FaultSessionStarted += OnSessionChanged;
        _center.FaultSessionEnded += OnSessionEnded;
        if (options is not null)
        {
            _options = options;
            _previousFallback = options.FaultFallback;
            options.FaultFallback = this;
            UsesFallback = true;
        }
    }

    /// <summary>保留最近结束的会话数。</summary>
    public int HistoryLimit { get; }

    /// <summary>是否接管了兜底界面。</summary>
    public bool UsesFallback { get; }

    /// <summary>会话开始、结束，或兜底询问出现、答复时发生（任意线程）。</summary>
    public event EventHandler? Changed;

    /// <summary>最近结束的会话，新的在前。</summary>
    public IReadOnlyList<WorkflowFaultSession> History
    {
        get
        {
            lock (_sync)
                return _history.ToArray();
        }
    }

    /// <summary>是否有等操作员选择的兜底询问。</summary>
    public bool IsAwaitingDecision
    {
        get
        {
            lock (_sync)
                return _pending is not null;
        }
    }

    /// <summary>取当前会话的显示数据；没有会话时为空。</summary>
    public WorkflowFaultSessionView? GetCurrent()
    {
        PendingFallback? pending;
        lock (_sync) pending = _pending;
        var session = pending?.Request.Session ?? _center.CurrentFaultSession;
        return session is null ? null : Describe(session, pending?.Request);
    }

    /// <summary>取某个会话（例如历史里的）的显示数据。</summary>
    /// <param name="session">会话。</param>
    public WorkflowFaultSessionView Describe(WorkflowFaultSession session) => Describe(session, null);

    /// <summary>兜底：按给出的去向放行（流程名 → 去向）。只能在等操作员选择时调用。</summary>
    /// <param name="directives">各流程的去向；必须给的流程都要有。"回退再继续"由统管层先回退再放行。</param>
    /// <param name="confirmManual">回退时不支持回退的节点交人确认；为空时遇到这类节点即停下，回到这里重新选。</param>
    /// <exception cref="InvalidOperationException">没有等待中的询问，或有流程的去向不被允许、缺去向。</exception>
    public void Release(
        IReadOnlyDictionary<string, WorkflowResumeDirective> directives,
        Func<WorkflowExecutionRecordEntry, CancellationToken, Task<bool>>? confirmManual = null)
    {
        Guard.NotNull(directives);
        var pending = TakePending(check: view =>
        {
            foreach (var flow in view.Flows)
            {
                if (!directives.TryGetValue(flow.FlowName, out var directive))
                {
                    if (flow.NeedsDirective) throw new InvalidOperationException($"流程 {flow.FlowName} 还没有选去向。");
                    continue;
                }
                var option = Evaluate(flow.StopPoints, directive);
                if (!option.Allowed) throw new InvalidOperationException($"流程 {flow.FlowName}：{option.Reason}");
            }
        });
        pending.Completion.TrySetResult(new WorkflowFaultFallbackDecision(WorkflowFaultFallbackChoice.UseDirectives,
            new Dictionary<string, WorkflowResumeDirective>(directives.ToDictionary(pair => pair.Key, pair => pair.Value), StringComparer.Ordinal),
            confirmManual));
        RaiseChanged();
    }

    /// <summary>兜底：重新运行处理流程（已经回退过的动作不会再回退）。</summary>
    /// <exception cref="InvalidOperationException">没有等待中的询问，或没有处理流程。</exception>
    public void RerunHandler()
    {
        var pending = TakePending(check: _ => { });
        if (!pending.Request.HasHandler)
        {
            lock (_sync) _pending ??= pending;
            throw new InvalidOperationException("这次故障没有处理流程可以重新运行。");
        }
        pending.Completion.TrySetResult(WorkflowFaultFallbackDecision.Rerun);
        RaiseChanged();
    }

    /// <summary>兜底：停止全部受影响的流程。</summary>
    /// <exception cref="InvalidOperationException">没有等待中的询问。</exception>
    public void StopAll()
    {
        var pending = TakePending(check: _ => { });
        pending.Completion.TrySetResult(WorkflowFaultFallbackDecision.StopAll);
        RaiseChanged();
    }

    /// <inheritdoc />
    public Task<WorkflowFaultFallbackDecision> DecideAsync(WorkflowFaultFallbackRequest request, CancellationToken cancellationToken)
    {
        Guard.NotNull(request);
        var pending = new PendingFallback(request);
        lock (_sync)
        {
            if (_disposed) return Task.FromResult(WorkflowFaultFallbackDecision.StopAll);
            _pending?.Completion.TrySetResult(WorkflowFaultFallbackDecision.StopAll);
            _pending = pending;
        }
        var registration = cancellationToken.Register(() =>
        {
            lock (_sync)
                if (ReferenceEquals(_pending, pending)) _pending = null;
            pending.Completion.TrySetCanceled();
            RaiseChanged();
        });
        _ = pending.Completion.Task.ContinueWith(_ => registration.Dispose(), TaskScheduler.Default);
        RaiseChanged();
        return pending.Completion.Task;
    }

    /// <summary>一个去向对一组停点是否允许（与统管层同一规则：停止作用于全部停点，其他去向作用于故障 / 中断的停点，没有时作用于第一个）。</summary>
    /// <param name="stops">一个流程的停点。</param>
    /// <param name="directive">去向。</param>
    public static WorkflowFaultDirectiveOption Evaluate(IReadOnlyList<WorkflowStopPoint> stops, WorkflowResumeDirective directive)
    {
        Guard.NotNull(stops);
        Guard.NotNull(directive);
        IEnumerable<WorkflowStopPoint> targets;
        if (directive.Kind == WorkflowResumeKind.Stop) targets = stops;
        else
        {
            var faulted = stops.Where(stop => stop.Reason != WorkflowStopReason.Requested).ToArray();
            targets = faulted.Length > 0 ? faulted : stops.Take(1);
        }
        foreach (var stop in targets)
            if (!stop.Allows(directive, out var reason))
                return new WorkflowFaultDirectiveOption(directive, false, reason);
        return new WorkflowFaultDirectiveOption(directive, true, null);
    }

    /// <summary>解除订阅；接管了兜底界面时还原，等待中的询问按“全部停止”答复。</summary>
    public void Dispose()
    {
        PendingFallback? pending;
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            pending = _pending;
            _pending = null;
        }
        _center.FaultSessionStarted -= OnSessionChanged;
        _center.FaultSessionEnded -= OnSessionEnded;
        if (_options is not null && ReferenceEquals(_options.FaultFallback, this)) _options.FaultFallback = _previousFallback;
        pending?.Completion.TrySetResult(WorkflowFaultFallbackDecision.StopAll);
    }

    private WorkflowFaultSessionView Describe(WorkflowFaultSession session, WorkflowFaultFallbackRequest? fallback)
    {
        var directives = session.Directives;
        var flows = session.StopPoints
            .GroupBy(stop => stop.FlowName, StringComparer.Ordinal)
            .Select(group =>
            {
                var stops = group.ToArray();
                return new WorkflowFaultFlowView(
                    group.Key,
                    stops,
                    Options(stops),
                    directives.TryGetValue(group.Key, out var directive) ? directive : null,
                    stops.Any(stop => stop.Reason != WorkflowStopReason.Requested));
            })
            .ToArray();
        return new WorkflowFaultSessionView(session, flows, fallback);
    }

    private static IReadOnlyList<WorkflowFaultDirectiveOption> Options(IReadOnlyList<WorkflowStopPoint> stops)
    {
        var candidates = new List<WorkflowResumeDirective>
        {
            WorkflowResumeDirective.RecoverInPlace("工作台：就地恢复")
        };
        var faulted = stops.Where(stop => stop.Reason != WorkflowStopReason.Requested).ToArray();
        var target = faulted.Length > 0 ? faulted : stops.Take(1).ToArray();
        // 入口只列所有目标停点都能回去的。
        var entries = target.Length == 0
            ? Array.Empty<string>()
            : target.Select(stop => stop.Entries.Select(entry => entry.Key))
                .Aggregate((left, right) => left.Intersect(right, StringComparer.Ordinal))
                .Distinct(StringComparer.Ordinal).ToArray();
        candidates.AddRange(entries.Select(key => WorkflowResumeDirective.RollBack(key, $"工作台：回退到入口 {key} 再继续")));
        candidates.Add(WorkflowResumeDirective.EndLap("工作台：结束本圈"));
        candidates.Add(WorkflowResumeDirective.Stop("工作台：停止流程"));
        return candidates.Select(directive => Evaluate(stops, directive)).ToArray();
    }

    private PendingFallback TakePending(Action<WorkflowFaultSessionView> check)
    {
        PendingFallback pending;
        lock (_sync)
        {
            pending = _pending ?? throw new InvalidOperationException("现在没有等待选择的故障处理。");
        }
        check(Describe(pending.Request.Session, pending.Request));
        lock (_sync)
        {
            if (!ReferenceEquals(_pending, pending)) throw new InvalidOperationException("这次询问已经答复过了。");
            _pending = null;
        }
        return pending;
    }

    private void OnSessionChanged(WorkflowFaultSession session) => RaiseChanged();

    private void OnSessionEnded(WorkflowFaultSession session)
    {
        lock (_sync)
        {
            _history.Remove(session);
            _history.Insert(0, session);
            if (_history.Count > HistoryLimit) _history.RemoveRange(HistoryLimit, _history.Count - HistoryLimit);
        }
        RaiseChanged();
    }

    private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);

    private sealed class PendingFallback(WorkflowFaultFallbackRequest request)
    {
        public WorkflowFaultFallbackRequest Request { get; } = request;

        public TaskCompletionSource<WorkflowFaultFallbackDecision> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
