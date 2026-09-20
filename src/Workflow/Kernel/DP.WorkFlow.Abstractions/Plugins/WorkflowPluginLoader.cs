using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;

namespace DP.WorkFlow;

/// <summary>插件 Manifest 中的标准 Module 分组。</summary>
public static class WorkflowPluginModuleGroups
{
    /// <summary>无桌面依赖的工作流运行 Module。</summary>
    public const string Runtime = "runtime";

    /// <summary>无桌面技术依赖的 Studio Module。</summary>
    public const string Studio = "studio";

    /// <summary>WinForms 平台 Module。</summary>
    public const string WinForms = "winForms";

    /// <summary>WPF 平台 Module。</summary>
    public const string Wpf = "wpf";
}

/// <summary>描述一个部署插件包及其按宿主类型分组的程序集。</summary>
public sealed class WorkflowPluginManifest
{
    /// <summary>获取或设置 Manifest 契约版本；当前只支持版本 1。</summary>
    public int ManifestVersion { get; set; } = 1;

    /// <summary>获取或设置跨版本稳定的插件标识。</summary>
    public string PluginId { get; set; } = string.Empty;

    /// <summary>获取或设置插件版本。</summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>获取或设置必须同时安装的插件标识。</summary>
    public string[] Requires { get; set; } = Array.Empty<string>();

