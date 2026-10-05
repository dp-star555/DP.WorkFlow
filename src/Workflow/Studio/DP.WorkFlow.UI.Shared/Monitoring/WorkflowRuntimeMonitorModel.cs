namespace DP.WorkFlow.UI;

/// <summary>描述运行时令牌当前所在节点和状态。</summary>
/// <param name="TokenId">运行时令牌标识。</param>
/// <param name="CurrentNodeId">令牌当前所在节点标识。</param>
/// <param name="ScopePath">令牌所在作用域路径。</param>
/// <param name="AncestorTokens">令牌的祖先链。</param>
public sealed record WorkflowTokenMonitorItem(
    long TokenId,
    string CurrentNodeId,
    string ScopePath,
    string AncestorTokens);

/// <summary>描述并行作用域及其活动分支。</summary>
/// <param name="ScopeId">并行作用域标识。</param>
/// <param name="ScopeNodeId">创建并行作用域的节点标识。</param>
/// <param name="MergeNodeId">并行作用域的汇合节点标识。</param>
/// <param name="CompletedBranches">已经完成的并行分支数。</param>
/// <param name="TotalBranches">并行作用域分支总数。</param>
/// <param name="IsCompleted">子工作流是否已经完成。</param>
public sealed record WorkflowScopeMonitorItem(
    long ScopeId,
    string ScopeNodeId,
    string MergeNodeId,
    int CompletedBranches,
    int TotalBranches,
    bool IsCompleted);

/// <summary>描述一条可显示和导出的运行轨迹。</summary>
/// <param name="Sequence">轨迹或快照序号。</param>
/// <param name="Timestamp">轨迹发生时间。</param>
/// <param name="NodeId">节点标识。</param>
/// <param name="TokenId">运行时令牌标识。</param>
/// <param name="ScopePath">令牌所在作用域路径。</param>
/// <param name="Step">趋势数据中的采样序号。</param>
/// <param name="Message">诊断或轨迹消息。</param>
public sealed record WorkflowTraceMonitorItem(
    long Sequence,
    DateTimeOffset Timestamp,
    string NodeId,
    long TokenId,
    string ScopePath,
    string Step,
    string Message);

/// <summary>定义 Trace CSV 的用户可见列标题。</summary>
public sealed record WorkflowTraceCsvHeaders(
    string Sequence,
    string Timestamp,
    string NodeId,
    string TokenId,
    string ScopePath,
    string Step,
    string Message)
{
    /// <summary>获取兼容既有导出格式的稳定英文列标题。</summary>
    public static WorkflowTraceCsvHeaders Invariant { get; } = new(
        "Sequence", "Timestamp", "NodeId", "TokenId", "ScopePath", "Step", "Message");
}

/// <summary>描述节点最近执行耗时的趋势数据。</summary>
/// <param name="NodeId">节点标识。</param>
/// <param name="ExecutionCount">节点累计执行次数。</param>
/// <param name="Elapsed">执行耗时。</param>
/// <param name="AverageMilliseconds">节点平均执行毫秒数。</param>
/// <param name="State">当前运行状态。</param>
public sealed record WorkflowNodeTimingTrendItem(
    string NodeId,
    int ExecutionCount,
    TimeSpan Elapsed,
    double AverageMilliseconds,
    E_NodeState State);

/// <summary>描述子工作流实例的运行状态。</summary>
/// <param name="ParentNodeId">父级复合节点标识；根层级为空。</param>
/// <param name="WorkflowName">工作流名称。</param>
/// <param name="State">当前运行状态。</param>
/// <param name="Elapsed">执行耗时。</param>
/// <param name="RunId">工作流运行实例标识。</param>
public sealed record WorkflowChildMonitorItem(
    string ParentNodeId,
    string WorkflowName,
    E_WorkflowExecutionState State,
    TimeSpan Elapsed,
    Guid RunId);

/// <summary>将不可变 RuntimeSnapshot 投影为双 UI 可直接显示的监视列表。</summary>
public sealed class WorkflowRuntimeMonitorModel
{
    private IReadOnlyList<WorkflowTraceMonitorItem> _allTraceEntries = Array.Empty<WorkflowTraceMonitorItem>();
    /// <summary>获取最近一次应用的运行时快照。</summary>
    public WorkflowRuntimeSnapshot? Snapshot { get; private set; }

    /// <summary>获取当前活动和已完成令牌的监视项。</summary>
    public IReadOnlyList<WorkflowTokenMonitorItem> Tokens { get; private set; } = Array.Empty<WorkflowTokenMonitorItem>();

