using System.Globalization;

namespace DP.WorkFlow;

/// <summary>运行日志的筛选。</summary>
public enum WorkflowRunLogFilter
{
    /// <summary>全部。</summary>
    All,

    /// <summary>只看失败与故障（和它们所在的各层）。</summary>
    Failures,

    /// <summary>只看提示（和它们所在的各层）。</summary>
    Notices
}

/// <summary>运行日志树展开成列表后的一行（快照，界面直接画它）。</summary>
/// <param name="Key">稳定键：展开 / 收起、选中都按它记。</param>
/// <param name="RowId">树里的行号；合并的“第 a–b 圈”为空。</param>
/// <param name="Depth">缩进层级（树根为 0）。</param>
/// <param name="Kind">种类。</param>
/// <param name="Title">标题。</param>
/// <param name="Detail">说明。</param>
/// <param name="Started">开始时间。</param>
/// <param name="Ended">结束时间。</param>
/// <param name="Elapsed">耗时（进行中的行按当前时间算）。</param>
/// <param name="State">结果。</param>
/// <param name="HasChildren">有没有下一层。</param>
/// <param name="Expanded">是否展开。</param>
/// <param name="HasFailure">这一层或下面有失败 / 故障。</param>
/// <param name="NoticeCount">这一层和下面的提示条数。</param>
/// <param name="NodeId">节点 ID。</param>
/// <param name="NodeType">节点类型。</param>
/// <param name="CanvasPath">所在画布。</param>
/// <param name="TokenId">令牌。</param>
/// <param name="ParentTokenId">父令牌。</param>
/// <param name="Path">从运行到这一行经过的标题，用“ / ”连接。</param>
/// <param name="Data">节点写的数据。</param>
/// <param name="IsGroup">是否是合并的连续成功圈。</param>
public sealed record WorkflowRunLogLine(
    string Key,
    long? RowId,
    int Depth,
    WorkflowRunLogRowKind Kind,
    string Title,
    string? Detail,
    DateTimeOffset Started,
    DateTimeOffset? Ended,
    TimeSpan? Elapsed,
    WorkflowRunLogState State,
    bool HasChildren,
    bool Expanded,
    bool HasFailure,
    int NoticeCount,
    string? NodeId,
    string? NodeType,
    string CanvasPath,
    long TokenId,
    long ParentTokenId,
    string Path,
    IReadOnlyDictionary<string, WorkflowTraceValue>? Data,
    bool IsGroup);

/// <summary>
/// 把运行日志树按折叠规则展开成列表。
/// </summary>
/// <remarks>
/// 自动折叠：成功结束的子流程、循环圈、并行分支收起；连续成功的圈合并成一行“第 a–b 圈”；
/// 有失败、故障或提示的层级保持展开，正在运行的层级也展开。手动展开 / 收起（<see cref="Overrides"/>）优先于自动规则。
/// </remarks>
public sealed class WorkflowRunLogView
{
    /// <summary>手动展开（true）/ 收起（false）的行，按 <see cref="WorkflowRunLogLine.Key"/>。</summary>
    public Dictionary<string, bool> Overrides { get; } = new(StringComparer.Ordinal);

    /// <summary>筛选。</summary>
    public WorkflowRunLogFilter Filter { get; set; }

    /// <summary>只看控制步骤：不显示节点自己写的进度 / 说明。</summary>
    public bool ControlOnly { get; set; }

    /// <summary>全部展开（手动设置仍然优先）。</summary>
    public bool ExpandAll { get; set; }

    /// <summary>搜索文字（标题、说明、节点 ID、节点类型）；空为不搜。</summary>
    public string? Search { get; set; }

    /// <summary>恢复自动折叠：清掉手动设置和“全部展开”。</summary>
    public void ResetFolding()
    {
        Overrides.Clear();
        ExpandAll = false;
    }

    /// <summary>切换一行的展开状态。</summary>
    /// <param name="line">行。</param>
    public void Toggle(WorkflowRunLogLine line)
    {
        Guard.NotNull(line);
        if (line.HasChildren)
            Overrides[line.Key] = !line.Expanded;
    }

