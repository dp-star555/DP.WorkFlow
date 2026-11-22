namespace DP.WorkFlow;

/// <summary>流程管理（运行中心）的设置。</summary>
public sealed class WorkflowRunCenterOptions
{
    /// <summary>公共数据是否只允许使用已登记的键。</summary>
    public bool RequireDeclaration { get; set; }

    /// <summary>"保持"项的存取，例如 <c>WorkflowPublicDataJsonFileStore</c>；为空时保持项只在内存中。</summary>
    public IWorkflowPublicDataPersistence? Persistence { get; set; }

    /// <summary>全局出口设置。</summary>
    public WorkflowOutletOptions GlobalOutlet { get; set; } = new();

    /// <summary>每个流程出口的默认设置。</summary>
    public WorkflowOutletOptions FlowOutlet { get; set; } = new();

    /// <summary>故障路由表：流程没有自带处理流程时，按顺序取第一个匹配项。</summary>
    public IList<WorkflowFaultRoute> FaultRoutes { get; } = new List<WorkflowFaultRoute>();

    /// <summary>默认处理流程：自带和路由表都没有时使用；为空时直接进入兜底。</summary>
    public WorkflowDocument? DefaultFaultHandler { get; set; }

    /// <summary>兜底界面；为空时从宿主服务里找，再没有时停止全部受影响的流程。</summary>
    public IWorkflowFaultFallback? FaultFallback { get; set; }

    /// <summary>第一个停点到达后，再等其他路径一起停下的时间（毫秒），避免同一故障拆成多次处理。</summary>
    public int FaultSettleMilliseconds { get; set; } = 50;

    /// <summary>合并执行记录最多保留的记录数（联合逆序撤回用）。</summary>
    public int ExecutionRecordCapacity { get; set; } = 500;
}

/// <summary>
/// 流程管理（流程运行中心，R2-d / R1-05 / R3-d）：由外部程序创建的库内对象。
/// 持有各流程共用的公共数据中心和全局出口，管理流程列表，统一创建运行宿主和引擎。
/// </summary>
/// <remarks>
/// 加载流程时按流程文件的公共数据清单逐项登记，先登记者为准；形态或类型冲突时拒绝加载。
/// 不使用流程管理时，单个运行宿主自建仅供自己使用的公共数据中心，行为不变。
/// </remarks>
public sealed partial class WorkflowRunCenter : IDisposable
{
    private const string LostCategory = "记录丢失";
    private readonly object _sync = new();
    private readonly WorkflowNodeCatalog _nodeCatalog;
    private readonly WorkflowNodeHandlerCatalog _handlerCatalog;
    private readonly IServiceProvider? _services;
    private readonly WorkflowRunCenterOptions _options;
    private readonly List<WorkflowRunCenterFlow> _flows = new();
    private readonly CancellationTokenSource _lifetime = new();
    private bool _disposed;

    /// <summary>初始化流程管理。</summary>
    /// <param name="nodeCatalog">编译期节点目录。</param>
    /// <param name="handlerCatalog">运行期处理器目录。</param>
    /// <param name="options">设置；为空时使用默认值。</param>
    /// <param name="services">宿主服务；同类服务宿主提供的优先。</param>
    public WorkflowRunCenter(
        WorkflowNodeCatalog nodeCatalog,
        WorkflowNodeHandlerCatalog handlerCatalog,
        WorkflowRunCenterOptions? options = null,
        IServiceProvider? services = null)
    {
        _nodeCatalog = nodeCatalog ?? throw new ArgumentNullException(nameof(nodeCatalog));
        _handlerCatalog = handlerCatalog ?? throw new ArgumentNullException(nameof(handlerCatalog));
        _options = options ?? new WorkflowRunCenterOptions();
        _services = services;
        PublicData = new WorkflowPublicDataCenter(_options.RequireDeclaration, _options.Persistence);
        Outlet = new WorkflowOutlet(_options.GlobalOutlet);
        PublicData.PersistenceFailed += OnPersistenceFailed;
        ExecutionRecord = new WorkflowExecutionRecord(_options.ExecutionRecordCapacity);
    }

    /// <summary>合并执行记录：全部流程成功执行过的节点，按真实先后；联合逆序撤回按它的倒序撤回。</summary>
    public WorkflowExecutionRecord ExecutionRecord { get; }

    /// <summary>各流程共用的公共数据中心；外部读写公共数据也经过它。</summary>
    public WorkflowPublicDataCenter PublicData { get; }

    /// <summary>全局出口：收全部流程的事件，每条带所属顶层流程名。</summary>
    public WorkflowOutlet Outlet { get; }

