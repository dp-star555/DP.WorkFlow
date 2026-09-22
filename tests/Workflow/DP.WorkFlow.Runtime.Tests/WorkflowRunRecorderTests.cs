namespace DP.WorkFlow.Tests;

/// <summary>
/// §20验收：Recorder 的统一序号、有界队列、FlushRequested 调度、fail-open 与健康状态。
/// 记录链路不得阻塞节点调度，也不得把外部写入失败传播成工作流故障。
/// </summary>
public sealed class WorkflowRunRecorderTests
{
    [Fact]
    public async Task Record_AssignsUniqueMonotonicSequence()
    {
        await using var recorder = new WorkflowRunRecorder(
            Guid.NewGuid(), "序号", 64, new WorkflowRunRecordingOptions());

        var receipts = Enumerable.Range(0, 20)
            .Select(index => recorder.Record(WorkflowRunEventDraft.Lifecycle("Step" + index)))
            .ToArray();

        Assert.All(receipts, receipt => Assert.True(receipt.Accepted));
        Assert.Equal(
            Enumerable.Range(1, 20).Select(value => (long)value),
            receipts.Select(receipt => receipt.Sequence));
        var batch = recorder.GetRecent();
        Assert.Equal(20, batch.Events.Count);
        Assert.Equal(20, batch.LastSequence);
        Assert.Equal(20, recorder.Health.RecordedCount);
        Assert.Equal(E_WorkflowRecordingHealth.Healthy, recorder.Health.State);
    }

    [Fact]
    public async Task Window_IsBoundedAndKeepsLatestEvents()
    {
        await using var recorder = new WorkflowRunRecorder(
            Guid.NewGuid(), "窗口", recentWindowCapacity: 5, new WorkflowRunRecordingOptions());

        for (var index = 0; index < 20; index++)
            recorder.Record(WorkflowRunEventDraft.Lifecycle("Step" + index));

        var batch = recorder.GetRecent();

        // 有界窗口始终为最新事件腾出空间：容量不足时淘汰最老数据，并累计丢弃计数。
        Assert.Equal(5, batch.Events.Count);
        Assert.Equal(16, batch.Events[0].Sequence);
        Assert.Equal(20, batch.LastSequence);
        Assert.Equal(15, recorder.Health.DroppedCount);
        Assert.Equal(new[] { 18L, 19L, 20L }, recorder.GetRecent(17).Events.Select(item => item.Sequence));
    }

    [Fact]
    public async Task PendingQueue_IsBoundedAndDropsOldestWithoutBlocking()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var options = new WorkflowRunRecordingOptions
        {
            Sink = new BlockingSink(gate.Task),
            PendingQueueCapacity = 8,
            BufferedBatchSize = 1_000,
            BufferedFlushInterval = TimeSpan.FromMilliseconds(20)
        };
        await using var recorder = new WorkflowRunRecorder(Guid.NewGuid(), "有界", 64, options);

        for (var index = 0; index < 200; index++)
            recorder.Record(WorkflowRunEventDraft.Lifecycle("Step" + index));

        var health = recorder.Health;
        Assert.Equal(200, health.RecordedCount);
        Assert.True(health.DroppedCount > 0, "待推送队列达到上限时必须淘汰最老事件，而不是无限占用内存。");

