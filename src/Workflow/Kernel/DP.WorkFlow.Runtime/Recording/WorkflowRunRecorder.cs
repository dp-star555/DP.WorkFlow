using System.Collections.Concurrent;

namespace DP.WorkFlow;

/// <summary>
/// 单个 Run 的事件记录深模块：分配序号、编码 Payload、维护最近窗口并向顶层 Sink 推送。
/// </summary>
/// <remarks>
/// <para>记录链路完全 fail-open：<see cref="Record"/> 只把事件追加到当前批次就返回，
/// 外部 Sink 的写入、刷新和失败都在独立路径中处理，不会阻塞节点调度或改变 Run 终态。</para>
/// <para>推送颗粒度是"已封装批次"而不是单条事件：前台在短临界区内把当前批次分离成不可变批次，
/// 后台只消费完整批次，通知也按批次而不是按事件计数。因此唤醒次数与批次数量同阶，
/// 不会因为 Sink 变慢而随事件条数累积。</para>
/// <para>因为记录不能阻塞运行，Runtime 无法保证进程闪退前最后几条事件一定已落盘；
/// 崩溃持久性只覆盖顶层 Sink 已经确认的事件，取决于 Sink 实现和实际确认进度。</para>
/// </remarks>
public sealed class WorkflowRunRecorder : IWorkflowRunRecorder
{
    private const int SchemaVersion = 1;

    private readonly Guid _runId;
    private readonly Guid? _parentRunId;
    private readonly WorkflowExecutionIdentity? _parentExecution;
    private readonly string? _workflowName;
    private readonly WorkflowRunRecordingOptions _options;
    private readonly IWorkflowRunEventSink? _sink;
    private readonly WorkflowTracePayloadEncoder _encoder;
    private readonly ConcurrentQueue<WorkflowRunEvent> _window = new();
    private readonly List<WorkflowRunEvent> _currentBatch = new();
    private readonly ConcurrentQueue<SealedBatch> _ready = new();
    private readonly SemaphoreSlim _batchReady = new(0);
    private readonly CancellationTokenSource _writeCancellation = new();
    private readonly object _recordSync = new();
    private readonly object _healthSync = new();
    private readonly int _windowCapacity;
    private volatile Task? _loop;
    private long _sequence;
    private long _recordedCount;
    private long _droppedCount;
    private long _failedWriteCount;
    private long _lastConfirmedSequence;
    private DateTimeOffset _currentBatchStartedAt;
    private bool _currentBatchFlush;
    private int _readyEventCount;
    private int _emptyDrainCount;
    private string? _lastError;
    private DateTimeOffset? _lastFailureAt;
    private long _diagnosticCount;
    private string? _lastDiagnostic;
    private DateTimeOffset? _lastDiagnosticAt;
    private DateTimeOffset _lastHealthNotification = DateTimeOffset.MinValue;
    private E_WorkflowRecordingHealth _health = E_WorkflowRecordingHealth.Healthy;
    private volatile bool _stopping;
    private bool _disposed;

    /// <summary>初始化一个 Run 事件记录器。</summary>
    /// <param name="runId">本次运行身份；不能为 <see cref="Guid.Empty"/>。</param>
    /// <param name="workflowName">本次运行的流程名称。</param>
    /// <param name="recentWindowCapacity">内存中保留的最近事件条数。</param>
    /// <param name="options">记录容量、批量策略和 Payload 上限。</param>
    /// <param name="parentRunId">子 Run 的父 Run 身份；根 Run 为空。</param>
    /// <param name="parentExecution">触发子 Run 的父节点执行身份；根 Run 为空。</param>
    /// <exception cref="ArgumentException"><paramref name="runId"/> 为空。</exception>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> 为空。</exception>
    /// <exception cref="ArgumentOutOfRangeException">最近窗口容量非正。</exception>
    public WorkflowRunRecorder(
        Guid runId,
        string? workflowName,
        int recentWindowCapacity,
        WorkflowRunRecordingOptions options,
        Guid? parentRunId = null,
        WorkflowExecutionIdentity? parentExecution = null)
    {
        if (runId == Guid.Empty)
            throw new ArgumentException("Run 身份不能为空。", nameof(runId));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _options.Validate();
        if (recentWindowCapacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(recentWindowCapacity));

        _runId = runId;
        _workflowName = workflowName;
        _windowCapacity = recentWindowCapacity;
        _sink = options.Sink;
        _parentRunId = parentRunId;
        _parentExecution = parentExecution;
        _encoder = new WorkflowTracePayloadEncoder(_options);
    }

    /// <inheritdoc />
    public event Action<WorkflowRecordingHealth>? HealthChanged;

    /// <inheritdoc />
    public WorkflowRecordingHealth Health
    {
        get
        {
            lock (_healthSync)
                return BuildHealthLocked();
        }
    }

    /// <summary>
    /// 获取后台消费者被唤醒但没有可写入批次的累计次数。
    /// 这是记录链路的资源诊断：它只应随批次数量增长，不应随事件条数增长。
    /// </summary>
    internal int EmptyDrainCount => Volatile.Read(ref _emptyDrainCount);

