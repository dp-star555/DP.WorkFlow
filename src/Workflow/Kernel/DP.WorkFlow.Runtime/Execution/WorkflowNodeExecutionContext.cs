namespace DP.WorkFlow;

internal sealed class WorkflowNodeExecutionContext : IWorkflowNodeExecutionContext, IWorkflowChildExecutionContext
{
    private readonly WorkflowContext _context;
    private readonly Action<IWorkflowNodeModel, string, string?, IReadOnlyDictionary<string, object?>?, WorkflowEventWriteMode> _traceWriter;
    private readonly Action<WorkflowRunEventDraft, WorkflowEventWriteMode> _eventRecorder;
    private readonly Action<string> _degradedReporter;
    private readonly Func<WorkflowExecutionPlan, WorkflowContext, CancellationToken, Task<WorkflowRunResult>> _childRunner;
    private readonly WorkflowExecutionPlan? _childDefinition;
    private readonly WorkflowNodeInputMap _inputMap;
    private readonly Dictionary<string, object> _pendingVariableWrites = new(StringComparer.Ordinal);
    private readonly HashSet<string> _pendingVariableRemovals = new(StringComparer.Ordinal);
    private readonly Dictionary<string, object> _pendingPublicDataWrites = new(StringComparer.Ordinal);
    private readonly HashSet<string> _pendingPublicDataRemovals = new(StringComparer.Ordinal);

