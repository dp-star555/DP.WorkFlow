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
}

/// <summary>
/// 流程管理（流程运行中心，R2-d / R1-05 / R3-d）：由外部程序创建的库内对象。
/// 持有各流程共用的公共数据中心和全局出口，管理流程列表，统一创建运行宿主和引擎。
/// </summary>
/// <remarks>
/// 加载流程时按流程文件的公共数据清单逐项登记，先登记者为准；形态或类型冲突时拒绝加载。
/// 不使用流程管理时，单个运行宿主自建仅供自己使用的公共数据中心，行为不变。
/// </remarks>
public sealed class WorkflowRunCenter : IDisposable
{
    private const string LostCategory = "记录丢失";
    private readonly object _sync = new();
    private readonly WorkflowNodeCatalog _nodeCatalog;
    private readonly WorkflowNodeHandlerCatalog _handlerCatalog;
    private readonly IServiceProvider? _services;
    private readonly WorkflowRunCenterOptions _options;
    private readonly List<WorkflowRunCenterFlow> _flows = new();
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
    }

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
            var outlet = new WorkflowOutlet(_options.FlowOutlet);
            var sink = new WorkflowOutletSink(flowName, outlet, Outlet, options.Notices, CollectNodeTitles(plan), options.Recording.Sink);
            options.Recording.Sink = sink;
            var host = CreateHost(options);
            host.ConfigurePlan(plan, new WorkflowContext(_services, PublicData));
            var flow = new WorkflowRunCenterFlow(flowName, plan, host, outlet);
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
            flows = _flows.ToArray();
            _flows.Clear();
        }
        foreach (var flow in flows)
            flow.Dispose();
        PublicData.PersistenceFailed -= OnPersistenceFailed;
        Outlet.Dispose();
    }

    private void OnPersistenceFailed(string message)
    {
        var runEvent = new WorkflowRunEvent(
            Guid.Empty, 0, WorkflowClock.UtcNow, WorkflowRunEventCategory.Fault, "PublicDataPersistenceFailed",
            WorkflowEventWriteMode.Buffered, null, null, null, null, null, null, 1,
            new Dictionary<string, WorkflowTraceValue>(StringComparer.Ordinal)
            {
                ["Message"] = new(WorkflowTraceValueKind.Scalar, typeof(string).FullName!, message, message)
            });
        Outlet.Post(new WorkflowOutletEvent("公共数据", runEvent, WorkflowLogLevel.Warning, "[公共数据] 保持数据保存失败：" + message));
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

    /// <summary>把一个流程（含子运行）的记录事件转给流程出口和全局出口；在记录器后台线程上调用。</summary>
    private sealed class WorkflowOutletSink(
        string flowName,
        WorkflowOutlet flowOutlet,
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
                if (!flowOutlet.Accepts(runEvent.Category, level) && !globalOutlet.Accepts(runEvent.Category, level))
                    continue;
                string? title = null;
                if (runEvent.NodeId is { } nodeId)
                    nodeTitles.TryGetValue(nodeId, out title);
                var item = new WorkflowOutletEvent(flowName, runEvent, level, WorkflowOutlet.FormatLine(flowName, runEvent, title));
                var dropped = flowOutlet.Post(item) + globalOutlet.Post(item);
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
    internal WorkflowRunCenterFlow(string name, WorkflowExecutionPlan plan, WorkflowRuntimeHost host, WorkflowOutlet outlet)
    {
        Name = name;
        Plan = plan;
        Host = host;
        Outlet = outlet;
    }

    /// <summary>流程名（顶层），也是出口事件里的流程名。</summary>
    public string Name { get; }

    /// <summary>编译后的执行计划。</summary>
    public WorkflowExecutionPlan Plan { get; }

    /// <summary>运行宿主：启动、暂停、停止、快照、提示都经由它。</summary>
    public WorkflowRuntimeHost Host { get; }

    /// <summary>该流程的出口：只出该流程（含子流程、循环块每一圈）的事件。</summary>
    public WorkflowOutlet Outlet { get; }

    /// <summary>当前状态。</summary>
    public E_WorkflowExecutionState State => Host.State;

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
        Host.Dispose();
        Outlet.Dispose();
    }
}
