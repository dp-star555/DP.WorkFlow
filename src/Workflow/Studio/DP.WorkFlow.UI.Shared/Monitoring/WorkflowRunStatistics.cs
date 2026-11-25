namespace DP.WorkFlow.UI;

/// <summary>
/// 运行监视页的统计：运行次数（成功 / 出错 / 取消）、根画布循环块的圈数，以及近若干次的节拍。
/// 只从界面拉到的快照推算，不在执行线程上做任何事。
/// </summary>
/// <remarks>
/// 节拍的算法：
/// <list type="bullet">
/// <item>根画布上有循环块或循环节点（<see cref="LoopNodeIds"/>，每经过一次算一圈）在转圈时，节拍是主循环（本次运行第一次看到时圈数最多的那个）相邻两圈开始的间隔；
/// 一帧里跳过多圈时按跳过的圈数平分。</item>
/// <item>没有循环块的流程，节拍是每次运行结束时的总耗时。</item>
/// </list>
/// 快照按界面节拍拉取，所以节拍的精度约为一帧（默认 100 ms）。
/// </remarks>
public sealed class WorkflowRunStatistics
{
    /// <summary>默认取近 60 次算平均节拍。</summary>
    public const int DefaultWindow = 60;

    private readonly Queue<TimeSpan> _cycles = new();
    private readonly Dictionary<string, int> _lapCounts = new(StringComparer.Ordinal);
    private Guid _runId;
    private Guid _countedRunId;
    private string? _mainLoop;
    private DateTimeOffset? _lastLapAt;
    private bool _runHadLaps;

    /// <summary>创建统计。</summary>
    /// <param name="window">算平均节拍取的次数，至少 1。</param>
    public WorkflowRunStatistics(int window = DefaultWindow)
    {
        Window = Math.Max(1, window);
    }

    /// <summary>算平均节拍取的次数。</summary>
    public int Window { get; }

    /// <summary>根画布上循环节点（回连到自己的“循环节点”）的 ID：每执行一次算开始一圈。由宿主按流程文档给出。</summary>
    public IReadOnlyCollection<string> LoopNodeIds { get; set; } = Array.Empty<string>();

    /// <summary>结束的运行次数（成功、出错、取消都算）。</summary>
    public int Runs => Succeeded + Failed + Canceled;

    /// <summary>成功结束的次数。</summary>
    public int Succeeded { get; private set; }

    /// <summary>出错结束的次数。</summary>
    public int Failed { get; private set; }

    /// <summary>取消结束的次数。</summary>
    public int Canceled { get; private set; }

    /// <summary>主循环转过的圈数（跨运行累计）。</summary>
    public long Laps { get; private set; }

    /// <summary>最近一次节拍；还没有时为空。</summary>
    public TimeSpan? LastCycle { get; private set; }

    /// <summary>近 <see cref="Window"/> 次的平均节拍；还没有时为空。</summary>
    public TimeSpan? AverageCycle => _cycles.Count == 0 ? null : TimeSpan.FromTicks((long)_cycles.Average(cycle => cycle.Ticks));

    /// <summary>参与平均的次数。</summary>
    public int CycleSamples => _cycles.Count;

    /// <summary>最近一帧快照的时刻与界面应用它的时刻之差，即显示延迟；还没有时为空。</summary>
    public TimeSpan? DisplayLatency { get; private set; }

    /// <summary>最近一帧快照的序号。</summary>
    public long LastSequence { get; private set; } = -1;

    /// <summary>读一帧快照。同一帧重复传入不会重复计数。</summary>
    /// <param name="snapshot">运行快照；为空时不变。</param>
    /// <param name="now">界面应用这帧的时刻，用于算显示延迟。</param>
    public void Observe(WorkflowRuntimeSnapshot? snapshot, DateTimeOffset now)
    {
        if (snapshot is null || snapshot.RunId == Guid.Empty)
            return;
        DisplayLatency = now >= snapshot.Timestamp ? now - snapshot.Timestamp : TimeSpan.Zero;
        LastSequence = snapshot.Sequence;
        if (snapshot.RunId != _runId)
        {
            _runId = snapshot.RunId;
            _lapCounts.Clear();
            _mainLoop = null;
            _lastLapAt = null;
            _runHadLaps = false;
        }
        ObserveLaps(snapshot);
        if (snapshot.ExecutionState is E_WorkflowExecutionState.Completed or E_WorkflowExecutionState.Faulted or E_WorkflowExecutionState.Canceled
            && _countedRunId != snapshot.RunId)
        {
            _countedRunId = snapshot.RunId;
            switch (snapshot.ExecutionState)
            {
                case E_WorkflowExecutionState.Completed: Succeeded++; break;
                case E_WorkflowExecutionState.Faulted: Failed++; break;
                default: Canceled++; break;
            }
            if (!_runHadLaps && snapshot.ExecutionState == E_WorkflowExecutionState.Completed)
                AddCycle(snapshot.TotalElapsed);
        }
    }

    /// <summary>清零。</summary>
    public void Reset()
    {
        _cycles.Clear();
        _lapCounts.Clear();
        _runId = Guid.Empty;
        _countedRunId = Guid.Empty;
        _mainLoop = null;
        _lastLapAt = null;
        _runHadLaps = false;
        Succeeded = Failed = Canceled = 0;
        Laps = 0;
        LastCycle = null;
        DisplayLatency = null;
        LastSequence = -1;
    }

    private void ObserveLaps(WorkflowRuntimeSnapshot snapshot)
    {
        var cycles = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var child in snapshot.EnumerateChildWorkflows())
            if (child.Cycle > 0 && (!cycles.TryGetValue(child.ParentNodeId, out var known) || child.Cycle > known))
                cycles[child.ParentNodeId] = child.Cycle;
        foreach (var id in LoopNodeIds)
            if (snapshot.Nodes.TryGetValue(id, out var info) && info.ExecutionCount > 0)
                cycles[id] = info.ExecutionCount;
        if (cycles.Count == 0)
            return;
        _runHadLaps = true;
        // 主循环：本次运行第一次看到时圈数最多的那个（圈数一样取节点 ID 小的），之后本次运行不再换。
        _mainLoop ??= cycles.OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key, StringComparer.Ordinal).First().Key;
        foreach (var pair in cycles)
        {
            var node = pair.Key;
            var cycle = pair.Value;
            var previous = _lapCounts.TryGetValue(node, out var seen) ? seen : 0;
            _lapCounts[node] = cycle;
            if (node != _mainLoop || cycle <= previous)
                continue;
            var advanced = cycle - previous;
            Laps += advanced;
            if (_lastLapAt is { } last && snapshot.Timestamp > last)
            {
                var each = TimeSpan.FromTicks((snapshot.Timestamp - last).Ticks / advanced);
                for (var i = 0; i < advanced; i++)
                    AddCycle(each);
            }
            _lastLapAt = snapshot.Timestamp;
        }
    }

    private void AddCycle(TimeSpan cycle)
    {
        LastCycle = cycle;
        _cycles.Enqueue(cycle);
        while (_cycles.Count > Window)
            _cycles.Dequeue();
    }
}