    /// <summary>展开成列表。必须在 <see cref="WorkflowRunLogBook.Read{T}"/> 的锁内调用。</summary>
    /// <param name="log">一次运行。</param>
    /// <param name="now">当前时间（算进行中的耗时）。</param>
    public IReadOnlyList<WorkflowRunLogLine> Build(WorkflowRunLog log, DateTimeOffset now)
    {
        Guard.NotNull(log);
        var lines = new List<WorkflowRunLogLine>();
        var search = string.IsNullOrWhiteSpace(Search) ? null : Search!.Trim();
        var filtering = Filter != WorkflowRunLogFilter.All || search is not null;
        HashSet<WorkflowRunLogRow>? keep = null;
        if (filtering)
        {
            keep = new HashSet<WorkflowRunLogRow>();
            Mark(log.Root, search, keep);
        }
        Emit(log.Root, 0, string.Empty, now, keep, lines);
        return lines;
    }

    private bool Mark(WorkflowRunLogRow row, string? search, HashSet<WorkflowRunLogRow> keep)
    {
        var any = false;
        foreach (var child in row.Children)
            if (Visible(child) && Mark(child, search, keep))
                any = true;
        if (any || Matches(row, search))
        {
            keep.Add(row);
            return true;
        }
        return false;
    }

    private bool Matches(WorkflowRunLogRow row, string? search)
    {
        var filter = Filter switch
        {
            WorkflowRunLogFilter.Failures => row.Kind is WorkflowRunLogRowKind.Fault or WorkflowRunLogRowKind.Recovery
                || row.State is WorkflowRunLogState.Failed or WorkflowRunLogState.Interrupted or WorkflowRunLogState.FaultRouted,
            WorkflowRunLogFilter.Notices => row.Kind == WorkflowRunLogRowKind.Notice,
            _ => true
        };
        if (!filter) return false;
        if (search is null) return true;
        return Contains(row.Title, search) || Contains(row.Detail, search) || Contains(row.NodeId, search) || Contains(row.NodeType, search);
    }

    private static bool Contains(string? text, string search) => text is not null && text.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;

    private bool Visible(WorkflowRunLogRow row) => !(ControlOnly && row.Kind == WorkflowRunLogRowKind.Message);

    private void Emit(WorkflowRunLogRow row, int depth, string path, DateTimeOffset now, HashSet<WorkflowRunLogRow>? keep, List<WorkflowRunLogLine> lines)
    {
        var children = row.Children.Where(child => Visible(child) && (keep is null || keep.Contains(child))).ToArray();
        var key = "r" + row.Id.ToString(CultureInfo.InvariantCulture);
        var expanded = Expanded(key, AutoExpanded(row) || (keep is not null && children.Length > 0), children.Length > 0);
        var linePath = row.Parent is null ? string.Empty : path.Length == 0 ? row.Title : path + " / " + row.Title;
        lines.Add(new WorkflowRunLogLine(key, row.Id, depth, row.Kind, row.Title, Describe(row, children), row.Started, row.Ended,
            Elapsed(row, now), row.State, children.Length > 0, expanded, row.HasFailure, row.NoticeCount, row.NodeId, row.NodeType,
            row.CanvasPath, row.TokenId, row.ParentTokenId, linePath, row.Data, false));
        if (!expanded) return;
        // 连续成功的圈合并成一行（筛选时不合并，直接列出命中的圈）。
        for (var index = 0; index < children.Length; index++)
        {
            var child = children[index];
            if (keep is null && Quiet(child))
            {
                var end = index;
                while (end + 1 < children.Length && Quiet(children[end + 1])) end++;
                if (end > index)
                {
                    EmitGroup(row, children, index, end, depth + 1, linePath, now, lines);
                    index = end;
                    continue;
                }
            }
            Emit(child, depth + 1, linePath, now, keep, lines);
        }
    }