        gate.TrySetResult();
        await recorder.CompleteAsync(
            new WorkflowRunCompletion(E_WorkflowExecutionState.Completed, "完成", TimeSpan.Zero));
    }

    [Fact]
    public async Task FlushRequestedEvent_TriggersImmediateDispatchBeforeBufferedInterval()
    {
        var sink = new CollectingSink();
        var options = new WorkflowRunRecordingOptions
        {
            Sink = sink,
            BufferedFlushInterval = TimeSpan.FromSeconds(30)
        };
        await using var recorder = new WorkflowRunRecorder(Guid.NewGuid(), "立即", 64, options);

        var receipt = recorder.Record(
            WorkflowRunEventDraft.Lifecycle("RunStarted"), WorkflowEventWriteMode.FlushRequested);

        Assert.True(receipt.FlushRequested);
        Assert.True(
            await sink.WaitForFirstWriteAsync(TimeSpan.FromSeconds(2)),
            "FlushRequested 事件必须在远早于 Buffered 间隔的时间内触发推送。");
        Assert.Contains(sink.Events, item => item.EventType == "RunStarted");
    }

    [Fact]
    public async Task Record_DoesNotWaitForExternalSink()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var options = new WorkflowRunRecordingOptions
        {
            Sink = new BlockingSink(gate.Task),
            BufferedFlushInterval = TimeSpan.FromMilliseconds(10)
        };
        await using var recorder = new WorkflowRunRecorder(Guid.NewGuid(), "非阻塞", 64, options);

        var watch = System.Diagnostics.Stopwatch.StartNew();
        for (var index = 0; index < 50; index++)
            recorder.Record(WorkflowRunEventDraft.Lifecycle("Step" + index), WorkflowEventWriteMode.FlushRequested);
        watch.Stop();

        Assert.True(
            watch.ElapsedMilliseconds < 500,
            $"记录调用被外部 Sink 阻塞了 {watch.ElapsedMilliseconds}ms；记录必须立即返回。");

        gate.TrySetResult();
        await recorder.CompleteAsync(
            new WorkflowRunCompletion(E_WorkflowExecutionState.Completed, "完成", TimeSpan.Zero));
    }

    [Fact]
    public async Task SinkFailure_UpdatesHealthAndNotifiesHost()
    {
        var options = new WorkflowRunRecordingOptions
        {
            Sink = new FailingSink("外部存储不可用。"),
            HealthNotificationInterval = TimeSpan.Zero
        };
        await using var recorder = new WorkflowRunRecorder(Guid.NewGuid(), "故障", 64, options);
        var notifications = new List<WorkflowRecordingHealth>();
        recorder.HealthChanged += health =>
        {
            lock (notifications)
                notifications.Add(health);
        };

        recorder.Record(WorkflowRunEventDraft.Lifecycle("RunStarted"), WorkflowEventWriteMode.FlushRequested);
        // 宿主通知与状态更新在同一失败路径上完成；等待通知到达，避免与后台推送线程竞态。
        await WaitUntilAsync(() =>
        {
            lock (notifications)
                return notifications.Count > 0;
        });

        var health = recorder.Health;
        Assert.Equal(E_WorkflowRecordingHealth.Failed, health.State);
        Assert.True(health.FailedWriteCount > 0);
        Assert.Contains("外部存储不可用", health.LastError);
        lock (notifications)
            Assert.Contains(notifications, item => item.State == E_WorkflowRecordingHealth.Failed);
    }

    [Fact]
    public async Task CompleteAsync_FlushesAcceptedEventsInSequenceOrder()
    {
        var sink = new CollectingSink();
        var options = new WorkflowRunRecordingOptions
        {
            Sink = sink,
            BufferedFlushInterval = TimeSpan.FromSeconds(30)
        };
        var recorder = new WorkflowRunRecorder(Guid.NewGuid(), "收尾", 64, options);

        recorder.Record(WorkflowRunEventDraft.Lifecycle("RunStarted"));
        recorder.Record(WorkflowRunEventDraft.Lifecycle("RunCompleted"), WorkflowEventWriteMode.FlushRequested);
        await recorder.CompleteAsync(
            new WorkflowRunCompletion(E_WorkflowExecutionState.Completed, "完成", TimeSpan.Zero));
        await recorder.DisposeAsync();

        Assert.Equal(new[] { 1L, 2L }, sink.Events.Select(item => item.Sequence));
        Assert.Equal("RunCompleted", sink.Events[^1].EventType);
        Assert.True(sink.FlushCallCount > 0, "正常收尾必须尽力 Flush 已接受事件。");
    }

    [Fact]
    public async Task Record_AfterComplete_IsRejectedWithoutThrowing()
    {
        var recorder = new WorkflowRunRecorder(
            Guid.NewGuid(), "停止", 64, new WorkflowRunRecordingOptions());
        await recorder.CompleteAsync(
            new WorkflowRunCompletion(E_WorkflowExecutionState.Completed, "完成", TimeSpan.Zero));

        var receipt = recorder.Record(WorkflowRunEventDraft.Lifecycle("Late"));

        Assert.False(receipt.Accepted);
        Assert.NotNull(receipt.Error);
        await recorder.DisposeAsync();
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        while (!condition())
            await Task.Delay(10, timeout.Token);
    }

    private sealed class CollectingSink : IWorkflowRunEventSink
    {
        private readonly object _sync = new();
        private readonly List<WorkflowRunEvent> _events = new();
        private readonly TaskCompletionSource _firstWrite = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int FlushCallCount { get; private set; }

        public IReadOnlyList<WorkflowRunEvent> Events
        {
            get
            {
                lock (_sync)
                    return _events.ToArray();
            }
        }

        public ValueTask<WorkflowRunEventWriteResult> WriteAsync(
            IReadOnlyList<WorkflowRunEvent> events,
            bool requestImmediateFlush,
            CancellationToken cancellationToken)
        {
            lock (_sync)
                _events.AddRange(events);
            _firstWrite.TrySetResult();
            return ValueTask.FromResult(WorkflowRunEventWriteResult.Success);
        }

        public ValueTask FlushAsync(CancellationToken cancellationToken)
        {
            FlushCallCount++;
            return ValueTask.CompletedTask;
        }

        public async Task<bool> WaitForFirstWriteAsync(TimeSpan timeout)
        {
            try
            {
                await _firstWrite.Task.WaitAsync(timeout);
                return true;
            }
            catch (TimeoutException)
            {
                return false;
            }
        }
    }

    private sealed class BlockingSink(Task gate) : IWorkflowRunEventSink
    {
        public async ValueTask<WorkflowRunEventWriteResult> WriteAsync(
            IReadOnlyList<WorkflowRunEvent> events,
            bool requestImmediateFlush,
            CancellationToken cancellationToken)
        {
            await gate.WaitAsync(cancellationToken);
            return WorkflowRunEventWriteResult.Success;
        }

        public ValueTask FlushAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }

    private sealed class FailingSink(string error) : IWorkflowRunEventSink
    {
        public ValueTask<WorkflowRunEventWriteResult> WriteAsync(
            IReadOnlyList<WorkflowRunEvent> events,
            bool requestImmediateFlush,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(WorkflowRunEventWriteResult.Failed(error));

        public ValueTask FlushAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }
}