    /// <summary>获取并行作用域监视项。</summary>
    public IReadOnlyList<WorkflowScopeMonitorItem> ParallelScopes { get; private set; } = Array.Empty<WorkflowScopeMonitorItem>();

    /// <summary>获取子工作流实例监视项。</summary>
    public IReadOnlyList<WorkflowChildMonitorItem> ChildWorkflows { get; private set; } = Array.Empty<WorkflowChildMonitorItem>();

    /// <summary>获取经过当前过滤策略处理后的轨迹项。</summary>
    public IReadOnlyList<WorkflowTraceMonitorItem> TraceEntries { get; private set; } = Array.Empty<WorkflowTraceMonitorItem>();

    /// <summary>脚本 Console.Write/WriteLine 产生的标准输出。</summary>
    public IReadOnlyList<WorkflowTraceMonitorItem> OutputEntries { get; private set; } = Array.Empty<WorkflowTraceMonitorItem>();

    /// <summary>获取按节点汇总的最近执行耗时趋势。</summary>
    public IReadOnlyList<WorkflowNodeTimingTrendItem> TimingTrends { get; private set; } = Array.Empty<WorkflowNodeTimingTrendItem>();

    /// <summary>获取轨迹界面是否处于暂停更新状态。</summary>
    public bool IsTracePaused { get; private set; }

    /// <summary>获取当前轨迹过滤文本。</summary>
    public string TraceFilter { get; private set; } = string.Empty;

    /// <summary>获取轨迹过滤起始日期；空值表示不限制起点。</summary>
    public DateTime? TraceStartDate { get; private set; }

    /// <summary>获取轨迹过滤结束日期；空值表示不限制终点。</summary>
    public DateTime? TraceEndDate { get; private set; }

    /// <summary>在模型内容发生变化、界面需要刷新时发生。</summary>
    public event EventHandler? Changed;