    /// <summary>已加载的流程，按加载顺序。</summary>
    public IReadOnlyList<WorkflowRunCenterFlow> Flows
    {
        get
        {
            lock (_sync)
                return _flows.ToArray();
        }
    }

    /// <summary>按名称查找流程。</summary>
    /// <param name="name">流程名。</param>
    public WorkflowRunCenterFlow? GetFlow(string name)
    {
        lock (_sync)
            return _flows.FirstOrDefault(flow => string.Equals(flow.Name, name, StringComparison.Ordinal));
    }

    /// <summary>
    /// 加载一个流程：编译、按清单登记公共数据（冲突时拒绝）、创建运行宿主。
    /// </summary>
    /// <param name="document">流程文档。</param>
    /// <param name="name">流程名；为空时使用文档名。</param>
    /// <param name="executionOptions">执行选项；为空时新建。出口会接到它的记录配置上，原有 Sink 照常收到事件。</param>
    /// <returns>加载的流程。</returns>
    /// <exception cref="WorkflowPublicDataConflictException">公共数据清单与已登记的形态或类型不一致。</exception>
    /// <exception cref="InvalidOperationException">同名流程已加载。</exception>
    public WorkflowRunCenterFlow AddFlow(WorkflowDocument document, string? name = null, WorkflowExecutionOptions? executionOptions = null)
    {
        ThrowIfDisposed();
        Guard.NotNull(document);
        var plan = new WorkflowCompiler(_nodeCatalog).Compile(document);
        var flowName = string.IsNullOrWhiteSpace(name) ? plan.Name : name!.Trim();
        if (string.IsNullOrWhiteSpace(flowName))
            throw new ArgumentException("流程名不能为空。", nameof(name));
        lock (_sync)
        {
            if (_flows.Any(flow => string.Equals(flow.Name, flowName, StringComparison.Ordinal)))
                throw new InvalidOperationException($"流程 {flowName} 已加载。");
            PublicData.DeclareAll(plan.PublicData);
            var options = executionOptions ?? new WorkflowExecutionOptions();
            // 流程停下（节点故障、被中断、被要求停下）时交给故障会话；调用方自带处理方时以调用方为准。
            options.StopPointHandler ??= new FlowStopPointHandler(this, flowName);
            var journal = new FlowJournal(ExecutionRecord, flowName);
            options.ExecutionJournal ??= journal;
            var outlet = new WorkflowOutlet(_options.FlowOutlet);
            var sink = new WorkflowOutletSink(flowName, outlet, Outlet, options.Notices, CollectNodeTitles(plan), options.Recording.Sink);
            options.Recording.Sink = sink;
            var host = CreateHost(options);
            host.ConfigurePlan(plan, new WorkflowContext(_services, PublicData));
            host.StateChanged += _ => TryStartFaultSession();
            var flow = new WorkflowRunCenterFlow(flowName, plan, host, outlet, ownsHost: true, journal);
            _flows.Add(flow);
            return flow;
        }
    }

    /// <summary>
    /// 接管一个由调用方创建和配置的运行宿主（例如设计器里的运行）：它出故障或被要求停下时同样交给故障会话，
    /// 处理流程按宿主当前配置的流程查找。宿主仍由调用方释放；它的公共数据不经过本流程管理。
    /// </summary>
    /// <param name="name">流程名。</param>
    /// <param name="host">运行宿主。</param>
    /// <returns>登记的流程。</returns>
    /// <exception cref="InvalidOperationException">同名流程已加载。</exception>
    public WorkflowRunCenterFlow AttachHost(string name, WorkflowRuntimeHost host)
    {
        ThrowIfDisposed();
        Guard.NotNullOrWhiteSpace(name);
        Guard.NotNull(host);
        var flowName = name.Trim();
        lock (_sync)
        {
            if (_flows.Any(flow => string.Equals(flow.Name, flowName, StringComparison.Ordinal)))
                throw new InvalidOperationException($"流程 {flowName} 已加载。");
            var options = host.ExecutionOptions;
            options.StopPointHandler ??= new FlowStopPointHandler(this, flowName);
            var journal = new FlowJournal(ExecutionRecord, flowName);
            options.ExecutionJournal ??= journal;
            var outlet = new WorkflowOutlet(_options.FlowOutlet);
            options.Recording.Sink = new WorkflowOutletSink(flowName, outlet, Outlet, options.Notices,
                new Dictionary<string, string>(StringComparer.Ordinal), options.Recording.Sink);
            host.StateChanged += _ => TryStartFaultSession();
            var flow = new WorkflowRunCenterFlow(flowName, null, host, outlet, ownsHost: false, journal);
            _flows.Add(flow);
            return flow;
        }
    }

