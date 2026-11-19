namespace DP.WorkFlow.Testing;

/// <summary>
/// R5-a 事件流规则：对一次运行（含继续运行）的完整事件序列做纯函数检查，返回违反的规则。
/// 规则只描述引擎"必须永远成立"的事实；刻意修改引擎行为时，在这里同步修改规则并说明原因。
/// </summary>
public static class WorkflowEventStreamInvariants
{
    private static readonly HashSet<string> TerminalEvents = new(StringComparer.Ordinal)
    {
        "RunCompleted", "RunCanceled", "RunFaulted"
    };

    private static readonly HashSet<string> NodeCloseEvents = new(StringComparer.Ordinal)
    {
        "NodeCompleted", "NodeFailed", "NodeCanceled"
    };

    /// <summary>作用域级事件：记在作用域节点名下，发生在该节点执行结束之后。</summary>
    private static readonly HashSet<string> StructureEvents = new(StringComparer.Ordinal)
    {
        "ParallelMerged"
    };

    /// <summary>
    /// 检查一次运行的事件序列。只检查由引擎产生的运行（第一条事件为 RunStarted）；
    /// 直接调用记录器写入的测试数据不在检查范围内，返回空。
    /// </summary>
    /// <param name="events">同一 RunId 的全部事件，顺序不限。</param>
    /// <param name="findRun">按 RunId 取其他运行的事件（用于父运行、数据来源查找）；找不到返回 null。</param>
    /// <returns>违反的规则说明；全部满足时为空。</returns>
    public static IReadOnlyList<string> Check(
        IReadOnlyList<WorkflowRunEvent> events,
        Func<Guid, IReadOnlyList<WorkflowRunEvent>?>? findRun = null)
    {
        var violations = new List<string>();
        if (events.Count == 0)
            return violations;
        var ordered = events.OrderBy(item => item.Sequence).ToArray();
        if (!string.Equals(ordered[0].EventType, "RunStarted", StringComparison.Ordinal))
            return violations;

        CheckSequence(ordered, violations);
        CheckLifecycleAndNodes(ordered, violations);
        CheckInputSources(ordered, findRun, violations);
        CheckParent(ordered, findRun, violations);
        return violations;
    }

    /// <summary>规则 1：同一运行内事件序号从 1 开始连续递增，不重复、不跳号（继续运行接着原序号）。</summary>
    private static void CheckSequence(WorkflowRunEvent[] ordered, List<string> violations)
    {
        long expected = 1;
        foreach (var item in ordered)
        {
            if (item.Sequence != expected)
            {
                violations.Add($"序号不连续：期望 #{expected}，实际 #{item.Sequence}（{item.EventType}）。");
                expected = item.Sequence;
            }
            expected++;
        }
    }

    /// <summary>
    /// 规则 2～5：运行起止与节点执行配对。
    /// 2. RunStarted 只出现一次且在最前；运行结束（完成/取消/失败）后只能出现 RunContinued 开始新的一段；
    /// 3. 每次节点开始（节点 + 第几次执行）恰好有一个完成 / 失败 / 取消，且在开始之后；
    /// 4. 节点名下的数据和步骤事件只出现在该次执行开始之后、结束之前（作用域级事件除外，只要求作用域节点已开始过）；
    /// 5. 每段运行结束时，没有仍未结束的节点执行；失败出口路由必须跟在该次执行的失败之后。
    /// </summary>
    private static void CheckLifecycleAndNodes(WorkflowRunEvent[] ordered, List<string> violations)
    {
        var running = false;
        var open = new Dictionary<(string NodeId, int Count), WorkflowRunEvent>();
        var started = new HashSet<(string NodeId, int Count)>();
        var failed = new HashSet<(string NodeId, int Count)>();
        foreach (var item in ordered)
        {
            var type = item.EventType;
            if (type == "RunStarted")
            {
                if (item.Sequence != 1)
                    violations.Add($"#{item.Sequence} RunStarted 不是第一条事件。");
                running = true;
                continue;
            }
            if (type == "RunContinued")
            {
                if (running)
                    violations.Add($"#{item.Sequence} RunContinued 出现在上一段运行结束之前。");
                running = true;
                continue;
            }
            if (TerminalEvents.Contains(type))
            {
                if (!running)
                    violations.Add($"#{item.Sequence} {type} 出现在运行已结束之后（重复结束）。");
                foreach (var pending in open.Values.OrderBy(value => value.Sequence))
                    violations.Add(
                        $"#{item.Sequence} {type} 时节点 {pending.NodeId} 第 {pending.ExecutionIdentity!.NodeExecutionCount} 次执行"
                        + $"（#{pending.Sequence} 开始）没有完成 / 失败 / 取消事件。");
                open.Clear();
                running = false;
                continue;
            }
            if (!running)
            {
                violations.Add($"#{item.Sequence} {type}（节点 {item.NodeId ?? "-"}）出现在运行结束之后。");
                continue;
            }
            if (item.NodeId is null || item.ExecutionIdentity is null)
                continue;

            var key = (item.NodeId, item.ExecutionIdentity.NodeExecutionCount);
            if (type == "NodeStarted")
            {
                if (!started.Add(key))
                    violations.Add($"#{item.Sequence} 节点 {key.NodeId} 第 {key.NodeExecutionCount} 次执行重复开始。");
                open[key] = item;
                continue;
            }
            if (NodeCloseEvents.Contains(type))
            {
                if (!open.Remove(key))
                    violations.Add(
                        $"#{item.Sequence} {type}：节点 {key.NodeId} 第 {key.NodeExecutionCount} 次执行"
                        + (started.Contains(key) ? "已经结束过（重复结束）。" : "没有对应的 NodeStarted。"));
                if (type == "NodeFailed")
                    failed.Add(key);
                continue;
            }
            if (StructureEvents.Contains(type))
            {
                // 作用域级事件（如并行汇聚）挂在作用域节点名下，但不属于该节点的某一次执行。
                if (!started.Any(value => value.NodeId == key.NodeId))
                    violations.Add($"#{item.Sequence} {type}：作用域节点 {key.NodeId} 此前没有开始过。");
                continue;
            }
            if (type == "NodeFaultRouted")
            {
                if (!failed.Contains(key))
                    violations.Add($"#{item.Sequence} NodeFaultRouted：节点 {key.NodeId} 第 {key.NodeExecutionCount} 次执行之前没有 NodeFailed。");
                continue;
            }
            if (item.Category is WorkflowRunEventCategory.DataFlow or WorkflowRunEventCategory.Trace
                && !open.ContainsKey(key))
                violations.Add(
                    $"#{item.Sequence} {type}：节点 {key.NodeId} 第 {key.NodeExecutionCount} 次执行不在进行中"
                    + "（节点名下的数据和步骤事件必须在开始与结束之间）。");
        }
    }

