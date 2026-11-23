using DP.WorkFlow.UI;

namespace DP.WorkFlow.Tests;

public sealed class WorkflowRunStatisticsTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 23, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void RunsWithoutLoops_CountEachEndOnceAndAverageTotalElapsed()
    {
        var statistics = new WorkflowRunStatistics();
        var first = Guid.NewGuid();
        statistics.Observe(Snapshot(first, 1, T0, E_WorkflowExecutionState.Running, TimeSpan.FromMilliseconds(100)), T0);
        statistics.Observe(Snapshot(first, 2, T0.AddMilliseconds(400), E_WorkflowExecutionState.Completed, TimeSpan.FromMilliseconds(400)), T0.AddMilliseconds(430));
        // 同一次运行结束的快照再来一遍不重复计数。
        statistics.Observe(Snapshot(first, 3, T0.AddMilliseconds(500), E_WorkflowExecutionState.Completed, TimeSpan.FromMilliseconds(400)), T0.AddMilliseconds(500));
        var second = Guid.NewGuid();
        statistics.Observe(Snapshot(second, 1, T0.AddSeconds(1), E_WorkflowExecutionState.Completed, TimeSpan.FromMilliseconds(600)), T0.AddSeconds(1));
        var third = Guid.NewGuid();
        statistics.Observe(Snapshot(third, 1, T0.AddSeconds(2), E_WorkflowExecutionState.Faulted, TimeSpan.FromMilliseconds(50)), T0.AddSeconds(2));

        Assert.Equal(3, statistics.Runs);
        Assert.Equal(2, statistics.Succeeded);
        Assert.Equal(1, statistics.Failed);
        Assert.Equal(2, statistics.CycleSamples);
        Assert.Equal(TimeSpan.FromMilliseconds(500), statistics.AverageCycle);
        Assert.Equal(TimeSpan.FromMilliseconds(600), statistics.LastCycle);
        Assert.Equal(0, statistics.Laps);
    }

    [Fact]
    public void LoopLaps_UseIntervalBetweenLapsAndSplitSkippedLaps()
    {
        var statistics = new WorkflowRunStatistics();
        var run = Guid.NewGuid();
        statistics.Observe(Snapshot(run, 1, T0, E_WorkflowExecutionState.Running, TimeSpan.Zero, ("loop", 1)), T0);
        statistics.Observe(Snapshot(run, 2, T0.AddMilliseconds(300), E_WorkflowExecutionState.Running, TimeSpan.Zero, ("loop", 2)), T0.AddMilliseconds(310));
        // 一帧里跳过两圈：600 ms 平分成两次 300 ms。
        statistics.Observe(Snapshot(run, 3, T0.AddMilliseconds(900), E_WorkflowExecutionState.Running, TimeSpan.Zero, ("loop", 4)), T0.AddMilliseconds(900));

        Assert.Equal(4, statistics.Laps);
        Assert.Equal(3, statistics.CycleSamples);
        Assert.Equal(TimeSpan.FromMilliseconds(300), statistics.AverageCycle);
        Assert.Equal(TimeSpan.Zero, statistics.DisplayLatency);

        // 有循环的运行结束时不再按总耗时记节拍。
        statistics.Observe(Snapshot(run, 4, T0.AddSeconds(1), E_WorkflowExecutionState.Canceled, TimeSpan.FromSeconds(1), ("loop", 4)), T0.AddSeconds(1));
        Assert.Equal(1, statistics.Canceled);
        Assert.Equal(3, statistics.CycleSamples);
    }

    [Fact]
    public void Window_KeepsOnlyRecentCycles_AndResetClears()
    {
        var statistics = new WorkflowRunStatistics(window: 2);
        foreach (var milliseconds in new[] { 100, 200, 400 })
            statistics.Observe(Snapshot(Guid.NewGuid(), 1, T0, E_WorkflowExecutionState.Completed, TimeSpan.FromMilliseconds(milliseconds)), T0.AddMilliseconds(20));

        Assert.Equal(2, statistics.CycleSamples);
        Assert.Equal(TimeSpan.FromMilliseconds(300), statistics.AverageCycle);
        Assert.Equal(TimeSpan.FromMilliseconds(20), statistics.DisplayLatency);

        statistics.Reset();
        Assert.Equal(0, statistics.Runs);
        Assert.Null(statistics.AverageCycle);
        Assert.Null(statistics.DisplayLatency);
    }

    private static WorkflowRuntimeSnapshot Snapshot(Guid runId, long sequence, DateTimeOffset at, E_WorkflowExecutionState state, TimeSpan elapsed,
        params (string Node, int Cycle)[] loops)
    {
        var children = loops.ToDictionary(
            loop => loop.Node,
            loop => new WorkflowChildRuntimeInfo(loop.Node,
                new WorkflowExecutionIdentity(runId, 1, Array.Empty<long>(), Array.Empty<long>(), 1),
                Snapshot(Guid.NewGuid(), 1, at, E_WorkflowExecutionState.Running, TimeSpan.Zero),
                loop.Cycle),
            StringComparer.Ordinal);
        return new WorkflowRuntimeSnapshot(runId, sequence, at, "flow", state, null, Array.Empty<string>(),
            new Dictionary<long, WorkflowActiveTokenInfo>(), elapsed, new Dictionary<string, WorkflowNodeRuntimeInfo>(),
            new Dictionary<long, WorkflowParallelScopeInfo>(), children, new Dictionary<string, WorkflowChildRuntimeInfo>(),
            Array.Empty<string>());
    }
}
