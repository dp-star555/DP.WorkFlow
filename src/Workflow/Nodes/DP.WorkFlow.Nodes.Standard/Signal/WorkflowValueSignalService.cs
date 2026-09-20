using System.Collections.Concurrent;

namespace DP.WorkFlow;

/// <summary>提供带版本的强类型命名数据信号。</summary>
public interface IWorkflowValueSignalService
{
    /// <summary>读取当前快照。</summary>
    WorkflowValueSignalSnapshot? ReadSnapshot(string signalKey);
    /// <summary>写入值并递增版本。</summary>
    WorkflowValueSignalSnapshot WriteValue(string signalKey, object value);
    /// <summary>等待版本大于指定版本且满足谓词的值。</summary>
    ValueTask<WorkflowValueSignalSnapshot?> WaitValueAsync(string signalKey, long minVersion, Func<object?, bool> predicate, TimeSpan? timeout, CancellationToken cancellationToken);
    /// <summary>兼容读取字符串值。</summary>
    string? Read(string signalKey);
    /// <summary>兼容写入字符串值。</summary>
    void Write(string signalKey, string value);
    /// <summary>兼容等待字符串相等。</summary>
    ValueTask<bool> WaitAsync(string signalKey, string expectedValue, TimeSpan timeout, CancellationToken cancellationToken);
}

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
        ArgumentNullException.ThrowIfNull(value);
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
        ArgumentNullException.ThrowIfNull(predicate);
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
    /// <inheritdoc />
    public string? Read(string signalKey) => Convert.ToString(ReadSnapshot(signalKey)?.Value, System.Globalization.CultureInfo.InvariantCulture);
    /// <inheritdoc />
    public void Write(string signalKey, string value) => WriteValue(signalKey, value);
    /// <inheritdoc />
    public async ValueTask<bool> WaitAsync(string signalKey, string expectedValue, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var current = ReadSnapshot(signalKey);
        if (string.Equals(Convert.ToString(current?.Value, System.Globalization.CultureInfo.InvariantCulture), expectedValue, StringComparison.Ordinal)) return true;
        return await WaitValueAsync(signalKey, current?.Version ?? 0, value => string.Equals(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture), expectedValue, StringComparison.Ordinal), timeout, cancellationToken).ConfigureAwait(false) is not null;
    }
    private static string Normalize(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("信号键不能为空。", nameof(key));
        return key.Trim();
    }
}

/// <summary>描述一个布尔信号初始值。</summary>
public sealed class WorkflowSignalStateItem
{
    /// <summary>获取或设置信号键。</summary>
    public string SignalKey { get; set; } = string.Empty;
    /// <summary>获取或设置初始状态。</summary>
    public bool Value { get; set; }
}

/// <summary>描述一个数据信号初始值。</summary>
public sealed class WorkflowSignalValueItem
{
    /// <summary>获取或设置信号键。</summary>
    public string SignalKey { get; set; } = string.Empty;
    /// <summary>获取或设置初始值。</summary>
    public string Value { get; set; } = string.Empty;
}