    private void EmitGroup(WorkflowRunLogRow parent, WorkflowRunLogRow[] children, int first, int last, int depth, string path, DateTimeOffset now,
        List<WorkflowRunLogLine> lines)
    {
        var start = children[first];
        var stop = children[last];
        var key = "g" + parent.Id.ToString(CultureInfo.InvariantCulture) + ":" + start.Id.ToString(CultureInfo.InvariantCulture);
        var expanded = Expanded(key, false, true);
        var count = last - first + 1;
        var total = TimeSpan.Zero;
        for (var index = first; index <= last; index++)
            total += Elapsed(children[index], now) ?? TimeSpan.Zero;
        var title = $"第 {start.Cycle.ToString(CultureInfo.InvariantCulture)}–{stop.Cycle.ToString(CultureInfo.InvariantCulture)} 圈";
        var detail = $"全部成功 · 平均 {WorkflowRunLogText.Duration(TimeSpan.FromTicks(total.Ticks / count))} / 圈";
        lines.Add(new WorkflowRunLogLine(key, null, depth, WorkflowRunLogRowKind.Cycle, title, detail, start.Started, stop.Ended,
            stop.Ended is { } ended ? ended - start.Started : null, WorkflowRunLogState.Succeeded, true, expanded, false, 0, null, null,
            start.CanvasPath, 0, 0, path.Length == 0 ? title : path + " / " + title, null, true));
        if (!expanded) return;
        for (var index = first; index <= last; index++)
            Emit(children[index], depth + 1, path, now, null, lines);
    }

    private bool Expanded(string key, bool auto, bool hasChildren)
    {
        if (!hasChildren) return false;
        if (Overrides.TryGetValue(key, out var manual)) return manual;
        return ExpandAll || auto;
    }

    private static bool Quiet(WorkflowRunLogRow row) =>
        row.Kind == WorkflowRunLogRowKind.Cycle && row.State == WorkflowRunLogState.Succeeded && !row.HasFailure && row.NoticeCount == 0;

    private static bool AutoExpanded(WorkflowRunLogRow row) =>
        row.Kind == WorkflowRunLogRowKind.Run
        || row.HasFailure
        || row.NoticeCount > 0
        || (row.IsContainer && row.State == WorkflowRunLogState.Running);

    private static TimeSpan? Elapsed(WorkflowRunLogRow row, DateTimeOffset now)
    {
        if (row.Elapsed is { } reported) return reported;
        if (row.Ended is { } ended) return ended - row.Started;
        return row.State == WorkflowRunLogState.Running ? now - row.Started : null;
    }

    private static string? Describe(WorkflowRunLogRow row, WorkflowRunLogRow[] children)
    {
        switch (row.Kind)
        {
            case WorkflowRunLogRowKind.LoopBlock:
            {
                var cycles = row.Children.Count(child => child.Kind == WorkflowRunLogRowKind.Cycle);
                var running = row.Children.LastOrDefault(child => child.Kind == WorkflowRunLogRowKind.Cycle && child.State == WorkflowRunLogState.Running);
                return running is null ? $"已转 {cycles} 圈" : $"已转 {cycles} 圈 · 第 {running.Cycle} 圈进行中";
            }
            case WorkflowRunLogRowKind.SubFlow:
            case WorkflowRunLogRowKind.Branch:
            case WorkflowRunLogRowKind.Cycle:
            {
                var steps = row.Children.Count(child => child.NodeId is not null && child.Kind is not (WorkflowRunLogRowKind.Message or WorkflowRunLogRowKind.Notice));
                var text = $"{steps} 步";
                if (row.State == WorkflowRunLogState.Running && row.Children.LastOrDefault(child => child.State == WorkflowRunLogState.Running) is { } current)
                    text += $" · 到“{current.Title}”";
                if (row.NoticeCount > 0) text += $" · 提示 {row.NoticeCount}";
                return string.IsNullOrWhiteSpace(row.Detail) || row.Kind != WorkflowRunLogRowKind.SubFlow ? text : row.Detail + " · " + text;
            }
            case WorkflowRunLogRowKind.Run:
                return row.Detail;
            default:
                return row.Detail;
        }
    }
}

/// <summary>运行日志的显示文字。</summary>
public static class WorkflowRunLogText
{
    /// <summary>种类的标签（列表里的小标记）；普通节点为空。</summary>
    /// <param name="kind">种类。</param>
    public static string? KindTag(WorkflowRunLogRowKind kind) => kind switch
    {
        WorkflowRunLogRowKind.Run => "运行",
        WorkflowRunLogRowKind.SubFlow => "子流程",
        WorkflowRunLogRowKind.LoopBlock => "循环块",
        WorkflowRunLogRowKind.Parallel => "并行",
        WorkflowRunLogRowKind.Fault => "故障",
        WorkflowRunLogRowKind.Recovery => "恢复",
        WorkflowRunLogRowKind.Notice => "提示",
        WorkflowRunLogRowKind.Message => "消息",
        _ => null
    };