    /// <summary>用运行时快照重建令牌、作用域、子流程和耗时数据。</summary>
    /// <param name="snapshot">运行时快照。</param>
    public void SetSnapshot(WorkflowRuntimeSnapshot? snapshot)
    {
        Snapshot = snapshot;
        Tokens = snapshot?.ActiveTokens.Values
            .OrderBy(token => token.TokenId)
            .Select(token => new WorkflowTokenMonitorItem(
                token.TokenId,
                token.CurrentNodeId ?? string.Empty,
                string.Join(" / ", token.ScopeIds),
                string.Join(" / ", token.AncestorTokenIds)))
            .ToArray() ?? Array.Empty<WorkflowTokenMonitorItem>();
        ParallelScopes = snapshot?.ParallelScopes.Values
            .OrderBy(scope => scope.RuntimeScopeId)
            .Select(scope => new WorkflowScopeMonitorItem(
                scope.RuntimeScopeId,
                scope.ScopeNodeId,
                scope.MergeNodeId,
                scope.CompletedBranches,
                scope.TotalBranches,
                scope.IsCompleted))
            .ToArray() ?? Array.Empty<WorkflowScopeMonitorItem>();
        TimingTrends = snapshot?.Nodes.Values
            .Where(node => node.ExecutionCount > 0)
            .OrderByDescending(node => node.Elapsed)
            .Select(node => new WorkflowNodeTimingTrendItem(
                node.NodeId,
                node.ExecutionCount,
                node.Elapsed,
                node.Elapsed.TotalMilliseconds / Math.Max(1, node.ExecutionCount),
                node.State))
            .ToArray() ?? Array.Empty<WorkflowNodeTimingTrendItem>();
        ChildWorkflows = snapshot?.EnumerateChildWorkflows()
            .OrderBy(child => child.ParentNodeId, StringComparer.Ordinal)
            .ThenBy(child => child.Snapshot.RunId)
            .Select(child => new WorkflowChildMonitorItem(
                child.ParentNodeId,
                child.Snapshot.WorkflowName,
                child.Snapshot.ExecutionState,
                child.Snapshot.TotalElapsed,
                child.Snapshot.RunId))
            .ToArray() ?? Array.Empty<WorkflowChildMonitorItem>();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>更新当前 Run 的有界 Trace 列表。</summary>
    /// <param name="batch">轨迹批次。</param>
    public void SetTraceBatch(WorkflowTraceBatch? batch)
    {
        _allTraceEntries = batch?.Entries
            .OrderBy(entry => entry.Sequence)
            .Select(entry => new WorkflowTraceMonitorItem(
                entry.Sequence,
                entry.Timestamp,
                entry.NodeId,
                entry.TokenId,
                string.Join(" / ", entry.ScopeIds ?? Array.Empty<long>()),
                entry.Step,
                entry.Message ?? string.Empty))
            .ToArray() ?? Array.Empty<WorkflowTraceMonitorItem>();
        OutputEntries = _allTraceEntries.Where(item => item.Step == "ScriptOutput").ToArray();
        if (!IsTracePaused) ApplyTraceFilter();
        else Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>暂停或恢复 Trace 列表刷新；暂停期间运行时仍继续收集有界 Trace。</summary>
    /// <param name="paused">是否暂停界面轨迹更新。</param>
    public void SetTracePaused(bool paused)
    {
        if (IsTracePaused == paused)
            return;
        IsTracePaused = paused;
        if (!paused)
            ApplyTraceFilter();
        else
            Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>按节点、Token、Scope、步骤或消息筛选 Trace。</summary>
    /// <param name="filter">轨迹过滤文本。</param>
    public void SetTraceFilter(string? filter)
    {
        var value = filter?.Trim() ?? string.Empty;
        if (string.Equals(TraceFilter, value, StringComparison.Ordinal))
            return;
        TraceFilter = value;
        ApplyTraceFilter();
    }

    /// <summary>按本地日期范围筛选 Trace；空端点表示开放边界。</summary>
    /// <param name="startDate">可选起始日期。</param>
    /// <param name="endDate">可选结束日期。</param>
    public void SetTraceDateRange(DateTime? startDate, DateTime? endDate)
    {
        startDate = startDate?.Date;
        endDate = endDate?.Date;
        if (startDate is not null && endDate is not null && startDate > endDate)
            (startDate, endDate) = (endDate, startDate);
        if (TraceStartDate == startDate && TraceEndDate == endDate) return;
        TraceStartDate = startDate;
        TraceEndDate = endDate;
        ApplyTraceFilter();
    }

    /// <summary>将当前筛选结果导出为 UTF-8 兼容 CSV 内容。</summary>
    /// <param name="writer">CSV 输出写入器。</param>
    /// <param name="headers">可选的本地化列标题；空值保留稳定英文格式。</param>
    public void ExportTraceCsv(TextWriter writer, WorkflowTraceCsvHeaders? headers = null)
    {
        ArgumentNullException.ThrowIfNull(writer);
        if (headers is null)
        {
            headers = WorkflowTraceCsvHeaders.Invariant;
            writer.WriteLine("Sequence,Timestamp,NodeId,TokenId,ScopePath,Step,Message");
        }
        else
        {
            writer.WriteLine(string.Join(",", new[]
            {
                Csv(headers.Sequence), Csv(headers.Timestamp), Csv(headers.NodeId), Csv(headers.TokenId),
                Csv(headers.ScopePath), Csv(headers.Step), Csv(headers.Message)
            }));
        }
        foreach (var item in TraceEntries)
        {
            writer.WriteLine(string.Join(",", new[]
            {
                item.Sequence.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Csv(item.Timestamp.ToString("O", System.Globalization.CultureInfo.InvariantCulture)),
                Csv(item.NodeId),
                item.TokenId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Csv(item.ScopePath),
                Csv(item.Step),
                Csv(item.Message)
            }));
        }
    }

    /// <summary>根据当前过滤条件生成可见轨迹列表。</summary>
    private void ApplyTraceFilter()
    {
        var query = _allTraceEntries.Where(item =>
        {
            var localDate = item.Timestamp.ToLocalTime().Date;
            return (TraceStartDate is null || localDate >= TraceStartDate.Value)
                && (TraceEndDate is null || localDate <= TraceEndDate.Value);
        });
        if (!string.IsNullOrWhiteSpace(TraceFilter))
            query = query.Where(item =>
                item.NodeId.Contains(TraceFilter, StringComparison.OrdinalIgnoreCase)
                || item.TokenId.ToString(System.Globalization.CultureInfo.InvariantCulture).Contains(TraceFilter, StringComparison.OrdinalIgnoreCase)
                || item.ScopePath.Contains(TraceFilter, StringComparison.OrdinalIgnoreCase)
                || item.Step.Contains(TraceFilter, StringComparison.OrdinalIgnoreCase)
                || item.Message.Contains(TraceFilter, StringComparison.OrdinalIgnoreCase));
        TraceEntries = query.ToArray();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>转义一个符合 CSV 规则的字段。</summary>
    /// <param name="value">要校验、转换或写入的值。</param>
    private static string Csv(string value) => $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
}
