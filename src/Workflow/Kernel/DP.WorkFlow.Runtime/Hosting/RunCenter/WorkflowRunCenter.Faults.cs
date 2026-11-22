namespace DP.WorkFlow;

/// <summary>
/// 统管层的故障会话（第 8 类）：上报故障 → 受影响的流程停下 → 运行处理流程（流程自带优先，其次路由表，最后默认）
/// → 核对各流程的去向 → 一起放行。处理流程出错或去向不被允许时进入兜底界面。
/// 同一时间只处理一个会话；处理期间新到的故障排在下一个会话里。
/// </summary>
public sealed partial class WorkflowRunCenter
{
    private readonly object _faultSync = new();
    private readonly Dictionary<WorkflowDocument, WorkflowExecutionPlan> _handlerPlans = new();
    private WorkflowFaultSession? _currentSession;
    private WorkflowFaultSession? _nextSession;
    private bool _sessionStarting;

    /// <summary>故障会话开始处理（处理流程即将运行）。</summary>
    public event Action<WorkflowFaultSession>? FaultSessionStarted;

    /// <summary>故障会话结束（各流程已拿到去向）。</summary>
    public event Action<WorkflowFaultSession>? FaultSessionEnded;

    /// <summary>当前的故障会话；没有时为空。</summary>
    public WorkflowFaultSession? CurrentFaultSession
    {
        get
        {
            lock (_faultSync)
                return _currentSession;
        }
    }

    /// <summary>
    /// 上报故障（业务系统调用）：按上报的停法让目标流程停下，等它们都停下后运行处理流程。
    /// 不在运行的目标流程不参与。
    /// </summary>
    /// <param name="report">故障上报。</param>
    /// <returns>接收这次上报的故障会话。</returns>
    /// <exception cref="ArgumentException">目标流程没有加载。</exception>
    public WorkflowFaultSession RaiseFault(WorkflowFaultReport report)
    {
        ThrowIfDisposed();
        Guard.NotNull(report);
        Guard.NotNullOrWhiteSpace(report.Message);
        var targets = report.TargetFlows.Count == 0
            ? Flows.Where(flow => flow.IsRunning).ToArray()
            : report.TargetFlows.Select(name => GetFlow(name) ?? throw new ArgumentException($"流程 {name} 没有加载。", nameof(report))).ToArray();
        WorkflowFaultSession session;
        lock (_faultSync)
        {
            session = CollectingSession(originFlow: null);
            session.AddFault(report);
            foreach (var flow in targets.Where(flow => flow.IsRunning))
                session.Expect(flow.Name);
        }
        PostCenterEvent("故障", WorkflowRunEventCategory.Recovery, "FaultRaised", report.Message,
            $"上报故障{(report.AlarmCode != 0 ? $"（报警 {report.AlarmCode}）" : string.Empty)}：{report.Message}；" +
            $"{(report.StopMode == WorkflowFaultStopMode.Interrupt ? "中断" : "停住")} {string.Join("、", targets.Select(flow => flow.Name))}");
        foreach (var flow in targets)
        {
            if (flow.Host.Engine is not { } engine || !flow.IsRunning)
                continue;
            if (report.StopMode == WorkflowFaultStopMode.Interrupt)
                engine.Interrupt(report.Message);
            else
                engine.RequestStop(report.Message);
        }
        TryStartFaultSession();
        return session;
    }

