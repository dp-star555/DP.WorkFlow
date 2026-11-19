using System.Collections.Concurrent;

namespace DP.WorkFlow;

/// <summary>
/// 引擎回调的分发器（R1-04 / R4-a）：执行线程只把通知放进队列，由后台按顺序逐个调用订阅方。
/// 订阅方再慢也不拖慢节点执行；订阅方抛出的异常被吞掉并计数，通过 <see cref="FailureReported"/> 交给引擎记为诊断。
/// </summary>
internal sealed class WorkflowNotificationDispatcher
{
    private readonly ConcurrentQueue<Action> _pending = new();
    private readonly object _drainSync = new();
    private Task _drain = Task.CompletedTask;
    private int _draining;
    private long _failureCount;

    /// <summary>订阅方回调抛出异常时发生（在分发线程上，参数为异常说明）。</summary>
    public Action<string>? FailureReported { get; set; }

    /// <summary>累计的订阅方异常次数。</summary>
    public long FailureCount => Interlocked.Read(ref _failureCount);

    /// <summary>把一次回调放入队列；没有订阅方时调用方应直接跳过，避免分配。</summary>
    public void Post(Action notification)
    {
        _pending.Enqueue(notification);
        if (Interlocked.CompareExchange(ref _draining, 1, 0) != 0)
            return;
        lock (_drainSync)
            _drain = Task.Run(DrainAsync);
    }

    /// <summary>等待此前放入队列的通知全部送达。</summary>
    public async Task FlushAsync()
    {
        while (true)
        {
            Task drain;
            lock (_drainSync)
                drain = _drain;
            await drain.ConfigureAwait(false);
            if (_pending.IsEmpty && Volatile.Read(ref _draining) == 0)
                return;
            await Task.Yield();
        }
    }

    private Task DrainAsync()
    {
        while (true)
        {
            while (_pending.TryDequeue(out var notification))
            {
                try
                {
                    notification();
                }
                catch (Exception exception)
                {
                    Interlocked.Increment(ref _failureCount);
                    try
                    {
                        FailureReported?.Invoke("回调订阅方异常：" + exception.Message);
                    }
                    catch
                    {
                        // 诊断上报本身失败时同样不能影响后续通知。
                    }
                }
            }
            Volatile.Write(ref _draining, 0);
            // 释放标志后若又有新通知且没有其他分发者接手，继续处理，避免通知滞留。
            if (_pending.IsEmpty || Interlocked.CompareExchange(ref _draining, 1, 0) != 0)
                return Task.CompletedTask;
        }
    }
}