    /// <inheritdoc />
    public WorkflowRunEventReceipt Record(
        WorkflowRunEventDraft @event,
        WorkflowEventWriteMode writeMode = WorkflowEventWriteMode.Buffered)
    {
        ArgumentNullException.ThrowIfNull(@event);
        WorkflowRunEvent recorded;
        bool flush;
        lock (_recordSync)
        {
            if (_stopping)
                return new WorkflowRunEventReceipt(false, 0, false, "Recorder 已经停止接收新事件。");

            var sequence = ++_sequence;
            recorded = new WorkflowRunEvent(
                _runId,
                sequence,
                DateTimeOffset.UtcNow,
                @event.Category,
                @event.EventType,
                writeMode,
                _workflowName,
                @event.NodeId,
                @event.NodeType,
                @event.ExecutionIdentity,
                @event.OperationId,
                @event.RecoveryCaseId,
                SchemaVersion,
                _encoder.Encode(@event.Data, @event.Message),
                _parentRunId,
                _parentExecution);
            AppendToWindow(recorded);
            _recordedCount++;

            flush = writeMode == WorkflowEventWriteMode.FlushRequested;
            if (_sink is null)
                return new WorkflowRunEventReceipt(true, recorded.Sequence, false);

            var notify = false;
            if (_currentBatch.Count == 0)
            {
                // 截止时间锚定在批次的第一条事件上；后续事件不得延后它。
                _currentBatchStartedAt = DateTimeOffset.UtcNow;
                notify = true;
            }

            _currentBatch.Add(recorded);
            if (flush)
                _currentBatchFlush = true;

            if (flush || _currentBatch.Count >= _options.BufferedBatchSize)
            {
                SealCurrentBatchLocked();
                notify = true;
            }

            // 通知是"至少有一个批次需要处理"，不是事件计数：同一批次最多两次唤醒。
            if (notify)
                _batchReady.Release();
            _loop ??= Task.Run(RunLoopAsync);
        }

        return new WorkflowRunEventReceipt(true, recorded.Sequence, flush);
    }

    /// <inheritdoc />
    public WorkflowRunEventBatch GetRecent(long afterSequence = 0)
    {
        var events = _window
            .Where(item => item.Sequence > afterSequence)
            .OrderBy(item => item.Sequence)
            .ToArray();
        return new WorkflowRunEventBatch(_runId, Array.AsReadOnly(events));
    }

