namespace DP.WorkFlow;

/// <summary>一次运行的概要（运行下拉用）。</summary>
/// <param name="Index">在该流程运行列表里的位置（0 = 最早保留的一次）。</param>
/// <param name="RunId">运行 ID。</param>
/// <param name="Started">开始时间。</param>
/// <param name="Ended">结束时间；进行中为空。</param>
/// <param name="State">结果。</param>
/// <param name="Events">事件条数。</param>
/// <param name="Steps">节点执行次数。</param>
/// <param name="Failures">失败 / 故障条数。</param>
/// <param name="Notices">提示条数。</param>
public sealed record WorkflowRunLogSummary(int Index, Guid RunId, DateTimeOffset Started, DateTimeOffset? Ended, WorkflowRunLogState State,
    long Events, long Steps, int Failures, int Notices);

/// <summary>
/// 运行日志本：收集每个流程最近若干次运行的事件，按运行建成日志树（<see cref="WorkflowRunLog"/>）。线程安全。
/// </summary>
/// <remarks>
/// 挂在流程管理的每个流程出口上（<see cref="Attach(WorkflowRunCenter)"/>）；流程管理要用 <see cref="OutletOptions"/> 创建，
/// 否则出口按默认级别把并行派发 / 汇聚、进度等调试级事件过滤掉。只在内存里，程序重启后清空。
/// </remarks>
public sealed class WorkflowRunLogBook
{
    private readonly object _sync = new();
    private readonly Dictionary<string, FlowLogs> _flows = new(StringComparer.Ordinal);
    private long _version;

    /// <summary>创建运行日志本。</summary>
    /// <param name="maxRuns">每个流程保留最近多少次运行。</param>
    /// <param name="maxRowsPerRun">每次运行最多多少行。</param>
    public WorkflowRunLogBook(int maxRuns = 20, int maxRowsPerRun = 100_000)
    {
        MaxRuns = Math.Max(1, maxRuns);
        MaxRowsPerRun = Math.Max(16, maxRowsPerRun);
    }

    /// <summary>每个流程保留的运行次数。</summary>
    public int MaxRuns { get; }

    /// <summary>每次运行最多的行数。</summary>
    public int MaxRowsPerRun { get; }

    /// <summary>有变化就加一。</summary>
    public long Version => Interlocked.Read(ref _version);

    /// <summary>
    /// 流程出口的推荐设置：调试级以上、不含数据流类别（输入解析、输出提交），队列放大到 16384。
    /// 创建流程管理时赋给 <c>WorkflowRunCenterOptions.FlowOutlet</c>。
    /// </summary>
    public static WorkflowOutletOptions OutletOptions() => new()
    {
        MinimumLevel = WorkflowLogLevel.Debug,
        Categories = new HashSet<WorkflowRunEventCategory>
        {
            WorkflowRunEventCategory.Lifecycle, WorkflowRunEventCategory.NodeExecution, WorkflowRunEventCategory.Trace,
            WorkflowRunEventCategory.Fault, WorkflowRunEventCategory.Recovery
        },
        Capacity = 16_384
    };

    /// <summary>挂到流程管理现有的全部流程上。流程管理换新（重新加载）后再调用一次；已有的运行记录保留。</summary>
    /// <param name="center">流程管理。</param>
    public void Attach(WorkflowRunCenter center)
    {
        Guard.NotNull(center);
        foreach (var flow in center.Flows)
            Attach(flow);
    }

    /// <summary>挂到一个流程的出口上（保留出口原有的回调）。</summary>
    /// <param name="flow">流程。</param>
    public void Attach(WorkflowRunCenterFlow flow)
    {
        Guard.NotNull(flow);
        var titles = CollectTitles(flow);
        var name = flow.Name;
        lock (_sync)
            Logs(name).Titles = titles;
        var previous = flow.Outlet.OnEvent;
        flow.Outlet.OnEvent = item =>
        {
            previous?.Invoke(item);
            Add(name, item.Event);
        };
    }

