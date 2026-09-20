using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;

namespace ScriptEngine.Compilation;

/// <summary>
/// 将可配置的脚本环境冻结为一次编译、分析和运行共同使用的不可变快照。
/// 快照指纹包含引用文件内容，避免同一路径 DLL 被替换后继续命中旧缓存。
/// </summary>
internal sealed class ScriptEnvironmentSnapshot
{
    private static readonly Lazy<PlatformReferenceSet> PlatformReferences = new(CreatePlatformReferences, LazyThreadSafetyMode.ExecutionAndPublication);
    private const int ReferenceCacheCapacity = 512;
    private static readonly ConcurrentDictionary<string, FileHashCacheEntry> FileHashes = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, ReferenceMetadataCacheEntry> ReferenceMetadata = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, EmbeddedReference> EmbeddedReferenceMetadata = new(StringComparer.Ordinal);
    private static readonly ConcurrentQueue<string> FileHashOrder = new();
    private static readonly ConcurrentQueue<string> ReferenceMetadataOrder = new();
    private static readonly ConcurrentQueue<string> EmbeddedReferenceOrder = new();

    private ScriptEnvironmentSnapshot(
        IReadOnlyList<string> imports,
        IReadOnlyList<MetadataReference> metadataReferences,
        IReadOnlyDictionary<string, string> runtimeReferencePaths,
        RoslynScriptSecurityPolicy securityPolicy,
        string fingerprint)
    {
        Imports = imports;
        MetadataReferences = metadataReferences;
        RuntimeReferencePaths = runtimeReferencePaths;
        SecurityPolicy = securityPolicy;
        Fingerprint = fingerprint;
    }

    /// <summary>自动导入的命名空间。</summary>
    public IReadOnlyList<string> Imports { get; }

    /// <summary>Roslyn 编译和语言分析共用的元数据引用。</summary>
    public IReadOnlyList<MetadataReference> MetadataReferences { get; }

    /// <summary>运行时需要从私有位置解析的非平台程序集。</summary>
    public IReadOnlyDictionary<string, string> RuntimeReferencePaths { get; }

    /// <summary>应用于当前编译的静态符号访问策略。</summary>
    public RoslynScriptSecurityPolicy SecurityPolicy { get; }

    /// <summary>导入和引用内容共同决定的环境指纹。</summary>
    public string Fingerprint { get; }

