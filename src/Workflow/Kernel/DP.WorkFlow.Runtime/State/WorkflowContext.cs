using System.Collections.Concurrent;

namespace DP.WorkFlow;

/// <summary>
/// 保存工作流作用域局部变量、按执行实例区分的节点输出、独立公共数据仓和宿主服务。
/// 删除变量必须显式调用 <see cref="RemoveVariable"/>，不再以 null 表示删除。
/// </summary>
public sealed class WorkflowContext : IWorkflowFaultRecoveryContext
{
    private readonly ConcurrentDictionary<string, object> _variables = new(StringComparer.Ordinal);
    private readonly object _variableSync = new();
    private WorkflowSignalScope _workflowSignals = new();
    private WorkflowRunState _runState = new();
    private readonly IWorkflowPublicDataStore _publicData;
    private bool _inheritsWorkflowSignals;

    /// <summary>初始化具有独立局部变量和节点输出状态的运行上下文。</summary>
    /// <param name="services">节点处理器可获取的宿主服务容器；为空时使用始终返回空的容器。</param>
    /// <param name="publicData">显式公共数据仓；为空时创建当前根上下文私有的数据仓。</param>
    public WorkflowContext(
        IServiceProvider? services = null,
        IWorkflowPublicDataStore? publicData = null)
    {
        Services = services ?? EmptyServiceProvider.Instance;
        _publicData = publicData ?? new WorkflowPublicDataStore();
    }

    /// <summary>获取宿主服务容器。</summary>
    public IServiceProvider Services { get; }

    /// <summary>获取与局部变量和节点输出分离的公共数据仓。</summary>
    public IWorkflowPublicDataStore PublicData => _publicData;

    /// <summary>设置或替换非空流程作用域局部变量。</summary>
    /// <param name="key">区分大小写的稳定变量键，首尾空白不会自动裁剪。</param>
    /// <param name="value">供后续节点或子流程复制使用的非空值。</param>
    /// <exception cref="ArgumentException"><paramref name="key"/> 为空或仅包含空白。</exception>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> 为空；删除变量应调用 <see cref="RemoveVariable"/>。</exception>
    public void SetVariable(string key, object value)
    {
        ValidateKey(key);
        ArgumentNullException.ThrowIfNull(value);
        lock (_variableSync)
            _variables[key] = value;
    }

    /// <summary>尝试以直接类型匹配方式读取流程作用域局部变量。</summary>
    /// <typeparam name="T">要求的运行时类型；该方法不执行数值或字符串转换。</typeparam>
    /// <param name="key">区分大小写的变量键。</param>
    /// <param name="value">成功时返回已转换为 <typeparamref name="T"/> 的值，否则为默认值。</param>
    /// <returns>变量存在且运行时值属于 <typeparamref name="T"/> 时返回 <see langword="true"/>。</returns>
    public bool TryGetVariable<T>(string key, out T? value)
    {
        ValidateKey(key);
        lock (_variableSync)
        {
            if (_variables.TryGetValue(key, out var raw) && raw is T typedValue)
            {
                value = typedValue;
                return true;
            }

            value = default;
            return false;
        }
    }

    /// <summary>删除指定流程作用域局部变量。</summary>
    /// <param name="key">区分大小写的变量键。</param>
    /// <returns>变量原先存在并被删除时返回 <see langword="true"/>。</returns>
    public bool RemoveVariable(string key)
    {
        ValidateKey(key);
        lock (_variableSync)
            return _variables.TryRemove(key, out _);
    }

    /// <summary>判断指定流程作用域局部变量是否存在，不检查其值类型。</summary>
    /// <param name="key">区分大小写的变量键。</param>
    /// <returns>当前变量表包含该键时返回 <see langword="true"/>。</returns>
    public bool ContainsVariable(string key)
    {
        ValidateKey(key);
        lock (_variableSync)
            return _variables.ContainsKey(key);
    }

    /// <summary>导出流程作用域局部变量字典的独立快照副本。</summary>
    /// <returns>字典结构不会随上下文变化；其中对象值本身不会深拷贝。</returns>
    public IReadOnlyDictionary<string, object> ExportVariables()
    {
        lock (_variableSync)
            return new Dictionary<string, object>(_variables, StringComparer.Ordinal);
    }

    /// <summary>幂等触发当前运行上下文中的一个命名信号。</summary>
    /// <param name="signalKey">信号稳定键；存储前会裁剪首尾空白。</param>
    public void RaiseWorkflowSignal(string signalKey) => _workflowSignals.Raise(signalKey);

