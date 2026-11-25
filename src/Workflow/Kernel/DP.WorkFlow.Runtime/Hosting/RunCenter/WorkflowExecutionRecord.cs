namespace DP.WorkFlow;

/// <summary>合并执行记录里的一笔：哪个流程、哪个节点、第几轮恢复、能否回退、是否已回退。</summary>
public sealed class WorkflowExecutionRecordEntry
{
    internal WorkflowExecutionRecordEntry(long sequence, string flowName, int round, WorkflowJournalEntry entry)
    {
        Sequence = sequence;
        FlowName = flowName;
        Round = round;
        NodeId = entry.NodeId;
        NodeTitle = entry.NodeTitle;
        NodeType = entry.NodeType;
        Position = entry.Position;
        IsCheckpoint = entry.IsCheckpoint;
        Undo = entry.Undo;
        CompletedAt = WorkflowClock.UtcNow;
    }

    /// <summary>全部流程统一的先后序号。</summary>
    public long Sequence { get; }
    /// <summary>流程名。</summary>
    public string FlowName { get; }
    /// <summary>执行时本流程的恢复轮次号。</summary>
    public int Round { get; }
    /// <summary>节点 ID。</summary>
    public string NodeId { get; }
    /// <summary>节点显示名。</summary>
    public string NodeTitle { get; }
    /// <summary>节点类型。</summary>
    public string NodeType { get; }
    /// <summary>执行位置。</summary>
    public string Position { get; }
    /// <summary>是否检查点节点。</summary>
    public bool IsCheckpoint { get; }
    /// <summary>完成时间。</summary>
    public DateTimeOffset CompletedAt { get; }
    /// <summary>节点是否支持回退。</summary>
    public bool CanUndo => Undo is not null;
    /// <summary>是否已经回退（或已由人确认处理好）。重新运行处理流程时不会再回退一次。</summary>
    public bool Undone { get; internal set; }
    internal Func<CancellationToken, Task>? Undo { get; }

    /// <inheritdoc />
    public override string ToString() => $"#{Sequence} {FlowName} {NodeTitle}（{Position}）";
}

/// <summary>逆序回退的一个目标：把某个流程回退到某个节点之后。</summary>
/// <param name="FlowName">流程名。</param>
/// <param name="BackToNodeId">回退到该节点最近一次执行之后（该节点本身不回退）；为空时回退本流程记录里的全部动作。</param>
public sealed record WorkflowUndoTarget(string FlowName, string? BackToNodeId);

/// <summary>回退到一笔时怎么做。</summary>
public enum WorkflowUndoStepKind
{
    /// <summary>调用节点的回退。</summary>
    Undone = 0,
    /// <summary>节点不支持回退，已由人确认处理好。</summary>
    Confirmed = 1
}

/// <summary>回退的一步。</summary>
/// <param name="Entry">记录。</param>
/// <param name="Kind">怎么处理的。</param>
public sealed record WorkflowUndoStep(WorkflowExecutionRecordEntry Entry, WorkflowUndoStepKind Kind);

/// <summary>逆序回退的结果。</summary>
/// <param name="Steps">按执行顺序处理过的步骤。</param>
/// <param name="Failure">没有完成的原因；完成时为空。</param>
public sealed record WorkflowUndoReport(IReadOnlyList<WorkflowUndoStep> Steps, string? Failure)
{
    /// <summary>是否全部回退完成。</summary>
    public bool Success => Failure is null;
}

/// <summary>
/// 合并执行记录（第 8 类）：流程管理里全部流程成功执行过的节点，按真实先后排成一列。
/// 回退再继续按它的倒序回退动作（例如先抬起 Z 再松爪）。
/// 流程走过检查点节点时，本流程更早的记录丢弃；总数超过容量时丢弃最早的记录。
/// </summary>
public sealed class WorkflowExecutionRecord
{
    private readonly object _sync = new();
    private readonly LinkedList<WorkflowExecutionRecordEntry> _entries = new();
    private long _sequence;

