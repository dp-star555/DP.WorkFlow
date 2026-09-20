namespace DP.WorkFlow;

internal sealed class WorkflowNodeExecutionContext : IWorkflowNodeExecutionContext, IWorkflowChildExecutionContext
{
    private readonly WorkflowContext _context;
    private readonly Action<IWorkflowNodeModel, string, string?, IReadOnlyDictionary<string, object?>?> _traceWriter;
    private readonly Func<WorkflowExecutionPlan, WorkflowContext, CancellationToken, Task<WorkflowRunResult>> _childRunner;
    private readonly WorkflowExecutionPlan? _childDefinition;
    private readonly Dictionary<string, object> _pendingVariableWrites = new(StringComparer.Ordinal);
    private readonly HashSet<string> _pendingVariableRemovals = new(StringComparer.Ordinal);
    private readonly Dictionary<string, object> _pendingPublicDataWrites = new(StringComparer.Ordinal);
    private readonly HashSet<string> _pendingPublicDataRemovals = new(StringComparer.Ordinal);

    /// <summary>创建单个节点执行实例看到的受限运行时适配器。</summary>
    /// <param name="context">底层共享运行上下文。</param>
    /// <param name="node">当前节点配置。</param>
    /// <param name="executionIdentity">当前 Run、Token、Scope 和执行次数身份。</param>
    /// <param name="childDefinition">复合节点的可选编译后子定义。</param>
    /// <param name="traceWriter">将节点自定义步骤写回父引擎的委托。</param>
    /// <param name="childRunner">在父引擎监管下启动子引擎的委托。</param>
    public WorkflowNodeExecutionContext(
        WorkflowContext context,
        IWorkflowNodeModel node,
        WorkflowExecutionIdentity executionIdentity,
        WorkflowExecutionPlan? childDefinition,
        Action<IWorkflowNodeModel, string, string?, IReadOnlyDictionary<string, object?>?> traceWriter,
        Func<WorkflowExecutionPlan, WorkflowContext, CancellationToken, Task<WorkflowRunResult>> childRunner)
    {
        _context = context;
        Node = node;
        ExecutionIdentity = executionIdentity;
        _childDefinition = childDefinition;
        _traceWriter = traceWriter;
        _childRunner = childRunner;
    }

    public IWorkflowNodeModel Node { get; }

    public WorkflowExecutionIdentity ExecutionIdentity { get; }

    public int NodeExecutionCount => ExecutionIdentity.NodeExecutionCount;

    public IServiceProvider Services => _context.Services;

    public TCapability GetRequiredCapability<TCapability>() where TCapability : class =>
        _context.Services.GetService(typeof(TCapability)) as TCapability
        ?? throw new InvalidOperationException(
            $"节点 {Node.Id}/{Node.NodeType} 所需运行能力 {typeof(TCapability).FullName} 未注册。");

    public bool TryGetVariable<T>(string key, out T? value)
    {
        if (_pendingVariableRemovals.Contains(key))
        {
            value = default;
            return false;
        }
        if (_pendingVariableWrites.TryGetValue(key, out var pending))
        {
            if (pending is T typed)
            {
                value = typed;
                return true;
            }
            value = default;
            return false;
        }
        return _context.TryGetVariable(key, out value);
    }

    public void SetVariable(string key, object value)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("键不能为空。", nameof(key));
        ArgumentNullException.ThrowIfNull(value);
        _pendingVariableRemovals.Remove(key);
        _pendingVariableWrites[key] = value;
    }

    public bool RemoveVariable(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("键不能为空。", nameof(key));
        var existed = _pendingVariableWrites.Remove(key) || _context.ContainsVariable(key);
        _pendingVariableRemovals.Add(key);
        return existed;
    }

    public bool TryGetPublicData<T>(string key, out T? value)
    {
        if (_pendingPublicDataRemovals.Contains(key))
        {
            value = default;
            return false;
        }
        if (_pendingPublicDataWrites.TryGetValue(key, out var pending))
        {
            if (pending is T typed)
            {
                value = typed;
                return true;
            }
            value = default;
            return false;
        }
        return _context.PublicData.TryGet(key, out value);
    }

    public void PublishData(string key, object value)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("公共数据键不能为空。", nameof(key));
        ArgumentNullException.ThrowIfNull(value);
        _pendingPublicDataRemovals.Remove(key);
        _pendingPublicDataWrites[key] = value;
    }

    public bool RemovePublicData(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("公共数据键不能为空。", nameof(key));
        var existed = _pendingPublicDataWrites.Remove(key)
            || _context.PublicData.Snapshot().ContainsKey(key);
        _pendingPublicDataRemovals.Add(key);
        return existed;
    }

    public T? ResolveInput<T>(WorkflowInput<T> input) =>
        new WorkflowBindingResolver(_context, ExecutionIdentity).Resolve(input);

    public void RaiseWorkflowSignal(string signalKey) => _context.RaiseWorkflowSignal(signalKey);

    public bool ContainsWorkflowSignal(string signalKey) => _context.ContainsWorkflowSignal(signalKey);

    public ValueTask<bool> WaitAllWorkflowSignalsAsync(IReadOnlyList<string> signalKeys, TimeSpan? timeout, CancellationToken cancellationToken) =>
        _context.WaitAllWorkflowSignalsAsync(signalKeys, timeout, cancellationToken);

    public void Trace(string step, string? message = null, IReadOnlyDictionary<string, object?>? data = null) =>
        _traceWriter(Node, step, message, data);

    public WorkflowExecutionPlan GetChildWorkflowExecutionPlan() =>
        _childDefinition ?? throw new InvalidOperationException($"节点 {Node.Id} 没有编译后的子流程定义。");

    public WorkflowContext CreateChildScope() => _context.CreateChildScope();

    public Task<WorkflowRunResult> RunChildWorkflowAsync(
        WorkflowExecutionPlan definition,
        WorkflowContext childContext,
        CancellationToken cancellationToken) =>
        _childRunner(definition, childContext, cancellationToken);

    internal IReadOnlyList<string> ChangedVariableKeys =>
        _pendingVariableWrites.Keys.Concat(_pendingVariableRemovals).Distinct(StringComparer.Ordinal).ToArray();

    /// <summary>Publishes local-variable and public-data changes staged by a successfully completed node.</summary>
    internal void CommitDataChanges()
    {
        // Public adapters may reject a commit; apply them before the in-memory local-variable update
        // so a rejected publication cannot leak local writes from a node that will be marked failed.
        _context.ApplyPublicDataChanges(_pendingPublicDataWrites, _pendingPublicDataRemovals);
        _context.ApplyVariableChanges(_pendingVariableWrites, _pendingVariableRemovals);
    }
}
