namespace DP.WorkFlow.Tests;

public sealed class QueueNodeTests
{
    [Fact]
    public async Task QueueService_WakesWaiterAndRemovesValue()
    {
        var queues = new WorkflowQueueService();
        var waiting = queues.WaitAsync("Jobs", E_SignalValueType.String, snapshot => snapshot.HasFirstValue,
            TimeSpan.FromSeconds(1), true, CancellationToken.None).AsTask();
        Assert.True(queues.EnqueueValue("Jobs", "A", E_SignalValueType.String, true, true));
        Assert.False(queues.EnqueueValue("Jobs", "A", E_SignalValueType.String, true, true));

        Assert.Equal("A", (await waiting)?.FirstValue);
        Assert.True(queues.TryDequeueValue("Jobs", E_SignalValueType.String, false, out var value));
        Assert.Equal("A", value);
        Assert.Equal(0, queues.ReadSnapshot("Jobs")!.Count);
    }

    [Fact]
    public void QueueService_EnforcesTypeAndPreservesVersionedSnapshot()
    {
        var queues = new WorkflowQueueService();
        Assert.True(queues.EnsureExists("Jobs", E_SignalValueType.Int32));
        Assert.True(queues.EnqueueValue("Jobs", 10, E_SignalValueType.Int32, true, false));
        Assert.False(queues.EnqueueValue("Jobs", 10, E_SignalValueType.Int32, true, false));
        Assert.True(queues.EnqueueValue("Jobs", 20, E_SignalValueType.Int32, true, false));

        var snapshot = queues.ReadSnapshot("Jobs");
        Assert.Equal(2, snapshot!.Count);
        Assert.Equal(2, snapshot.Version);
        Assert.Equal(10, snapshot.FirstValue);
        Assert.Throws<InvalidOperationException>(() => queues.EnqueueValue("Jobs", "bad", E_SignalValueType.String, false, false));
    }
}
