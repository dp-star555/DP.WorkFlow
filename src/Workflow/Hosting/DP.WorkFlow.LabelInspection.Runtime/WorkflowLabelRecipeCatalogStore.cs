using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DP.LabelInspection.Contracts;
using DP.LabelInspection.Runtime;
using DP.LabelInspection.Storage;

namespace DP.WorkFlow.LabelInspection;

/// <summary>轻量索引中的一个不可变配方版本；详细配方JSON在首次使用时才读取。</summary>
/// <param name="Id">配方标识。</param><param name="Version">正整数版本。</param>
/// <param name="ProfilePath">完整配方描述文件，相对节点资源根。</param><param name="ProfileSha256">描述文件内容摘要。</param>
public sealed record WorkflowLabelRecipeCatalogEntry(string Id, int Version, string ProfilePath, string ProfileSha256)
{
    /// <summary>可绑定到节点“本次配方”的精确版本键。</summary>
    public string Key => Id + "@" + Version;
}

/// <summary>配方目录的有界读取与显式发布；不复制资产、不加载推理引擎，不提供文件监听。</summary>
public static class WorkflowLabelRecipeCatalogStore
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, WriteIndented = true, MaxDepth = 64
    };
    private sealed record IndexFile
    {
        public int SchemaVersion { get; init; } = 1;
        public WorkflowLabelRecipeCatalogEntry[] Recipes { get; init; } = [];
    }
    /// <summary>读取轻量索引，最多4096个版本；不读取详细配方或模型。</summary>
    /// <param name="root">显式资源根。</param><param name="indexPath">根内索引路径。</param><param name="token">取消。</param>
    /// <returns>独立、只读的版本登记集合。</returns>
    public static Task<IReadOnlyList<WorkflowLabelRecipeCatalogEntry>> ReadIndexAsync(string root, string indexPath, CancellationToken token = default) => Task.Run(() =>
    {
        var path = WorkflowLabelInspectionResources.ResolvePath(root, indexPath);
        var file = JsonSerializer.Deserialize<IndexFile>(WorkflowLabelInspectionResources.ReadBounded(path, 1024 * 1024, token), Json)
            ?? throw new InvalidDataException("配方索引为空。");
        if (file.SchemaVersion != 1 || file.Recipes is null || file.Recipes.Length > 4096) throw new InvalidDataException("配方索引格式或数量无效。");
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in file.Recipes)
        {
            if (entry is null) throw new InvalidDataException("配方索引含空项。");
            ValidateId(entry.Id, entry.Version); ValidateHash(entry.ProfileSha256);
            _ = WorkflowLabelInspectionResources.ResolvePath(root, entry.ProfilePath);
            if (!keys.Add(entry.Key)) throw new InvalidDataException("配方索引版本重复：" + entry.Key);
        }
        return (IReadOnlyList<WorkflowLabelRecipeCatalogEntry>)Array.AsReadOnly(file.Recipes);
    }, token);

    /// <summary>校验描述摘要并读取一套详细配置，不加载模型。</summary>
    /// <param name="root">资产及描述路径的显式根。</param><param name="entry">已经固定的版本描述。</param><param name="token">取消。</param>
    /// <returns>与索引ID、版本、摘要一致的配方。</returns>
    public static Task<WorkflowLabelRecipeProfile> ReadProfileAsync(string root, WorkflowLabelRecipeCatalogEntry entry, CancellationToken token = default) => Task.Run(() =>
    {
        var bytes = WorkflowLabelInspectionResources.ReadBounded(WorkflowLabelInspectionResources.ResolvePath(root, entry.ProfilePath), 2 * 1024 * 1024, token);
        if (!string.Equals(WorkflowLabelInspectionResources.Hash(bytes), entry.ProfileSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("配方描述摘要不一致，禁止原位修改已发布版本：" + entry.Key);
        var profile = JsonSerializer.Deserialize<WorkflowLabelRecipeProfile>(bytes, Json) ?? throw new InvalidDataException("配方描述为空。");
        ValidateProfile(root, profile, requireAssetHashes: true);
        if (profile.Id != entry.Id || profile.Version != entry.Version) throw new InvalidDataException("配方描述与索引身份不一致：" + entry.Key);
        return profile;
    }, token);

    /// <summary>捕获活动资产摘要并原子发布新版本；同一ID/版本不允许重写。外部文件写入不随节点编辑取消回滚。</summary>
    /// <param name="root">资源根，所有资产必须已位于根内。</param><param name="indexPath">根内索引路径。</param>
    /// <param name="profile">完整配方配置。</param><param name="token">取消，发布索引之前生效。</param>
    /// <returns>已发布版本键；原有检测不会被替换。</returns>
    public static async Task<WorkflowLabelRecipeCatalogEntry> PublishAsync(string root, string indexPath, WorkflowLabelRecipeProfile profile, CancellationToken token = default)
    {
        root = Path.GetFullPath(root); ValidateProfile(root, profile);
        var recipe = new InspectionRecipeSerializer(new OpenCvImageCodec()).Deserialize(profile.RecipeJson);
        var captured = await Task.Run(() => profile with
        {
            ReferenceSha256 = recipe.Mode == EInspectionMode.Template ? Capture(profile.ReferenceImagePath, 64 * 1024 * 1024) : null,
            RecognitionSha256 = string.IsNullOrWhiteSpace(profile.RecognitionModelPath) ? null : Capture(profile.RecognitionModelPath, 256 * 1024 * 1024),
            AnomalySha256 = string.IsNullOrWhiteSpace(profile.AnomalyBackbonePath) ? null : Capture(profile.AnomalyBackbonePath, 256 * 1024 * 1024)
        }, token).ConfigureAwait(false);
        string Capture(string path, int maximum) => WorkflowLabelInspectionResources.Hash(WorkflowLabelInspectionResources.ReadBounded(
            WorkflowLabelInspectionResources.ResolvePath(root, path), maximum, token));
        var bytes = JsonSerializer.SerializeToUtf8Bytes(captured, Json);
        if (bytes.Length > 2 * 1024 * 1024) throw new InvalidDataException("配方描述超过2MB预算。");
        var hash = WorkflowLabelInspectionResources.Hash(bytes);
        var idFolder = WorkflowLabelInspectionResources.Hash(Encoding.UTF8.GetBytes(profile.Id))[..16];
        var relative = $"recipes/{idFolder}/v{profile.Version}-{hash}.json";
        var entry = new WorkflowLabelRecipeCatalogEntry(profile.Id, profile.Version, relative, hash);
        var index = WorkflowLabelInspectionResources.ResolvePath(root, indexPath);
        Directory.CreateDirectory(Path.GetDirectoryName(index)!);
        // 文件锁跨进程协调发布，最多等待30秒；不以进程static字典永久持有目录锁。
        await using var fileLock = await AcquirePublishLockAsync(WorkflowLabelInspectionResources.ResolvePath(root, indexPath + ".publish.lock"), token).ConfigureAwait(false);
        var entries = File.Exists(index) ? (await ReadIndexAsync(root, indexPath, token).ConfigureAwait(false)).ToList() : [];
        if (entries.FirstOrDefault(e => e.Key == entry.Key) is { } old)
        {
            if (!string.Equals(old.ProfileSha256, hash, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("已发布版本不能覆盖，请增加版本号：" + entry.Key);
            _ = await ReadProfileAsync(root, old, token).ConfigureAwait(false);
            return old;
        }
        if (entries.Count >= 4096) throw new InvalidDataException("配方索引超过4096个版本预算。");
        var target = WorkflowLabelInspectionResources.ResolvePath(root, relative);
        if (string.Equals(index, target, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("索引不能覆盖配方描述文件。");
        var folder = Path.GetDirectoryName(target)!;
        Directory.CreateDirectory(folder);
        // 不以从索引删除旧条目来解除版本冻结；已写出的历史/孤立快照也保留版本占用。
        foreach (var previous in Directory.EnumerateFiles(folder, $"v{profile.Version}-*.json", SearchOption.TopDirectoryOnly))
            if (!string.Equals(previous, target, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("历史版本不能重写，请增加版本号：" + entry.Key);
        await WriteAtomicAsync(target, bytes, token).ConfigureAwait(false);
        entries.Add(entry);
        var indexBytes = JsonSerializer.SerializeToUtf8Bytes(new IndexFile { Recipes = entries.OrderBy(e => e.Id, StringComparer.Ordinal).ThenBy(e => e.Version).ToArray() }, Json);
        if (indexBytes.Length > 1024 * 1024) throw new InvalidDataException("配方索引超过1MB预算。");
        await WriteAtomicAsync(index, indexBytes, token).ConfigureAwait(false);
        return entry;
    }

    private static async Task<FileStream> AcquirePublishLockAsync(string path, CancellationToken token)
    {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        while (true)
        {
            token.ThrowIfCancellationRequested();
            try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (timer.Elapsed < TimeSpan.FromSeconds(30)) { await Task.Delay(50, token).ConfigureAwait(false); }
        }
    }
    private static async Task WriteAtomicAsync(string path, byte[] bytes, CancellationToken token)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { await File.WriteAllBytesAsync(temporary, bytes, token).ConfigureAwait(false); token.ThrowIfCancellationRequested(); File.Move(temporary, path, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    internal static void ValidateId(string id, int version)
    {
        if (string.IsNullOrWhiteSpace(id) || id != id.Trim() || id.Length > 128 || id.Any(c => char.IsControl(c) || c == '@') || version < 1)
            throw new InvalidDataException("配方标识/版本无效：ID最多128字符、不含@或控制字符，版本为正整数。");
    }
    private static void ValidateHash(string hash)
    {
        if (hash is null || hash.Length != 64 || hash.Any(c => !char.IsAsciiHexDigit(c))) throw new InvalidDataException("配方/资产SHA256无效。");
    }
    private static void ValidateProfile(string root, WorkflowLabelRecipeProfile profile, bool requireAssetHashes = false)
    {
        ArgumentNullException.ThrowIfNull(profile); ValidateId(profile.Id, profile.Version);
        var node = new InspectLabelNodeModel { ResourceRoot = root, Frame = WorkflowInput<DP.Vision.ImageFrame>.FromBinding(new("source", "$")) };
        profile.ApplyTo(node);
        var errors = node.ValidateConfiguration();
        if (errors.Count != 0 || !node.HasRecipe) throw new InvalidDataException("外部配方无效：" + string.Join("；", errors));
        var recipe = new InspectionRecipeSerializer(new OpenCvImageCodec()).Deserialize(profile.RecipeJson);
        _ = WorkflowLabelInspectionResources.ResolvePath(root, profile.DataDirectory);
        foreach (var path in new[] { profile.RecognitionModelPath, profile.AnomalyBackbonePath, profile.AuthorImagePath })
            if (!string.IsNullOrWhiteSpace(path)) _ = WorkflowLabelInspectionResources.ResolvePath(root, path);
        if (recipe.Mode == EInspectionMode.Template) _ = WorkflowLabelInspectionResources.ResolvePath(root, profile.ReferenceImagePath);
        foreach (var hash in new[] { profile.ReferenceSha256, profile.RecognitionSha256, profile.AnomalySha256 }) if (hash is not null) ValidateHash(hash);
        if (requireAssetHashes && ((recipe.Mode == EInspectionMode.Template && profile.ReferenceSha256 is null)
            || (!string.IsNullOrWhiteSpace(profile.RecognitionModelPath) && profile.RecognitionSha256 is null)
            || (!string.IsNullOrWhiteSpace(profile.AnomalyBackbonePath) && profile.AnomalySha256 is null)))
            throw new InvalidDataException("已发布配方必须包含活动参考/模型的内容摘要，请使用发布入口捕获完整版本。");
    }
}
