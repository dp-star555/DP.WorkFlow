using DP.Vision.Algorithms;

namespace DP.WorkFlow;

/// <summary>节点算法槽位；节点不引用具体引擎。</summary>
public sealed record WorkflowVisionAlgorithmSlot(string Name, Type ContractType, VisionAlgorithmSelection Selection, IReadOnlyList<string>? RequiredFeatures = null);

/// <summary>向准备服务声明算法槽位的视觉节点。</summary>
public interface IWorkflowVisionAlgorithmNode
{
    /// <summary>读取此冻结节点的算法选择。</summary>
    IReadOnlyList<WorkflowVisionAlgorithmSlot> GetAlgorithmSlots();
}

/// <summary>节点通过此入口调用已准备的类型化算法；入口协调资源租约及并发。</summary>
public interface IWorkflowVisionAlgorithmBindings
{
    /// <summary>按当前计划位置与槽位执行算法。</summary>
    TResult Invoke<T, TResult>(IWorkflowNodeExecutionContext context, string slot, Func<T, TResult> invoke, CancellationToken cancellationToken) where T : class;
    /// <summary>异步算法调用，任务完成之前保持资源租约。</summary>
    Task<TResult> InvokeAsync<T, TResult>(IWorkflowNodeExecutionContext context, string slot, Func<T, CancellationToken, Task<TResult>> invoke, CancellationToken cancellationToken) where T : class;
}