    /// <summary>判断当前运行上下文信号是否已经触发。</summary>
    /// <param name="signalKey">要查询的信号键。</param>
    /// <returns>信号在本次运行或继承的父信号域中已触发时返回 <see langword="true"/>。</returns>
    public bool ContainsWorkflowSignal(string signalKey) => _workflowSignals.Contains(signalKey);

    /// <summary>异步等待当前上下文中的全部指定信号。</summary>
    /// <param name="signalKeys">待等待的信号键；空白项被忽略，重复项被去重。</param>
    /// <param name="timeout">可选等待上限；为空表示仅受取消令牌控制。</param>
    /// <param name="cancellationToken">调用方取消令牌，取消时抛出异常而不是返回 <see langword="false"/>。</param>
    /// <returns>全部信号到达时为 <see langword="true"/>；仅因超时结束时为 <see langword="false"/>。</returns>
    public ValueTask<bool> WaitAllWorkflowSignalsAsync(IReadOnlyList<string> signalKeys, TimeSpan? timeout, CancellationToken cancellationToken) =>
        _workflowSignals.WaitAllAsync(signalKeys, timeout, cancellationToken);

    /// <summary>尝试获取节点最近一次标准输出。该 API 仅用于监视、子流程最终映射和旧版兼容。</summary>
    /// <param name="nodeId">画布内节点实例 ID。</param>
    /// <param name="output">成功时返回最近执行实例的输出记录。</param>
    /// <returns>该节点在本次运行中至少产生过一次输出时返回 <see langword="true"/>。</returns>
    /// <remarks>该方法不校验 Token/Scope 可见性；普通节点绑定应使用执行上下文的解析器。</remarks>
    public bool TryGetNodeOutput(string nodeId, out WorkflowNodeOutput? output)
    {
        ValidateKey(nodeId);
        return _runState.TryGetLatestNodeOutput(nodeId, out output);
    }

    /// <summary>导出本次运行的全部节点输出执行实例。</summary>
    /// <returns>按全局执行序号排列的输出历史快照，包括同一节点的重复执行。</returns>
    public IReadOnlyList<WorkflowNodeOutput> ExportNodeOutputs() => _runState.NodeOutputs;

    /// <summary>创建使用同一服务容器、默认不继承父变量且隔离节点输出的子流程上下文。</summary>
    /// <returns>仅共享工作流信号域的新上下文；数据必须通过复合节点显式输入映射。</returns>
    public WorkflowContext CreateChildScope()
    {
        var child = new WorkflowContext(Services, _publicData);
        child._workflowSignals = _workflowSignals;
        child._inheritsWorkflowSignals = true;
        return child;
    }

    /// <summary>原子应用一个成功节点暂存的局部变量写入和删除集合。</summary>
    internal void ApplyVariableChanges(
        IReadOnlyDictionary<string, object> writes,
        IReadOnlyCollection<string> removals)
    {
        ArgumentNullException.ThrowIfNull(writes);
        ArgumentNullException.ThrowIfNull(removals);
        lock (_variableSync)
        {
            foreach (var key in removals)
                _variables.TryRemove(key, out _);
            foreach (var pair in writes)
                _variables[pair.Key] = pair.Value;
        }
    }

    /// <summary>原子应用一个成功节点暂存的公共数据写入和删除集合。</summary>
    internal void ApplyPublicDataChanges(
        IReadOnlyDictionary<string, object> writes,
        IReadOnlyCollection<string> removals) =>
        _publicData.Apply(new WorkflowPublicDataChangeSet(writes, removals));

    /// <summary>将上下文绑定到本轮独立运行状态，并重置非继承信号域。</summary>
    /// <param name="runState">拥有本轮输出、作用域和故障事实的运行状态。</param>
    internal void BeginRun(WorkflowRunState runState)
    {
        _runState = runState ?? throw new ArgumentNullException(nameof(runState));
        if (!_inheritsWorkflowSignals)
            _workflowSignals = new WorkflowSignalScope();
    }

    /// <summary>获取当前或最近一次使用该 Context 的运行 ID。</summary>
    public Guid CurrentRunId => _runState.RunId;

    /// <summary>记录节点执行实例的标准输出，并更新按节点 ID 查询的 Latest 视图。</summary>
    /// <param name="nodeId">产生输出的节点 ID。</param>
    /// <param name="identity">节点执行实例的 Run、Token、Scope 和次数身份。</param>
    /// <param name="value">允许为空的节点标准输出。</param>
    /// <returns>写入历史队列的不可变输出记录。</returns>
    internal WorkflowNodeOutput SetNodeOutput(
        string nodeId,
        WorkflowExecutionIdentity identity,
        object? value)
    {
        ValidateKey(nodeId);
        return _runState.CommitNodeOutput(nodeId, identity, value);
    }

