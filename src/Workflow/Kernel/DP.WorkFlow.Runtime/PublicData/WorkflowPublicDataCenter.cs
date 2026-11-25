namespace DP.WorkFlow;

/// <summary>
/// 公共数据中心（R2-d）：所有公共数据（bool 信号、数值、队列）的登记和当前值，各流程共用一份。
/// </summary>
/// <remarks>
/// <para>每项公共数据有键、形态、值类型、默认值；所有形态共用一个键空间。读写时按登记核对形态和类型，
/// 同一个键不能一处当队列、另一处当数值，也不能一处按 Int32、另一处按 Double 使用。</para>
/// <para><see cref="RequireDeclaration"/> 为 <see langword="true"/> 时只能使用已登记的键（"先登记后使用"）；
/// 为 <see langword="false"/> 时首次使用自动登记，供还没有公共数据清单的旧流程使用，行为与过去一致。</para>
/// <para>写入立即生效（其他流程可能在等待），节点失败也不撤销，与原来的队列、信号语义相同。</para>
/// <para>不使用流程管理时，每个根 <see cref="WorkflowContext"/> 自建一个只供自己使用的中心。</para>
/// <para>登记为"保持"的项在登记时恢复上次保存的值（形态和类型一致时，优先于默认值），每次写入后保存，
/// 需要构造时传入 <see cref="IWorkflowPublicDataPersistence"/>。</para>
/// </remarks>
public sealed class WorkflowPublicDataCenter
{
    private readonly object _sync = new();
    private readonly Dictionary<string, WorkflowPublicDataDeclaration> _declarations = new(StringComparer.Ordinal);
    private readonly WorkflowSignalService _signals = new();
    private readonly WorkflowValueSignalService _values = new();
    private readonly WorkflowQueueService _queues = new();
    private readonly IWorkflowPublicDataPersistence? _persistence;
    private Dictionary<string, WorkflowPersistedPublicData>? _saved;
    private long _persistenceFailureCount;

    /// <summary>初始化公共数据中心。</summary>
    /// <param name="requireDeclaration">是否只允许使用已登记的键。</param>
    /// <param name="persistence">"保持"项的存取；为空时保持项只在内存中。</param>
    public WorkflowPublicDataCenter(bool requireDeclaration = false, IWorkflowPublicDataPersistence? persistence = null)
    {
        RequireDeclaration = requireDeclaration;
        _persistence = persistence;
        Signals = new CheckedSignals(this);
        Values = new CheckedValues(this);
        Queues = new CheckedQueues(this);
    }

    /// <summary>保存或读出"保持"项失败时发生（参数为说明）；失败不影响流程。</summary>
    public event Action<string>? PersistenceFailed;

    /// <summary>累计的保存 / 读出失败次数。</summary>
    public long PersistenceFailureCount => Interlocked.Read(ref _persistenceFailureCount);

    /// <summary>是否只允许使用已登记的键；否则首次使用时自动登记。</summary>
    public bool RequireDeclaration { get; set; }

    /// <summary>bool 信号。</summary>
    public IWorkflowSignalService Signals { get; }

    /// <summary>数值。</summary>
    public IWorkflowValueSignalService Values { get; }

    /// <summary>队列。</summary>
    public IWorkflowQueueService Queues { get; }

    /// <summary>全部登记项，按键排序。</summary>
    public IReadOnlyList<WorkflowPublicDataDeclaration> Declarations
    {
        get
        {
            lock (_sync)
                return _declarations.Values.OrderBy(item => item.Key, StringComparer.Ordinal).ToArray();
        }
    }

    /// <summary>查找登记项。</summary>
    /// <param name="key">键。</param>
    /// <param name="declaration">找到时返回登记项。</param>
    public bool TryGetDeclaration(string key, out WorkflowPublicDataDeclaration? declaration)
    {
        lock (_sync)
            return _declarations.TryGetValue(Normalize(key), out declaration);
    }

