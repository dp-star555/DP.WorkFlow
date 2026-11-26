using System.Reflection;

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
/// 否则出口按默认级别把并行派发 / 汇聚、进度等调试级事件过滤掉。只在内存里，程序重启后清空；
/// 更早的运行由外部保存，经 <see cref="IWorkflowRunLogHistory"/> 读回后用 <see cref="Replay"/> 重建。
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
        var name = flow.Name;
        WorkflowExecutionPlan? plan;
        try
        {
            plan = flow.Plan;
        }
        catch (InvalidOperationException)
        {
            plan = null;
        }
        UsePlan(name, plan);
        var previous = flow.Outlet.OnEvent;
        flow.Outlet.OnEvent = item =>
        {
            previous?.Invoke(item);
            Add(name, item.Event);
        };
    }

    /// <summary>
    /// 不归流程管理的运行宿主（例如设计页的试运行）用的记录器：放进宿主的 <c>WorkflowExecutionOptions.Recording.Sink</c>，
    /// 事件按 <see cref="OutletOptions"/> 同样的级别和类别过滤后记在 <paramref name="flowName"/> 名下；每次启动时按当时的执行计划取节点名称。
    /// </summary>
    /// <param name="flowName">记在哪个名字下。</param>
    /// <param name="plan">取宿主当前的执行计划。</param>
    public IWorkflowRunEventSink CreateSink(string flowName, Func<WorkflowExecutionPlan?> plan)
    {
        Guard.NotNullOrWhiteSpace(flowName);
        var name = flowName.Trim();
        return CreateSink(() => name, plan);
    }

    /// <summary>
    /// 同上，但名字在每次启动时取一次（例如“试运行 · 上料”，按设计页当前打开的流程分开记）；这次运行后面的事件都记在这个名字下。
    /// </summary>
    /// <param name="flowName">每次启动时取名字；在记录线程上调用，不要访问界面。</param>
    /// <param name="plan">取宿主当前的执行计划。</param>
    public IWorkflowRunEventSink CreateSink(Func<string> flowName, Func<WorkflowExecutionPlan?> plan)
    {
        Guard.NotNull(flowName);
        Guard.NotNull(plan);
        return new Sink(this, flowName, plan);
    }

    private sealed class Sink(WorkflowRunLogBook book, Func<string> flowName, Func<WorkflowExecutionPlan?> plan) : IWorkflowRunEventSink
    {
        private static readonly WorkflowOutletOptions Filter = OutletOptions();
        private string? _current;

        public ValueTask<WorkflowRunEventWriteResult> WriteAsync(IReadOnlyList<WorkflowRunEvent> events, bool requestImmediateFlush,
            CancellationToken cancellationToken)
        {
            foreach (var runEvent in events)
            {
                if (WorkflowOutlet.GetLevel(runEvent) < Filter.MinimumLevel || Filter.Categories?.Contains(runEvent.Category) == false) continue;
                if (IsRootStart(runEvent) || _current is null)
                {
                    var name = flowName()?.Trim();
                    _current = string.IsNullOrEmpty(name) ? "试运行" : name;
                    if (IsRootStart(runEvent)) book.UsePlan(_current, plan());
                }
                book.Add(_current, runEvent);
            }
            return new ValueTask<WorkflowRunEventWriteResult>(WorkflowRunEventWriteResult.Success);
        }

        public ValueTask FlushAsync(CancellationToken cancellationToken) => default;
    }

    private static bool IsRootStart(WorkflowRunEvent runEvent) =>
        runEvent.EventType == "RunStarted" && runEvent.Parents is not { Count: > 0 } && runEvent.ParentRunId is null;

    private void UsePlan(string flowName, WorkflowExecutionPlan? plan)
    {
        var labels = new Dictionary<string, string>(StringComparer.Ordinal);
        var titles = plan is null ? new Dictionary<string, string>(StringComparer.Ordinal) : CollectTitles(plan, labels);
        lock (_sync)
        {
            Logs(flowName).Titles = titles;
            Logs(flowName).Labels = labels;
        }
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
            if (IsRootStart(runEvent) || logs.Runs.Count == 0)
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

    /// <summary>
    /// 用历史事件（外部保存后读回来的）重建一次运行，规则和实时记录完全相同；节点名称按该名字当前的执行计划取。
    /// 不放进日志本，不影响最近若干次的记录。
    /// </summary>
    /// <param name="flowName">记在哪个名字下。</param>
    /// <param name="events">这次运行的事件，按收到的顺序。</param>
    /// <returns>重建的运行；没有事件时开始时间为当前时间。</returns>
    public WorkflowRunLog Replay(string flowName, IEnumerable<WorkflowRunEvent> events)
    {
        Guard.NotNullOrWhiteSpace(flowName);
        Guard.NotNull(events);
        IReadOnlyDictionary<string, string> titles;
        lock (_sync)
            titles = _flows.TryGetValue(flowName, out var logs) ? logs.Titles : new Dictionary<string, string>();
        WorkflowRunLog? log = null;
        foreach (var runEvent in events)
        {
            log ??= new WorkflowRunLog(flowName, runEvent.Timestamp, (path, node) => Title(titles, path, node), MaxRowsPerRun);
            log.Add(runEvent);
        }
        return log ?? new WorkflowRunLog(flowName, DateTimeOffset.UtcNow, null, MaxRowsPerRun);
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

    /// <summary>
    /// 记录里数据项的显示名称。记录本身只存键（和语言无关，导出、比对都用键），显示时按这里换成名称：
    /// 先看引擎自己写的常用键，再看节点类型声明的名称（输出类型属性上的 DisplayName、节点属性上的 WorkflowProperty），
    /// 都没有时显示键本身。以后做多语言，只要把这一步换成按语言取文字即可。
    /// </summary>
    /// <param name="flowName">流程名。</param>
    /// <param name="nodeType">写这条记录的节点类型；流程级记录为空。</param>
    /// <param name="key">数据键。</param>
    public string DataLabel(string flowName, string? nodeType, string key)
    {
        Guard.NotNull(key);
        const string output = "OutputValue.";
        if (key.StartsWith(output, StringComparison.Ordinal))
            return "输出 · " + DataLabel(flowName, nodeType, key.Substring(output.Length));
        if (CommonLabels.TryGetValue(key, out var common)) return common;
        lock (_sync)
            if (nodeType is not null && _flows.TryGetValue(flowName, out var logs)
                && logs.Labels.TryGetValue(nodeType + "|" + key, out var label))
                return label;
        return key;
    }

    private static readonly IReadOnlyDictionary<string, string> CommonLabels = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Message"] = "消息", ["Elapsed"] = "耗时 (ms)", ["SelectedPort"] = "出口", ["Reason"] = "原因", ["Progress"] = "进度",
        ["State"] = "状态", ["WorkflowName"] = "流程", ["PlanPath"] = "画布", ["NodeCount"] = "节点数", ["EntryNodeId"] = "入口节点",
        ["Directive"] = "去向", ["Attempt"] = "第几次", ["Position"] = "位置", ["AlarmCode"] = "报警码", ["CanRecoverInPlace"] = "可就地恢复",
        ["ManualPause"] = "人工暂停", ["HoldReasons"] = "挂起原因", ["Mode"] = "方式", ["NodeId"] = "节点", ["Cycle"] = "第几圈",
        ["Count"] = "次数", ["Category"] = "类别", ["Branches"] = "分支", ["BranchCount"] = "分支数", ["MergeNodeId"] = "汇聚节点",
        ["ExceptionType"] = "异常类型", ["InterruptAlarmCode"] = "中断报警码", ["Keys"] = "键", ["OutputKeys"] = "输出项",
        ["OutputType"] = "输出类型", ["PublicDataKey"] = "公共数据", ["SourceNodeId"] = "来源节点", ["SourceKind"] = "来源",
        ["InputKey"] = "输入项", ["TargetType"] = "目标类型", ["ResolvedValueSummary"] = "取到的值", ["Port"] = "出口",
        ["OutputExecutionSequence"] = "输出序号", ["SourceOutputSequence"] = "来源输出序号", ["SourceOutputKey"] = "来源输出项",
        ["InputMetadataStatus"] = "输入来源方式", ["FromSequence"] = "起始序号"
    };

    private static Dictionary<string, string> CollectTitles(WorkflowExecutionPlan plan, Dictionary<string, string> names)
    {
        var titles = new Dictionary<string, string>(StringComparer.Ordinal);
        void Visit(WorkflowExecutionPlan current, string path)
        {
            foreach (var id in current.NodeIds)
            {
                var node = current.GetNodeOrThrow(id);
                CollectLabels(node, names);
                var title = node.Title;
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

    /// <summary>节点类型声明的数据名称：输出类型属性的 DisplayName，节点属性的 WorkflowProperty 名称。</summary>
    private static void CollectLabels(IWorkflowNodeModel node, Dictionary<string, string> labels)
    {
        var type = node.GetType();
        if (labels.ContainsKey(node.NodeType + "|")) return;
        labels[node.NodeType + "|"] = string.Empty;
        void Add(string key, string? label)
        {
            if (!string.IsNullOrWhiteSpace(label)) labels[node.NodeType + "|" + key] = label!.Trim();
        }
        foreach (var property in type.GetProperties())
            Add(property.Name, property.GetCustomAttribute<WorkflowPropertyAttribute>()?.DisplayName);
        if (type.GetCustomAttribute<WorkflowNodeAttribute>()?.OutputType is { } output)
        {
            foreach (var property in output.GetProperties())
                Add(property.Name, property.GetCustomAttribute<System.ComponentModel.DisplayNameAttribute>()?.DisplayName);
            // 记录类型的 DisplayName 写在主构造参数上（[property: DisplayName]）时也会落到属性上，上面已经取到。
        }
    }

    private sealed class FlowLogs
    {
        public IReadOnlyDictionary<string, string> Labels { get; set; } = new Dictionary<string, string>();
        public List<WorkflowRunLog> Runs { get; } = new();
        public IReadOnlyDictionary<string, string> Titles { get; set; } = new Dictionary<string, string>();
    }
}
