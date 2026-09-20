namespace DP.WorkFlow;

/// <summary>
/// 提供不占用工作线程的异步手动复位事件。
/// </summary>
internal sealed class AsyncManualResetEvent
{
    private TaskCompletionSource<bool> _source = CreateSource(completed: true);

    public bool IsSet => Volatile.Read(ref _source).Task.IsCompleted;

    public Task WaitAsync(CancellationToken cancellationToken) =>
        Volatile.Read(ref _source).Task.WaitAsync(cancellationToken);

    public void Set() => Volatile.Read(ref _source).TrySetResult(true);

    public void Reset()
    {
        while (true)
        {
            var source = _source;
            if (!source.Task.IsCompleted)
                return;
            var replacement = CreateSource(completed: false);
            if (ReferenceEquals(Interlocked.CompareExchange(ref _source, replacement, source), source))
                return;
        }
    }

    private static TaskCompletionSource<bool> CreateSource(bool completed)
    {
        var source = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (completed)
            source.SetResult(true);
        return source;
    }
}