    /// <summary>
    /// 规则 6：每条"读取节点输出"的输入事件，指向的输出必须在它之前已经提交（OutputCommitted，
    /// 同一节点、同一输出序号），可以在本运行或其祖先运行中。
    /// </summary>
    private static void CheckInputSources(
        WorkflowRunEvent[] ordered,
        Func<Guid, IReadOnlyList<WorkflowRunEvent>?>? findRun,
        List<string> violations)
    {
        var committed = new HashSet<(string NodeId, long Sequence)>();
        // 祖先运行在子运行开始前提交的输出同样可读；子运行开始后祖先不再推进，按全部已提交输出近似。
        foreach (var ancestor in EnumerateAncestors(ordered[0], findRun))
            foreach (var output in ancestor.Where(item => item.EventType == "OutputCommitted"))
                if (TryCommittedKey(output, out var ancestorKey))
                    committed.Add(ancestorKey);

        foreach (var item in ordered)
        {
            if (item.EventType == "OutputCommitted")
            {
                if (TryCommittedKey(item, out var key))
                    committed.Add(key);
                continue;
            }
            if (item.EventType != "InputResolved" || item.Data is null)
                continue;
            if (Text(item.Data, "SourceKind") != "NodeOutput")
                continue;
            var sourceNodeId = Text(item.Data, "SourceNodeId");
            var sourceSequence = Long(item.Data, "SourceOutputSequence");
            if (sourceNodeId is null || sourceSequence is null)
                continue; // 解析失败的尝试：没有取到值，也就没有来源可核对。
            if (!committed.Contains((sourceNodeId, sourceSequence.Value)))
                violations.Add(
                    $"#{item.Sequence} InputResolved：节点 {item.NodeId} 读取了 {sourceNodeId} 的输出 #{sourceSequence}，"
                    + "但此前没有对应的 OutputCommitted。");
        }
    }

    /// <summary>规则 7：子运行的父运行必须已经开始，且子运行开始时父运行尚未结束。</summary>
    private static void CheckParent(
        WorkflowRunEvent[] ordered,
        Func<Guid, IReadOnlyList<WorkflowRunEvent>?>? findRun,
        List<string> violations)
    {
        var first = ordered[0];
        if (first.ParentRunId is not { } parentId || findRun is null)
            return;
        var parent = findRun(parentId);
        if (parent is null || parent.Count == 0)
        {
            violations.Add($"子运行的父运行 {parentId} 没有任何事件。");
            return;
        }
        // 父运行事件与子运行事件来自不同记录器，只能按时间比较；同一时刻视为不冲突。
        var parentTerminal = parent
            .Where(item => TerminalEvents.Contains(item.EventType))
            .OrderBy(item => item.Sequence)
            .LastOrDefault();
        var parentContinued = parent.Any(item => item.EventType == "RunContinued");
        if (parentTerminal is not null && !parentContinued && parentTerminal.Timestamp < first.Timestamp)
            violations.Add($"子运行在父运行结束（{parentTerminal.EventType}）之后才开始。");
    }

    private static IEnumerable<IReadOnlyList<WorkflowRunEvent>> EnumerateAncestors(
        WorkflowRunEvent first,
        Func<Guid, IReadOnlyList<WorkflowRunEvent>?>? findRun)
    {
        if (findRun is null)
            yield break;
        var visited = new HashSet<Guid> { first.RunId };
        var parentId = first.ParentRunId;
        while (parentId is { } id && visited.Add(id))
        {
            var parent = findRun(id);
            if (parent is null || parent.Count == 0)
                yield break;
            yield return parent;
            parentId = parent.OrderBy(item => item.Sequence).First().ParentRunId;
        }
    }

    private static bool TryCommittedKey(WorkflowRunEvent output, out (string NodeId, long Sequence) key)
    {
        key = default;
        if (output.NodeId is null || output.Data is null)
            return false;
        var sequence = Long(output.Data, "OutputExecutionSequence");
        if (sequence is null)
            return false;
        key = (output.NodeId, sequence.Value);
        return true;
    }

    private static string? Text(IReadOnlyDictionary<string, WorkflowTraceValue> data, string key) =>
        data.TryGetValue(key, out var value) ? value.Text : null;

    private static long? Long(IReadOnlyDictionary<string, WorkflowTraceValue> data, string key)
    {
        if (!data.TryGetValue(key, out var value) || value.Scalar is null)
            return null;
        try
        {
            return Convert.ToInt64(value.Scalar, System.Globalization.CultureInfo.InvariantCulture);
        }
        catch
        {
            return null;
        }
    }
}
