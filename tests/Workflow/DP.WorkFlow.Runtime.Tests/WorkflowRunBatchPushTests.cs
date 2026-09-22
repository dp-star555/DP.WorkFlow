namespace DP.WorkFlow.Tests;

/// <summary>
/// §20「崩溃与可靠性」的批次级验收：Recorder 以"前台不可变批次"而不是单条事件作为推送颗粒度。
/// 这些用例保护封箱条件、FlushRequested 立即刷意图的归属、通知粒度以及按批次核算的待发送容量。
/// </summary>
public sealed class WorkflowRunBatchPushTests
{
    [Fact]
    public async Task Batch_SealsExactlyAtConfiguredSize_AndTailStaysInCurrentBatch()
    {
        var sink = new RecordingSink();
        var options = new WorkflowRunRecordingOptions
        {
            Sink = sink,
            BufferedBatchSize = 128,
            BufferedFlushInterval = TimeSpan.FromSeconds(30)
        };
        await using var recorder = new WorkflowRunRecorder(Guid.NewGuid(), "封箱", 4_096, options);

        for (var index = 0; index < 129; index++)
            recorder.Record(WorkflowRunEventDraft.Lifecycle("Step" + index));

        await WaitUntilAsync(() => sink.Batches.Count >= 1);

        // 第 128 条在临界区内立即封箱；第 129 条留在当前批次，不受 30s 间隔影响。
        Assert.Equal(new[] { 128 }, sink.BatchSizes);
        Assert.Equal(128, sink.Batches[0].Events[^1].Sequence);

        await recorder.CompleteAsync(
            new WorkflowRunCompletion(E_WorkflowExecutionState.Completed, "完成", TimeSpan.Zero));

        // Run 收尾封装非空尾批次，第 129 条进入的是新批次而不是追加到已封箱的批次。
        Assert.Equal(new[] { 128, 1 }, sink.BatchSizes);
        Assert.Equal(129, sink.Batches[1].Events[0].Sequence);
    }

    [Fact]
    public async Task FlushRequested_AppendsToCurrentBatch_ThenSealsWholeBatchWithImmediateFlush()
    {
        var sink = new RecordingSink();
        var options = new WorkflowRunRecordingOptions
        {
            Sink = sink,
            BufferedBatchSize = 128,
            BufferedFlushInterval = TimeSpan.FromSeconds(30)
        };
        await using var recorder = new WorkflowRunRecorder(Guid.NewGuid(), "立即封箱", 4_096, options);

        for (var index = 0; index < 20; index++)
            recorder.Record(WorkflowRunEventDraft.Lifecycle("Buffered" + index));
        recorder.Record(WorkflowRunEventDraft.Lifecycle("RunCompleted"), WorkflowEventWriteMode.FlushRequested);

        await WaitUntilAsync(() => sink.Batches.Count >= 1);

        Assert.Equal(new[] { 21 }, sink.BatchSizes);
        Assert.True(sink.Batches[0].RequestImmediateFlush);
        Assert.Equal("RunCompleted", sink.Batches[0].Events[^1].EventType);
    }

    [Fact]
    public async Task FlushRequested_KeepsImmediateFlushIntent_WhenConsumerIsBusyWithEarlierBatch()
    {
        var sink = new RecordingSink(holdFirstWrite: true);
        var options = new WorkflowRunRecordingOptions
        {
            Sink = sink,
            BufferedBatchSize = 128,
            BufferedFlushInterval = TimeSpan.FromSeconds(30)
        };
        await using var recorder = new WorkflowRunRecorder(Guid.NewGuid(), "忙时封箱", 4_096, options);

        for (var index = 0; index < 128; index++)
            recorder.Record(WorkflowRunEventDraft.Lifecycle("Buffered" + index));

        // 消费者已经进入第一批的写入并被 Sink 卡住，此时 FlushRequested 事件到达。
        await WaitUntilAsync(() => sink.Batches.Count >= 1);
        Assert.Equal(128, sink.Batches[0].Events.Count);

        recorder.Record(WorkflowRunEventDraft.Lifecycle("RunCompleted"), WorkflowEventWriteMode.FlushRequested);
        sink.ReleaseWrite();

        await WaitUntilAsync(() => sink.Batches.Count >= 2);

        // 立即刷意图必须跟着"含有该 FlushRequested 事件的批次"走，不能被更早的批次吞掉。
        var flushRequestedEvent = Assert.Single(sink.Batches[1].Events);
        Assert.Equal("RunCompleted", flushRequestedEvent.EventType);
        Assert.True(
            sink.Batches[1].RequestImmediateFlush,
            "FlushRequested 的立即刷意图必须绑定到含该事件的批次，而不是全局标记位。");
    }

    [Fact]
    public async Task Batch_SealsOnIntervalWhileInputKeepsArriving()
    {
        var sink = new RecordingSink();
        var options = new WorkflowRunRecordingOptions
        {
            Sink = sink,
            BufferedBatchSize = 1_000,
            BufferedFlushInterval = TimeSpan.FromMilliseconds(50)
        };
        await using var recorder = new WorkflowRunRecorder(Guid.NewGuid(), "间隔封箱", 4_096, options);

        using var producerCancellation = new CancellationTokenSource();
        var producer = Task.Run(
            async () =>
            {
                while (!producerCancellation.IsCancellationRequested)
                {
                    recorder.Record(WorkflowRunEventDraft.Lifecycle("Tick"));
                    try
                    {
                        await Task.Delay(25, producerCancellation.Token).ConfigureAwait(false);
                    }
                    catch (TaskCanceledException)
                    {
                        return;
                    }
                }
            },
            producerCancellation.Token);

        // 条数条件（1000）远未达到，只能靠间隔封箱；输入持续不断时仍必须按周期送出批次。
        var delivered = await sink.WaitForBatchCountAsync(2, TimeSpan.FromSeconds(2));
        producerCancellation.Cancel();
        await producer;

        Assert.True(
            delivered,
            "持续低频输入不得无限延后批次截止时间：2 秒内至少应送出 2 个批次。");
    }