    /// <summary>
    /// 按一份清单登记（流程文件携带的公共数据清单）：没有的登记并写入默认值；已有且形态、类型一致的保留现值；
    /// 有任何一项不一致时整份清单都不登记，并在异常中列出全部冲突（先登记者为准）。
    /// </summary>
    /// <param name="declarations">要登记的项。</param>
    /// <exception cref="WorkflowPublicDataConflictException">清单与已有登记不一致，或清单自身有冲突。</exception>
    public void DeclareAll(IEnumerable<WorkflowPublicDataDeclaration> declarations)
    {
        Guard.NotNull(declarations);
        var items = declarations.Select(Validate).ToArray();
        lock (_sync)
        {
            var conflicts = new List<string>();
            var pending = new Dictionary<string, WorkflowPublicDataDeclaration>(StringComparer.Ordinal);
            foreach (var item in items)
            {
                var existing = _declarations.TryGetValue(item.Key, out var known) ? known
                    : pending.TryGetValue(item.Key, out var listed) ? listed : null;
                if (existing is null)
                    pending[item.Key] = item;
                else if (!existing.IsCompatibleWith(item))
                    conflicts.Add($"公共数据 {item.Key} 已登记为 {existing.Describe()}，不能再作为 {item.Describe()} 使用。");
            }
            if (conflicts.Count > 0)
                throw new WorkflowPublicDataConflictException(conflicts);
            foreach (var item in pending.Values)
                AddLocked(item);
        }
    }

    /// <summary>登记一项公共数据；已有且一致时不变。</summary>
    /// <param name="declaration">登记项。</param>
    public void Declare(WorkflowPublicDataDeclaration declaration) => DeclareAll(new[] { declaration });

    /// <summary>
    /// 把中心提供的三种形态作为宿主服务的后备：宿主自己注册了同类型服务时以宿主为准。
    /// </summary>
    /// <param name="hostServices">宿主服务。</param>
    /// <returns>组合后的服务容器。</returns>
    public IServiceProvider WrapServices(IServiceProvider hostServices) => new FallbackServiceProvider(hostServices, this);

    /// <summary>
    /// 恢复时复位：把标了"恢复时复位"的项复位为默认值（信号为默认值或假，数值为默认值，队列清空）。
    /// 由统管层在故障会话放行前调用。
    /// </summary>
    /// <returns>复位了的键。</returns>
    public IReadOnlyList<string> ResetForRecovery()
    {
        WorkflowPublicDataDeclaration[] items;
        lock (_sync)
            items = _declarations.Values.Where(item => item.ResetOnRecovery).ToArray();
        foreach (var item in items)
        {
            switch (item.Kind)
            {
                case WorkflowPublicDataKind.Signal:
                    Signals.Write(item.Key, item.DefaultValueText is { } signalDefault
                        && (bool)WorkflowPublicDataValues.Convert(signalDefault, E_SignalValueType.Boolean));
                    break;
                case WorkflowPublicDataKind.Value:
                    if (item.DefaultValueText is { } valueDefault)
                        Values.WriteValue(item.Key, WorkflowPublicDataValues.Convert(valueDefault, item.ValueType));
                    break;
                case WorkflowPublicDataKind.Queue:
                    Queues.Clear(item.Key, item.ValueType, true);
                    break;
            }
        }
        return items.Select(item => item.Key).ToArray();
    }

    private void AddLocked(WorkflowPublicDataDeclaration item)
    {
        _declarations[item.Key] = item;
        if (item.Persistent && TryRestoreLocked(item))
            return;
        switch (item.Kind)
        {
            case WorkflowPublicDataKind.Signal:
                if (item.DefaultValueText is { } signalDefault)
                    _signals.Write(item.Key, (bool)WorkflowPublicDataValues.Convert(signalDefault, E_SignalValueType.Boolean));
                break;
            case WorkflowPublicDataKind.Value:
                if (item.DefaultValueText is { } valueDefault && _values.ReadSnapshot(item.Key) is null)
                    _values.WriteValue(item.Key, WorkflowPublicDataValues.Convert(valueDefault, item.ValueType));
                break;
            case WorkflowPublicDataKind.Queue:
                _queues.EnsureExists(item.Key, item.ValueType);
                break;
        }
    }

