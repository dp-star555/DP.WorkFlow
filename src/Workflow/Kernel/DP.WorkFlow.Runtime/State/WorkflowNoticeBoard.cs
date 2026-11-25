namespace DP.WorkFlow;

/// <summary>一条提示（R1-01）：不影响运行结果的非致命信息，归属到某个节点或整个画布。</summary>
/// <param name="Id">提示板内递增的编号；同一条提示重复出现时换新编号。</param>
/// <param name="Timestamp">最近一次出现的时间。</param>
/// <param name="RunId">最近一次出现所在的运行。</param>
/// <param name="PlanPath">所在画布在根流程中的路径（"$" 为根画布）。</param>
/// <param name="NodeId">所属节点；画布级提示（无法归属到节点，例如记录丢失）为空。</param>
/// <param name="Category">类别。</param>
/// <param name="Message">说明。</param>
/// <param name="Count">连续重复出现的次数。</param>
public sealed record WorkflowNotice(
    long Id,
    DateTimeOffset Timestamp,
    Guid RunId,
    string PlanPath,
    string? NodeId,
    string Category,
    string Message,
    int Count = 1);

/// <summary>
/// 提示板（R1-01 节点提示）：按"画布路径 + 节点"累计提示，跨运行保留，直到使用者确认清除。
/// </summary>
/// <remarks>
/// 不在执行路径上：添加提示只做一次有界的列表操作，不抛异常、不影响节点结果。
/// 同一位置连续出现相同的提示只增加次数；每个位置最多保留 <see cref="MaxPerTarget"/> 条，超出时丢弃最早的并计入 <see cref="DroppedCount"/>。
/// 同一个提示板由宿主的执行选项共享给每次运行及其子运行。
/// </remarks>
public sealed class WorkflowNoticeBoard
{
    private readonly object _sync = new();
    private readonly Dictionary<(string PlanPath, string? NodeId), List<WorkflowNotice>> _notices = new();
    private long _nextId;
    private long _version;
    private long _dropped;

    /// <summary>初始化提示板。</summary>
    /// <param name="maxPerTarget">每个节点（或画布）最多保留的提示条数。</param>
    public WorkflowNoticeBoard(int maxPerTarget = 50)
    {
        if (maxPerTarget <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxPerTarget), "每个位置的提示上限必须大于 0。");
        MaxPerTarget = maxPerTarget;
    }

    /// <summary>每个节点（或画布）最多保留的提示条数。</summary>
    public int MaxPerTarget { get; }

    /// <summary>提示变化计数：添加或清除时递增，界面比较它决定是否重新读取。</summary>
    public long Version => Interlocked.Read(ref _version);

    /// <summary>因超出上限被丢弃的提示条数。</summary>
    public long DroppedCount => Interlocked.Read(ref _dropped);

    /// <summary>添加一条提示。</summary>
    /// <param name="runId">所在运行。</param>
    /// <param name="planPath">所在画布路径。</param>
    /// <param name="nodeId">所属节点；画布级提示为空。</param>
    /// <param name="category">类别。</param>
    /// <param name="message">说明。</param>
    /// <returns>新增或更新后的提示。</returns>
    public WorkflowNotice Raise(Guid runId, string planPath, string? nodeId, string category, string message)
    {
        var key = (string.IsNullOrWhiteSpace(planPath) ? "$" : planPath, string.IsNullOrWhiteSpace(nodeId) ? null : nodeId);
        WorkflowNotice notice;
        lock (_sync)
        {
            if (!_notices.TryGetValue(key, out var list))
                _notices[key] = list = new List<WorkflowNotice>();
            var last = list.Count > 0 ? list[^1] : null;
            if (last is not null
                && string.Equals(last.Category, category, StringComparison.Ordinal)
                && string.Equals(last.Message, message, StringComparison.Ordinal))
            {
                notice = last with { Id = ++_nextId, Timestamp = WorkflowClock.UtcNow, RunId = runId, Count = last.Count + 1 };
                list[^1] = notice;
            }
            else
            {
                notice = new WorkflowNotice(++_nextId, WorkflowClock.UtcNow, runId, key.Item1, key.Item2, category, message);
                list.Add(notice);
                if (list.Count > MaxPerTarget)
                {
                    list.RemoveAt(0);
                    _dropped++;
                }
            }
            _version++;
        }
        return notice;
    }

    /// <summary>读取全部提示，按出现先后排列。</summary>
    public IReadOnlyList<WorkflowNotice> GetAll()
    {
        lock (_sync)
            return _notices.Values.SelectMany(list => list).OrderBy(notice => notice.Id).ToArray();
    }

    /// <summary>读取一个节点（或画布级）的提示。</summary>
    /// <param name="planPath">画布路径。</param>
    /// <param name="nodeId">节点；为空时读取画布级提示。</param>
    public IReadOnlyList<WorkflowNotice> Get(string planPath, string? nodeId)
    {
        lock (_sync)
            return _notices.TryGetValue((planPath, nodeId), out var list) ? list.ToArray() : Array.Empty<WorkflowNotice>();
    }

    /// <summary>使用者确认后清除一个节点（或画布级）的提示。</summary>
    /// <param name="planPath">画布路径。</param>
    /// <param name="nodeId">节点；为空时清除画布级提示。</param>
    /// <returns>清除的条数。</returns>
    public int Acknowledge(string planPath, string? nodeId)
    {
        lock (_sync)
        {
            if (!_notices.Remove((planPath, nodeId), out var list))
                return 0;
            _version++;
            return list.Count;
        }
    }

    /// <summary>清除全部提示。</summary>
    /// <returns>清除的条数。</returns>
    public int AcknowledgeAll()
    {
        lock (_sync)
        {
            var count = _notices.Values.Sum(list => list.Count);
            _notices.Clear();
            _dropped = 0;
            _version++;
            return count;
        }
    }
}