    /// <summary>创建环境快照。</summary>
    /// <param name="environment">宿主提供的脚本环境。</param>
    /// <returns>可安全用于缓存键的不可变环境快照。</returns>
    public static ScriptEnvironmentSnapshot Create(RoslynScriptEnvironment environment)
    {
        if (environment is null) throw new ArgumentNullException(nameof(environment));

        var imports = environment.Imports
            .Select(item => item?.Trim() ?? string.Empty)
            .Where(item => item.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(item => item, StringComparer.Ordinal)
            .ToArray();

        var platform = PlatformReferences.Value;
        var explicitReferencePaths = environment.ReferencePaths
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var missingExplicitPath = explicitReferencePaths.FirstOrDefault(path => !File.Exists(path));
        if (missingExplicitPath is not null)
            throw new FileNotFoundException($"脚本引用不存在：{missingExplicitPath}", missingExplicitPath);
        if (environment.ValidateReferenceArchitecture)
            ValidateArchitectures(explicitReferencePaths);
        var resolvedReferencePaths = environment.ResolveReferenceDependencies
            ? ResolvePrivateDependencies(explicitReferencePaths, platform.AssemblyNames)
            : explicitReferencePaths;

        var referenceAssemblies = environment.References
            .Where(item => !item.IsDynamic)
            .Concat(new[] { typeof(ICSharpProgram).Assembly, typeof(ValueTask).Assembly })
            .Distinct(AssemblyIdentityComparer.Instance)
            .ToArray();
        var assemblyLocations = referenceAssemblies
            .Select(GetAssemblyLocation)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Cast<string>();
        var additionalPaths = assemblyLocations
            .Concat(resolvedReferencePaths)
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var missingPath = additionalPaths.FirstOrDefault(path => !File.Exists(path));
        if (missingPath is not null)
            throw new FileNotFoundException($"脚本引用不存在：{missingPath}", missingPath);

        var additionalReferences = additionalPaths
            .Select(path => new ReferenceFile(path, GetFileHash(path)))
            .ToArray();
        var embeddedReferences = referenceAssemblies
            .Where(assembly => string.IsNullOrWhiteSpace(GetAssemblyLocation(assembly)))
            .Select(CreateEmbeddedReference)
            .ToArray();

        var material = new StringBuilder();
        material.AppendLine("ScriptEnvironment-v3");
        material.Append("ResolveDependencies:").AppendLine(environment.ResolveReferenceDependencies.ToString());
        material.Append("ValidateArchitecture:").AppendLine(environment.ValidateReferenceArchitecture.ToString());
        foreach (var import in imports) material.Append("I:").AppendLine(import);
        foreach (var deniedNamespace in environment.SecurityPolicy.DeniedNamespacePrefixes.OrderBy(item => item, StringComparer.Ordinal))
            material.Append("DN:").AppendLine(deniedNamespace);
        foreach (var deniedSymbol in environment.SecurityPolicy.DeniedSymbolPrefixes.OrderBy(item => item, StringComparer.Ordinal))
            material.Append("DS:").AppendLine(deniedSymbol);
        material.Append("P:").AppendLine(platform.Fingerprint);
        foreach (var reference in additionalReferences)
            material.Append("R:").Append(reference.Path).Append(':').AppendLine(reference.Hash);
        foreach (var reference in embeddedReferences)
            material.Append("E:").Append(reference.Identity).Append(':').AppendLine(reference.Hash);

        // MetadataReference 是不可变对象。平台引用在整个进程内共享，附加引用按“路径+内容哈希”复用，
        // 避免每次热执行都重新解析一百多个系统程序集并制造大量短命 Roslyn 元数据对象。
        var metadataReferences = platform.MetadataReferences
            .Concat(additionalReferences
                .Where(reference => !platform.PathSet.Contains(reference.Path))
                .Select(reference => GetReferenceMetadata(reference).MetadataReference))
            .Concat(embeddedReferences.Select(reference => reference.MetadataReference))
            .ToArray();

        var runtimeReferences = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var reference in additionalReferences)
        {
            var assemblyName = GetReferenceMetadata(reference).AssemblyName;
            if (runtimeReferences.TryGetValue(assemblyName, out var existing)
                && !string.Equals(existing, reference.Path, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"脚本引用中存在同名程序集冲突：{assemblyName}。\n{existing}\n{reference.Path}");
            runtimeReferences[assemblyName] = reference.Path;
        }

        return new ScriptEnvironmentSnapshot(
            imports,
            metadataReferences,
            runtimeReferences,
            environment.SecurityPolicy,
            ComputeHash(material.ToString()));
    }

    /// <summary>计算单文件源码和当前环境共同决定的脚本修订。</summary>
    public string ComputeRevision(string source) => ComputeProjectRevision(new[]
    {
        new RoslynScriptSourceFile("Program.cs", source ?? string.Empty)
    });

    /// <summary>计算多文件源码和当前环境共同决定的脚本修订。</summary>
    public string ComputeProjectRevision(IReadOnlyList<RoslynScriptSourceFile> files)
    {
        var material = new StringBuilder(
            "RoslynScript-v3\nLanguageVersion=Latest\nNullable=Enable\nOptimization=Release\n"
            + Fingerprint);
        foreach (var file in files.OrderBy(item => item.FileName, StringComparer.OrdinalIgnoreCase))
        {
            material.Append("\n--file:").Append(file.FileName).AppendLine("--");
            material.Append(file.Source);
        }
        return ComputeHash(material.ToString());
    }

    private static PlatformReferenceSet CreatePlatformReferences()
    {
#if NETFRAMEWORK
        // .NET Framework 没有 TRUSTED_PLATFORM_ASSEMBLIES，直接使用当前 Framework 目录中的引用集合。
        var frameworkDirectory = Path.GetDirectoryName(typeof(object).Assembly.Location)
            ?? throw new InvalidOperationException("无法定位 .NET Framework 程序集目录。");
        var paths = Directory.GetFiles(frameworkDirectory, "*.dll", SearchOption.TopDirectoryOnly)
            .Concat(Directory.Exists(Path.Combine(frameworkDirectory, "Facades"))
                ? Directory.GetFiles(Path.Combine(frameworkDirectory, "Facades"), "*.dll", SearchOption.TopDirectoryOnly)
                : Array.Empty<string>())
            .ToArray();
#else
        var paths = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))
            ?.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            ?? Array.Empty<string>();
#endif
        var normalized = paths
            .Where(File.Exists)
            .Where(IsManagedAssembly)
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (normalized.Length == 0)
            throw new InvalidOperationException("没有找到当前运行时的平台程序集引用。");

