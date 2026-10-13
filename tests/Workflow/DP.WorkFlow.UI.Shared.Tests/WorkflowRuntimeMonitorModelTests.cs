using DP.WorkFlow.UI;

namespace DP.WorkFlow.Tests;

public sealed class WorkflowRuntimeMonitorModelTests
{
    [Fact]
    public void SetSnapshot_ProjectsTokensAndParallelScopes()
    {
        var snapshot = new WorkflowRuntimeSnapshot(
            Guid.NewGuid(),
            3,
            DateTimeOffset.UtcNow,
            "Monitor",
            E_WorkflowExecutionState.Running,
            "A",
            new[] { "A", "B" },
            new Dictionary<long, WorkflowActiveTokenInfo>
            {
                [2] = new(2, new[] { 1L }, new[] { 9L }, "B"),
                [1] = new(1, Array.Empty<long>(), Array.Empty<long>(), "A")
            },
            TimeSpan.FromSeconds(1),
            new Dictionary<string, WorkflowNodeRuntimeInfo>
            {
                ["A"] = new("A", E_NodeState.Completed, 2, 2, DateTimeOffset.UtcNow.AddMilliseconds(-20), DateTimeOffset.UtcNow, TimeSpan.FromMilliseconds(20), null)
            },
            new Dictionary<long, WorkflowParallelScopeInfo>
            {
                [9] = new(9, "Parallel", "Merge", 2, 1, false)
            },
            new Dictionary<string, WorkflowChildRuntimeInfo>(),
            new Dictionary<string, WorkflowChildRuntimeInfo>(),
            Array.Empty<string>());
        var model = new WorkflowRuntimeMonitorModel();

        model.SetSnapshot(snapshot);

        Assert.Equal(new[] { 1L, 2L }, model.Tokens.Select(item => item.TokenId));
        var scope = Assert.Single(model.ParallelScopes);
        Assert.Equal(1, scope.CompletedBranches);
        Assert.Equal(2, scope.TotalBranches);
        var timing = Assert.Single(model.TimingTrends);
        Assert.Equal(10, timing.AverageMilliseconds);

        model.SetTraceBatch(new WorkflowTraceBatch(
            snapshot.RunId,
            4,
            new[]
            {
                new WorkflowTraceEntry(
                    4,
                    DateTimeOffset.UtcNow,
                    "B",
                    "Action",
                    "Invoke",
                    "执行动作",
                    null,
                    2,
                    new[] { 9L })
            }));
        var trace = Assert.Single(model.TraceEntries);
        Assert.Equal(2, trace.TokenId);
        Assert.Equal("9", trace.ScopePath);
        model.SetTracePaused(true);
        model.SetTraceBatch(new WorkflowTraceBatch(snapshot.RunId, 5, Array.Empty<WorkflowTraceEntry>()));
        Assert.Single(model.TraceEntries);
        model.SetTracePaused(false);
        Assert.Empty(model.TraceEntries);
        model.SetTraceBatch(new WorkflowTraceBatch(snapshot.RunId, 4, new[]
        {
            new WorkflowTraceEntry(4, DateTimeOffset.UtcNow, "B", "Action", "Invoke", "执行动作", null, 2, new[] { 9L })
        }));

        model.SetTraceFilter("动作");
        Assert.Single(model.TraceEntries);
        model.SetTraceFilter("missing");
        Assert.Empty(model.TraceEntries);
        model.SetTraceFilter(null);
        using var writer = new StringWriter();
        model.ExportTraceCsv(writer);
        Assert.Contains("Sequence,Timestamp", writer.ToString(), StringComparison.Ordinal);
        Assert.Contains("\"执行动作\"", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ChildRunTrace_ShowsCompositeNodeAndCycleInNodeColumn()
    {
        var model = new WorkflowRuntimeMonitorModel();
        var now = DateTimeOffset.UtcNow;
        model.SetTraceBatch(new WorkflowTraceBatch(Guid.NewGuid(), 2, new[]
        {
            new WorkflowTraceEntry(2, now.AddMilliseconds(2), "Loop", "LoopBlock", "NodeStarted", null, null),
            new WorkflowTraceEntry(1, now.AddMilliseconds(5), "FindLine", "Vision.FindLine", "NodeCompleted", null, null)
            {
                Parents = new[] { new WorkflowTraceParent("Loop", 3) }
            }
        }));

        Assert.Equal(new[] { "Loop", "Loop·第3圈 › FindLine" }, model.TraceEntries.Select(item => item.NodeId));
        model.SetTraceFilter("第3圈");
        Assert.Single(model.TraceEntries);
        Assert.Equal("第3圈 · FindLine", WorkflowRuntimeMonitorModel.DescribeChildProgress(
            new WorkflowChildMonitorItem("Loop", "Child", E_WorkflowExecutionState.Running, TimeSpan.Zero, Guid.NewGuid(), 3, "FindLine")));
    }

    [Fact]
    public void AppendTraceBatch_AddsOnlyNewEntries_KeepsTimeOrderFilterAndCapacity()
    {
        var run = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        WorkflowTraceEntry Entry(long sequence, int milliseconds, string node, string step = "NodeCompleted") =>
            new(sequence, now.AddMilliseconds(milliseconds), node, "Action", step, null, null) { RunId = run };
        WorkflowTraceBatch Batch(params WorkflowTraceEntry[] entries) => new(run, entries.Max(entry => entry.Sequence), entries);
        var model = new WorkflowRuntimeMonitorModel { TraceCapacity = 4 };
        model.SetTraceFilter("A");
        var changes = 0;
        model.Changed += (_, _) => changes++;

        model.AppendTraceBatch(Batch(Entry(1, 0, "A1"), Entry(2, 1, "B1")));
        model.AppendTraceBatch(Batch(Entry(3, 2, "A2")));
        Assert.Equal(new[] { "A1", "A2" }, model.TraceEntries.Select(item => item.NodeId));
        Assert.Equal(2, changes);

        // 子运行的条目晚到但发生得更早：按时间插入到中间。
        model.AppendTraceBatch(Batch(Entry(4, 1, "A0")));
        Assert.Equal(new[] { "A1", "A0", "A2" }, model.TraceEntries.Select(item => item.NodeId));

        // 超出容量丢弃最早的条目，筛选结果同步丢弃。
        model.AppendTraceBatch(Batch(Entry(5, 3, "A3"), Entry(6, 4, "B2")));
        Assert.Equal(new[] { "A0", "A2", "A3" }, model.TraceEntries.Select(item => item.NodeId));

        // 空的增量批次不触发刷新；新一轮运行清空此前的条目。
        var before = changes;
        model.AppendTraceBatch(new WorkflowTraceBatch(run, 6, Array.Empty<WorkflowTraceEntry>()));
        Assert.Equal(before, changes);
        model.AppendTraceBatch(new WorkflowTraceBatch(Guid.NewGuid(), 1, new[]
        {
            new WorkflowTraceEntry(1, now, "A9", "Action", "NodeStarted", null, null)
        }));
        Assert.Equal("A9", Assert.Single(model.TraceEntries).NodeId);
    }

    [Fact]
    public void AppendTraceBatch_KeepsCursorFromSource()
    {
        var model = new WorkflowRuntimeMonitorModel();
        var cursor = WorkflowTraceCursor.Empty;
        model.AppendTraceBatch(new WorkflowTraceBatch(Guid.NewGuid(), 0, Array.Empty<WorkflowTraceEntry>()) { Cursor = cursor });
        Assert.Same(cursor, model.TraceCursor);
        model.SetTraceBatch(null);
        Assert.Null(model.TraceCursor);
        Assert.Empty(model.TraceEntries);
    }
}