    /// <summary>流程停在停点上：放进收集中的会话，等去向。</summary>
    private async Task<WorkflowResumeDirective> OnFlowStoppedAsync(string flowName, WorkflowStopPoint point, CancellationToken cancellationToken)
    {
        WorkflowFaultSession session;
        WorkflowFaultSession.ParkedStop stop;
        // 停点上的流程名用流程管理里的名称（同一文档可以以不同名称加载多次）。
        point = point with { FlowName = flowName };
        lock (_faultSync)
        {
            session = CollectingSession(point.Reason == WorkflowStopReason.Requested ? null : flowName);
            if (point.Reason != WorkflowStopReason.Requested || session.Faults.Count == 0)
            {
                session.AddFault(new WorkflowFaultReport
                {
                    Message = point.Reason == WorkflowStopReason.Requested
                        ? $"流程 {flowName} 被要求停下：{point.Message}"
                        : point.Message ?? $"节点 {point.NodeTitle} 故障。",
                    AlarmCode = point.AlarmCode,
                    Source = flowName,
                    TargetFlows = new[] { flowName }
                });
            }
            if (point.Rejection is not null)
                session.LastFailure = point.Rejection;
            stop = session.Park(flowName, point);
        }
        PostCenterEvent(flowName, WorkflowRunEventCategory.Recovery, "FlowParked", point.Message ?? point.Reason.ToString(),
            $"停在 {point.Position}（{point.NodeTitle}）：{DescribeReason(point)}", GetFlow(flowName)?.Outlet);
        using (cancellationToken.Register(() =>
               {
                   if (session.Unpark(stop))
                       stop.Directive.TrySetCanceled();
                   TryStartFaultSession();
               }))
        {
            TryStartFaultSession();
            return await stop.Directive.Task.ConfigureAwait(false);
        }
    }

    private static string DescribeReason(WorkflowStopPoint point) => point.Reason switch
    {
        WorkflowStopReason.Fault => "故障 " + point.Message,
        WorkflowStopReason.Interrupted => "被中断 " + point.Message,
        _ => "被要求停下 " + point.Message
    };

    /// <summary>收集中的会话：当前会话还在收集时并入它，正在处理时排进下一个。调用方持有 <see cref="_faultSync"/>。</summary>
    private WorkflowFaultSession CollectingSession(string? originFlow)
    {
        if (_currentSession is null)
            return _currentSession = new WorkflowFaultSession(this, originFlow);
        if (_currentSession.State == WorkflowFaultSessionState.Collecting)
            return _currentSession;
        return _nextSession ??= new WorkflowFaultSession(this, originFlow);
    }

    /// <summary>当前会话里受影响的流程都已停下（或已不在运行）时开始处理。</summary>
    private void TryStartFaultSession()
    {
        WorkflowFaultSession? session;
        bool empty;
        lock (_faultSync)
        {
            if (_disposed)
                return;
            if (_currentSession is null && _nextSession is not null)
            {
                _currentSession = _nextSession;
                _nextSession = null;
            }
            session = _currentSession;
            if (session is null || session.State != WorkflowFaultSessionState.Collecting || _sessionStarting)
                return;
            var waiting = session.AffectedFlows.Any(name => !session.IsParked(name) && (GetFlow(name)?.IsRunning ?? false));
            if (waiting)
                return;
            empty = !session.HasStops;
            if (empty)
            {
                // 受影响的流程都没有停在这里（已经结束或不在运行）：会话直接结束。
                session.State = WorkflowFaultSessionState.Ended;
                _currentSession = null;
            }
            else
                _sessionStarting = true;
        }
        if (empty)
        {
            PostCenterEvent("故障", WorkflowRunEventCategory.Recovery, "FaultSessionEnded", "没有流程停下。", "故障处理结束：没有流程停下。");
            TryStartFaultSession();
            return;
        }
        _ = Task.Run(() => HandleSessionAsync(session));
    }

