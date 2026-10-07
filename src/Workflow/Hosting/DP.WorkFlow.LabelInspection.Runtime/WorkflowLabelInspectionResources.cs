using System.Security.Cryptography;
using System.Text;
using DP.LabelInspection.Contracts;
using DP.LabelInspection.Core;
using DP.LabelInspection.Runtime;
using DP.LabelInspection.Storage;
using DP.Vision;

namespace DP.WorkFlow.LabelInspection;

/// <summary>一次准备的标签资源快照；由运行租约或配置页面拥有，不在每张图重新加载ONNX。</summary>
public sealed class WorkflowLabelInspectionResources : IDisposable
{
    private readonly string _temporary;
    private bool _disposed;
    private WorkflowLabelInspectionResources(string temporary, LabelInspectionHost host, InspectionEngine engine,
        InspectionRecipe recipe, ImageFrame? reference, string recipeHash, string identity)
    { _temporary = temporary; Host = host; Engine = engine; Recipe = recipe; Reference = reference; RecipeSha256 = recipeHash; ResourceIdentity = identity; }
    /// <summary>页面可复用的库管理与训练宿主；生产引擎另使用不可变库快照。</summary>
    public LabelInspectionHost Host { get; }
    /// <summary>拥有原生后端的无界面引擎。</summary>
    public InspectionEngine Engine { get; }
    /// <summary>当前配方快照。</summary>
    public InspectionRecipe Recipe { get; }
    /// <summary>当前模板模式的参考租约。</summary>
    public ImageFrame? Reference { get; }
    /// <summary>原生配方的规范化JSON摘要。</summary>
    public string RecipeSha256 { get; }
    /// <summary>配方、实际参考、模型文件和固定库修订的组合摘要。</summary>
    public string ResourceIdentity { get; }

    /// <summary>异步捕获配置副本和当前资源；失败/取消释放所有候选资源。</summary>
    public static Task<WorkflowLabelInspectionResources> CreateAsync(InspectLabelNodeModel node, string baseDirectory, CancellationToken token = default)
        => CaptureAsync(node, baseDirectory, false, token);

    /// <summary>配置工作台使用可编辑的库仓；显式发布的外部修订不随节点取消回滚。</summary>
    public static Task<WorkflowLabelInspectionResources> CreateForEditingAsync(InspectLabelNodeModel node, string baseDirectory, CancellationToken token = default)
        => CaptureAsync(node, baseDirectory, true, token);

    private static Task<WorkflowLabelInspectionResources> CaptureAsync(InspectLabelNodeModel node, string baseDirectory, bool editing, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(node);
        var copy = (InspectLabelNodeModel)WorkflowNodeConfigurationSnapshotter.Capture(node);
        var root = Path.GetFullPath(copy.ResourceRoot, Path.GetFullPath(baseDirectory));
        return Task.Run(() => Create(copy, root, editing, token), token);
    }

