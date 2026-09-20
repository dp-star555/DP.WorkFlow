using System.Collections.Concurrent;
using System.Reflection;
#if !NETFRAMEWORK
using System.Runtime.Loader;
#endif

namespace ScriptEngine.Runtime;

/// <summary>
/// 按稳定脚本标识保存当前热版本；脚本修订变化时原子替换，旧版本在活动执行归零后卸载。
/// </summary>
internal sealed class ScriptRuntimeRegistry : IDisposable
{
    private readonly ConcurrentDictionary<string, ScriptRuntimeSlot> _slots = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<string> _insertionOrder = new();
    private readonly int _capacity;
    private bool _disposed;

    public ScriptRuntimeRegistry(int capacity)
    {
        if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
        _capacity = capacity;
    }

    /// <summary>脚本版本完成卸载时发生，供编译缓存同步移除旧产物。</summary>
    public event Action<string>? RevisionUnloaded;

    /// <summary>获取指定脚本和修订的运行租约。</summary>
    public ScriptExecutionLease Acquire(string scriptId, RoslynScriptCompilationArtifact artifact)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(ScriptRuntimeRegistry));
        if (string.IsNullOrWhiteSpace(scriptId)) throw new ArgumentException("脚本标识不能为空。", nameof(scriptId));
        var created = new ScriptRuntimeSlot(scriptId, OnRevisionUnloaded);
        var slot = _slots.GetOrAdd(scriptId, created);
        if (ReferenceEquals(slot, created))
        {
            _insertionOrder.Enqueue(scriptId);
            Trim();
        }
        return slot.Acquire(artifact);
    }

    /// <summary>移除一个脚本的当前热版本；活动执行完成后再实际卸载。</summary>
    public void Remove(string scriptId)
    {
        if (_slots.TryRemove(scriptId, out var slot)) slot.RetireAll();
    }

    /// <summary>释放全部脚本运行槽。</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var pair in _slots.ToArray())
            if (_slots.TryRemove(pair.Key, out var slot)) slot.RetireAll(force: true);
    }

    private void Trim()
    {
        while (_slots.Count > _capacity && _insertionOrder.TryDequeue(out var scriptId))
            if (_slots.TryRemove(scriptId, out var slot)) slot.RetireAll();
    }

    private void OnRevisionUnloaded(string revision) => RevisionUnloaded?.Invoke(revision);
}

/// <summary>单个逻辑脚本的当前版本和等待卸载版本。</summary>
internal sealed class ScriptRuntimeSlot
{
    private readonly object _gate = new();
    private readonly string _scriptId;
    private readonly Action<string> _revisionUnloaded;
    private readonly List<LoadedScriptRevision> _retired = new();
    private LoadedScriptRevision? _current;

    public ScriptRuntimeSlot(string scriptId, Action<string> revisionUnloaded)
    {
        _scriptId = scriptId;
        _revisionUnloaded = revisionUnloaded;
    }

    public ScriptExecutionLease Acquire(RoslynScriptCompilationArtifact artifact)
    {
        List<LoadedScriptRevision>? unload = null;
        LoadedScriptRevision selected;
        lock (_gate)
        {
            if (_current is not null && string.Equals(_current.Revision, artifact.Revision, StringComparison.Ordinal))
            {
                selected = _current;
            }
            else
            {
                // 先完整加载候选版本，加载失败时不会破坏仍可运行的当前版本。
                var candidate = LoadedScriptRevision.Load(artifact);
                var previous = _current;
                _current = candidate;
                selected = candidate;
                if (previous is not null)
                {
                    previous.Retired = true;
                    _retired.Add(previous);
                }
                unload = CollectUnloadableLocked();
            }
            selected.ActiveRuns++;
        }
        Unload(unload);
        return new ScriptExecutionLease(selected, Release);
    }

