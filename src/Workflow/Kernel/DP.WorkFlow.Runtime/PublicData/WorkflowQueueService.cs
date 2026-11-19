using System.Collections.Concurrent;

namespace DP.WorkFlow;

/// <summary>默认进程内命名队列服务。</summary>
public sealed class WorkflowQueueService : IWorkflowQueueService
{
    private sealed class Entry
    {
        internal Entry(E_SignalValueType type) => Type = type;
        internal readonly object Sync = new();
        internal readonly List<object> Items = new();
        internal readonly E_SignalValueType Type;
        internal long Version;
        internal TaskCompletionSource<long> Changed = NewChange();
        internal static TaskCompletionSource<long> NewChange() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
    private readonly ConcurrentDictionary<string, Entry> _queues = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public bool EnsureExists(string queueKey, E_SignalValueType valueType)
    {
        var key = Normalize(queueKey);
        var created = false;
        var entry = _queues.GetOrAdd(key, _ => { created = true; return new Entry(valueType); });
        EnsureType(entry, valueType, key);
        return created;
    }
    /// <inheritdoc />
    public bool Clear(string queueKey, E_SignalValueType valueType, bool autoCreate)
    {
        var entry = Get(queueKey, valueType, autoCreate);
        if (entry is null) return false;
        TaskCompletionSource<long>? changed = null;
        lock (entry.Sync)
        {
            if (entry.Items.Count == 0) return false;
            entry.Items.Clear(); entry.Version++; changed = entry.Changed; entry.Changed = Entry.NewChange();
        }
        changed.TrySetResult(entry.Version); return true;
    }
    /// <inheritdoc />
    public bool EnqueueValue(string queueKey, object value, E_SignalValueType valueType, bool preventDuplicate, bool autoCreate)
    {
        var entry = Get(queueKey, valueType, autoCreate) ?? throw new KeyNotFoundException("队列不存在: " + queueKey);
        TaskCompletionSource<long> changed;
        lock (entry.Sync)
        {
            if (preventDuplicate && entry.Items.Contains(value)) return false;
            entry.Items.Add(value); entry.Version++; changed = entry.Changed; entry.Changed = Entry.NewChange();
        }
        changed.TrySetResult(entry.Version); return true;
    }
    /// <inheritdoc />
    public bool TryDequeueValue(string queueKey, E_SignalValueType valueType, bool autoCreate, out object? value)
    {
        var entry = Get(queueKey, valueType, autoCreate);
        if (entry is null) { value = null; return false; }
        TaskCompletionSource<long>? changed;
        lock (entry.Sync)
        {
            if (entry.Items.Count == 0) { value = null; return false; }
            value = entry.Items[0]; entry.Items.RemoveAt(0); entry.Version++; changed = entry.Changed; entry.Changed = Entry.NewChange();
        }
        changed.TrySetResult(entry.Version); return true;
    }
    /// <inheritdoc />
    public bool RemoveValue(string queueKey, object value, E_SignalValueType valueType, bool autoCreate)
    {
        var entry = Get(queueKey, valueType, autoCreate);
        if (entry is null) return false;
        TaskCompletionSource<long>? changed;
        lock (entry.Sync)
        {
            var index = entry.Items.IndexOf(value); if (index < 0) return false;
            entry.Items.RemoveAt(index); entry.Version++; changed = entry.Changed; entry.Changed = Entry.NewChange();
        }
        changed.TrySetResult(entry.Version); return true;
    }
    /// <inheritdoc />
    public WorkflowQueueSnapshot? ReadSnapshot(string queueKey)
    {
        var key = Normalize(queueKey); if (!_queues.TryGetValue(key, out var entry)) return null;
        lock (entry.Sync) return Snapshot(key, entry);
    }
    /// <inheritdoc />
    public async ValueTask<WorkflowQueueSnapshot?> WaitAsync(string queueKey, E_SignalValueType valueType, Func<WorkflowQueueSnapshot, bool> predicate, TimeSpan? timeout, bool autoCreate, CancellationToken cancellationToken)
    {
        Guard.NotNull(predicate);
        var key = Normalize(queueKey); var entry = Get(key, valueType, autoCreate);
        if (entry is null) return null;
        using var timeoutSource = timeout.HasValue ? new CancellationTokenSource(timeout.Value) : null;
        using var linked = timeoutSource is null ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken) : CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);
        while (true)
        {
            Task changed; WorkflowQueueSnapshot snapshot;
            lock (entry.Sync) { snapshot = Snapshot(key, entry); if (predicate(snapshot)) return snapshot; changed = entry.Changed.Task; }
            try { await changed.WaitAsync(linked.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (timeoutSource?.IsCancellationRequested == true && !cancellationToken.IsCancellationRequested) { return null; }
        }
    }
    private Entry? Get(string queueKey, E_SignalValueType type, bool autoCreate)
    {
        var key = Normalize(queueKey);
        if (!_queues.TryGetValue(key, out var entry)) { if (!autoCreate) return null; entry = _queues.GetOrAdd(key, _ => new Entry(type)); }
        EnsureType(entry, type, key); return entry;
    }
    private static void EnsureType(Entry entry, E_SignalValueType type, string key) { if (entry.Type != type) throw new InvalidOperationException($"队列 '{key}' 类型为 {entry.Type}，不能按 {type} 使用。"); }
    private static WorkflowQueueSnapshot Snapshot(string key, Entry entry) => new(key, entry.Type, entry.Items.ToArray(), entry.Version);
    private static string Normalize(string key) { if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("队列键不能为空。", nameof(key)); return key.Trim(); }
}