    /// <summary>结果文字。</summary>
    /// <param name="state">结果。</param>
    public static string State(WorkflowRunLogState state) => state switch
    {
        WorkflowRunLogState.Running => "运行中",
        WorkflowRunLogState.Succeeded => "成功",
        WorkflowRunLogState.Failed => "失败",
        WorkflowRunLogState.Interrupted => "中断",
        WorkflowRunLogState.Canceled => "取消",
        WorkflowRunLogState.FaultRouted => "故障出口",
        WorkflowRunLogState.Undone => "已撤回",
        _ => string.Empty
    };

    /// <summary>时间（本地时间，到毫秒）。</summary>
    /// <param name="time">时间。</param>
    public static string Time(DateTimeOffset time) => time.ToLocalTime().ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);

    /// <summary>耗时：不到 1 秒用 ms，不到 1 分钟用 s，再长用 分:秒。</summary>
    /// <param name="span">耗时。</param>
    public static string Duration(TimeSpan span)
    {
        if (span < TimeSpan.Zero) span = TimeSpan.Zero;
        if (span.TotalMilliseconds < 1) return span.TotalMilliseconds.ToString("0.0", CultureInfo.InvariantCulture) + " ms";
        if (span.TotalSeconds < 1) return span.TotalMilliseconds.ToString("0", CultureInfo.InvariantCulture) + " ms";
        if (span.TotalSeconds < 10) return span.TotalSeconds.ToString("0.00", CultureInfo.InvariantCulture) + " s";
        if (span.TotalMinutes < 1) return span.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture) + " s";
        if (span.TotalHours < 1) return ((int)span.TotalMinutes).ToString(CultureInfo.InvariantCulture) + ":" + span.Seconds.ToString("00", CultureInfo.InvariantCulture);
        return ((int)span.TotalHours).ToString(CultureInfo.InvariantCulture) + ":" + span.Minutes.ToString("00", CultureInfo.InvariantCulture) + ":" + span.Seconds.ToString("00", CultureInfo.InvariantCulture);
    }

    /// <summary>详情面板文字：位置、节点、结果、时间、令牌、消息和数据。</summary>
    /// <param name="flowName">流程名。</param>
    /// <param name="line">行。</param>
    public static string Details(string flowName, WorkflowRunLogLine line)
    {
        Guard.NotNull(line);
        var text = new System.Text.StringBuilder();
        text.Append("位置：").Append(flowName);
        if (line.Path.Length > 0) text.Append(" / ").Append(line.Path);
        text.AppendLine();
        if (line.NodeId is not null)
            text.Append("节点：").Append(line.Title).Append("（").Append(line.NodeType ?? line.NodeId).Append("，ID ").Append(line.NodeId).AppendLine("）");
        if (KindTag(line.Kind) is { } tag) text.Append("种类：").AppendLine(tag);
        if (line.State != WorkflowRunLogState.None) text.Append("结果：").AppendLine(State(line.State));
        text.Append("开始：").AppendLine(Time(line.Started));
        if (line.Ended is { } ended) text.Append("结束：").AppendLine(Time(ended));
        if (line.Elapsed is { } elapsed) text.Append("耗时：").AppendLine(Duration(elapsed));
        if (line.TokenId != 0)
        {
            text.Append("令牌：T-").Append(line.TokenId.ToString(CultureInfo.InvariantCulture));
            if (line.ParentTokenId != 0) text.Append(" · 父 T-").Append(line.ParentTokenId.ToString(CultureInfo.InvariantCulture));
            text.AppendLine();
        }
        if (line.CanvasPath != "$") text.Append("画布：").AppendLine(line.CanvasPath);
        if (!string.IsNullOrWhiteSpace(line.Detail)) text.Append("消息：").AppendLine(line.Detail);
        if (line.Data is { Count: > 0 } data)
        {
            var values = data.Where(pair => pair.Key != "Message").ToArray();
            if (values.Length > 0)
            {
                text.AppendLine("数据：");
                foreach (var pair in values)
                    text.Append("  ").Append(pair.Key).Append(" = ").AppendLine(pair.Value?.Text ?? Convert.ToString(pair.Value?.Scalar, CultureInfo.InvariantCulture));
            }
        }
        return text.ToString();
    }
}