    /// <summary>创建单个节点执行实例看到的受限运行时适配器。</summary>
    /// <param name="context">底层共享运行上下文。</param>
    /// <param name="node">当前节点配置。</param>
    /// <param name="executionIdentity">当前 Run、Token、Scope 和执行次数身份。</param>
    /// <param name="childDefinition">复合节点的可选编译后子定义。</param>
    /// <param name="inputLayout">绑定阶段冻结的输入槽元数据；用于自动识别普通输入键。</param>
    /// <param name="traceWriter">将节点自定义步骤按推送优先级写回父引擎的委托。</param>
    /// <param name="eventRecorder">将数据血缘等结构化事件写回父引擎的委托。</param>
    /// <param name="degradedReporter">报告记录元数据降级的委托；不得抛出异常或改变节点结果。</param>
    /// <param name="childRunner">在父引擎监管下启动子引擎的委托。</param>
    public WorkflowNodeExecutionContext(
        WorkflowContext context,
        IWorkflowNodeModel node,
        WorkflowExecutionIdentity executionIdentity,
        WorkflowExecutionPlan? childDefinition,
        WorkflowNodeInputLayout inputLayout,
        Action<IWorkflowNodeModel, string, string?, IReadOnlyDictionary<string, object?>?, WorkflowEventWriteMode> traceWriter,
        Action<WorkflowRunEventDraft, WorkflowEventWriteMode> eventRecorder,
        Action<string> degradedReporter,
        Func<WorkflowExecutionPlan, WorkflowContext, CancellationToken, Task<WorkflowRunResult>> childRunner)
    {
        _context = context;
        Node = node;
        ExecutionIdentity = executionIdentity;
        _childDefinition = childDefinition;
        _traceWriter = traceWriter;
        _eventRecorder = eventRecorder;
        _degradedReporter = degradedReporter;
        _childRunner = childRunner;
        // 执行计划每次返回新的节点快照，因此引用映射必须按本次执行建立，不进入全局状态。
        _inputMap = inputLayout.Bind(node);
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

    public T? ResolveInput<T>(WorkflowInput<T> input)
    {
        ArgumentNullException.ThrowIfNull(input);
        // 用引用映射把"Handler 实际传入的输入实例"定位回它的公开属性名；查不到说明是动态输入。
        var inputKey = _inputMap.TryGetKey(input, out var key) ? key : null;
        var metadataStatus = inputKey is null
            ? WorkflowInputMetadataStatus.Unresolved
            : WorkflowInputMetadataStatus.Automatic;
        return ResolveAndRecord(input, inputKey, metadataStatus);
    }

    public T? ResolveDynamicInput<T>(string inputKey, WorkflowInput<T> input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (string.IsNullOrWhiteSpace(inputKey))
            throw new ArgumentException("动态输入键不能为空白。", nameof(inputKey));
        return ResolveAndRecord(input, inputKey.Trim(), WorkflowInputMetadataStatus.ExplicitDynamic);
    }

    private T? ResolveAndRecord<T>(WorkflowInput<T> input, string? inputKey, string metadataStatus)
    {
        var descriptor = WorkflowBindingSourceDescriptor.From(input);
        try
        {
            var resolution = new WorkflowBindingResolver(_context, ExecutionIdentity).ResolveWithSource(input);
            RecordInputResolved(
                inputKey,
                metadataStatus,
                descriptor,
                resolution.SourceNodeId,
                resolution.SourceOutputSequence,
                resolution.Value,
                null);
            ReportUnresolvedInputIfNeeded(metadataStatus);
            return resolution.Value;
        }
        catch (Exception exception)
        {
            // 记录失败诊断不得覆盖绑定解析的原始异常。
            RecordInputResolved(
                inputKey,
                metadataStatus,
                descriptor,
                descriptor.SourceNodeId,
                null,
                null,
                exception.Message);
            ReportUnresolvedInputIfNeeded(metadataStatus);
            throw;
        }
    }

    private void ReportUnresolvedInputIfNeeded(string metadataStatus)
    {
        if (metadataStatus != WorkflowInputMetadataStatus.Unresolved)
            return;
        // 输入仍然正常解析；这里只让"无法自动识别"在 RecordingHealth 上可见，不改变节点结果。
        _degradedReporter(
            $"节点 {Node.Id}/{Node.NodeType} 的输入未识别出普通输入槽，已按 Unresolved 记录数据血缘。");
    }

    private void RecordInputResolved(
        string? inputKey,
        string metadataStatus,
        WorkflowBindingSourceDescriptor descriptor,
        string? sourceNodeId,
        long? sourceOutputSequence,
        object? resolvedValue,
        string? failure)
    {
        _eventRecorder(
            new WorkflowRunEventDraft(
                WorkflowRunEventCategory.DataFlow,
                "InputResolved",
                failure,
                Node.Id,
                Node.NodeType,
                ExecutionIdentity,
                Data: new Dictionary<string, object?>
                {
                    ["InputKey"] = inputKey,
                    ["InputMetadataStatus"] = metadataStatus,
                    ["SourceKind"] = descriptor.SourceKind,
                    ["SourceNodeId"] = sourceNodeId,
                    ["SourceOutputKey"] = descriptor.SourceOutputKey,
                    ["SourceOutputSequence"] = sourceOutputSequence,
                    ["PublicDataKey"] = descriptor.PublicDataKey,
                    ["TargetType"] = descriptor.TargetType,
                    ["ResolvedValueSummary"] = resolvedValue
                }),
            WorkflowEventWriteMode.Buffered);
    }

    public void RaiseWorkflowSignal(string signalKey) => _context.RaiseWorkflowSignal(signalKey);

    public bool ContainsWorkflowSignal(string signalKey) => _context.ContainsWorkflowSignal(signalKey);

    public ValueTask<bool> WaitAllWorkflowSignalsAsync(IReadOnlyList<string> signalKeys, TimeSpan? timeout, CancellationToken cancellationToken) =>
        _context.WaitAllWorkflowSignalsAsync(signalKeys, timeout, cancellationToken);

    public void Trace(string step, string? message = null, IReadOnlyDictionary<string, object?>? data = null) =>
        _traceWriter(Node, step, message, data, WorkflowEventWriteMode.Buffered);

    public void Trace(
        string step,
        string? message,
        IReadOnlyDictionary<string, object?>? data,
        WorkflowEventWriteMode writeMode) =>
        _traceWriter(Node, step, message, data, writeMode);

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

    internal IReadOnlyList<string> ChangedPublicDataKeys =>
        _pendingPublicDataWrites.Keys.Concat(_pendingPublicDataRemovals).Distinct(StringComparer.Ordinal).ToArray();

    /// <summary>Publishes local-variable and public-data changes staged by a successfully completed node.</summary>
    internal void CommitDataChanges()
    {
        // Public adapters may reject a commit; apply them before the in-memory local-variable update
        // so a rejected publication cannot leak local writes from a node that will be marked failed.
        _context.ApplyPublicDataChanges(_pendingPublicDataWrites, _pendingPublicDataRemovals);
        _context.ApplyVariableChanges(_pendingVariableWrites, _pendingVariableRemovals);
    }
}