    /// <summary>恢复保持项上次保存的值；没有保存过、形态或类型已变、值无法解析时返回 false（改用默认值）。</summary>
    private bool TryRestoreLocked(WorkflowPublicDataDeclaration item)
    {
        if (_persistence is null)
            return false;
        if (_saved is null)
        {
            try
            {
                _saved = _persistence.Load()
                    .Where(saved => saved is not null && !string.IsNullOrWhiteSpace(saved.Key))
                    .GroupBy(saved => saved.Key.Trim(), StringComparer.Ordinal)
                    .ToDictionary(group => group.Key, group => group.Last(), StringComparer.Ordinal);
            }
            catch (Exception exception)
            {
                _saved = new Dictionary<string, WorkflowPersistedPublicData>(StringComparer.Ordinal);
                ReportPersistenceFailure($"读出保持的公共数据失败：{exception.Message}");
            }
        }
        if (!_saved.TryGetValue(item.Key, out var saved) || saved.Kind != item.Kind || saved.ValueType != item.ValueType)
            return false;
        try
        {
            var values = saved.Values.Select(text => WorkflowPublicDataValues.Convert(text, item.ValueType)).ToArray();
            switch (item.Kind)
            {
                case WorkflowPublicDataKind.Signal:
                    _signals.Write(item.Key, values.Length > 0 && (bool)values[0]);
                    break;
                case WorkflowPublicDataKind.Value:
                    if (values.Length == 0)
                        return item.DefaultValueText is null;
                    _values.WriteValue(item.Key, values[0]);
                    break;
                default:
                    _queues.EnsureExists(item.Key, item.ValueType);
                    _queues.Clear(item.Key, item.ValueType, false);
                    foreach (var value in values)
                        _queues.EnqueueValue(item.Key, value, item.ValueType, false, false);
                    break;
            }
            return true;
        }
        catch (Exception exception) when (exception is FormatException or InvalidCastException or OverflowException)
        {
            ReportPersistenceFailure($"保持的公共数据 {item.Key} 无法按 {item.ValueType} 恢复，改用默认值：{exception.Message}");
            return false;
        }
    }

    /// <summary>写入后保存保持项的当前值。</summary>
    private void Persist(string key)
    {
        if (_persistence is null)
            return;
        WorkflowPublicDataDeclaration? declaration;
        lock (_sync)
            if (!_declarations.TryGetValue(key, out declaration) || !declaration.Persistent)
                return;
        IReadOnlyList<string> values = declaration.Kind switch
        {
            WorkflowPublicDataKind.Signal => new[] { Text(_signals.Read(key)) },
            WorkflowPublicDataKind.Value => _values.ReadSnapshot(key) is { HasValue: true } snapshot
                ? new[] { Text(snapshot.Value) }
                : Array.Empty<string>(),
            _ => (_queues.ReadSnapshot(key)?.Items ?? Array.Empty<object>()).Select(Text).ToArray()
        };
        try
        {
            _persistence.Save(new WorkflowPersistedPublicData(key, declaration.Kind, declaration.ValueType, values));
        }
        catch (Exception exception)
        {
            ReportPersistenceFailure($"保存公共数据 {key} 失败：{exception.Message}");
        }
    }

    private static string Text(object? value) =>
        System.Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;

    private void ReportPersistenceFailure(string message)
    {
        Interlocked.Increment(ref _persistenceFailureCount);
        try
        {
            PersistenceFailed?.Invoke(message);
        }
        catch
        {
            // 报告本身失败不影响公共数据读写。
        }
    }