    private async Task HandleSessionAsync(WorkflowFaultSession session)
    {
        try
        {
            // 同一故障可能让同一流程的多条路径陆续停下，稍等让它们并入同一个会话。
            if (_options.FaultSettleMilliseconds > 0)
                await Task.Delay(_options.FaultSettleMilliseconds, _lifetime.Token).ConfigureAwait(false);
            lock (_faultSync)
            {
                session.State = WorkflowFaultSessionState.Handling;
                _sessionStarting = false;
            }
            PostCenterEvent("故障", WorkflowRunEventCategory.Recovery, "FaultSessionStarted", session.PrimaryFault.Message,
                $"开始故障处理：{session.PrimaryFault.Message}；停下的流程 {string.Join("、", session.StopPoints.Select(point => point.FlowName).Distinct())}");
            FaultSessionStarted?.Invoke(session);
            var handler = ResolveFaultHandler(session);
            var runHandler = handler is not null;
            string? failure = handler is null ? "没有配置处理流程。" : null;
            while (true)
            {
                if (runHandler)
                {
                    session.Attempt++;
                    session.ClearDirectives();
                    failure = await RunFaultHandlerAsync(session, handler!.Value.Name, handler.Value.Plan).ConfigureAwait(false)
                              ?? TryRelease(session);
                    if (failure is null)
                        return;
                }
                session.LastFailure = failure;
                PostCenterEvent("故障", WorkflowRunEventCategory.Recovery, "FaultHandlingFailed", failure!, "故障处理没有完成：" + failure);
                var decision = await AskFallbackAsync(session, failure!, handler is not null).ConfigureAwait(false);
                switch (decision.Choice)
                {
                    case WorkflowFaultFallbackChoice.RerunHandler when handler is not null:
                        // 重新运行处理流程：已经撤回过的动作在合并执行记录里有标记，不会再撤回一次。
                        runHandler = true;
                        continue;
                    case WorkflowFaultFallbackChoice.UseDirectives:
                        runHandler = false;
                        session.ClearDirectives();
                        try
                        {
                            foreach (var pair in decision.Directives ?? new Dictionary<string, WorkflowResumeDirective>())
                                session.SetDirective(pair.Key, pair.Value);
                            failure = TryRelease(session);
                        }
                        catch (InvalidOperationException exception)
                        {
                            failure = exception.Message;
                        }
                        if (failure is null)
                            return;
                        continue;
                    default:
                        ReleaseAllStopped(session, "故障处理选择停止流程。");
                        return;
                }
            }
        }
        catch (Exception exception)
        {
            ReleaseAllStopped(session, "故障处理出错：" + exception.Message);
        }
        finally
        {
            lock (_faultSync)
            {
                session.State = WorkflowFaultSessionState.Ended;
                _sessionStarting = false;
                if (ReferenceEquals(_currentSession, session))
                    _currentSession = null;
            }
            if (!_disposed)
            {
                PostCenterEvent("故障", WorkflowRunEventCategory.Recovery, "FaultSessionEnded", session.PrimaryFault.Message,
                    "故障处理结束：" + string.Join("；", session.Directives.Select(pair => $"{pair.Key} {pair.Value.DisplayName}")));
                FaultSessionEnded?.Invoke(session);
                TryStartFaultSession();
            }
        }
    }

    private async Task<WorkflowFaultFallbackDecision> AskFallbackAsync(WorkflowFaultSession session, string failure, bool hasHandler)
    {
        var fallback = _options.FaultFallback ?? _services?.GetService(typeof(IWorkflowFaultFallback)) as IWorkflowFaultFallback;
        if (fallback is null)
            return WorkflowFaultFallbackDecision.StopAll;
        return await fallback.DecideAsync(new WorkflowFaultFallbackRequest(session, failure, hasHandler), _lifetime.Token).ConfigureAwait(false)
               ?? WorkflowFaultFallbackDecision.StopAll;
    }

    /// <summary>核对各停点的去向，全部合格时一起放行；返回不合格的原因。</summary>
    private static string? TryRelease(WorkflowFaultSession session)
    {
        var missing = session.Assign(out var assignment);
        if (missing.Count > 0)
            return $"处理流程没有给流程 {string.Join("、", missing)} 去向。";
        foreach (var (stop, directive) in assignment)
        {
            if (!stop.Point.Allows(directive, out var reason))
                return $"流程 {stop.FlowName}：{reason}";
        }
        foreach (var (stop, directive) in assignment)
            stop.Directive.TrySetResult(directive);
        return null;
    }

    private static void ReleaseAllStopped(WorkflowFaultSession session, string message)
    {
        foreach (var stop in session.TakeStops())
            stop.Directive.TrySetResult(WorkflowResumeDirective.Stop(message));
    }