    /// <summary>加一条事件：根运行的 RunStarted 开始新的一次运行，其余加到该流程最近一次运行里；还没有运行时丢掉统管层事件。</summary>
    /// <param name="flowName">流程名。</param>
    /// <param name="runEvent">运行事件。</param>
    public void Add(string flowName, WorkflowRunEvent runEvent)
    {
        Guard.NotNullOrWhiteSpace(flowName);
        Guard.NotNull(runEvent);
        lock (_sync)
        {
            var logs = Logs(flowName);
            // 统管层自己的事件（故障、停靠等）不属于某次运行，且可能先于 RunStarted 到达：只挂到已有运行上，不单独开一次运行。
            if (runEvent.RunId == Guid.Empty && logs.Runs.Count == 0) return;
            var starts = runEvent.EventType == "RunStarted" && runEvent.Parents is not { Count: > 0 } && runEvent.ParentRunId is null;
            if (starts || logs.Runs.Count == 0)
            {
                var titles = logs.Titles;
                logs.Runs.Add(new WorkflowRunLog(flowName, runEvent.Timestamp, (path, node) => Title(titles, path, node), MaxRowsPerRun));
                while (logs.Runs.Count > MaxRuns)
                    logs.Runs.RemoveAt(0);
            }
            logs.Runs[logs.Runs.Count - 1].Add(runEvent);
            _version++;
        }
    }

    /// <summary>有记录的流程名。</summary>
    public IReadOnlyList<string> Flows
    {
        get
        {
            lock (_sync)
                return _flows.Where(pair => pair.Value.Runs.Count > 0).Select(pair => pair.Key).OrderBy(name => name, StringComparer.Ordinal).ToArray();
        }
    }

    /// <summary>某流程保留的运行（最早的在前）。</summary>
    /// <param name="flowName">流程名。</param>
    public IReadOnlyList<WorkflowRunLogSummary> Runs(string flowName)
    {
        lock (_sync)
        {
            if (!_flows.TryGetValue(flowName, out var logs)) return Array.Empty<WorkflowRunLogSummary>();
            return logs.Runs.Select((run, index) => new WorkflowRunLogSummary(index, run.RunId, run.Root.Started, run.Root.Ended, run.Root.State,
                run.EventCount, run.StepCount, run.Root.FailureCount, run.Root.NoticeCount)).ToArray();
        }
    }

    /// <summary>在锁内读一次运行（不要把树里的对象带出 <paramref name="read"/>）。</summary>
    /// <typeparam name="T">读出的结果。</typeparam>
    /// <param name="flowName">流程名。</param>
    /// <param name="index">运行位置；超出范围时读最近一次。</param>
    /// <param name="read">读法。</param>
    /// <returns>读出的结果；没有运行时为默认值。</returns>
    public T? Read<T>(string flowName, int index, Func<WorkflowRunLog, T> read)
    {
        Guard.NotNull(read);
        lock (_sync)
        {
            if (!_flows.TryGetValue(flowName, out var logs) || logs.Runs.Count == 0) return default;
            var run = index >= 0 && index < logs.Runs.Count ? logs.Runs[index] : logs.Runs[logs.Runs.Count - 1];
            return read(run);
        }
    }

    /// <summary>清掉某流程的记录。</summary>
    /// <param name="flowName">流程名。</param>
    public void Clear(string flowName)
    {
        lock (_sync)
        {
            if (_flows.TryGetValue(flowName, out var logs))
                logs.Runs.Clear();
            _version++;
        }
    }

    private FlowLogs Logs(string flowName)
    {
        if (!_flows.TryGetValue(flowName, out var logs))
            _flows[flowName] = logs = new FlowLogs();
        return logs;
    }

    private static string? Title(IReadOnlyDictionary<string, string> titles, string path, string node) =>
        titles.TryGetValue(path + "|" + node, out var title) ? title : titles.TryGetValue("|" + node, out title) ? title : null;

    private static IReadOnlyDictionary<string, string> CollectTitles(WorkflowRunCenterFlow flow)
    {
        var titles = new Dictionary<string, string>(StringComparer.Ordinal);
        WorkflowExecutionPlan plan;
        try
        {
            plan = flow.Plan;
        }
        catch (InvalidOperationException)
        {
            return titles;
        }
        void Visit(WorkflowExecutionPlan current, string path)
        {
            foreach (var id in current.NodeIds)
            {
                var title = current.GetNodeOrThrow(id).Title;
                if (string.IsNullOrWhiteSpace(title)) continue;
                titles[path + "|" + id] = title;
                if (!titles.ContainsKey("|" + id))
                    titles["|" + id] = title;
            }
            foreach (var child in current.ChildPlans)
                Visit(child.Value, path + "/" + child.Key);
        }
        Visit(plan, "$");
        return titles;
    }

    private sealed class FlowLogs
    {
        public List<WorkflowRunLog> Runs { get; } = new();
        public IReadOnlyDictionary<string, string> Titles { get; set; } = new Dictionary<string, string>();
    }
}
