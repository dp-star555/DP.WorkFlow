namespace DP.WorkFlow.Tests;

/// <summary>R1-01：运行事实的时间戳使用高精度、单调不减的时钟。</summary>
public sealed class WorkflowClockTests
{
    [Fact]
    public void UtcNow_IsMonotonicAndCloseToSystemTime()
    {
        var previous = WorkflowClock.UtcNow;
        var distinct = 1;
        for (var index = 0; index < 10_000; index++)
        {
            var current = WorkflowClock.UtcNow;
            Assert.True(current >= previous, $"时间倒退：{previous:O} → {current:O}");
            if (current != previous)
                distinct++;
            previous = current;
        }

        // 一万次连续读取通常在 1ms 内完成；低分辨率时钟（1～15ms）在这里只会给出极少几个不同值。
        Assert.True(distinct > 100, $"一万次读取只得到 {distinct} 个不同时间，分辨率不足。");
        Assert.True((WorkflowClock.UtcNow - DateTimeOffset.UtcNow).Duration() < TimeSpan.FromSeconds(1));
        Assert.Equal(TimeSpan.Zero, WorkflowClock.UtcNow.Offset);
    }

    [Fact]
    public async Task UtcNow_IsMonotonicAcrossThreads()
    {
        var failures = 0;
        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
        {
            var previous = WorkflowClock.UtcNow;
            for (var index = 0; index < 50_000; index++)
            {
                var current = WorkflowClock.UtcNow;
                if (current < previous)
                    Interlocked.Increment(ref failures);
                previous = current;
            }
        })));

        Assert.Equal(0, failures);
    }
}