    /// <summary>从最新到最旧查找对指定消费执行实例可见的节点输出。</summary>
    /// <param name="nodeId">来源节点 ID。</param>
    /// <param name="consumer">消费节点的 Token 祖先链和并行 Scope 路径。</param>
    /// <param name="output">成功时返回同 Token、祖先 Token 或已完成并行分支中的最近输出。</param>
    /// <returns>找到属于同一 Run 且满足作用域可见性规则的输出时返回 <see langword="true"/>。</returns>
    internal bool TryGetVisibleNodeOutput(
        string nodeId,
        WorkflowExecutionIdentity consumer,
        out WorkflowNodeOutput? output)
    {
        ValidateKey(nodeId);
        return _runState.TryGetVisibleNodeOutput(nodeId, consumer, out output);
    }

    /// <summary>注册运行时并行作用域及其父 Token，用于分支输出可见性判断。</summary>
    /// <param name="scopeId">本轮运行中单调递增的作用域实例 ID。</param>
    /// <param name="ownerTokenId">派生并行分支的父 Token ID。</param>
    internal void RegisterParallelScope(long scopeId, long ownerTokenId)
    {
        _runState.RegisterParallelScope(scopeId, ownerTokenId);
    }

    /// <summary>标记并行作用域全部分支已经到达共同汇聚点。</summary>
    /// <param name="scopeId">先前注册的运行时作用域 ID；不存在时保持幂等。</param>
    internal void CompleteParallelScope(long scopeId)
    {
        _runState.CompleteParallelScope(scopeId);
    }

    private static void ValidateKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("键不能为空。", nameof(key));
    }

    private sealed class WorkflowSignalScope
    {
        private readonly object _sync = new();
        private readonly HashSet<string> _signals = new(StringComparer.Ordinal);
        private TaskCompletionSource _changed = NewChange();
        internal void Raise(string key)
        {
            ValidateKey(key);
            TaskCompletionSource changed;
            lock (_sync) { if (!_signals.Add(key.Trim())) return; changed = _changed; _changed = NewChange(); }
            changed.TrySetResult();
        }
        internal bool Contains(string key) { ValidateKey(key); lock (_sync) return _signals.Contains(key.Trim()); }
        internal async ValueTask<bool> WaitAllAsync(IReadOnlyList<string> keys, TimeSpan? timeout, CancellationToken cancellationToken)
        {
            var normalized = keys.Where(key => !string.IsNullOrWhiteSpace(key)).Select(key => key.Trim()).Distinct(StringComparer.Ordinal).ToArray();
            if (normalized.Length == 0) return true;
            using var timeoutSource = timeout.HasValue ? new CancellationTokenSource(timeout.Value) : null;
            using var linked = timeoutSource is null ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken) : CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);
            while (true)
            {
                Task changed;
                lock (_sync) { if (normalized.All(_signals.Contains)) return true; changed = _changed.Task; }
                try { await changed.WaitAsync(linked.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) when (timeoutSource?.IsCancellationRequested == true && !cancellationToken.IsCancellationRequested) { return false; }
            }
        }
        private static TaskCompletionSource NewChange() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

}

/// <summary>
/// 表示一个具有 Run、Token、Scope 和执行次数身份的节点输出。
/// </summary>
/// <param name="ExecutionSequence">本次运行中跨节点单调递增的输出序号。</param>
/// <param name="RunId">产生输出的运行 ID，防止复用上下文时读取上一轮结果。</param>
/// <param name="NodeId">产生输出的节点实例 ID。</param>
/// <param name="NodeExecutionCount">该节点在本轮运行中的第几次执行。</param>
/// <param name="TokenId">执行该节点的路径 Token ID。</param>
/// <param name="ScopeIds">从外到内的运行时并行作用域路径。</param>
/// <param name="Timestamp">输出写入上下文的 UTC 时间。</param>
/// <param name="Value">节点处理器返回的标准输出，可以为空。</param>
public sealed record WorkflowNodeOutput(
    long ExecutionSequence,
    Guid RunId,
    string NodeId,
    int NodeExecutionCount,
    long TokenId,
    IReadOnlyList<long> ScopeIds,
    DateTimeOffset Timestamp,
    object? Value);

internal sealed class EmptyServiceProvider : IServiceProvider
{
    public static EmptyServiceProvider Instance { get; } = new();

    public object? GetService(Type serviceType) => null;
}