    /// <summary>找处理流程：出故障的流程自带的处理流程块优先，其次路由表第一个匹配项，最后默认处理流程。</summary>
    private (string Name, WorkflowExecutionPlan Plan)? ResolveFaultHandler(WorkflowFaultSession session)
    {
        var origin = session.OriginFlow ?? session.StopPoints.FirstOrDefault(point => point.Reason != WorkflowStopReason.Requested)?.FlowName;
        if (origin is not null && GetFlow(origin) is { } flow)
        {
            var plan = flow.Plan;
            foreach (var id in plan.NodeIds)
            {
                if (plan.GetNodeOrThrow(id) is IWorkflowFaultHandlerNode)
                    return ($"{origin} 的处理流程", plan.GetChildPlanOrThrow(id));
            }
        }
        foreach (var route in _options.FaultRoutes)
        {
            if (route.Matches(session))
                return (route.Name, CompileHandler(route.Handler));
        }
        return _options.DefaultFaultHandler is { } fallback ? ("默认处理流程", CompileHandler(fallback)) : null;
    }

    private WorkflowExecutionPlan CompileHandler(WorkflowDocument document)
    {
        lock (_handlerPlans)
        {
            if (!_handlerPlans.TryGetValue(document, out var plan))
                _handlerPlans[document] = plan = new WorkflowCompiler(_nodeCatalog).Compile(document);
            return plan;
        }
    }

    /// <summary>
    /// 执行处理流程：在故障会话里嵌套运行（准备按嵌套作用域声明，不退役也不重新取得根运行的资源），
    /// 处理流程里的节点从服务里取得故障会话。返回出错原因，成功时为空。
    /// </summary>
    private async Task<string?> RunFaultHandlerAsync(WorkflowFaultSession session, string name, WorkflowExecutionPlan plan)
    {
        var origin = session.OriginFlow is { } originName ? GetFlow(originName) : null;
        var options = new WorkflowExecutionOptions();
        options.Recording.Sink = new WorkflowOutletSink(name, origin?.Outlet, Outlet, options.Notices, CollectNodeTitles(plan), null);
        var services = new FaultSessionServices(_services, session);
        try
        {
            var bound = new WorkflowRuntimeBinder(_handlerCatalog).Bind(plan);
            WorkflowRuntimeCapabilityValidator.Validate(bound, services);
            if (services.GetService(typeof(IWorkflowRunPreparationService)) is IWorkflowRunPreparationService preparation)
                await preparation.PrepareAsync(new WorkflowRunPreparationContext(
                        EnumerateNodes(plan).ToArray(),
                        WorkflowRunScopeKind.Nested,
                        session.StopPoints.FirstOrDefault(point => point.Reason != WorkflowStopReason.Requested)?.Position.NodeId,
                        services),
                    _lifetime.Token).ConfigureAwait(false);
            var context = new WorkflowContext(services, PublicData);
            var result = await new WorkflowEngine(bound, context, options).RunAsync(_lifetime.Token).ConfigureAwait(false);
            return result.Success ? null : $"处理流程出错：{result.Message}";
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return $"处理流程出错：{exception.Message}";
        }
    }

    private static IEnumerable<IWorkflowNodeModel> EnumerateNodes(WorkflowExecutionPlan plan)
    {
        foreach (var id in plan.NodeIds)
            yield return plan.GetNodeOrThrow(id);
        foreach (var child in plan.ChildPlans.Values)
        foreach (var node in EnumerateNodes(child))
            yield return node;
    }

    /// <summary>处理流程的服务：在宿主服务之上提供故障会话。</summary>
    private sealed class FaultSessionServices(IServiceProvider? inner, WorkflowFaultSession session) : IServiceProvider
    {
        public object? GetService(Type serviceType) =>
            serviceType == typeof(WorkflowFaultSession) ? session
            // 根运行的资源只由根运行宿主退役和取得；处理流程嵌套在故障会话里，不能碰它们。
            : serviceType == typeof(IWorkflowRunResourceOwner) || serviceType == typeof(IWorkflowRunScopeOwner) ? null
            : inner?.GetService(serviceType);
    }

    /// <summary>一个流程的停点处理方：把停点交给统管层的故障会话。</summary>
    private sealed class FlowStopPointHandler(WorkflowRunCenter center, string flowName) : IWorkflowStopPointHandler
    {
        public Task<WorkflowResumeDirective> OnStoppedAsync(WorkflowEngine engine, WorkflowStopPoint stopPoint, CancellationToken cancellationToken) =>
            center.OnFlowStoppedAsync(flowName, stopPoint, cancellationToken);
    }
}
