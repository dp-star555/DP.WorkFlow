using DP.LabelInspection.Contracts;
using DP.Vision;

namespace DP.WorkFlow.LabelInspection;

/// <summary>按绑定作用域和计划路径准备标签节点；根/嵌套准备独立提交、回滚和退役。</summary>
public sealed class WorkflowLabelInspectionRuntime : IWorkflowLabelInspectionService, IWorkflowTransactionalRunPreparationService, IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly Func<string> _baseDirectory;
    private readonly IWorkflowRunPreparationService? _next;
    private readonly Dictionary<Guid, Dictionary<(string Path, string Node), Entry>> _active = new();
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
                var resource = await WorkflowLabelInspectionResources.CreateAsync(node, baseDirectory, cancellationToken).ConfigureAwait(false);
                var entry = new Entry(resource);
                if (!entries.TryAdd((position.PlanPath, node.Id), entry))
                { await entry.RetireAsync().ConfigureAwait(false); throw new InvalidOperationException("标签计划位置重复。"); }
            }
            if (_next is IWorkflowTransactionalRunPreparationService transactional)
                next = await transactional.PrepareRunAsync(context, cancellationToken).ConfigureAwait(false);
            else if (_next is not null) await _next.PrepareAsync(context, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return new Prepared(this, context.BindingScopeId, entries, next);
        }
        catch
        {
            try { await RetireAsync(entries.Values).ConfigureAwait(false); }
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
                    resource.RecipeSha256, resource.ResourceIdentity, report);
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
            _disposed = true; entries = _active.Values.SelectMany(p => p.Values).Distinct().ToArray(); _active.Clear();
        }
        await RetireAsync(entries).ConfigureAwait(false);
    }
    private static Task RetireAsync(IEnumerable<Entry> entries) => Task.WhenAll(entries.Select(e => e.RetireAsync()));

    private sealed class Entry(WorkflowLabelInspectionResources resource)
    {
        private readonly object _gate = new();
        private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _users;
        private bool _retired, _cleaning;
        internal WorkflowLabelInspectionResources Resource { get; } = resource;
        internal SemaphoreSlim Serial { get; } = new(1, 1);
        internal void Acquire()
        { lock (_gate) { if (_retired) throw new InvalidOperationException("标签资源已退役。"); _users++; } }
        internal void Release()
        { lock (_gate) { _users--; CleanIfIdle(); } }
        internal Task RetireAsync()
        { lock (_gate) { _retired = true; CleanIfIdle(); return _released.Task; } }
        private void CleanIfIdle()
        {
            if (!_retired || _users != 0 || _cleaning) return;
            _cleaning = true;
            try { Resource.Dispose(); Serial.Dispose(); _released.TrySetResult(); }
            catch (Exception error) { _released.TrySetException(error); }
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
                next?.Commit(); owner._active.Add(id, entries); _committed = true;
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
            try { await RetireAsync(entries.Values).ConfigureAwait(false); }
            finally { if (next is not null) await next.DisposeAsync().ConfigureAwait(false); }
        }
    }
}
