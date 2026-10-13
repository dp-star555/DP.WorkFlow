namespace DP.WorkFlow;

/// <summary>
/// 编辑器里“运行本节点一次”：用最近一次运行的上游输出（或调用方替换的输入）执行单个节点，
/// 不进入流程、不提交输出/投影/变量/公共数据，也不开始根运行作用域（不退役上一轮资源、不布防采集）。
/// </summary>
public sealed class WorkflowNodeTrialRunner
{
    private readonly WorkflowNodeHandlerCatalog _handlers;
    private readonly Func<WorkflowContext> _context;

    /// <summary>创建试运行器。</summary>
    /// <param name="handlers">节点处理器目录。</param>
    /// <param name="context">流程运行使用的上下文，提供宿主服务和最近一次运行的节点输出。</param>
    public WorkflowNodeTrialRunner(WorkflowNodeHandlerCatalog handlers, WorkflowContext context)
        : this(handlers, Fixed(context ?? throw new ArgumentNullException(nameof(context)))) { }

    private static Func<WorkflowContext> Fixed(WorkflowContext context) => () => context;

    /// <summary>创建试运行器；每次运行时取当前的流程上下文（宿主可能稍后才创建或替换上下文）。</summary>
    /// <param name="handlers">节点处理器目录。</param>
    /// <param name="context">返回流程运行上下文。</param>
    public WorkflowNodeTrialRunner(WorkflowNodeHandlerCatalog handlers, Func<WorkflowContext> context)
    {
        _handlers = handlers ?? throw new ArgumentNullException(nameof(handlers));
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    /// <summary>执行节点一次并返回处理器结果；结果不写回流程。</summary>
    /// <param name="node">要执行的节点配置（通常是编辑副本）。</param>
    /// <param name="inputOverrides">按输入实例引用替换的值，例如把输入帧换成手动读取的图像。</param>
    /// <param name="cancellationToken">取消。</param>
    /// <returns>处理器结果；投影未提交。</returns>
    public async Task<NodeExecutionResult> RunAsync(IWorkflowNodeModel node, IReadOnlyDictionary<object, object?>? inputOverrides = null,
        CancellationToken cancellationToken = default)
    {
        Guard.NotNull(node);
        var handler = _handlers.Resolve(node);
        var source = _context() ?? throw new InvalidOperationException("宿主还没有流程运行上下文。");
        var scope = Guid.NewGuid();
        var preparation = new WorkflowRunPreparationContext(new[] { node }, WorkflowRunScopeKind.Nested, Services: source.Services,
            PositionedNodes: new[] { new WorkflowPreparationNode("$", node) }, BindingScopeId: scope);
        IWorkflowPreparedRun? prepared = null;
        try
        {
            if (source.Services.GetService(typeof(IWorkflowRunPreparationService)) is IWorkflowTransactionalRunPreparationService transactional)
                prepared = await transactional.PrepareRunAsync(preparation, cancellationToken).ConfigureAwait(false);
            else if (source.Services.GetService(typeof(IWorkflowRunPreparationService)) is IWorkflowRunPreparationService plain)
                await plain.PrepareAsync(preparation, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            prepared?.Commit();
            var context = new TrialContext(source, node, scope, inputOverrides);
            return await handler.ExecuteAsync(node, context, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (prepared is not null) await prepared.DisposeAsync().ConfigureAwait(false);
        }
    }

    private sealed class TrialContext(WorkflowContext context, IWorkflowNodeModel node, Guid scope,
        IReadOnlyDictionary<object, object?>? overrides) : IWorkflowNodeExecutionContext
    {
        // 试运行的写入只在本次执行内可见，结束即丢弃。
        private readonly Dictionary<string, object?> _variables = new(StringComparer.Ordinal);
        private readonly Dictionary<string, object?> _publicData = new(StringComparer.Ordinal);

        public Guid BindingScopeId => scope;
        public string PlanPath => "$";
        public IReadOnlyDictionary<string, object?> SubflowInputs => WorkflowSubflowValues.EmptyInputs;
        public IWorkflowNodeModel Node => node;
        public WorkflowExecutionIdentity ExecutionIdentity { get; } = new(Guid.NewGuid(), 0, Array.Empty<long>(), Array.Empty<long>(), 1);
        public int NodeExecutionCount => 1;
        public IServiceProvider Services => context.Services;

        public TCapability GetRequiredCapability<TCapability>() where TCapability : class =>
            context.Services.GetService(typeof(TCapability)) as TCapability
            ?? throw new InvalidOperationException($"节点 {node.Id}/{node.NodeType} 所需运行能力 {typeof(TCapability).FullName} 未注册。");

        public bool TryGetVariable<T>(string key, out T? value)
        {
            if (_variables.TryGetValue(key, out var local)) { value = local is T typed ? typed : default; return local is T; }
            return context.TryGetVariable(key, out value);
        }
        public void SetVariable(string key, object value) => _variables[key] = value ?? throw new ArgumentNullException(nameof(value));
        public bool RemoveVariable(string key)
        {
            bool existed = _variables.TryGetValue(key, out var local) ? local is not null : context.ContainsVariable(key);
            _variables[key] = null;
            return existed;
        }

        public bool TryGetPublicData<T>(string key, out T? value)
        {
            if (_publicData.TryGetValue(key, out var local)) { value = local is T typed ? typed : default; return local is T; }
            return context.PublicData.TryGet(key, out value);
        }
        public void PublishData(string key, object value) => _publicData[key] = value ?? throw new ArgumentNullException(nameof(value));
        public bool RemovePublicData(string key)
        {
            bool existed = _publicData.TryGetValue(key, out var local) ? local is not null : context.PublicData.Snapshot().ContainsKey(key);
            _publicData[key] = null;
            return existed;
        }

        public T? ResolveInput<T>(WorkflowInput<T> input)
        {
            Guard.NotNull(input);
            if (overrides is not null && overrides.TryGetValue(input, out var replaced)) return (T?)replaced;
            return WorkflowBindingResolver.ResolveLatestInput(context, input);
        }
        public T? ResolveDynamicInput<T>(string inputKey, WorkflowInput<T> input) => ResolveInput(input);

        public void RaiseWorkflowSignal(string signalKey) { }
        public bool ContainsWorkflowSignal(string signalKey) => false;
        public ValueTask<bool> WaitAllWorkflowSignalsAsync(IReadOnlyList<string> signalKeys, TimeSpan? timeout, CancellationToken cancellationToken) =>
            signalKeys.Count == 0 ? Polyfill.FromResult(true) : throw new InvalidOperationException("单节点试运行不能等待流程信号。");
        public void Trace(string step, string? message = null, IReadOnlyDictionary<string, object?>? data = null) { }
        public void Trace(string step, string? message, IReadOnlyDictionary<string, object?>? data, WorkflowEventWriteMode writeMode) { }
    }
}