    /// <summary>创建合并执行记录。</summary>
    /// <param name="capacity">最多保留的记录数。</param>
    public WorkflowExecutionRecord(int capacity = 500)
    {
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity));
        Capacity = capacity;
    }

    /// <summary>最多保留的记录数。</summary>
    public int Capacity { get; }

    /// <summary>当前记录，按执行先后。</summary>
    public IReadOnlyList<WorkflowExecutionRecordEntry> Entries
    {
        get
        {
            lock (_sync)
                return _entries.ToArray();
        }
    }

    /// <summary>某个流程的当前记录，按执行先后。</summary>
    /// <param name="flowName">流程名。</param>
    public IReadOnlyList<WorkflowExecutionRecordEntry> EntriesOf(string flowName)
    {
        lock (_sync)
            return _entries.Where(entry => string.Equals(entry.FlowName, flowName, StringComparison.Ordinal)).ToArray();
    }

    /// <summary>清空记录。</summary>
    public void Clear()
    {
        lock (_sync)
            _entries.Clear();
    }

    internal void Add(string flowName, int round, WorkflowJournalEntry journalEntry)
    {
        lock (_sync)
        {
            var entry = new WorkflowExecutionRecordEntry(++_sequence, flowName, round, journalEntry);
            if (entry.IsCheckpoint)
            {
                // 走过检查点：本流程之前的动作已经交接出去，不再回退。
                for (var node = _entries.First; node is not null;)
                {
                    var next = node.Next;
                    if (string.Equals(node.Value.FlowName, flowName, StringComparison.Ordinal))
                        _entries.Remove(node);
                    node = next;
                }
            }
            _entries.AddLast(entry);
            while (_entries.Count > Capacity)
                _entries.RemoveFirst();
        }
    }

    /// <summary>
    /// 逆序回退：把各目标流程从现在回退到指定节点之后，全部目标的记录合在一起按真实执行的倒序处理。
    /// 支持回退的节点调用回退（无副作用的节点回退为空操作）；其余节点交人确认处理好（<paramref name="confirmManual"/>），
    /// 没有人确认或确认被拒绝时停下并返回原因。已经回退过的记录不会再回退。
    /// </summary>
    /// <param name="targets">回退目标。</param>
    /// <param name="confirmManual">不支持回退的节点交人确认；返回 true 表示已处理好。为空时遇到这类节点即停下。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task<WorkflowUndoReport> UndoAsync(
        IReadOnlyList<WorkflowUndoTarget> targets,
        Func<WorkflowExecutionRecordEntry, CancellationToken, Task<bool>>? confirmManual,
        CancellationToken cancellationToken)
    {
        Guard.NotNull(targets);
        var steps = new List<WorkflowUndoStep>();
        List<WorkflowExecutionRecordEntry> pending;
        lock (_sync)
        {
            pending = new List<WorkflowExecutionRecordEntry>();
            foreach (var target in targets)
            {
                var flowEntries = _entries.Where(entry => string.Equals(entry.FlowName, target.FlowName, StringComparison.Ordinal)).ToList();
                var start = 0;
                if (!string.IsNullOrWhiteSpace(target.BackToNodeId))
                {
                    var index = flowEntries.FindLastIndex(entry => string.Equals(entry.NodeId, target.BackToNodeId!.Trim(), StringComparison.Ordinal));
                    if (index < 0)
                        return new WorkflowUndoReport(steps,
                            $"流程 {target.FlowName} 的执行记录里没有节点 {target.BackToNodeId}（之后经过了检查点，或记录已满被挤掉），不能回退到这里。");
                    start = index + 1;
                }
                pending.AddRange(flowEntries.Skip(start).Where(entry => !entry.Undone));
            }
        }
        foreach (var entry in pending.Distinct().OrderByDescending(entry => entry.Sequence))
        {
            cancellationToken.ThrowIfCancellationRequested();
            WorkflowUndoStepKind kind;
            if (entry.Undo is { } undo)
            {
                try
                {
                    await undo(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    return new WorkflowUndoReport(steps, $"回退 {entry} 失败：{exception.Message}");
                }
                kind = WorkflowUndoStepKind.Undone;
            }
            else
            {
                if (confirmManual is null || !await confirmManual(entry, cancellationToken).ConfigureAwait(false))
                    return new WorkflowUndoReport(steps, $"{entry} 不支持回退，需要人工处理。");
                kind = WorkflowUndoStepKind.Confirmed;
            }
            entry.Undone = true;
            steps.Add(new WorkflowUndoStep(entry, kind));
        }
        return new WorkflowUndoReport(steps, null);
    }
}