    private static WorkflowLabelInspectionResources Create(InspectLabelNodeModel node, string root, bool editing, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var codec = new OpenCvImageCodec();
        var serializer = new InspectionRecipeSerializer(codec);
        var recipe = serializer.Deserialize(node.RecipeJson);
        string recipeHash = Hash(Encoding.UTF8.GetBytes(serializer.Serialize(recipe)));
        string temporary = Path.Combine(Path.GetTempPath(), "workflow-label-" + Guid.NewGuid().ToString("N"));
        LabelInspectionHost? host = null;
        InspectionEngine? engine = null;
        ImageFrame? reference = null;
        try
        {
            Directory.CreateDirectory(temporary);
            var identities = new List<string> { "recipe:" + recipeHash };
            string? SnapshotModel(string path, string key)
            {
                if (string.IsNullOrWhiteSpace(path)) return null;
                var bytes = ReadBounded(ResolvePath(root, path), 256 * 1024 * 1024, token);
                var captured = Path.Combine(temporary, key + ".onnx");
                File.WriteAllBytes(captured, bytes);
                identities.Add(key + ":" + Hash(bytes));
                return captured;
            }
            var options = new LabelInspectionHostOptions(ResolvePath(root, node.DataDirectory))
            {
                RecognitionModel = SnapshotModel(node.RecognitionModelPath, "ocr"),
                AnomalyBackbone = SnapshotModel(node.AnomalyBackbonePath, "backbone"),
                MaximumParallelRois = node.MaximumParallelRois
            };
            host = LabelInspectionHost.Create(options);
            var repositories = new SnapshotRepositories();
            foreach (var region in recipe.Regions)
            {
                token.ThrowIfCancellationRequested();
                // 停用分支不加载，缺失活动引用则在首节点前明确失败。
                if (!editing && region.Tasks.CheckQuality && region.Field.LibraryId is { } id)
                {
                    var key = (id, LibraryRevision: region.Field.LibraryRevision ?? throw new InvalidDataException("字库引用缺少修订。"));
                    if (!repositories.Glyphs.ContainsKey(key))
                    {
                        var library = host.Store.Load(key.id, key.LibraryRevision);
                        repositories.Glyphs.Add(key, library);
                        identities.Add($"glyph:{library.Id}:r{library.Revision}:" + string.Join(",", library.Glyphs.OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => g.Key + ":" + g.Value.Sha256)));
                    }
                }
                if (!editing && region.Tasks.DetectAnomaly && region.Anomaly is { } binding)
                {
                    var key = (binding.LibraryId, binding.LibraryRevision);
                    if (!repositories.Anomalies.ContainsKey(key))
                    {
                        var library = host.Store.AnomalyLibraries.LoadAnomalyLibrary(key.LibraryId, key.LibraryRevision);
                        repositories.Anomalies.Add(key, library);
                        identities.Add($"anomaly:{library.Id}:r{library.Revision}:" + string.Join(",", library.Models.OrderBy(m => m.Key, StringComparer.Ordinal).Select(m => m.Key + ":" + m.Value.Sha256)));
                    }
                }
            }
            if (recipe.Mode == EInspectionMode.Template)
            {
                if (string.IsNullOrWhiteSpace(node.ReferenceImagePath)) throw new InvalidDataException("模板配方缺少参考图路径。");
                var bytes = ReadBounded(ResolvePath(root, node.ReferenceImagePath), 64 * 1024 * 1024, token);
                string hash = Hash(bytes);
                var pixels = codec.Decode(bytes);
                if (pixels.Width != recipe.Width || pixels.Height != recipe.Height) throw new InvalidDataException("参考图与标签配方尺寸不一致。");
                reference = FromSnapshot(pixels, "label-reference:" + hash);
                identities.Add("reference:" + hash);
            }
            engine = editing ? host.CreateEngine() : host.CreateEngineFromRepositories(repositories, repositories);
            token.ThrowIfCancellationRequested();
            return new WorkflowLabelInspectionResources(temporary, host, engine, recipe, reference, recipeHash,
                Hash(Encoding.UTF8.GetBytes(string.Join("\n", identities.OrderBy(i => i, StringComparer.Ordinal)))));
        }
        catch
        {
            engine?.Dispose(); reference?.Dispose(); host?.Dispose();
            DeleteTemporary(temporary);
            throw;
        }
    }

    /// <summary>
    /// 复用判断用的配置指纹：节点资源配置、解析后的根目录，以及模型/参考图文件的大小和修改时间。
    /// 不读取文件内容；任何一项变化都会使下一轮重新加载。无法计算（例如路径越界）时返回空，交给加载过程报告错误。
    /// 字库/异常库按配方中的库ID与修订引用，已发布修订不可变，因此不单独计入。
    /// </summary>
    /// <param name="node">节点配置。</param><param name="baseDirectory">流程目录。</param>
    public static string? Fingerprint(InspectLabelNodeModel node, string baseDirectory)
    {
        ArgumentNullException.ThrowIfNull(node);
        try
        {
            var root = Path.GetFullPath(node.ResourceRoot, Path.GetFullPath(baseDirectory));
            string Stamp(string path)
            {
                if (string.IsNullOrWhiteSpace(path)) return "-";
                var info = new FileInfo(ResolvePath(root, path));
                return info.Exists ? $"{info.FullName}|{info.Length}|{info.LastWriteTimeUtc.Ticks}" : info.FullName + "|missing";
            }
            var text = string.Join("\n", root, ResolvePath(root, node.DataDirectory), node.MaximumParallelRois.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Stamp(node.ReferenceImagePath), Stamp(node.RecognitionModelPath), Stamp(node.AnomalyBackbonePath), node.RecipeJson);
            return Hash(Encoding.UTF8.GetBytes(text));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or InvalidDataException)
        { return null; }
    }

    /// <summary>在显式根内解析资源，不提供开发目录兜底，也不接受子目录重解析点。</summary>
    public static string ResolvePath(string root, string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("资源路径为空。", nameof(path));
        root = Path.GetFullPath(root);
        string resolved = Path.GetFullPath(path, root);
        string prefix = Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar;
        if (!resolved.Equals(root, StringComparison.OrdinalIgnoreCase) && !resolved.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("标签资源路径超出显式资源根目录：" + path);
        var relative = Path.GetRelativePath(root, resolved);
        var current = root;
        foreach (var segment in relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            if (segment == ".") continue;
            current = Path.Combine(current, segment);
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("标签资源不允许通过子目录链接越界：" + path);
        }
        return resolved;
    }

    /// <summary>作者样张的有界加载，不把它作为生产输入。</summary>
    public static Task<ImageFrame> ReadImageAsync(string root, string path, CancellationToken token = default) => Task.Run(() =>
    {
        var bytes = ReadBounded(ResolvePath(root, path), 64 * 1024 * 1024, token);
        var pixels = new OpenCvImageCodec().Decode(bytes);
        token.ThrowIfCancellationRequested();
        return FromSnapshot(pixels, "label-author:" + Hash(bytes));
    }, token);

    private static ImageFrame FromSnapshot(PixelSnapshot pixels, string id)
    {
        using var image = VisionImage.CopyFrom(new ImageInfo(pixels.Width, pixels.Height,
            pixels.Format == EImagePixelFormat.Gray8 ? EPixelLayout.Gray8 : EPixelLayout.Bgr24), pixels.CopyPixels());
        return new ImageFrame(id, image);
    }
    private static byte[] ReadBounded(string path, int maximum, CancellationToken token)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length is < 1 || stream.Length > maximum) throw new InvalidDataException("标签资源文件超出预算：" + path);
        var bytes = new byte[checked((int)stream.Length)];
        int offset = 0;
        while (offset < bytes.Length)
        {
            token.ThrowIfCancellationRequested();
            int count = stream.Read(bytes, offset, Math.Min(65536, bytes.Length - offset));
            if (count == 0) throw new EndOfStreamException(path);
            offset += count;
        }
        return bytes;
    }
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    /// <summary>必须在所有原生调用完成后释放；运行服务与页面负责等待。</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { Engine.Dispose(); }
        finally
        {
            Reference?.Dispose(); Host.Dispose();
            DeleteTemporary(_temporary);
        }
    }

    // 临时模型副本只是快照：删除失败（例如文件仍被占用）不能让资源释放或运行收尾失败，只记录。
    private static void DeleteTemporary(string directory)
    {
        try { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { System.Diagnostics.Trace.TraceWarning($"标签临时模型目录删除失败：{directory}：{error.Message}"); }
    }

    private sealed class SnapshotRepositories : IGlyphLibraryRepository, IAnomalyLibraryRepository
    {
        internal Dictionary<(string, int), GlyphLibrarySnapshot> Glyphs { get; } = new();
        internal Dictionary<(string, int), AnomalyLibrarySnapshot> Anomalies { get; } = new();
        public GlyphLibrarySnapshot Load(string id, int revision) => Glyphs.TryGetValue((id, revision), out var result) ? result : throw new InvalidDataException($"本轮未捕获字库{id}/r{revision}。");
        public AnomalyLibrarySnapshot LoadAnomalyLibrary(string id, int revision) => Anomalies.TryGetValue((id, revision), out var result) ? result : throw new InvalidDataException($"本轮未捕获异常库{id}/r{revision}。");
    }
}
