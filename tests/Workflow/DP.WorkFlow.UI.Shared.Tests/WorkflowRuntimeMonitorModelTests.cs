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
}