    /// <summary>卸下一个未在运行的流程。已登记的公共数据保留。</summary>
    /// <param name="name">流程名。</param>
    /// <returns>找到并卸下返回 true。</returns>
    /// <exception cref="InvalidOperationException">流程正在运行。</exception>
    public bool RemoveFlow(string name)
    {
        WorkflowRunCenterFlow? flow;
        lock (_sync)
        {
            flow = _flows.FirstOrDefault(item => string.Equals(item.Name, name, StringComparison.Ordinal));
            if (flow is null)
                return false;
            if (flow.IsRunning)
                throw new InvalidOperationException($"流程 {name} 正在运行，不能卸下。");
            _flows.Remove(flow);
        }
        flow.Dispose();
        return true;
    }

    /// <summary>
    /// 创建运行宿主的统一方法（R3-d）：使用本流程管理的节点目录和处理器目录。
    /// 通过 <see cref="AddFlow"/> 加载的流程都经由这里创建。
    /// </summary>
    /// <param name="executionOptions">执行选项。</param>
    /// <returns>新的运行宿主，调用方负责配置和释放。</returns>
    public WorkflowRuntimeHost CreateHost(WorkflowExecutionOptions? executionOptions = null)
    {
        ThrowIfDisposed();
        return new WorkflowRuntimeHost(_nodeCatalog, _handlerCatalog, executionOptions);
    }

    /// <summary>启动全部未在运行的流程，不等待它们结束。</summary>
    /// <param name="cancellationToken">取消全部本次启动的运行。</param>
    /// <returns>各流程本次运行的任务，按加载顺序。</returns>
    public IReadOnlyList<Task<WorkflowRunResult>> StartAll(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return Flows.Select(flow => flow.Host.RunAsync(cancellationToken)).ToArray();
    }

    /// <summary>停止全部流程并等待它们结束。</summary>
    public Task StopAllAsync() => Task.WhenAll(Flows.Select(flow => flow.Host.StopAsync()));

    /// <summary>各流程当前状态，按加载顺序。</summary>
    public IReadOnlyList<(string Name, E_WorkflowExecutionState State)> GetStates() =>
        Flows.Select(flow => (flow.Name, flow.State)).ToArray();

    /// <summary>取消全部流程，释放宿主和出口。</summary>
    public void Dispose()
    {
        WorkflowRunCenterFlow[] flows;
        lock (_sync)
        {
            if (_disposed)
                return;
            _disposed = true;
            _lifetime.Cancel();
            flows = _flows.ToArray();
            _flows.Clear();
        }
        foreach (var flow in flows)
            flow.Dispose();
        PublicData.PersistenceFailed -= OnPersistenceFailed;
        Outlet.Dispose();
        _lifetime.Dispose();
    }

    private void OnPersistenceFailed(string message) =>
        PostCenterEvent("公共数据", WorkflowRunEventCategory.Fault, "PublicDataPersistenceFailed", message, "保持数据保存失败：" + message);

    /// <summary>向全局出口（有流程时也向该流程出口）发一条统管层自己的事件。</summary>
    private void PostCenterEvent(string source, WorkflowRunEventCategory category, string eventType, string message, string line,
        WorkflowOutlet? flowOutlet = null)
    {
        var runEvent = new WorkflowRunEvent(
            Guid.Empty, 0, WorkflowClock.UtcNow, category, eventType,
            WorkflowEventWriteMode.Buffered, null, null, null, null, null, null, 1,
            new Dictionary<string, WorkflowTraceValue>(StringComparer.Ordinal)
            {
                ["Message"] = new(WorkflowTraceValueKind.Scalar, typeof(string).FullName!, message, message)
            });
        var item = new WorkflowOutletEvent(source, runEvent, WorkflowLogLevel.Warning, $"[{source}] {line}");
        flowOutlet?.Post(item);
        Outlet.Post(item);
    }

