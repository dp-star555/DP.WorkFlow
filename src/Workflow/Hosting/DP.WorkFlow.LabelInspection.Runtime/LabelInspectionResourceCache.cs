namespace DP.WorkFlow.LabelInspection;

internal sealed class LabelInspectionCacheCapacityException : InvalidOperationException
{
    internal LabelInspectionCacheCapacityException() : base("标签资源缓存已满且资源正在使用/加载，请等待后重试或提高缓存数量上限。") { }
}

// 内部缓存模块：合并加载、调用租约、闲置LRU/TTL、故障回滚和停机排空。模型与配方共享同一生命周期规则。
internal sealed class LabelInspectionResourceCache<T> : IAsyncDisposable where T : class, IDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly HashSet<Entry> _live = new();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _loadSlots;
    private readonly TimeProvider _clock;
    private readonly TimeSpan _idle;
    private readonly int _maximum;
    private bool _disposed;
    private int _loads, _hits, _evictions;

    internal LabelInspectionResourceCache(int maximum, int concurrentLoads, TimeSpan idle, TimeProvider clock)
    { _maximum = maximum; _loadSlots = new(concurrentLoads); _idle = idle; _clock = clock; }
    internal int Count { get { lock (_gate) return _entries.Count; } }
    internal int Loads => Volatile.Read(ref _loads);
    internal int Hits => Volatile.Read(ref _hits);
    internal int Evictions => Volatile.Read(ref _evictions);

    internal async Task<Lease> AcquireAsync(string key, Func<CancellationToken, Task<T>> load, CancellationToken token,
        string? group = null)
    {
        token.ThrowIfCancellationRequested();
        Entry? entry = null;
        bool start = false;
        List<Entry> release = new();
        try
        {
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                CollectExpiredLocked(release);
                if (_entries.TryGetValue(key, out entry))
                { entry.Users++; entry.LastUsed = _clock.GetTimestamp(); Interlocked.Increment(ref _hits); }
                else
                {
                    // 同一节点的新版本不永久占据第二个缓存位置；旧调用的租约继续有效。
                    if (group is not null)
                        foreach (var old in _entries.Values.Where(e => e.Group == group).ToArray()) RetireLocked(old, release);
                    // 已退役但仍在用的旧版本也占加载名额，避免连续换版本绕过数量上限。
                    if (_live.Count(e => !e.Disposing && (e.Value is not null || !e.LoadFinished)) >= _maximum)
                    {
                        var oldest = _entries.Values.Where(e => e.Users == 0 && e.Value is not null)
                            .OrderBy(e => e.LastUsed).FirstOrDefault();
                        if (oldest is null) throw new LabelInspectionCacheCapacityException();
                        RetireLocked(oldest, release);
                    }
                    entry = new Entry(key, group, _clock.GetTimestamp()) { Users = 1 };
                    _entries.Add(key, entry); _live.Add(entry); start = true;
                }
            }
        }
        finally { DisposeEntries(release); }
        if (start) _ = LoadAsync(entry!, load);
        try
        {
            // 单个请求取消只退出自己的等待，不取消其他请求共同需要的加载。
            var value = await entry!.Ready.Task.WaitAsync(token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            return new Lease(this, entry, value);
        }
        catch { Release(entry!); throw; }
    }

    internal void Cleanup(bool removeAllIdle = false)
    {
        List<Entry> release = new();
        lock (_gate)
        {
            if (_disposed) return;
            foreach (var entry in _entries.Values.ToArray())
                if (entry.Users == 0 && entry.Value is not null && (removeAllIdle || Expired(entry))) RetireLocked(entry, release);
        }
        DisposeEntries(release);
    }

    private async Task LoadAsync(Entry entry, Func<CancellationToken, Task<T>> load)
    {
        bool slot = false;
        try
        {
            await _loadSlots.WaitAsync(_lifetime.Token).ConfigureAwait(false); slot = true;
            var value = await load(_lifetime.Token).ConfigureAwait(false);
            lock (_gate) { entry.Value = value; entry.LastUsed = _clock.GetTimestamp(); }
            Interlocked.Increment(ref _loads);
            entry.Ready.TrySetResult(value);
        }
        catch (Exception error)
        {
            lock (_gate)
            {
                if (_entries.TryGetValue(entry.Key, out var found) && ReferenceEquals(found, entry)) _entries.Remove(entry.Key);
                entry.Retired = true;
            }
            entry.Ready.TrySetException(error);
            _ = entry.Ready.Task.Exception; // 所有等待者已取消时，也观察加载故障。
        }
        finally
        {
            if (slot) _loadSlots.Release();
            List<Entry> release = new();
            lock (_gate) { entry.LoadFinished = true; CollectDisposalLocked(entry, release); }
            DisposeEntries(release);
        }
    }

    private bool Expired(Entry entry) => _clock.GetElapsedTime(entry.LastUsed, _clock.GetTimestamp()) >= _idle;
    private void CollectExpiredLocked(List<Entry> release)
    {
        foreach (var entry in _entries.Values.ToArray())
            if (entry.Users == 0 && entry.Value is not null && Expired(entry)) RetireLocked(entry, release);
    }
    private void RetireLocked(Entry entry, List<Entry> release)
    {
        if (entry.Retired) return;
        entry.Retired = true;
        _entries.Remove(entry.Key);
        Interlocked.Increment(ref _evictions);
        CollectDisposalLocked(entry, release);
    }
    private static void CollectDisposalLocked(Entry entry, List<Entry> release)
    {
        if (entry.Retired && entry.Users == 0 && entry.LoadFinished && !entry.Disposing)
        { entry.Disposing = true; release.Add(entry); }
    }
    private void Release(Entry entry, bool touch = true)
    {
        List<Entry> release = new();
        lock (_gate)
        {
            entry.Users--; if (touch) entry.LastUsed = _clock.GetTimestamp();
            CollectDisposalLocked(entry, release);
        }
        DisposeEntries(release);
    }
    private void DisposeEntries(IEnumerable<Entry> entries)
    {
        foreach (var entry in entries)
        {
            try { entry.Value?.Dispose(); entry.Drained.TrySetResult(); }
            catch (Exception error) { entry.Drained.TrySetException(error); _ = entry.Drained.Task.Exception; System.Diagnostics.Trace.TraceError($"标签缓存释放失败：{error}"); }
            finally { lock (_gate) _live.Remove(entry); }
        }
    }
    public async ValueTask DisposeAsync()
    {
        Entry[] entries;
        List<Entry> release = new();
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true; entries = _live.ToArray();
            foreach (var entry in entries) { entry.Retired = true; CollectDisposalLocked(entry, release); }
            _entries.Clear();
        }
        _lifetime.Cancel(); DisposeEntries(release);
        try { await Task.WhenAll(entries.Select(e => e.Drained.Task)).ConfigureAwait(false); }
        finally { _loadSlots.Dispose(); _lifetime.Dispose(); }
    }

    internal sealed class Entry(string key, string? group, long timestamp)
    {
        internal string Key { get; } = key;
        internal string? Group { get; } = group;
        internal long LastUsed = timestamp;
        internal int Users;
        internal bool Retired, LoadFinished, Disposing;
        internal T? Value;
        internal TaskCompletionSource<T> Ready { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Drained { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
    internal sealed class Lease(LabelInspectionResourceCache<T> owner, Entry entry, T value) : IDisposable
    {
        private LabelInspectionResourceCache<T>? _owner = owner;
        internal T Value { get; } = value;
        internal void Touch() { var owner = _owner; if (owner is not null) lock (owner._gate) entry.LastUsed = owner._clock.GetTimestamp(); }
        internal void DisposePreservingLastUse() => Interlocked.Exchange(ref _owner, null)?.Release(entry, false);
        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.Release(entry);
    }
}
