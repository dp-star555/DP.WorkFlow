using System.Collections.Concurrent;

namespace DP.WorkFlow;

/// <summary>默认线程安全、广播变更通知的数据信号服务。</summary>
public sealed class WorkflowValueSignalService : IWorkflowValueSignalService
{
    private sealed class Entry
    {
        internal readonly object Sync = new();
        internal object? Value;
        internal long Version;
        internal bool HasValue;
        internal TaskCompletionSource<long> Changed = NewChange();
        internal static TaskCompletionSource<long> NewChange() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public WorkflowValueSignalSnapshot? ReadSnapshot(string signalKey)
    {
        var key = Normalize(signalKey);
        if (!_entries.TryGetValue(key, out var entry)) return null;
        lock (entry.Sync) return new(key, entry.Value, entry.Version, entry.HasValue);
    }
    /// <inheritdoc />
    public WorkflowValueSignalSnapshot WriteValue(string signalKey, object value)
    {
        Guard.NotNull(value);
        var key = Normalize(signalKey);
        var entry = _entries.GetOrAdd(key, static _ => new Entry());
        TaskCompletionSource<long> changed;
        WorkflowValueSignalSnapshot snapshot;
        lock (entry.Sync)
        {
            entry.Value = value;
            entry.HasValue = true;
            entry.Version++;
            changed = entry.Changed;
            entry.Changed = Entry.NewChange();
            snapshot = new(key, value, entry.Version, true);
        }
        changed.TrySetResult(snapshot.Version);
        return snapshot;
    }
    /// <inheritdoc />
    public async ValueTask<WorkflowValueSignalSnapshot?> WaitValueAsync(string signalKey, long minVersion, Func<object?, bool> predicate, TimeSpan? timeout, CancellationToken cancellationToken)
    {
        Guard.NotNull(predicate);
        var key = Normalize(signalKey);
        var entry = _entries.GetOrAdd(key, static _ => new Entry());
        using var timeoutSource = timeout.HasValue ? new CancellationTokenSource(timeout.Value) : null;
        using var linked = timeoutSource is null ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken) : CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);
        while (true)
        {
            Task changed;
            lock (entry.Sync)
            {
                if (entry.HasValue && entry.Version > minVersion && predicate(entry.Value)) return new(key, entry.Value, entry.Version, true);
                changed = entry.Changed.Task;
            }
            try { await changed.WaitAsync(linked.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (timeoutSource?.IsCancellationRequested == true && !cancellationToken.IsCancellationRequested) { return null; }
        }
    }
    private static string Normalize(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("信号键不能为空。", nameof(key));
        return key.Trim();
    }
}
