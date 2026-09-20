namespace DP.WorkFlow.Tests;

public sealed class QueueNodeTests
{
    [Fact]
    public async Task QueueService_WakesWaiterAndRemovesValue()
    {
        var queues = new WorkflowQueueService();
        Assert.True(queues.Enqueue("Jobs", "A", preventDuplicates: true));
        Assert.False(queues.Enqueue("Jobs", "A", preventDuplicates: true));

        var waiting = queues.WaitDequeueAsync("Jobs", TimeSpan.FromSeconds(1), CancellationToken.None).AsTask();
        Assert.Equal("A", await waiting);
        Assert.Empty(queues.Snapshot("Jobs"));
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