    [Fact]
    public async Task EmptyBatch_IsNeverWrittenToSink()
    {
        var sink = new RecordingSink();
        var options = new WorkflowRunRecordingOptions
        {
            Sink = sink,
            BufferedFlushInterval = TimeSpan.FromMilliseconds(20)
        };
        await using var recorder = new WorkflowRunRecorder(Guid.NewGuid(), "空批次", 64, options);

        // 空闲若干轮询周期后仍不得产生任何 Sink 调用。
        await Task.Delay(200);
        Assert.Empty(sink.Batches);

        recorder.Record(WorkflowRunEventDraft.Lifecycle("RunStarted"));
        await WaitUntilAsync(() => sink.Batches.Count >= 1);

        await recorder.CompleteAsync(
            new WorkflowRunCompletion(E_WorkflowExecutionState.Completed, "完成", TimeSpan.Zero));

        Assert.All(sink.Batches, batch => Assert.NotEmpty(batch.Events));
    }

    [Fact]
    public async Task BlockedSink_DoesNotAccumulateEmptyWakeupsPerEvent()
    {
        var sink = new RecordingSink(holdFirstWrite: true);
        var options = new WorkflowRunRecordingOptions
        {
            Sink = sink,
            BufferedBatchSize = 128,
            PendingQueueCapacity = 4_096,
            BufferedFlushInterval = TimeSpan.FromSeconds(30)
        };
        await using var recorder = new WorkflowRunRecorder(Guid.NewGuid(), "空唤醒", 4_096, options);

        for (var index = 0; index < 1_000; index++)
            recorder.Record(WorkflowRunEventDraft.Lifecycle("Step" + index));

        await WaitUntilAsync(() => sink.Batches.Count >= 1);
        sink.ReleaseWrite();
        await WaitUntilAsync(() => sink.Batches.Count >= 7);
        await Task.Delay(200);

        // 1000 条事件只应产生个位数批次的唤醒；按事件累计唤醒会得到数百次空转。
        Assert.True(
            recorder.EmptyDrainCount < 64,
            $"通知必须按批次累计，实际空唤醒 {recorder.EmptyDrainCount} 次，已随事件条数增长。");
    }

    [Fact]
    public async Task Capacity_EvictsWholeBatches_AndCountsTheirEvents()
    {
        var sink = new RecordingSink(holdFirstWrite: true);
        const int batchSize = 128;
        const int total = batchSize * 10;
        var options = new WorkflowRunRecordingOptions
        {
            Sink = sink,
            BufferedBatchSize = batchSize,
            PendingQueueCapacity = 200,
            BufferedFlushInterval = TimeSpan.FromSeconds(30)
        };
        await using var recorder = new WorkflowRunRecorder(Guid.NewGuid(), "淘汰", 4_096, options);

        for (var index = 0; index < total; index++)
            recorder.Record(WorkflowRunEventDraft.Lifecycle("Step" + index));

        var dropped = recorder.Health.DroppedCount;

        // 容量按事件数核算，但淘汰单位是"整批"：丢弃数必须是批次大小的整数倍。
        Assert.True(dropped > 0, "容量远小于已封箱事件数时必须淘汰最老批次。");
        Assert.True(
            dropped % batchSize == 0,
            $"按批次整体淘汰时丢弃数应为 {batchSize} 的整数倍，实际为 {dropped}。");

        sink.ReleaseWrite();
        await WaitUntilAsync(
            () => sink.Batches.Count > 0 && sink.Batches[^1].Events[^1].Sequence == total);

        // 始终优先保留最新数据：最后送到 Sink 的事件必须是刚刚记录的最新事件。
        Assert.Equal(total, sink.Batches[^1].Events[^1].Sequence);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMilliseconds = 3_000)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(timeoutMilliseconds));
        while (!condition())
            await Task.Delay(10, timeout.Token).ConfigureAwait(false);
    }

    private sealed record RecordedBatch(IReadOnlyList<WorkflowRunEvent> Events, bool RequestImmediateFlush);

    private sealed class RecordingSink(bool holdFirstWrite = false) : IWorkflowRunEventSink
    {
        private readonly object _sync = new();
        private readonly List<RecordedBatch> _batches = new();
        private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool _holdsNextWrite = holdFirstWrite;

        public IReadOnlyList<RecordedBatch> Batches
        {
            get
            {
                lock (_sync)
                    return _batches.ToArray();
            }
        }

        public int[] BatchSizes => Batches.Select(batch => batch.Events.Count).ToArray();

        public void ReleaseWrite() => _gate.TrySetResult();

        public async ValueTask<WorkflowRunEventWriteResult> WriteAsync(
            IReadOnlyList<WorkflowRunEvent> events,
            bool requestImmediateFlush,
            CancellationToken cancellationToken)
        {
            bool hold;
            lock (_sync)
            {
                hold = _holdsNextWrite;
                _holdsNextWrite = false;
                _batches.Add(new RecordedBatch(events.ToArray(), requestImmediateFlush));
            }

            if (hold)
                await _gate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);

            return WorkflowRunEventWriteResult.Success;
        }

        public ValueTask FlushAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public async Task<bool> WaitForBatchCountAsync(int count, TimeSpan timeout)
        {
            var deadline = DateTimeOffset.UtcNow + timeout;
            while (Batches.Count < count)
            {
                if (DateTimeOffset.UtcNow > deadline)
                    return false;
                await Task.Delay(10).ConfigureAwait(false);
            }

            return true;
        }
    }
}