    public void RetireAll(bool force = false)
    {
        List<LoadedScriptRevision> unload;
        lock (_gate)
        {
            if (_current is not null)
            {
                _current.Retired = true;
                _retired.Add(_current);
                _current = null;
            }
            unload = force ? _retired.ToList() : CollectUnloadableLocked();
            if (force) _retired.Clear();
        }
        Unload(unload);
    }

    private void Release(LoadedScriptRevision revision)
    {
        List<LoadedScriptRevision>? unload;
        lock (_gate)
        {
            if (revision.ActiveRuns <= 0)
                throw new InvalidOperationException($"脚本 {_scriptId}/{revision.Revision} 的运行租约计数无效。");
            revision.ActiveRuns--;
            unload = CollectUnloadableLocked();
        }
        Unload(unload);
    }

    private List<LoadedScriptRevision> CollectUnloadableLocked()
    {
        var result = _retired.Where(item => item.Retired && item.ActiveRuns == 0).ToList();
        foreach (var item in result) _retired.Remove(item);
        return result;
    }

    private void Unload(IEnumerable<LoadedScriptRevision>? revisions)
    {
        if (revisions is null) return;
        foreach (var revision in revisions)
        {
            revision.Dispose();
            _revisionUnloaded(revision.Revision);
        }
    }
}

/// <summary>一次执行期间持有脚本版本的租约，防止正在运行的旧版本被提前卸载。</summary>
internal sealed class ScriptExecutionLease : IDisposable
{
    private LoadedScriptRevision? _revision;
    private readonly Action<LoadedScriptRevision> _release;

    public ScriptExecutionLease(LoadedScriptRevision revision, Action<LoadedScriptRevision> release)
    {
        _revision = revision;
        _release = release;
    }

    /// <summary>为本次运行创建全新的脚本实例，避免实例字段污染下一次运行。</summary>
    public ICSharpProgram CreateProgram()
    {
        var revision = _revision ?? throw new ObjectDisposedException(nameof(ScriptExecutionLease));
        return revision.CreateProgram();
    }

    public void Dispose()
    {
        var revision = Interlocked.Exchange(ref _revision, null);
        if (revision is not null) _release(revision);
    }
}

/// <summary>已经加载并完成入口解析的一个脚本修订。</summary>
internal sealed class LoadedScriptRevision : IDisposable
{
    private Type? _programType;
#if NETFRAMEWORK
    private ResolveEventHandler? _assemblyResolver;
#else
    private CollectibleScriptLoadContext? _loadContext;
#endif

    private LoadedScriptRevision(string revision, Type programType
#if NETFRAMEWORK
        , ResolveEventHandler assemblyResolver
#else
        , CollectibleScriptLoadContext loadContext
#endif
        )
    {
        Revision = revision;
        _programType = programType;
#if NETFRAMEWORK
        _assemblyResolver = assemblyResolver;
#else
        _loadContext = loadContext;
#endif
    }

    public string Revision { get; }
    public int ActiveRuns { get; set; }
    public bool Retired { get; set; }