    /// <summary>按形态和类型核对一次使用；未登记时按 <see cref="RequireDeclaration"/> 拒绝或自动登记。</summary>
    private string Check(string key, WorkflowPublicDataKind kind, E_SignalValueType? valueType)
    {
        var normalized = Normalize(key);
        lock (_sync)
        {
            if (_declarations.TryGetValue(normalized, out var declared))
            {
                if (declared.Kind != kind)
                    throw new InvalidOperationException($"公共数据 {normalized} 已登记为 {declared.Describe()}，不能作为 {Describe(kind, valueType)} 使用。");
                if (valueType is { } type && kind != WorkflowPublicDataKind.Signal && declared.ValueType != type)
                    throw new InvalidOperationException($"公共数据 {normalized} 已登记为 {declared.Describe()}，不能按 {type} 使用。");
                return normalized;
            }
            if (RequireDeclaration)
                throw new InvalidOperationException($"公共数据 {normalized} 没有登记；先在流程的公共数据清单中登记后再使用。");
            if (valueType is null && kind != WorkflowPublicDataKind.Signal)
                return normalized; // 只读且无法确定类型：不登记，按"不存在"处理。
            _declarations[normalized] = new WorkflowPublicDataDeclaration(
                normalized, kind, kind == WorkflowPublicDataKind.Signal ? E_SignalValueType.Boolean : valueType!.Value);
            return normalized;
        }
    }

    private static string Describe(WorkflowPublicDataKind kind, E_SignalValueType? type) =>
        new WorkflowPublicDataDeclaration("_", kind, type ?? E_SignalValueType.String).Describe();

    private static WorkflowPublicDataDeclaration Validate(WorkflowPublicDataDeclaration item)
    {
        Guard.NotNull(item);
        var key = Normalize(item.Key);
        if (item.Kind == WorkflowPublicDataKind.Signal && item.ValueType != E_SignalValueType.Boolean)
            throw new ArgumentException($"bool 信号 {key} 的值类型必须是 Boolean。", nameof(item));
        if (item.DefaultValueText is { } text)
            _ = WorkflowPublicDataValues.Convert(text, item.ValueType); // 默认值必须能按类型解析。
        return item with { Key = key };
    }

    private static string Normalize(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("公共数据键不能为空。", nameof(key));
        return key.Trim();
    }

    private static E_SignalValueType? TypeOf(object value) => value switch
    {
        string => E_SignalValueType.String,
        int => E_SignalValueType.Int32,
        long => E_SignalValueType.Int64,
        double => E_SignalValueType.Double,
        bool => E_SignalValueType.Boolean,
        _ => null
    };

    private sealed class CheckedSignals(WorkflowPublicDataCenter owner) : IWorkflowSignalService
    {
        public bool Read(string signalKey) =>
            owner._signals.Read(owner.Check(signalKey, WorkflowPublicDataKind.Signal, E_SignalValueType.Boolean));

        public void Write(string signalKey, bool value)
        {
            var key = owner.Check(signalKey, WorkflowPublicDataKind.Signal, E_SignalValueType.Boolean);
            owner._signals.Write(key, value);
            owner.Persist(key);
        }

        public ValueTask<bool> WaitAsync(string signalKey, bool expectedValue, TimeSpan timeout, CancellationToken cancellationToken) =>
            owner._signals.WaitAsync(owner.Check(signalKey, WorkflowPublicDataKind.Signal, E_SignalValueType.Boolean), expectedValue, timeout, cancellationToken);
    }

    private sealed class CheckedValues(WorkflowPublicDataCenter owner) : IWorkflowValueSignalService
    {
        public WorkflowValueSignalSnapshot? ReadSnapshot(string signalKey) =>
            owner._values.ReadSnapshot(owner.Check(signalKey, WorkflowPublicDataKind.Value, null));

        public WorkflowValueSignalSnapshot WriteValue(string signalKey, object value)
        {
            Guard.NotNull(value);
            var type = TypeOf(value) ?? throw new ArgumentException($"公共数据只支持 5 种基本类型，不能写入 {value.GetType().FullName}。", nameof(value));
            var key = owner.Check(signalKey, WorkflowPublicDataKind.Value, type);
            var snapshot = owner._values.WriteValue(key, value);
            owner.Persist(key);
            return snapshot;
        }

