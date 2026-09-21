using System.Collections.Concurrent;

namespace DP.WorkFlow;

/// <summary>
/// 单个 Run 的事件记录深模块：分配序号、编码 Payload、维护最近窗口并向顶层 Sink 推送。
/// </summary>
/// <remarks>
/// <para>记录链路完全 fail-open：<see cref="Record"/> 只把事件放入有界队列就返回，
/// 外部 Sink 的写入、刷新和失败都在独立路径中处理，不会阻塞节点调度或改变 Run 终态。</para>
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
    private readonly ConcurrentQueue<WorkflowRunEvent> _pending = new();
    private readonly SemaphoreSlim _signal = new(0);
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
    private int _flushRequested;
    private string? _lastError;
    private DateTimeOffset? _lastFailureAt;
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

            flush = writeMode == WorkflowEventWriteMode.Durable;
            if (flush)
                Interlocked.Exchange(ref _flushRequested, 1);
            if (_sink is not null)
            {
                _pending.Enqueue(recorded);
                TrimPending();
                if (flush || _pending.Count >= _options.BufferedBatchSize)
                    _signal.Release();
                _loop ??= Task.Run(RunLoopAsync);
            }
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
        _signal.Release();
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
        _signal.Release();
        await WaitForLoopAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
        _writeCancellation.Cancel();
        _writeCancellation.Dispose();
        _signal.Dispose();
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
        var interval = _options.BufferedFlushInterval;
        while (!_stopping)
        {
            try
            {
                await _signal.WaitAsync(interval).ConfigureAwait(false);
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            if (_stopping)
                break;
            await PushPendingAsync(Interlocked.Exchange(ref _flushRequested, 0) == 1).ConfigureAwait(false);
        }
        await PushPendingAsync(true).ConfigureAwait(false);
    }

    private async Task PushPendingAsync(bool immediate)
    {
        var sink = _sink;
        if (sink is null || _writeCancellation.IsCancellationRequested)
            return;

        var firstBatch = true;
        while (true)
        {
            var batch = new List<WorkflowRunEvent>(_options.BufferedBatchSize);
            while (batch.Count < _options.BufferedBatchSize && _pending.TryDequeue(out var item))
                batch.Add(item);
            if (batch.Count == 0)
                return;

            try
            {
                var result = await sink
                    .WriteAsync(batch, firstBatch && immediate, _writeCancellation.Token)
                    .ConfigureAwait(false);
                if (result is null || !result.Succeeded)
                    ReportFailure(result?.Error ?? "运行事件写入未确认。");
                else
                    MarkConfirmed(batch[^1].Sequence);
            }
            catch (OperationCanceledException) when (_writeCancellation.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                ReportFailure("写入运行事件失败：" + exception.Message);
            }
            firstBatch = false;
        }
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

    private void TrimPending()
    {
        // 有界队列始终为最新事件腾出空间：容量不足时淘汰最老数据，绝不阻塞流程。
        while (_pending.Count > _options.PendingQueueCapacity)
        {
            if (!_pending.TryDequeue(out _))
                break;
            Interlocked.Increment(ref _droppedCount);
        }
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
        _lastFailureAt);

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
}
