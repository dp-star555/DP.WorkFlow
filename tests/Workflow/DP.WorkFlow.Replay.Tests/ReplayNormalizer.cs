using System.Globalization;
using System.Text;

namespace DP.WorkFlow.Tests.Replay;

/// <summary>
/// 把一次运行（含子运行）的事件规整成稳定文本，用于和基准逐行对比。
/// </summary>
/// <remarks>
/// 去掉每次运行都会变的内容：RunId、时间、耗时、令牌和作用域的内部编号。
/// 并行分支之间的先后顺序本身不确定，因此按令牌分段：每个令牌内部保持事件顺序，
/// 令牌用"进入分支的第一个节点"命名（如 <c>主线/Left</c>），不用运行时编号。
/// 子运行按开始时间排列，挂在所属运行下面。
/// </remarks>
public static class ReplayNormalizer
{
    private static readonly string[] VolatileKeyFragments =
    {
        "Elapsed", "RunId", "Timestamp", "Duration", "Time"
    };

    public static string Normalize(IReadOnlyList<WorkflowRunEvent> events)
    {
        var runs = events.GroupBy(item => item.RunId)
            .ToDictionary(group => group.Key, group => group.OrderBy(item => item.Sequence).ToArray());
        var roots = runs.Values
            .Where(run => run[0].ParentRunId is not { } parentId || !runs.ContainsKey(parentId))
            .OrderBy(run => run[0].Timestamp)
            .ToArray();
        var builder = new StringBuilder();
        foreach (var root in roots)
            WriteRun(builder, root, runs, depth: 0);
        return builder.ToString();
    }

    private static void WriteRun(
        StringBuilder builder,
        WorkflowRunEvent[] run,
        IReadOnlyDictionary<Guid, WorkflowRunEvent[]> runs,
        int depth)
    {
        var indent = new string(' ', depth * 4);
        var first = run[0];
        builder.Append(indent).Append("运行 [").Append(first.WorkflowName).Append(']');
        if (first.ParentExecution is { } parent)
            builder.Append(" 父节点第 ").Append(parent.NodeExecutionCount).Append(" 次执行");
        builder.AppendLine();

        var labels = LabelTokens(run);
        var outputs = LabelOutputs(run);
        foreach (var lane in run
                     .GroupBy(item => labels.TryGetValue(item.ExecutionIdentity?.TokenId ?? 0, out var label) ? label : "主线")
                     .OrderBy(group => group.Key == "主线" ? string.Empty : group.Key, StringComparer.Ordinal))
        {
            builder.Append(indent).Append("  [").Append(lane.Key).AppendLine("]");
            foreach (var item in lane)
                builder.Append(indent).Append("    ").AppendLine(Describe(item, outputs));
        }

        var children = runs.Values
            .Where(child => child[0].ParentRunId == first.RunId)
            .OrderBy(child => child[0].Timestamp)
            .ToArray();
        foreach (var child in children)
            WriteRun(builder, child, runs, depth + 1);
    }

    /// <summary>令牌命名：根令牌为"主线"，分支令牌为"父令牌名/分支第一个节点"。</summary>
    private static Dictionary<long, string> LabelTokens(WorkflowRunEvent[] run)
    {
        var firstNode = new Dictionary<long, string>();
        var parents = new Dictionary<long, long?>();
        foreach (var item in run)
        {
            if (item.ExecutionIdentity is not { } identity)
                continue;
            if (item.NodeId is not null && item.EventType == "NodeStarted")
                firstNode.TryAdd(identity.TokenId, item.NodeId);
            parents.TryAdd(identity.TokenId, identity.AncestorTokenIds.Count == 0 ? null : identity.AncestorTokenIds[^1]);
        }
        var labels = new Dictionary<long, string>();
        string Label(long token)
        {
            if (labels.TryGetValue(token, out var known))
                return known;
            var parent = parents.TryGetValue(token, out var value) ? value : null;
            var label = parent is null
                ? "主线"
                : Label(parent.Value) + "/" + (firstNode.TryGetValue(token, out var node) ? node : "?");
            labels[token] = label;
            return label;
        }
        foreach (var token in parents.Keys)
            Label(token);
        return labels;
    }

    /// <summary>
    /// 输出序号按提交先后分配，并行分支谁先提交不确定；规整为"节点#第几次执行"，
    /// 读取方的来源序号也换成同一写法，数据血缘仍可核对。
    /// </summary>
    private static Dictionary<string, string> LabelOutputs(WorkflowRunEvent[] run)
    {
        var labels = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var item in run)
        {
            if (item.EventType != "OutputCommitted" || item.Data is null || item.NodeId is null)
                continue;
            if (item.Data.TryGetValue(OutputSequenceKey, out var value) && Format(value) is { } sequence)
                labels[sequence] = item.NodeId + "#" + (item.ExecutionIdentity?.NodeExecutionCount ?? 0);
        }
        return labels;
    }

    private const string OutputSequenceKey = "OutputExecutionSequence";
    private const string SourceSequenceKey = "SourceOutputSequence";

    private static string Describe(WorkflowRunEvent item, IReadOnlyDictionary<string, string> outputs)
    {
        var line = new StringBuilder(item.EventType);
        if (item.NodeId is not null)
        {
            line.Append(' ').Append(item.NodeId);
            if (item.ExecutionIdentity is { NodeExecutionCount: > 0 } identity)
            {
                line.Append('#').Append(identity.NodeExecutionCount);
                if (identity.LoopIteration is { } lap)
                    line.Append(" 圈").Append(lap);
            }
        }
        if (item.Data is null)
            return line.ToString();
        foreach (var pair in item.Data.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            if (VolatileKeyFragments.Any(fragment => pair.Key.Contains(fragment, StringComparison.OrdinalIgnoreCase)))
                continue;
            var text = Format(pair.Value);
            if (text is null)
                continue;
            if (pair.Key is OutputSequenceKey or SourceSequenceKey)
                text = outputs.TryGetValue(text, out var label) ? label : "?" + text;
            line.Append(' ').Append(pair.Key).Append('=').Append(text);
        }
        return line.ToString();
    }

    private static string? Format(WorkflowTraceValue value)
    {
        var raw = value.Scalar is IFormattable formattable
            ? formattable.ToString(null, CultureInfo.InvariantCulture)
            : value.Scalar?.ToString() ?? value.Text;
        if (raw is null)
            return null;
        if (Guid.TryParse(raw, out _))
            return "<guid>";
        return raw.Replace("\r", "\\r").Replace("\n", "\\n");
    }
}