        public ValueTask<WorkflowValueSignalSnapshot?> WaitValueAsync(string signalKey, long minVersion, Func<object?, bool> predicate, TimeSpan? timeout, CancellationToken cancellationToken) =>
            owner._values.WaitValueAsync(owner.Check(signalKey, WorkflowPublicDataKind.Value, null), minVersion, predicate, timeout, cancellationToken);
    }

    private sealed class CheckedQueues(WorkflowPublicDataCenter owner) : IWorkflowQueueService
    {
        public bool EnsureExists(string queueKey, E_SignalValueType valueType) =>
            owner._queues.EnsureExists(owner.Check(queueKey, WorkflowPublicDataKind.Queue, valueType), valueType);

        public bool Clear(string queueKey, E_SignalValueType valueType, bool autoCreate) =>
            Mutate(queueKey, valueType, key => owner._queues.Clear(key, valueType, autoCreate));

        public bool EnqueueValue(string queueKey, object value, E_SignalValueType valueType, bool preventDuplicate, bool autoCreate) =>
            Mutate(queueKey, valueType, key => owner._queues.EnqueueValue(key, value, valueType, preventDuplicate, autoCreate));

        public bool TryDequeueValue(string queueKey, E_SignalValueType valueType, bool autoCreate, out object? value)
        {
            object? removed = null;
            var changed = Mutate(queueKey, valueType, key => owner._queues.TryDequeueValue(key, valueType, autoCreate, out removed));
            value = removed;
            return changed;
        }

        public bool RemoveValue(string queueKey, object value, E_SignalValueType valueType, bool autoCreate) =>
            Mutate(queueKey, valueType, key => owner._queues.RemoveValue(key, value, valueType, autoCreate));

        /// <summary>执行一次改变队列的操作；有变化时保存保持项。</summary>
        private bool Mutate(string queueKey, E_SignalValueType valueType, Func<string, bool> change)
        {
            var key = owner.Check(queueKey, WorkflowPublicDataKind.Queue, valueType);
            var changed = change(key);
            if (changed)
                owner.Persist(key);
            return changed;
        }

        public WorkflowQueueSnapshot? ReadSnapshot(string queueKey) =>
            owner._queues.ReadSnapshot(owner.Check(queueKey, WorkflowPublicDataKind.Queue, null));

        public ValueTask<WorkflowQueueSnapshot?> WaitAsync(string queueKey, E_SignalValueType valueType, Func<WorkflowQueueSnapshot, bool> predicate, TimeSpan? timeout, bool autoCreate, CancellationToken cancellationToken) =>
            owner._queues.WaitAsync(owner.Check(queueKey, WorkflowPublicDataKind.Queue, valueType), valueType, predicate, timeout, autoCreate, cancellationToken);
    }

    private sealed class FallbackServiceProvider(IServiceProvider host, WorkflowPublicDataCenter center) : IServiceProvider
    {
        public object? GetService(Type serviceType)
        {
            var hosted = host.GetService(serviceType);
            if (hosted is not null)
                return hosted;
            if (serviceType == typeof(IWorkflowSignalService))
                return center.Signals;
            if (serviceType == typeof(IWorkflowValueSignalService))
                return center.Values;
            if (serviceType == typeof(IWorkflowQueueService))
                return center.Queues;
            if (serviceType == typeof(WorkflowPublicDataCenter))
                return center;
            return null;
        }
    }
}

/// <summary>公共数据清单与已有登记不一致。</summary>
public sealed class WorkflowPublicDataConflictException : InvalidOperationException
{
    /// <summary>初始化冲突异常。</summary>
    /// <param name="conflicts">每项冲突的说明。</param>
    public WorkflowPublicDataConflictException(IReadOnlyList<string> conflicts)
        : base(string.Join(Environment.NewLine, conflicts)) => Conflicts = conflicts;

    /// <summary>每项冲突的说明。</summary>
    public IReadOnlyList<string> Conflicts { get; }
}