    /// <summary>获取或设置 Module 分组及其相对于 Manifest 的程序集路径。</summary>
    public Dictionary<string, string[]> Modules { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>从受信任插件目录读取 Manifest，并按宿主请求的窄接口实例化 Module。</summary>
public sealed class WorkflowPluginLoader
{
    private const string ManifestFileName = "plugin.json";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly Dictionary<string, LoadedPackage> _packages = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _sync = new();

    /// <summary>从目录中加载指定分组且实现目标接口的插件 Module。</summary>
    /// <typeparam name="TModule">宿主拥有的窄 Module 接口。</typeparam>
    /// <param name="pluginRoot">包含插件包子目录或单个 Manifest 的根目录。</param>
    /// <param name="moduleGroup">Manifest Module 分组。</param>
    /// <returns>按 PluginId、程序集路径和类型名确定性排序的 Module。</returns>
    public IReadOnlyList<TModule> LoadModules<TModule>(string pluginRoot, string moduleGroup)
        where TModule : class
    {
        if (string.IsNullOrWhiteSpace(pluginRoot))
            throw new ArgumentException("插件根目录不能为空。", nameof(pluginRoot));
        if (string.IsNullOrWhiteSpace(moduleGroup))
            throw new ArgumentException("插件 Module 分组不能为空。", nameof(moduleGroup));
        var root = Path.GetFullPath(pluginRoot);
        if (!Directory.Exists(root))
            return Array.Empty<TModule>();

        lock (_sync)
        {
            var manifests = OrderByDependencies(DiscoverManifests(root));
            var modules = new List<(int PackageOrder, string AssemblyPath, string TypeName, TModule Module)>();
            for (var packageOrder = 0; packageOrder < manifests.Count; packageOrder++)
            {
                var item = manifests[packageOrder];
                if (!item.Manifest.Modules.TryGetValue(moduleGroup.Trim(), out var assemblyFiles))
                    continue;
                foreach (var assemblyFile in assemblyFiles ?? Array.Empty<string>())
                {
                    var assemblyPath = ResolveModulePath(item.ManifestPath, assemblyFile);
                    var package = GetOrLoadPackage(item);
                    var assembly = package.Load(assemblyPath);
                    var moduleTypes = GetModuleTypes<TModule>(assembly);
                    if (moduleTypes.Count == 0)
                        throw new InvalidOperationException(
                            $"插件“{item.Manifest.PluginId}”把程序集“{assemblyFile}”声明到“{moduleGroup}”分组，但其中没有实现 {typeof(TModule).FullName} 的公开 Module。");
                    foreach (var type in moduleTypes)
                    {
                        var module = Activator.CreateInstance(type) as TModule
                            ?? throw new InvalidOperationException(
                                $"插件 Module“{type.FullName}”无法通过公开无参数构造函数创建。");
                        modules.Add((packageOrder, assemblyPath, type.FullName!, module));
                    }
                }
            }
            return modules
                .OrderBy(item => item.PackageOrder)
                .ThenBy(item => item.AssemblyPath, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.TypeName, StringComparer.Ordinal)
                .Select(item => item.Module)
                .ToArray();
        }
    }

    private LoadedPackage GetOrLoadPackage(ManifestEntry entry)
    {
        if (_packages.TryGetValue(entry.ManifestPath, out var package))
            return package;
        package = new LoadedPackage(entry.Manifest.PluginId, Path.GetDirectoryName(entry.ManifestPath)!);
        _packages.Add(entry.ManifestPath, package);
        return package;
    }

    private static IReadOnlyList<ManifestEntry> DiscoverManifests(string root)
    {
        var paths = new List<string>();
        var rootManifest = Path.Combine(root, ManifestFileName);
        if (File.Exists(rootManifest))
            paths.Add(rootManifest);
        paths.AddRange(Directory.EnumerateDirectories(root)
            .Select(directory => Path.Combine(directory, ManifestFileName))
            .Where(File.Exists));

        return paths
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(ReadManifest)
            .ToArray();
    }

    private static ManifestEntry ReadManifest(string manifestPath)
    {
        WorkflowPluginManifest manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<WorkflowPluginManifest>(File.ReadAllText(manifestPath), JsonOptions)
                ?? throw new InvalidOperationException("Manifest 内容为空。");
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException($"插件 Manifest“{manifestPath}”不是有效 JSON。", exception);
        }

        if (manifest.ManifestVersion != 1)
            throw new InvalidOperationException(
                $"插件 Manifest“{manifestPath}”使用不受支持的契约版本 {manifest.ManifestVersion}；当前只支持版本 1。");
        if (string.IsNullOrWhiteSpace(manifest.PluginId))
            throw new InvalidOperationException($"插件 Manifest“{manifestPath}”缺少 PluginId。");
        if (string.IsNullOrWhiteSpace(manifest.Version))
            throw new InvalidOperationException($"插件“{manifest.PluginId}”缺少 Version。");
        manifest.PluginId = manifest.PluginId.Trim();
        manifest.Version = manifest.Version.Trim();
        manifest.Requires = (manifest.Requires ?? Array.Empty<string>())
            .Select(item => item?.Trim() ?? string.Empty)
            .Where(item => item.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (manifest.Requires.Contains(manifest.PluginId, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException($"插件“{manifest.PluginId}”不能依赖自身。");
        manifest.Modules = manifest.Modules is null
            ? new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string[]>(manifest.Modules, StringComparer.OrdinalIgnoreCase);
        return new ManifestEntry(Path.GetFullPath(manifestPath), manifest);
    }

    private static void EnsureUniquePluginIds(IReadOnlyList<ManifestEntry> entries)
    {
        var duplicate = entries
            .GroupBy(item => item.Manifest.PluginId, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
            throw new InvalidOperationException(
                $"插件标识“{duplicate.Key}”由多个 Manifest 声明：{string.Join(", ", duplicate.Select(item => item.ManifestPath))}。");
    }

    private static IReadOnlyList<ManifestEntry> OrderByDependencies(IReadOnlyList<ManifestEntry> entries)
    {
        EnsureUniquePluginIds(entries);
        var byId = entries.ToDictionary(item => item.Manifest.PluginId, StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            var missing = entry.Manifest.Requires.Where(required => !byId.ContainsKey(required)).ToArray();
            if (missing.Length > 0)
                throw new InvalidOperationException(
                    $"插件“{entry.Manifest.PluginId}”缺少依赖：{string.Join(", ", missing)}。");
        }

        var ordered = new List<ManifestEntry>(entries.Count);
        var states = new Dictionary<string, VisitState>(StringComparer.OrdinalIgnoreCase);
        var path = new Stack<string>();
        foreach (var entry in entries.OrderBy(item => item.Manifest.PluginId, StringComparer.Ordinal))
            Visit(entry);
        return ordered;

        void Visit(ManifestEntry entry)
        {
            if (states.TryGetValue(entry.Manifest.PluginId, out var state))
            {
                if (state == VisitState.Visited)
                    return;
                var cycle = path.Reverse().Append(entry.Manifest.PluginId);
                throw new InvalidOperationException($"插件依赖存在循环：{string.Join(" -> ", cycle)}。");
            }
            states[entry.Manifest.PluginId] = VisitState.Visiting;
            path.Push(entry.Manifest.PluginId);
            foreach (var required in entry.Manifest.Requires.OrderBy(item => item, StringComparer.Ordinal))
                Visit(byId[required]);
            path.Pop();
            states[entry.Manifest.PluginId] = VisitState.Visited;
            ordered.Add(entry);
        }
    }

    private static string ResolveModulePath(string manifestPath, string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            throw new InvalidOperationException($"插件 Manifest“{manifestPath}”包含空程序集路径。");
        var packageRoot = Path.GetFullPath(Path.GetDirectoryName(manifestPath)! + Path.DirectorySeparatorChar);
        var path = Path.GetFullPath(Path.Combine(packageRoot, relativePath));
        if (!path.StartsWith(packageRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"插件程序集路径不能离开插件包目录：{relativePath}。");
        if (!File.Exists(path))
            throw new FileNotFoundException($"找不到插件程序集“{relativePath}”。", path);
        return path;
    }

    private static IReadOnlyList<Type> GetModuleTypes<TModule>(Assembly assembly)
        where TModule : class
    {
        try
        {
            return assembly.GetExportedTypes()
                .Where(type => typeof(TModule).IsAssignableFrom(type)
                    && type is { IsAbstract: false, IsInterface: false }
                    && type.GetConstructor(Type.EmptyTypes) is not null)
                .OrderBy(type => type.FullName, StringComparer.Ordinal)
                .ToArray();
        }
        catch (ReflectionTypeLoadException exception)
        {
            var details = string.Join(Environment.NewLine,
                exception.LoaderExceptions.Where(item => item is not null).Select(item => item!.Message));
            throw new InvalidOperationException(
                $"无法检查插件程序集“{assembly.Location}”中的 Module。{Environment.NewLine}{details}", exception);
        }
    }

    private enum VisitState
    {
        Visiting,
        Visited
    }

    private sealed record ManifestEntry(string ManifestPath, WorkflowPluginManifest Manifest);

    private sealed class LoadedPackage
    {
        private readonly PluginLoadContext _context;
        private readonly Dictionary<string, Assembly> _assemblies = new(StringComparer.OrdinalIgnoreCase);

        public LoadedPackage(string pluginId, string packageRoot) =>
            _context = new PluginLoadContext(pluginId, packageRoot);

        public Assembly Load(string assemblyPath)
        {
            if (_assemblies.TryGetValue(assemblyPath, out var assembly))
                return assembly;
            _context.AddResolver(assemblyPath);
            var name = AssemblyName.GetAssemblyName(assemblyPath);
            assembly = AssemblyLoadContext.Default.Assemblies.FirstOrDefault(candidate =>
                AssemblyName.ReferenceMatchesDefinition(candidate.GetName(), name))
                ?? _context.LoadFromAssemblyPath(assemblyPath);
            _assemblies.Add(assemblyPath, assembly);
            return assembly;
        }
    }

    private sealed class PluginLoadContext : AssemblyLoadContext
    {
        private readonly string _packageRoot;
        private readonly List<AssemblyDependencyResolver> _resolvers = new();
        private readonly HashSet<string> _resolverPaths = new(StringComparer.OrdinalIgnoreCase);

        public PluginLoadContext(string pluginId, string packageRoot)
            : base($"WorkflowPlugin:{pluginId}", isCollectible: false) =>
            _packageRoot = Path.GetFullPath(packageRoot);

        public void AddResolver(string assemblyPath)
        {
            if (_resolverPaths.Add(assemblyPath))
                _resolvers.Add(new AssemblyDependencyResolver(assemblyPath));
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            var shared = Default.Assemblies.FirstOrDefault(candidate =>
                AssemblyName.ReferenceMatchesDefinition(candidate.GetName(), assemblyName));
            if (shared is not null)
                return shared;
            foreach (var resolver in _resolvers)
            {
                var path = resolver.ResolveAssemblyToPath(assemblyName);
                if (path is not null)
                    return LoadFromAssemblyPath(path);
            }
            var localPath = Path.Combine(_packageRoot, $"{assemblyName.Name}.dll");
            return File.Exists(localPath) ? LoadFromAssemblyPath(localPath) : null;
        }

        protected override nint LoadUnmanagedDll(string unmanagedDllName)
        {
            foreach (var resolver in _resolvers)
            {
                var path = resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
                if (path is not null)
                    return LoadUnmanagedDllFromPath(path);
            }
            return nint.Zero;
        }
    }
}