    /// <inheritdoc />
    public async ValueTask CompleteAsync(
        WorkflowRunCompletion completion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(completion);
        if (_disposed)
            return;

        _stopping = true;
        _batchReady.Release();
        await WaitForLoopAsync(_options.CompletionFlushTimeout).ConfigureAwait(false);

        if (_sink is null || _writeCancellation.IsCancellationRequested)
            return;
        try
        {
            await _sink.FlushAsync(_writeCancellation.Token)
                .AsTask()
                .WaitAsync(_options.CompletionFlushTimeout, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            ReportFailure("刷新运行事件失败：" + exception.Message);
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;
        _stopping = true;
        _batchReady.Release();
        await WaitForLoopAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
        _writeCancellation.Cancel();
        _writeCancellation.Dispose();
        _batchReady.Dispose();
    }

    private async Task WaitForLoopAsync(TimeSpan timeout)
    {
        var loop = _loop;
        if (loop is null)
            return;
        try
        {
            await loop.WaitAsync(timeout, CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            // 收尾等待超时或后台推送异常都不能改写 Run 终态。
        }
    }

    private async Task RunLoopAsync()
    {
        while (!_stopping)
        {
            try
            {
                await _batchReady.WaitAsync(NextWaitTimeout()).ConfigureAwait(false);
            }
            catch (ObjectDisposedException)
            {
                return;
            }

            if (_stopping)
                break;

            SealDueBatch();
            await DrainReadyBatchesAsync().ConfigureAwait(false);
        }

        // Run 结束或宿主正常关闭：封装非空尾批次并尽力送出。
        lock (_recordSync)
            SealCurrentBatchLocked();
        await DrainReadyBatchesAsync().ConfigureAwait(false);
    }

    private TimeSpan NextWaitTimeout()
    {
        lock (_recordSync)
        {
            if (_currentBatch.Count == 0)
                return Timeout.InfiniteTimeSpan;

            var remaining = _currentBatchStartedAt + _options.BufferedFlushInterval - DateTimeOffset.UtcNow;
            return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
        }
    }

    private void SealDueBatch()
    {
        lock (_recordSync)
        {
            if (_currentBatch.Count == 0)
                return;
            if (DateTimeOffset.UtcNow - _currentBatchStartedAt < _options.BufferedFlushInterval)
                return;
            SealCurrentBatchLocked();
        }
    }

    private void SealCurrentBatchLocked()
    {
        if (_currentBatch.Count == 0)
            return;

        var sealedBatch = new SealedBatch(_currentBatch.ToArray(), _currentBatchFlush);
        _currentBatch.Clear();
        _currentBatchFlush = false;
        _currentBatchStartedAt = default;
        _ready.Enqueue(sealedBatch);
        Interlocked.Add(ref _readyEventCount, sealedBatch.Events.Count);
        TrimReadyBatches();
    }

    private void TrimReadyBatches()
    {
        // 容量按事件数核算，淘汰单位却是整批：始终为最新批次腾出空间，绝不淘汰正在写入的批次
        // （它已经出队），也绝不阻塞流程。至少保留最新一个批次，避免容量小于单批时清空队列。
        while (Volatile.Read(ref _readyEventCount) > _options.PendingQueueCapacity && _ready.Count > 1)
        {
            if (!_ready.TryDequeue(out var evicted))
                break;
            Interlocked.Add(ref _readyEventCount, -evicted.Events.Count);
            Interlocked.Add(ref _droppedCount, evicted.Events.Count);
        }
    }

    private async Task DrainReadyBatchesAsync()
    {
        var sink = _sink;
        if (sink is null || _writeCancellation.IsCancellationRequested)
            return;

        var drained = false;
        while (_ready.TryDequeue(out var batch))
        {
            Interlocked.Add(ref _readyEventCount, -batch.Events.Count);
            drained = true;
            try
            {
                var result = await sink
                    .WriteAsync(batch.Events, batch.RequestImmediateFlush, _writeCancellation.Token)
                    .ConfigureAwait(false);
                if (result is null || !result.Succeeded)
                    ReportFailure(result?.Error ?? "运行事件写入未确认。");
                else
                    MarkConfirmed(batch.Events[^1].Sequence);
            }
            catch (OperationCanceledException) when (_writeCancellation.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                ReportFailure("写入运行事件失败：" + exception.Message);
            }
        }

        if (!drained)
            Interlocked.Increment(ref _emptyDrainCount);
    }

    private void AppendToWindow(WorkflowRunEvent @event)
    {
        _window.Enqueue(@event);
        while (_window.Count > _windowCapacity)
        {
            if (!_window.TryDequeue(out _))
                break;
            Interlocked.Increment(ref _droppedCount);
        }
    }

    /// <inheritdoc />
    public void ReportDegraded(string message)
    {
        // fail-open：诊断报告不得抛异常，也不得把已经 Failed 的链路伪造成 Degraded。
        WorkflowRecordingHealth? notification = null;
        lock (_healthSync)
        {
            _diagnosticCount++;
            _lastDiagnostic = message ?? string.Empty;
            _lastDiagnosticAt = DateTimeOffset.UtcNow;
            if (_health == E_WorkflowRecordingHealth.Healthy)
                _health = E_WorkflowRecordingHealth.Degraded;
            var now = DateTimeOffset.UtcNow;
            if (now - _lastHealthNotification >= _options.HealthNotificationInterval)
            {
                _lastHealthNotification = now;
                notification = BuildHealthLocked();
            }
        }
        if (notification is not null)
            RaiseHealthChanged(notification);
    }

    private void ReportFailure(string message)
    {
        WorkflowRecordingHealth? notification = null;
        lock (_healthSync)
        {
            _failedWriteCount++;
            _lastError = message;
            _lastFailureAt = DateTimeOffset.UtcNow;
            _health = E_WorkflowRecordingHealth.Failed;
            var now = DateTimeOffset.UtcNow;
            if (now - _lastHealthNotification >= _options.HealthNotificationInterval)
            {
                _lastHealthNotification = now;
                notification = BuildHealthLocked();
            }
        }
        if (notification is not null)
            RaiseHealthChanged(notification);
    }

    private void MarkConfirmed(long sequence)
    {
        Interlocked.Exchange(ref _lastConfirmedSequence, sequence);
        WorkflowRecordingHealth? notification = null;
        lock (_healthSync)
        {
            if (_health == E_WorkflowRecordingHealth.Healthy)
                return;
            _health = E_WorkflowRecordingHealth.Degraded;
            _lastHealthNotification = DateTimeOffset.UtcNow;
            notification = BuildHealthLocked();
        }
        if (notification is not null)
            RaiseHealthChanged(notification);
    }

    private WorkflowRecordingHealth BuildHealthLocked() => new(
        _health,
        Interlocked.Read(ref _recordedCount),
        Interlocked.Read(ref _droppedCount),
        Interlocked.Read(ref _failedWriteCount),
        Interlocked.Read(ref _lastConfirmedSequence),
        _lastError,
        _lastFailureAt,
        Interlocked.Read(ref _diagnosticCount),
        _lastDiagnostic,
        _lastDiagnosticAt);

    private void RaiseHealthChanged(WorkflowRecordingHealth health)
    {
        var handlers = HealthChanged;
        if (handlers is null)
            return;
        foreach (Action<WorkflowRecordingHealth> handler in handlers.GetInvocationList())
        {
            try
            {
                handler(health);
            }
            catch
            {
                // 宿主通知异常不得影响记录链路，更不得影响工作流运行。
            }
        }
    }

    private sealed record SealedBatch(IReadOnlyList<WorkflowRunEvent> Events, bool RequestImmediateFlush);
}
