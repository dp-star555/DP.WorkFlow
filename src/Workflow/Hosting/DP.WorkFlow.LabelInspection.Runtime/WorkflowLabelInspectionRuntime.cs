using DP.LabelInspection.Contracts;
using DP.Vision;
using DP.Vision.Algorithms;

namespace DP.WorkFlow.LabelInspection;

/// <summary>
/// 启动时只登记计划配置；执行标签节点时按需加载资源。配方缓存可过期/淘汰，模型跨节点按内容共享。
/// 检测调用独立持有租约；闲置清理不改变在途检测或释放正在使用的原生实例。
/// </summary>
public sealed class WorkflowLabelInspectionRuntime : IWorkflowLabelInspectionService, IWorkflowLabelRecipeInspectionService, IWorkflowTransactionalRunPreparationService, IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly Func<string> _baseDirectory;
    private readonly IWorkflowRunPreparationService? _next;
    private readonly Dictionary<Guid, Dictionary<(string Path, string Node), Registration>> _active = new();
    private readonly Dictionary<string, CatalogSlot> _catalogs = new(StringComparer.OrdinalIgnoreCase);
    private readonly LabelInspectionResourceCache<LoadedRecipe> _recipes;
    private readonly WorkflowLabelInspectionModels _models;
    private readonly CancellationTokenSource _maintenanceCancellation = new();
    private readonly Task _maintenance;
    private bool _disposed;

    /// <summary>创建宿主范围的按需缓存，不与独立宿主共享全局资源。</summary>
    /// <param name="baseDirectory">资源根相对流程文件目录解析。</param>
    /// <param name="next">可串接图像/采集运行准备。</param>
    /// <param name="options">空闲期限、缓存数量及加载并发策略；null使用默认值。</param>
    /// <param name="anomalyImplementations">厂商实现工厂，仅登记元数据，未选实现不加载SDK/许可。</param>
    public WorkflowLabelInspectionRuntime(Func<string> baseDirectory, IWorkflowRunPreparationService? next = null,
        WorkflowLabelInspectionCacheOptions? options = null, IEnumerable<IAnomalyImplementation>? anomalyImplementations = null)
        : this(baseDirectory, next, options ?? new(), null, null, anomalyImplementations) { }

    internal WorkflowLabelInspectionRuntime(Func<string> baseDirectory, IWorkflowRunPreparationService? next,
        WorkflowLabelInspectionCacheOptions options, Func<string, ITextLineRecognizer>? ocr, Func<string, IPatchAnomalyDetector>? anomaly,
        IEnumerable<IAnomalyImplementation>? anomalyImplementations = null)
    {
        _baseDirectory = baseDirectory ?? throw new ArgumentNullException(nameof(baseDirectory)); _next = next;
        options.Validate(); CacheOptions = options;
        _recipes = new(options.MaximumCachedRecipes, options.MaximumConcurrentLoads, options.IdleExpiration, options.TimeProvider);
        _models = new(options, ocr, anomaly);
        AnomalyImplementations = new AnomalyImplementationRegistry(anomalyImplementations ?? []).Implementations;
        _maintenance = MaintainAsync();
    }
    /// <summary>冻结厂商工厂快照；编辑实例独立，生产模型统一占共享缓存名额。</summary>
    public IReadOnlyList<IAnomalyImplementation> AnomalyImplementations { get; }
    /// <summary>本宿主固定的缓存策略。</summary>
    public WorkflowLabelInspectionCacheOptions CacheOptions { get; }
    /// <summary>累计成功详细加载配方的次数；登记配方不计入。</summary>
    public int ResourceLoadCount => _recipes.Loads;
    /// <summary>缓存中的配方资源数，包含正在加载的候选。</summary>
    public int CachedResourceCount => _recipes.Count;
    /// <summary>读取资源缓存诊断，不暴露引擎/图像对象。</summary>
    public WorkflowLabelInspectionCacheStatistics CacheStatistics
    {
        get
        {
            int registered;
            lock (_gate) registered = _active.Values.Sum(p => p.Count);
            return new(registered, _recipes.Count, _models.Count, _recipes.Loads, _models.Loads,
                _recipes.Hits, _models.Hits, _recipes.Evictions, _models.Evictions);
        }
    }

    /// <inheritdoc/>
    public async ValueTask<IWorkflowPreparedRun> PrepareRunAsync(WorkflowRunPreparationContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested(); ArgumentNullException.ThrowIfNull(context);
        lock (_gate) ObjectDisposedException.ThrowIf(_disposed, this);
        if (context.BindingScopeId == Guid.Empty) throw new InvalidOperationException("标签准备需要明确绑定作用域。");
        var duplicate = context.Nodes.GroupBy(n => n.Id, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1 && g.Any(n => n is InspectLabelNodeModel));
        if (duplicate is not null) throw new InvalidOperationException("标签预览节点ID跨子计划重复：" + duplicate.Key);
        var entries = new Dictionary<(string, string), Registration>();
        IWorkflowPreparedRun? next = null;
        try
        {
            var positions = context.PositionedNodes ?? context.Nodes.Select(n => new WorkflowPreparationNode("$", n)).ToArray();
            var directory = Path.GetFullPath(_baseDirectory());
            foreach (var position in positions.Where(p => p.Node is InspectLabelNodeModel))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var node = (InspectLabelNodeModel)WorkflowNodeConfigurationSnapshotter.Capture(position.Node);
                var errors = node.ValidateConfiguration();
                if (errors.Count > 0) throw new InvalidOperationException($"标签节点{node.Id}：" + string.Join("；", errors));
                if (!node.HasRecipe && !node.UsesRecipeCatalog) continue;
                CatalogSlot? catalog = null;
                if (node.UsesRecipeCatalog)
                    catalog = await GetCatalogAsync(Path.GetFullPath(node.ResourceRoot, directory), node.RecipeCatalogPath, cancellationToken).ConfigureAwait(false);
                var key = (position.PlanPath, node.Id);
                var group = directory + "\n" + position.PlanPath + "\n" + node.Id;
                if (!entries.TryAdd(key, new(node, directory, group, catalog))) throw new InvalidOperationException("标签计划位置重复。");
            }
            // 此处不读取参考像素、字库修订或模型内容；未选中分支不会详细加载。
            if (_next is IWorkflowTransactionalRunPreparationService transactional)
                next = await transactional.PrepareRunAsync(context, cancellationToken).ConfigureAwait(false);
            else if (_next is not null) await _next.PrepareAsync(context, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return new Prepared(this, context.BindingScopeId, entries, next);
        }
        catch { if (next is not null) await next.DisposeAsync().ConfigureAwait(false); throw; }
    }
    /// <inheritdoc/>
    public ValueTask PrepareAsync(WorkflowRunPreparationContext context, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("标签准备必须由支持提交/回滚的运行宿主调用。");

    /// <inheritdoc/>
    public async Task<WorkflowLabelInspectionResult> InspectAsync(IWorkflowNodeExecutionContext context, ImageFrame frame,
        string? cycleId, TaskDataSnapshot? taskData, InspectionPlacement? placement, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var registration = GetRegistration(context);
        if (registration.Catalog is not null) throw new InvalidOperationException("目录模式必须提供本次配方选择。");
        // 详细加载和排队之前保留输入；不把运行图像交给缓存持有。
        using var retained = frame.Retain();
        var fingerprint = WorkflowLabelInspectionResources.Fingerprint(registration.Node, registration.Directory) ?? Guid.NewGuid().ToString("N");
        using var lease = await _recipes.AcquireAsync(registration.Group + "\n" + fingerprint,
            token => LoadRecipeAsync(registration, token), cancellationToken, registration.Group).ConfigureAwait(false);
        return await InspectLoadedAsync(lease.Value, retained, cycleId, taskData, placement, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<WorkflowLabelInspectionResult> InspectRecipeAsync(IWorkflowNodeExecutionContext context, ImageFrame frame,
        string recipeKey, string? cycleId, TaskDataSnapshot? taskData, VisionCoordinateSystem? coordinates, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var registration = GetRegistration(context);
        var catalog = registration.Catalog ?? throw new InvalidOperationException("节点未配置外部配方索引。");
        // 目录选择和输入保留都发生在第一个异步等待前；刷新目录不改变此entry。
        var entry = catalog.Resolve(recipeKey);
        using var retained = frame.Retain(); coordinates?.ValidateFrame(retained);
        using var lease = await AcquireCatalogRecipeAsync(catalog, entry, registration.Node.MaximumParallelRois, cancellationToken).ConfigureAwait(false);
        var value = lease.Value;
        var placement = value.Node!.Placement(coordinates);
        return await InspectLoadedAsync(value, retained, cycleId, taskData, placement, cancellationToken).ConfigureAwait(false);
    }
    private Task<LabelInspectionResourceCache<LoadedRecipe>.Lease> AcquireCatalogRecipeAsync(CatalogSlot catalog,
        WorkflowLabelRecipeCatalogEntry entry, int parallelRois, CancellationToken cancellationToken)
    {
        // 相同目录/完整版本/执行预算可共享串行引擎；输入、定位和报告始终按请求独立。
        var group = catalog.Identity + "\n" + entry.Key + "\n" + parallelRois;
        return _recipes.AcquireAsync(group + "\n" + entry.ProfileSha256, async token =>
        {
            var profile = await WorkflowLabelRecipeCatalogStore.ReadProfileAsync(catalog.Root, entry, token).ConfigureAwait(false);
            var node = new InspectLabelNodeModel { ResourceRoot = catalog.Root, MaximumParallelRois = parallelRois };
            profile.ApplyTo(node);
            var loaded = await LoadRecipeAsync(new(node, catalog.Root, group, null), token).ConfigureAwait(false);
            try
            {
                CheckAsset(profile.ReferenceSha256, loaded.Resource.ReferenceSha256, "参考图");
                CheckAsset(profile.RecognitionSha256, loaded.Resource.RecognitionSha256, "OCR");
                CheckAsset(profile.AnomalySha256, loaded.Resource.AnomalySha256, "异常骨干");
                loaded.Profile = profile; loaded.Node = node; return loaded;
            }
            catch { loaded.Dispose(); throw; }
        }, cancellationToken, group);
    }
    /// <summary>主动预加载指定目录版本，使用生产同一缓存；不发布检测结果，也不固定全局当前配方。</summary>
    /// <param name="root">节点资源根。</param><param name="indexPath">索引路径。</param><param name="recipeKey">ID或ID@版本。</param>
    /// <param name="maximumParallelRois">须与生产节点相同才复用该执行预算下的引擎。</param><param name="token">取消等待。</param>
    public async Task PrewarmRecipeAsync(string root, string indexPath, string recipeKey, int maximumParallelRois = 1, CancellationToken token = default)
    {
        if (maximumParallelRois is < 1 or > 128) throw new ArgumentOutOfRangeException(nameof(maximumParallelRois));
        var catalog = await GetCatalogAsync(Path.GetFullPath(root), indexPath, token).ConfigureAwait(false);
        var entry = catalog.Resolve(recipeKey);
        using var lease = await AcquireCatalogRecipeAsync(catalog, entry, maximumParallelRois, token).ConfigureAwait(false);
        lease.Value.Resource.TouchModels();
    }

    private static void CheckAsset(string? expected, string? actual, string name)
    {
        if (expected is not null && !string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(name + "资源已变化，与发布配方摘要不一致；请发布新版本，不原位修改旧资产。");
    }
    private Registration GetRegistration(IWorkflowNodeExecutionContext context)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_active.TryGetValue(context.BindingScopeId, out var plan) || !plan.TryGetValue((context.PlanPath, context.Node.Id), out var registration))
                throw new InvalidOperationException("本次运行未提交此标签节点的配方登记。");
            return registration;
        }
    }
    private static async Task<WorkflowLabelInspectionResult> InspectLoadedAsync(LoadedRecipe loaded, ImageFrame frame,
        string? cycleId, TaskDataSnapshot? taskData, InspectionPlacement? placement, CancellationToken cancellationToken)
    {
        var resource = loaded.Resource;
        if (placement is null && (frame.Image.Info.Width != resource.Recipe.Width || frame.Image.Info.Height != resource.Recipe.Height))
            throw new InvalidOperationException($"标签输入图 {frame.Image.Info.Width}×{frame.Image.Info.Height} 与配方尺寸 {resource.Recipe.Width}×{resource.Recipe.Height} 不一致；图像尺寸不同时请绑定“标签坐标系”。");
        using var request = InspectionRequest.FromVision(frame, resource.Recipe, resource.Reference, cycleId, taskData, placement);
        await loaded.Serial.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            resource.TouchModels();
            var report = await resource.Engine.InspectAsync(request, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return new WorkflowLabelInspectionResult(request.FrameId, request.CycleId, resource.Recipe.Name,
                resource.RecipeSha256, resource.ResourceIdentity, report, resource.Recipe.Regions, placement,
                loaded.Profile?.Id, loaded.Profile?.Version, resource.RecipeJson);
        }
        finally { resource.TouchModels(); loaded.Serial.Release(); }
    }

    private async Task<LoadedRecipe> LoadRecipeAsync(Registration registration, CancellationToken token)
    {
        var node = registration.Node;
        var requiredModels = (string.IsNullOrWhiteSpace(node.RecognitionModelPath) ? 0 : 1) + (string.IsNullOrWhiteSpace(node.AnomalyBackbonePath) ? 0 : 1);
        if (requiredModels > CacheOptions.MaximumCachedModels) throw new InvalidOperationException("单个配方所需模型数超过模型缓存上限。");
        try { return new(await WorkflowLabelInspectionResources.CreateSharedAsync(node, registration.Directory, _models, token, AnomalyImplementations).ConfigureAwait(false)); }
        catch (LabelInspectionCacheCapacityException)
        {
            // 模型预算紧张时先归还闲置配方持有的模型；不能淘汰正在加载或检测的配方。
            _recipes.Cleanup(removeAllIdle: true); _models.Cleanup(allIdle: true);
            return new(await WorkflowLabelInspectionResources.CreateSharedAsync(node, registration.Directory, _models, token, AnomalyImplementations).ConfigureAwait(false));
        }
    }
    private async Task<CatalogSlot> GetCatalogAsync(string root, string indexPath, CancellationToken token)
    {
        var key = root + "\n" + WorkflowLabelInspectionResources.ResolvePath(root, indexPath);
        lock (_gate) { ObjectDisposedException.ThrowIf(_disposed, this); if (_catalogs.TryGetValue(key, out var found)) return found; }
        var entries = await WorkflowLabelRecipeCatalogStore.ReadIndexAsync(root, indexPath, token).ConfigureAwait(false);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_catalogs.TryGetValue(key, out var found)) return found;
            var slot = new CatalogSlot(root, key); slot.Update(entries); _catalogs.Add(key, slot); return slot;
        }
    }
    /// <summary>有界读取并原子刷新目录元数据；失败保留旧目录，不替换已取得的配方租约，也不加载重资源。</summary>
    /// <param name="root">必须与节点相同的显式资源根。</param><param name="indexPath">根内索引。</param><param name="token">取消。</param>
    public async Task ReloadRecipeCatalogAsync(string root, string indexPath, CancellationToken token = default)
    {
        root = Path.GetFullPath(root);
        var key = root + "\n" + WorkflowLabelInspectionResources.ResolvePath(root, indexPath);
        var entries = await WorkflowLabelRecipeCatalogStore.ReadIndexAsync(root, indexPath, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_catalogs.TryGetValue(key, out var slot)) { slot = new(root, key); _catalogs.Add(key, slot); }
            slot.Update(entries);
        }
    }

    /// <summary>清理过期资源；removeAllIdle=true时主动卸载全部闲置资源，不影响在途调用。</summary>
    /// <param name="removeAllIdle">是否忽略空闲期限立即清理闲置缓存。</param>
    public void CleanupIdleResources(bool removeAllIdle = false)
    { _recipes.Cleanup(removeAllIdle); _models.Cleanup(removeAllIdle); }
    private async Task MaintainAsync()
    {
        using var timer = new PeriodicTimer(CacheOptions.CleanupInterval, CacheOptions.TimeProvider);
        try
        {
            while (await timer.WaitForNextTickAsync(_maintenanceCancellation.Token).ConfigureAwait(false)) CleanupIdleResources();
        }
        catch (OperationCanceledException) when (_maintenanceCancellation.IsCancellationRequested) { }
    }
    /// <summary>停止接受调用，取消未完成加载，等待在途/排队检测归还租约后释放资源。</summary>
    public async ValueTask DisposeAsync()
    {
        lock (_gate) { if (_disposed) return; _disposed = true; _active.Clear(); _catalogs.Clear(); }
        _maintenanceCancellation.Cancel();
        try { await _maintenance.ConfigureAwait(false); }
        finally
        {
            try { await _recipes.DisposeAsync().ConfigureAwait(false); }
            finally { await _models.DisposeAsync().ConfigureAwait(false); _maintenanceCancellation.Dispose(); }
        }
    }

    private sealed record Registration(InspectLabelNodeModel Node, string Directory, string Group, CatalogSlot? Catalog);
    private sealed class CatalogSlot(string root, string identity)
    {
        private readonly object _sync = new();
        private Dictionary<string, WorkflowLabelRecipeCatalogEntry> _versions = new(StringComparer.Ordinal);
        private Dictionary<string, WorkflowLabelRecipeCatalogEntry> _latest = new(StringComparer.Ordinal);
        private readonly Dictionary<string, WorkflowLabelRecipeCatalogEntry> _published = new(StringComparer.Ordinal);
        internal string Root { get; } = root;
        internal string Identity { get; } = identity;
        internal WorkflowLabelRecipeCatalogEntry Resolve(string key)
        {
            lock (_sync)
            {
                if (string.IsNullOrWhiteSpace(key)) throw new InvalidOperationException("本次配方为空；请提供ID或ID@版本。");
                if ((key.Contains('@') ? _versions : _latest).TryGetValue(key, out var entry)) return entry;
                throw new InvalidOperationException("配方目录中没有此版本，不回退固定配方：" + key);
            }
        }
        internal void Update(IReadOnlyList<WorkflowLabelRecipeCatalogEntry> entries)
        {
            lock (_sync)
            {
                foreach (var entry in entries)
                    if (_published.TryGetValue(entry.Key, out var old) && (!string.Equals(old.ProfileSha256, entry.ProfileSha256, StringComparison.OrdinalIgnoreCase) || old.ProfilePath != entry.ProfilePath))
                        throw new InvalidDataException("配方版本不可原位改写：" + entry.Key);
                if (_published.Count + entries.Count(e => !_published.ContainsKey(e.Key)) > 8192)
                    throw new InvalidDataException("宿主累计配方版本超过8192条元数据预算，请维护目录历史。");
                var versions = entries.ToDictionary(e => e.Key, StringComparer.Ordinal);
                var latest = entries.GroupBy(e => e.Id, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.MaxBy(e => e.Version)!, StringComparer.Ordinal);
                foreach (var entry in entries) _published[entry.Key] = entry;
                _versions = versions; _latest = latest;
            }
        }
    }
    private sealed class LoadedRecipe(WorkflowLabelInspectionResources resource) : IDisposable
    {
        internal WorkflowLabelInspectionResources Resource { get; } = resource;
        internal SemaphoreSlim Serial { get; } = new(1, 1);
        internal WorkflowLabelRecipeProfile? Profile { get; set; }
        internal InspectLabelNodeModel? Node { get; set; }
        public void Dispose() { try { Resource.Dispose(); } finally { Serial.Dispose(); } }
    }
    private sealed class Prepared(WorkflowLabelInspectionRuntime owner, Guid id, Dictionary<(string, string), Registration> entries,
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
                _released = true; if (_committed) owner._active.Remove(id);
            }
            if (next is not null) await next.DisposeAsync().ConfigureAwait(false);
        }
    }
}
