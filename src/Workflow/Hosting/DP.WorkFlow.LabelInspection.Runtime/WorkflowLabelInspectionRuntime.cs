using DP.LabelInspection.Contracts;
using DP.Vision;

namespace DP.WorkFlow.LabelInspection;

/// <summary>
/// 按绑定作用域和计划路径准备标签节点；根/嵌套准备独立提交、回滚和退役。
/// 配置与资源文件（大小、修改时间）都没变时，多轮运行复用同一套已加载的模型和引擎，不再每轮重新读取、复制和加载ONNX；
/// 有变化时下一轮准备重新加载，旧资源在所有在途调用结束后释放。
/// </summary>
public sealed class WorkflowLabelInspectionRuntime : IWorkflowLabelInspectionService, IWorkflowTransactionalRunPreparationService, IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly Func<string> _baseDirectory;
    private readonly IWorkflowRunPreparationService? _next;
    private readonly Dictionary<Guid, Dictionary<(string Path, string Node), Entry>> _active = new();
    // 已提交过的资源，按计划位置缓存；缓存本身持有一个租约。
    private readonly Dictionary<(string Path, string Node), Entry> _cache = new();
    private bool _disposed;
    /// <summary>资源根相对流程文件目录解析；可串接帧/采集准备，不释放上一轮资源。</summary>
    public WorkflowLabelInspectionRuntime(Func<string> baseDirectory, IWorkflowRunPreparationService? next = null)
    { _baseDirectory = baseDirectory ?? throw new ArgumentNullException(nameof(baseDirectory)); _next = next; }

    /// <inheritdoc/>
    public async ValueTask<IWorkflowPreparedRun> PrepareRunAsync(WorkflowRunPreparationContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        lock (_gate) ObjectDisposedException.ThrowIf(_disposed, this);
        if (context.BindingScopeId == Guid.Empty) throw new InvalidOperationException("标签准备需要明确绑定作用域。");
        var duplicate = context.Nodes.GroupBy(n => n.Id, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1 && g.Any(n => n is InspectLabelNodeModel));
        if (duplicate is not null) throw new InvalidOperationException("标签预览节点ID跨子计划重复：" + duplicate.Key);
        var entries = new Dictionary<(string, string), Entry>();
        IWorkflowPreparedRun? next = null;
        try
        {
            var positions = context.PositionedNodes ?? context.Nodes.Select(n => new WorkflowPreparationNode("$", n)).ToArray();
            var baseDirectory = _baseDirectory();
            foreach (var position in positions.Where(p => p.Node is InspectLabelNodeModel))
            {
                var node = (InspectLabelNodeModel)position.Node;
                var errors = node.ValidateConfiguration();
                if (errors.Count > 0) throw new InvalidOperationException($"标签节点{node.Id}：" + string.Join("；", errors));
                var key = (position.PlanPath, node.Id);
                if (entries.ContainsKey(key)) throw new InvalidOperationException("标签计划位置重复。");
                var fingerprint = WorkflowLabelInspectionResources.Fingerprint(node, baseDirectory);
                Entry? entry = null;
                lock (_gate)
                    if (fingerprint is not null && _cache.TryGetValue(key, out var cached) && cached.Fingerprint == fingerprint && cached.TryAddLease())
                        entry = cached;
                if (entry is null)
                {
                    var resource = await WorkflowLabelInspectionResources.CreateAsync(node, baseDirectory, cancellationToken).ConfigureAwait(false);
                    entry = new Entry(resource, fingerprint);
                    Interlocked.Increment(ref _loadCount);
                }
                entries.Add(key, entry);
            }
            if (_next is IWorkflowTransactionalRunPreparationService transactional)
                next = await transactional.PrepareRunAsync(context, cancellationToken).ConfigureAwait(false);
            else if (_next is not null) await _next.PrepareAsync(context, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return new Prepared(this, context.BindingScopeId, entries, next);
        }
        catch
        {
            try { await ReleaseAsync(entries.Values).ConfigureAwait(false); }
            finally { if (next is not null) await next.DisposeAsync().ConfigureAwait(false); }
            throw;
        }
    }
    /// <inheritdoc/>
    public ValueTask PrepareAsync(WorkflowRunPreparationContext context, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("标签准备必须由支持提交/回滚的运行宿主调用。");

    /// <inheritdoc/>
    public async Task<WorkflowLabelInspectionResult> InspectAsync(IWorkflowNodeExecutionContext context, ImageFrame frame,
        string? cycleId, TaskDataSnapshot? taskData, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Entry entry;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_active.TryGetValue(context.BindingScopeId, out var plan) || !plan.TryGetValue((context.PlanPath, context.Node.Id), out entry!))
                throw new InvalidOperationException("本次运行未提交此标签节点的资源绑定。");
            entry.Acquire();
        }
        try
        {
            var resource = entry.Resource;
            if (frame.Image.Info.Width != resource.Recipe.Width || frame.Image.Info.Height != resource.Recipe.Height)
                throw new InvalidOperationException("标签输入图与配方尺寸不一致。");
            // 在第一个异步等待之前独立保留两个输入；请求既用于检测，也可由独立宿主存储策略使用。
            using var request = InspectionRequest.FromVision(frame, resource.Recipe, resource.Reference, cycleId, taskData);
            await entry.Serial.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var report = await resource.Engine.InspectAsync(request, cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                return new WorkflowLabelInspectionResult(request.FrameId, request.CycleId, resource.Recipe.Name,
                    resource.RecipeSha256, resource.ResourceIdentity, report, resource.Recipe.Regions);
            }
            finally { entry.Serial.Release(); }
        }
        finally { entry.Release(); }
    }

    /// <summary>停止接受调用，等待所有在途/排队调用退出后释放模型；不会提前释放原生资源。</summary>
    public async ValueTask DisposeAsync()
    {
        Entry[] entries;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            entries = _active.Values.SelectMany(p => p.Values).Concat(_cache.Values).Distinct().ToArray();
            _active.Clear(); _cache.Clear();
        }
        await Task.WhenAll(entries.Select(e => e.RetireAsync())).ConfigureAwait(false);
    }
    private static Task ReleaseAsync(IEnumerable<Entry> entries) => Task.WhenAll(entries.Select(e => e.ReleaseLeaseAsync()));

    private int _loadCount;
    /// <summary>累计实际加载标签资源（模型、引擎、参考图）的次数；复用缓存的轮次不计入（诊断用）。</summary>
    public int ResourceLoadCount => Volatile.Read(ref _loadCount);
    /// <summary>已加载并缓存、可供下一轮复用的标签资源份数（诊断用）。</summary>
    public int CachedResourceCount { get { lock (_gate) return _cache.Count; } }

    // 提交后把本轮新加载的资源放入缓存，替换同一位置的旧资源（旧资源在没有租约和在途调用后释放）。
    private void PublishToCache(Dictionary<(string, string), Entry> entries)
    {
        foreach (var (key, entry) in entries)
        {
            if (entry.Fingerprint is null || _cache.TryGetValue(key, out var cached) && ReferenceEquals(cached, entry) || !entry.TryAddLease()) continue;
            if (_cache.Remove(key, out var old)) _ = old.ReleaseLeaseAsync();
            _cache[key] = entry;
        }
    }

    /// <summary>一套已加载资源；准备中的运行与缓存各持一个租约，租约归零后在在途调用结束时释放。</summary>
    private sealed class Entry(WorkflowLabelInspectionResources resource, string? fingerprint)
    {
        private readonly object _gate = new();
        private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _users, _leases = 1;
        private bool _retired, _cleaning;
        internal WorkflowLabelInspectionResources Resource { get; } = resource;
        internal string? Fingerprint { get; } = fingerprint;
        internal SemaphoreSlim Serial { get; } = new(1, 1);
        internal void Acquire()
        { lock (_gate) { if (_retired) throw new InvalidOperationException("标签资源已退役。"); _users++; } }
        internal void Release()
        { lock (_gate) { _users--; CleanIfIdle(); } }
        internal bool TryAddLease()
        { lock (_gate) { if (_retired) return false; _leases++; return true; } }
        internal Task ReleaseLeaseAsync()
        {
            lock (_gate)
            {
                if (_retired) return _released.Task;
                if (--_leases > 0) return Task.CompletedTask;
                _retired = true; CleanIfIdle(); return _released.Task;
            }
        }
        internal Task RetireAsync()
        { lock (_gate) { _retired = true; CleanIfIdle(); return _released.Task; } }
        private void CleanIfIdle()
        {
            if (!_retired || _users != 0 || _cleaning) return;
            _cleaning = true;
            try { Resource.Dispose(); _released.TrySetResult(); }
            catch (Exception error) { _released.TrySetException(error); }
            finally { Serial.Dispose(); }
        }
    }
    private sealed class Prepared(WorkflowLabelInspectionRuntime owner, Guid id, Dictionary<(string, string), Entry> entries,
        IWorkflowPreparedRun? next) : IWorkflowPreparedRun
    {
        private bool _committed, _released;
        public void Commit()
        {
            lock (owner._gate)
            {
                ObjectDisposedException.ThrowIf(owner._disposed || _released, owner);
                if (_committed) return;
                if (owner._active.ContainsKey(id)) throw new InvalidOperationException("标签绑定作用域已经发布。");
                next?.Commit(); owner._active.Add(id, entries); owner.PublishToCache(entries); _committed = true;
            }
        }
        public async ValueTask DisposeAsync()
        {
            lock (owner._gate)
            {
                if (_released) return;
                _released = true;
                if (_committed) owner._active.Remove(id);
            }
            try { await ReleaseAsync(entries.Values).ConfigureAwait(false); }
            finally { if (next is not null) await next.DisposeAsync().ConfigureAwait(false); }
        }
    }
}