        // 平台引用在进程生命周期内不会变化，内容指纹和 Roslyn 元数据对象都只创建一次。
        var fingerprintMaterial = string.Join("\n", normalized.Select(path => path + ":" + GetFileHash(path)));
        var metadataReferences = normalized.Select(path => MetadataReference.CreateFromFile(path)).ToArray();
        var assemblyNames = normalized
            .Select(path => AssemblyName.GetAssemblyName(path).Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Cast<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return new PlatformReferenceSet(
            metadataReferences,
            new HashSet<string>(normalized, StringComparer.OrdinalIgnoreCase),
            assemblyNames,
            ComputeHash(fingerprintMaterial));
    }

    private static IReadOnlyList<string> ResolvePrivateDependencies(
        IReadOnlyList<string> explicitPaths,
        ISet<string> platformAssemblyNames)
    {
        var resolved = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<string>(explicitPaths);
        while (queue.Count > 0)
        {
            var path = Path.GetFullPath(queue.Dequeue());
            var info = RoslynScriptReferenceInspector.Inspect(path);
            if (resolved.TryGetValue(info.AssemblyName, out var existing))
            {
                if (!string.Equals(existing, path, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"脚本引用中存在同名程序集冲突：{info.AssemblyName}。\n{existing}\n{path}");
                continue;
            }
            resolved[info.AssemblyName] = path;
            var directory = Path.GetDirectoryName(path);
            if (directory is null) continue;
            foreach (var dependency in info.Dependencies)
            {
                if (platformAssemblyNames.Contains(dependency) || resolved.ContainsKey(dependency)) continue;
                var candidate = Path.Combine(directory, dependency + ".dll");
                if (File.Exists(candidate)) queue.Enqueue(candidate);
            }
        }
        return resolved.Values.OrderBy(item => item, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static void ValidateArchitectures(IEnumerable<string> paths)
    {
        var expected = Environment.Is64BitProcess ? "x64" : "x86";
        foreach (var path in paths)
        {
            var info = RoslynScriptReferenceInspector.Inspect(path);
            if (info.Architecture is "AnyCPU" or "I386" || string.Equals(info.Architecture, expected, StringComparison.OrdinalIgnoreCase))
                continue;
            throw new BadImageFormatException(
                $"脚本引用架构 {info.Architecture} 与当前 {expected} 进程不兼容：{path}");
        }
    }

    private static bool IsManagedAssembly(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new PEReader(stream, PEStreamOptions.LeaveOpen);
            return reader.HasMetadata && reader.GetMetadataReader().IsAssembly;
        }
        catch (BadImageFormatException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }

#if !NETFRAMEWORK
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(
        "SingleFile",
        "IL3000:Avoid accessing Assembly file path when publishing as a single file",
        Justification = "Location is used when available and raw assembly metadata is copied when it is empty.")]
#endif
    private static string? GetAssemblyLocation(Assembly assembly)
    {
        var location = assembly.Location;
        return string.IsNullOrWhiteSpace(location) ? null : location;
    }

    private static EmbeddedReference CreateEmbeddedReference(Assembly assembly)
    {
        var identity = (assembly.FullName ?? assembly.GetName().Name ?? "EmbeddedAssembly")
                       + ":" + assembly.ManifestModule.ModuleVersionId;
        if (EmbeddedReferenceMetadata.TryGetValue(identity, out var cached)) return cached;
#if NETFRAMEWORK
        throw new InvalidOperationException($"无法从无文件位置的程序集创建脚本引用：{assembly.FullName}");
#else
        unsafe
        {
            if (!assembly.TryGetRawMetadata(out var metadata, out var length) || metadata is null || length <= 0)
                throw new InvalidOperationException($"无法读取单文件程序中程序集的原始元数据：{assembly.FullName}");
            var bytes = new ReadOnlySpan<byte>(metadata, length).ToArray();
            using var sha256 = SHA256.Create();
            var created = new EmbeddedReference(
                identity,
                ToHex(sha256.ComputeHash(bytes)),
                MetadataReference.CreateFromImage(ImmutableArray.CreateRange(bytes)));
            EmbeddedReferenceMetadata[identity] = created;
            EmbeddedReferenceOrder.Enqueue(identity);
            TrimCache(EmbeddedReferenceMetadata, EmbeddedReferenceOrder);
            return created;
        }
#endif
    }

    private static string GetFileHash(string path)
    {
        var info = new FileInfo(path);
        info.Refresh();
        var key = Path.GetFullPath(path);
        if (FileHashes.TryGetValue(key, out var cached)
            && cached.Length == info.Length
            && cached.LastWriteUtcTicks == info.LastWriteTimeUtc.Ticks)
            return cached.Hash;

        using var stream = new FileStream(key, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var sha256 = SHA256.Create();
        var hash = ToHex(sha256.ComputeHash(stream));
        FileHashes[key] = new FileHashCacheEntry(info.Length, info.LastWriteTimeUtc.Ticks, hash);
        FileHashOrder.Enqueue(key);
        TrimCache(FileHashes, FileHashOrder);
        return hash;
    }

    private static ReferenceMetadataCacheEntry GetReferenceMetadata(ReferenceFile reference)
    {
        if (ReferenceMetadata.TryGetValue(reference.Path, out var cached)
            && string.Equals(cached.Hash, reference.Hash, StringComparison.Ordinal))
            return cached;

        var assemblyName = AssemblyName.GetAssemblyName(reference.Path).Name
            ?? throw new InvalidOperationException($"无法读取脚本引用程序集名称：{reference.Path}");
        var created = new ReferenceMetadataCacheEntry(
            reference.Hash,
            MetadataReference.CreateFromFile(reference.Path),
            assemblyName);
        ReferenceMetadata[reference.Path] = created;
        ReferenceMetadataOrder.Enqueue(reference.Path);
        TrimCache(ReferenceMetadata, ReferenceMetadataOrder);
        return created;
    }

    private static void TrimCache<T>(
        ConcurrentDictionary<string, T> cache,
        ConcurrentQueue<string> order)
    {
        while (cache.Count > ReferenceCacheCapacity && order.TryDequeue(out var key))
            cache.TryRemove(key, out _);
    }

    internal static string ComputeHash(string value)
    {
        using var sha256 = SHA256.Create();
        return ToHex(sha256.ComputeHash(Encoding.UTF8.GetBytes(value)));
    }

    private static string ToHex(byte[] bytes)
    {
        var builder = new StringBuilder(bytes.Length * 2);
        foreach (var value in bytes) builder.Append(value.ToString("X2", System.Globalization.CultureInfo.InvariantCulture));
        return builder.ToString();
    }

    private sealed record PlatformReferenceSet(
        IReadOnlyList<MetadataReference> MetadataReferences,
        HashSet<string> PathSet,
        HashSet<string> AssemblyNames,
        string Fingerprint);
    private sealed record ReferenceFile(string Path, string Hash);
    private sealed record EmbeddedReference(string Identity, string Hash, MetadataReference MetadataReference);
    private sealed record ReferenceMetadataCacheEntry(
        string Hash,
        MetadataReference MetadataReference,
        string AssemblyName);
    private sealed record FileHashCacheEntry(long Length, long LastWriteUtcTicks, string Hash);

    private sealed class AssemblyIdentityComparer : IEqualityComparer<Assembly>
    {
        public static AssemblyIdentityComparer Instance { get; } = new();

        public bool Equals(Assembly? x, Assembly? y) => ReferenceEquals(x, y)
            || x is not null && y is not null
            && string.Equals(x.FullName, y.FullName, StringComparison.Ordinal)
            && x.ManifestModule.ModuleVersionId == y.ManifestModule.ModuleVersionId;

        public int GetHashCode(Assembly obj) => (obj.FullName ?? string.Empty).GetHashCode()
                                                ^ obj.ManifestModule.ModuleVersionId.GetHashCode();
    }
}