/// <summary>视觉算法准备与绑定桥接，支持失败回滚和根/嵌套运行隔离。</summary>
public sealed class WorkflowVisionAlgorithmBindings : IWorkflowVisionAlgorithmBindings, IWorkflowNodeCapabilityProvider,
    IWorkflowTransactionalRunPreparationService, IDisposable
{
    private readonly object _gate = new();
    private readonly VisionAlgorithmRuntime _runtime;
    private readonly IWorkflowRunPreparationService? _next;
    private readonly Func<VisionAlgorithmResourceContext?>? _resources;
    private readonly Dictionary<Guid, VisionAlgorithmPlan> _active = new();
    private bool _disposed;

    /// <summary>组合算法准备和已有采集/帧准备服务，后者失败时候选算法计划回滚。</summary>
    public WorkflowVisionAlgorithmBindings(VisionAlgorithmRuntime runtime, IWorkflowRunPreparationService? next = null)
    { _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime)); _next = next; }

    /// <summary>按当前配方取得资源上下文；每次准备只捕获一次。</summary>
    public WorkflowVisionAlgorithmBindings(VisionAlgorithmRuntime runtime, IWorkflowRunPreparationService? next, Func<VisionAlgorithmResourceContext?> resources)
        : this(runtime, next) { _resources = resources ?? throw new ArgumentNullException(nameof(resources)); }

    /// <summary>候选准备、提交、失败、取消和释放通知；订阅者异常不影响资源事务。</summary>
    public event Action<WorkflowVisionAlgorithmPreparationReport>? PreparationChanged;

    private void Notify(WorkflowVisionAlgorithmPreparationReport report)
    {
        foreach (var callback in PreparationChanged?.GetInvocationList() ?? Array.Empty<Delegate>())
            try { ((Action<WorkflowVisionAlgorithmPreparationReport>)callback)(report); } catch { /* 诊断订阅者不能改变执行结果。 */ }
    }

    /// <inheritdoc/>
    public bool Provides(IWorkflowNodeModel node, Type capabilityType) => node is IWorkflowVisionAlgorithmNode algorithm
        && algorithm.GetAlgorithmSlots().Any(s => capabilityType.IsAssignableFrom(s.ContractType));

    /// <inheritdoc/>
    public async ValueTask<IWorkflowPreparedRun> PrepareRunAsync(WorkflowRunPreparationContext context, CancellationToken cancellationToken)
    {
        lock (_gate) ObjectDisposedException.ThrowIf(_disposed, this);
        if (context.BindingScopeId == Guid.Empty) throw new InvalidOperationException("算法准备需要明确的绑定作用域身份。");
        var positioned = context.PositionedNodes ?? context.Nodes.Select(n => new WorkflowPreparationNode("$", n)).ToArray();
        var requests = positioned.Where(p => p.Node is IWorkflowVisionAlgorithmNode).SelectMany(position =>
            ((IWorkflowVisionAlgorithmNode)position.Node).GetAlgorithmSlots().Select(slot => new VisionAlgorithmRequest(
                Key(position.PlanPath, position.Node.Id, slot.Name), slot.ContractType, slot.Selection, slot.RequiredFeatures))).ToArray();
        var locations = positioned.Where(p => p.Node is IWorkflowVisionAlgorithmNode).SelectMany(position =>
            ((IWorkflowVisionAlgorithmNode)position.Node).GetAlgorithmSlots().Select(slot => new WorkflowVisionAlgorithmLocation(
                Key(position.PlanPath, position.Node.Id, slot.Name), position.PlanPath, position.Node.Id, slot.Name))).ToArray();
        Notify(new(context.BindingScopeId, "Preparing", locations, Array.Empty<VisionAlgorithmIssue>()));
        VisionAlgorithmPlan plan;
        try { plan = await (_resources == null ? _runtime.PrepareAsync(requests, cancellationToken)
            : _runtime.PrepareAsync(requests, _resources.Invoke(), cancellationToken)).ConfigureAwait(false); }
        catch (OperationCanceledException) { Notify(new(context.BindingScopeId, "Cancelled", locations, Array.Empty<VisionAlgorithmIssue>())); throw; }
        catch (Exception error)
        {
            var issues = VisionAlgorithmExceptionDiagnostics.Read(error);
            Notify(new(context.BindingScopeId, "Failed", locations, issues.Count > 0 ? issues : new[]
                { new VisionAlgorithmIssue("ALG_RUN_PREPARATION_FAILED", "Preparation", "", "", "", error.Message, error.ToString()) })); throw;
        }
        IWorkflowPreparedRun? nextPrepared = null;
        try
        {
            // 模板资源由算法准备捕获、验证并持有租约；节点制作缓存不是资源更新的否决条件。
            if (_next is IWorkflowTransactionalRunPreparationService transactional)
                nextPrepared = await transactional.PrepareRunAsync(context, cancellationToken).ConfigureAwait(false);
            else if (_next is not null) await _next.PrepareAsync(context, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            Notify(new(context.BindingScopeId, "Prepared", locations, Array.Empty<VisionAlgorithmIssue>()));
            return new Prepared(this, context.BindingScopeId, plan, nextPrepared, locations);
        }
        catch (Exception error)
        {
            var issues = VisionAlgorithmExceptionDiagnostics.Read(error);
            Notify(new(context.BindingScopeId, error is OperationCanceledException ? "Cancelled" : "Failed", locations,
                error is OperationCanceledException ? Array.Empty<VisionAlgorithmIssue>() : issues.Count > 0 ? issues : new[] { new VisionAlgorithmIssue("ALG_RUN_PREPARATION_FAILED", "Preparation", "", "", "", error.Message, error.ToString()) }));
            try { plan.Dispose(); }
            finally { if (nextPrepared is not null) await nextPrepared.DisposeAsync().ConfigureAwait(false); }
            throw;
        }
    }

    /// <inheritdoc/>
    public ValueTask PrepareAsync(WorkflowRunPreparationContext context, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("视觉算法准备必须通过支持提交/回滚的运行宿主调用。");

    /// <inheritdoc/>
    public TResult Invoke<T, TResult>(IWorkflowNodeExecutionContext context, string slot, Func<T, TResult> invoke, CancellationToken cancellationToken) where T : class
    {
        VisionAlgorithmPlan plan;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_active.TryGetValue(context.BindingScopeId, out plan!)) throw new InvalidOperationException("本次运行没有提交算法绑定。");
        }
        return plan.Invoke(Key(context.PlanPath, context.Node.Id, slot), invoke, cancellationToken);
    }

    /// <inheritdoc/>
    public Task<TResult> InvokeAsync<T, TResult>(IWorkflowNodeExecutionContext context, string slot, Func<T, CancellationToken, Task<TResult>> invoke, CancellationToken cancellationToken) where T : class
    {
        VisionAlgorithmPlan plan;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_active.TryGetValue(context.BindingScopeId, out plan!)) throw new InvalidOperationException("本次运行没有提交算法绑定。");
        }
        return plan.InvokeAsync(Key(context.PlanPath, context.Node.Id, slot), invoke, cancellationToken);
    }

    /// <summary>明确编码计划路径、节点Id及槽位，避免字符串分隔符碰撞。</summary>
    public static string Key(string planPath, string nodeId, string slot) => planPath + "|" + Uri.EscapeDataString(nodeId) + "|" + Uri.EscapeDataString(slot);

    /// <summary>停止接受调用并归还已发布计划，计划自身保留在途调用。</summary>
    public void Dispose()
    {
        VisionAlgorithmPlan[] plans;
        lock (_gate) { if (_disposed) return; _disposed = true; plans = _active.Values.ToArray(); _active.Clear(); }
        foreach (var plan in plans) plan.Dispose();
    }

    private sealed class Prepared(WorkflowVisionAlgorithmBindings owner, Guid id, VisionAlgorithmPlan plan, IWorkflowPreparedRun? next,
        IReadOnlyList<WorkflowVisionAlgorithmLocation> locations) : IWorkflowPreparedRun
    {
        private bool _committed;
        private bool _released;
        public void Commit()
        {
            lock (owner._gate)
            {
                ObjectDisposedException.ThrowIf(owner._disposed || _released, owner);
                if (_committed) return;
                if (owner._active.ContainsKey(id)) throw new InvalidOperationException("绑定作用域已经发布。");
                next?.Commit();
                owner._active.Add(id, plan); _committed = true;
            }
            owner.Notify(new(id, "Ready", locations, Array.Empty<VisionAlgorithmIssue>()));
        }
        public async ValueTask DisposeAsync()
        {
            lock (owner._gate)
            {
                if (_released) return;
                _released = true;
                if (_committed) owner._active.Remove(id);
            }
            try { plan.Dispose(); }
            finally { if (next is not null) await next.DisposeAsync().ConfigureAwait(false); }
            owner.Notify(new(id, "Released", locations, Array.Empty<VisionAlgorithmIssue>()));
        }
    }
}

/// <summary>运行绑定的结构化位置。</summary>
public sealed record WorkflowVisionAlgorithmLocation(string BindingKey, string PlanPath, string NodeId, string Slot);

/// <summary>一次算法准备事务的状态与问题。</summary>
public sealed record WorkflowVisionAlgorithmPreparationReport(Guid ScopeId, string Status,
    IReadOnlyList<WorkflowVisionAlgorithmLocation> Locations, IReadOnlyList<VisionAlgorithmIssue> Issues);