    private static IReadOnlyDictionary<string, string> CollectNodeTitles(WorkflowExecutionPlan plan)
    {
        var titles = new Dictionary<string, string>(StringComparer.Ordinal);
        void Visit(WorkflowExecutionPlan current)
        {
            foreach (var id in current.NodeIds)
            {
                var title = current.GetNodeOrThrow(id).Title;
                if (!string.IsNullOrWhiteSpace(title) && !titles.ContainsKey(id))
                    titles[id] = title;
            }
            foreach (var child in current.ChildPlans.Values)
                Visit(child);
        }
        Visit(plan);
        return titles;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(WorkflowRunCenter));
    }

    /// <summary>一个流程的执行记录入口：记入合并执行记录，并保管本流程的恢复轮次号。</summary>
    internal sealed class FlowJournal(WorkflowExecutionRecord record, string flowName) : IWorkflowExecutionJournal
    {
        private int _round;

        public int RecoveryRound => Volatile.Read(ref _round);

        public void NodeCompleted(WorkflowJournalEntry entry) => record.Add(flowName, RecoveryRound, entry);

        internal void Advance() => Interlocked.Increment(ref _round);
    }

    /// <summary>把一个流程（含子运行）的记录事件转给流程出口和全局出口；在记录器后台线程上调用。</summary>
    private sealed class WorkflowOutletSink(
        string flowName,
        WorkflowOutlet? flowOutlet,
        WorkflowOutlet globalOutlet,
        WorkflowNoticeBoard notices,
        IReadOnlyDictionary<string, string> nodeTitles,
        IWorkflowRunEventSink? inner) : IWorkflowRunEventSink
    {
        public async ValueTask<WorkflowRunEventWriteResult> WriteAsync(
            IReadOnlyList<WorkflowRunEvent> events,
            bool requestImmediateFlush,
            CancellationToken cancellationToken)
        {
            foreach (var runEvent in events)
            {
                var level = WorkflowOutlet.GetLevel(runEvent);
                if (!(flowOutlet?.Accepts(runEvent.Category, level) ?? false) && !globalOutlet.Accepts(runEvent.Category, level))
                    continue;
                string? title = null;
                if (runEvent.NodeId is { } nodeId)
                    nodeTitles.TryGetValue(nodeId, out title);
                var item = new WorkflowOutletEvent(flowName, runEvent, level, WorkflowOutlet.FormatLine(flowName, runEvent, title));
                var dropped = (flowOutlet?.Post(item) ?? 0) + globalOutlet.Post(item);
                if (dropped > 0)
                    notices.Raise(runEvent.RunId, "$", null, LostCategory, "出口处理不过来，已丢弃最早的记录。");
            }
            return inner is null
                ? WorkflowRunEventWriteResult.Success
                : await inner.WriteAsync(events, requestImmediateFlush, cancellationToken).ConfigureAwait(false);
        }

        public ValueTask FlushAsync(CancellationToken cancellationToken) =>
            inner?.FlushAsync(cancellationToken) ?? default;
    }
}

/// <summary>流程管理中的一个流程。</summary>
public sealed class WorkflowRunCenterFlow : IDisposable
{
    private readonly WorkflowExecutionPlan? _plan;
    private readonly bool _ownsHost;
    private readonly WorkflowRunCenter.FlowJournal _journal;

    internal WorkflowRunCenterFlow(string name, WorkflowExecutionPlan? plan, WorkflowRuntimeHost host, WorkflowOutlet outlet, bool ownsHost,
        WorkflowRunCenter.FlowJournal journal)
    {
        _journal = journal;
        Name = name;
        _plan = plan;
        Host = host;
        Outlet = outlet;
        _ownsHost = ownsHost;
    }

    /// <summary>流程名（顶层），也是出口事件里的流程名。</summary>
    public string Name { get; }

    /// <summary>编译后的执行计划；接管的宿主为它当前配置的计划。</summary>
    public WorkflowExecutionPlan Plan => _plan ?? Host.Plan ?? throw new InvalidOperationException($"流程 {Name} 还没有配置。");

    /// <summary>运行宿主：启动、暂停、停止、快照、提示都经由它。</summary>
    public WorkflowRuntimeHost Host { get; }

    /// <summary>该流程的出口：只出该流程（含子流程、循环块每一圈）的事件。</summary>
    public WorkflowOutlet Outlet { get; }

    /// <summary>当前状态。</summary>
    public E_WorkflowExecutionState State => Host.State;

    /// <summary>恢复轮次号：故障会话每放行本流程一次加一。跨流程交接时带上它，对方能认出恢复前的旧握手。</summary>
    public int RecoveryRound => _journal.RecoveryRound;

    internal void AdvanceRecoveryRound() => _journal.Advance();

    /// <summary>是否正在运行（含暂停中）。</summary>
    public bool IsRunning => State is E_WorkflowExecutionState.Running or E_WorkflowExecutionState.Paused;

    /// <summary>启动一次运行；已在运行时返回当前运行的任务。</summary>
    /// <param name="cancellationToken">取消本次运行。</param>
    public Task<WorkflowRunResult> RunAsync(CancellationToken cancellationToken = default) => Host.RunAsync(cancellationToken);

    /// <summary>停止并等待结束。</summary>
    public Task StopAsync() => Host.StopAsync();

    /// <inheritdoc />
    public void Dispose()
    {
        if (_ownsHost)
            Host.Dispose();
        Outlet.Dispose();
    }
}
