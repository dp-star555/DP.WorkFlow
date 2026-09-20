namespace DP.WorkFlow;

/// <summary>线程安全、区分大小写的进程内公共数据仓实现。</summary>
public sealed class WorkflowPublicDataStore : IWorkflowPublicDataStore
{
    private readonly Dictionary<string, object> _values = new(StringComparer.Ordinal);
    private readonly object _sync = new();

    /// <inheritdoc />
    public bool TryGet<T>(string key, out T? value)
    {
        ValidateKey(key);
        lock (_sync)
        {
            if (_values.TryGetValue(key, out var raw) && raw is T typed)
            {
                value = typed;
                return true;
            }
            value = default;
            return false;
        }
    }

    /// <inheritdoc />
    public void Apply(WorkflowPublicDataChangeSet changes)
    {
        ArgumentNullException.ThrowIfNull(changes);
        foreach (var key in changes.Removals)
            ValidateKey(key);
        foreach (var pair in changes.Writes)
        {
            ValidateKey(pair.Key);
            ArgumentNullException.ThrowIfNull(pair.Value);
        }
        lock (_sync)
        {
            foreach (var key in changes.Removals)
                _values.Remove(key);
            foreach (var pair in changes.Writes)
                _values[pair.Key] = pair.Value;
        }
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, object> Snapshot()
    {
        lock (_sync)
            return new Dictionary<string, object>(_values, StringComparer.Ordinal);
    }

    private static void ValidateKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("公共数据键不能为空。", nameof(key));
    }
}