    public static LoadedScriptRevision Load(RoslynScriptCompilationArtifact artifact)
    {
#if NETFRAMEWORK
        // .NET Framework 的进程内兼容模式无法真正卸载程序集；完整隔离需要独立 Worker。
        // 显式 DLL 在加载脚本前进入 LoadFrom 上下文，确保首次执行可以解析用户引用。
        Assembly? ResolveReference(object? _, ResolveEventArgs eventArgs)
        {
            var requested = new AssemblyName(eventArgs.Name);
            var loaded = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(item =>
                !item.IsDynamic && AssemblyName.ReferenceMatchesDefinition(item.GetName(), requested));
            if (loaded is not null) return loaded;
            return requested.Name is not null
                   && artifact.RuntimeReferencePaths.TryGetValue(requested.Name, out var path)
                   && File.Exists(path)
                ? Assembly.LoadFrom(path)
                : null;
        }
        ResolveEventHandler resolver = ResolveReference;
        AppDomain.CurrentDomain.AssemblyResolve += resolver;
        try
        {
            foreach (var path in artifact.RuntimeReferencePaths.Values.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var referenceName = AssemblyName.GetAssemblyName(path);
                var alreadyLoaded = AppDomain.CurrentDomain.GetAssemblies().Any(item =>
                    !item.IsDynamic && AssemblyName.ReferenceMatchesDefinition(item.GetName(), referenceName));
                if (!alreadyLoaded) Assembly.LoadFrom(path);
            }
            var assembly = Assembly.Load(artifact.AssemblyBytes, artifact.PdbBytes);
            var programType = ResolveProgramType(assembly, artifact.EntryTypeName);
            return new LoadedScriptRevision(artifact.Revision, programType, resolver);
        }
        catch
        {
            AppDomain.CurrentDomain.AssemblyResolve -= resolver;
            throw;
        }
#else
        var context = new CollectibleScriptLoadContext(artifact.RuntimeReferencePaths);
        try
        {
            using var pe = new MemoryStream(artifact.AssemblyBytes, writable: false);
            using var pdb = new MemoryStream(artifact.PdbBytes, writable: false);
            var assembly = context.LoadFromStream(pe, pdb);
            var programType = ResolveProgramType(assembly, artifact.EntryTypeName);
            return new LoadedScriptRevision(artifact.Revision, programType, context);
        }
        catch
        {
            context.Unload();
            throw;
        }
#endif
    }

    public ICSharpProgram CreateProgram()
    {
        var type = _programType ?? throw new ObjectDisposedException(nameof(LoadedScriptRevision));
        return Activator.CreateInstance(type) as ICSharpProgram
            ?? throw new InvalidOperationException($"无法创建脚本入口类型：{type.FullName}。");
    }

    public void Dispose()
    {
        _programType = null;
#if NETFRAMEWORK
        var resolver = _assemblyResolver;
        _assemblyResolver = null;
        if (resolver is not null) AppDomain.CurrentDomain.AssemblyResolve -= resolver;
#else
        var context = _loadContext;
        _loadContext = null;
        context?.Unload();
#endif
    }

    private static Type ResolveProgramType(Assembly assembly, string entryTypeName)
    {
        var type = assembly.GetType(entryTypeName, throwOnError: false, ignoreCase: false)
            ?? throw new InvalidOperationException($"编译产物中找不到脚本入口类型：{entryTypeName}。");
        if (type.IsAbstract || !typeof(ICSharpProgram).IsAssignableFrom(type))
            throw new InvalidOperationException($"脚本入口类型未正确实现 {nameof(ICSharpProgram)}：{entryTypeName}。");
        if (type.GetConstructor(Type.EmptyTypes) is null)
            throw new InvalidOperationException($"脚本入口类型没有公共无参数构造函数：{entryTypeName}。");
        return type;
    }
}

#if !NETFRAMEWORK
/// <summary>现代 .NET 中按脚本修订隔离并支持协作卸载的程序集加载上下文。</summary>
internal sealed class CollectibleScriptLoadContext : AssemblyLoadContext
{
    private readonly IReadOnlyDictionary<string, string> _runtimeReferencePaths;

    public CollectibleScriptLoadContext(IReadOnlyDictionary<string, string> runtimeReferencePaths)
        : base(isCollectible: true)
    {
        _runtimeReferencePaths = runtimeReferencePaths;
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        // 契约和宿主已加载程序集必须共享类型身份，否则 HostContext 强类型转换会失败。
        var shared = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(item =>
            !item.IsDynamic && AssemblyName.ReferenceMatchesDefinition(item.GetName(), assemblyName));
        if (shared is not null) return shared;

        if (assemblyName.Name is not null
            && _runtimeReferencePaths.TryGetValue(assemblyName.Name, out var path)
            && File.Exists(path))
            return LoadFromAssemblyPath(path);
        return null;
    }
}
#endif
