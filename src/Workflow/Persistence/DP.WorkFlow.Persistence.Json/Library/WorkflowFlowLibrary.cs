using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DP.WorkFlow.Persistence.Json;

/// <summary>流程库里的一个流程文件。</summary>
/// <param name="Name">库里的名字，即文件名（不含扩展名）。</param>
/// <param name="FilePath">文件路径；已删除的流程为空。</param>
/// <param name="FlowName">文件里写的流程名；读不出时为空。</param>
/// <param name="NodeCount">根画布节点数。</param>
/// <param name="ConnectionCount">根画布连线数。</param>
/// <param name="ModifiedUtc">文件最后修改时间；已删除的为最后一个版本的保存时间。</param>
/// <param name="Version">最新版本号；从没记过版本时为 0。</param>
/// <param name="ModifiedBy">最新版本的保存人；不知道时为空。</param>
/// <param name="Error">读文件出的错；没错时为空。</param>
/// <param name="IsDeleted">是否已删除（文件没了，版本还在）。</param>
public sealed record WorkflowLibraryEntry(
    string Name,
    string? FilePath,
    string? FlowName,
    int NodeCount,
    int ConnectionCount,
    DateTime ModifiedUtc,
    int Version,
    string? ModifiedBy,
    string? Error,
    bool IsDeleted = false);

/// <summary>流程的一个历史版本。</summary>
/// <param name="Version">版本号，从 1 开始。</param>
/// <param name="SavedUtc">保存时间。</param>
/// <param name="SavedBy">保存人；不知道时为空。</param>
/// <param name="Note">说明，例如“删除前”“恢复到 v3 前”；没有时为空。</param>
/// <param name="FilePath">版本文件路径。</param>
public sealed record WorkflowLibraryVersion(int Version, DateTime SavedUtc, string? SavedBy, string? Note, string FilePath);

/// <summary>两个流程文件的差别（只比根画布；复合节点的子画布变了算这个节点参数变了）。</summary>
/// <param name="AddedNodes">新增的节点（标题）。</param>
/// <param name="RemovedNodes">删掉的节点（标题）。</param>
/// <param name="ChangedNodes">类型、标题、参数或子画布变了的节点（标题）。</param>
/// <param name="MovedNodes">只改了位置或大小的节点（标题）。</param>
/// <param name="AddedConnections">新增的连线（“起点.端口 → 终点”）。</param>
/// <param name="RemovedConnections">删掉的连线。</param>
/// <param name="OtherChanges">流程名、入口、公共数据清单等其他变化。</param>
public sealed record WorkflowFlowComparison(
    IReadOnlyList<string> AddedNodes,
    IReadOnlyList<string> RemovedNodes,
    IReadOnlyList<string> ChangedNodes,
    IReadOnlyList<string> MovedNodes,
    IReadOnlyList<string> AddedConnections,
    IReadOnlyList<string> RemovedConnections,
    IReadOnlyList<string> OtherChanges)
{
    /// <summary>是否完全一样。</summary>
    public bool IsSame => AddedNodes.Count + RemovedNodes.Count + ChangedNodes.Count + MovedNodes.Count
        + AddedConnections.Count + RemovedConnections.Count + OtherChanges.Count == 0;
}

/// <summary>
/// 流程库：一个目录里的流程文件（一个文件一个流程），加上每个流程的历史版本。
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>流程文件放在 <see cref="Directory"/> 下，文件名就是库里的名字；文件里的流程名跟着改成同名，保证流程管理里不重名。</item>
/// <item>历史版本放在 <c>.history\名字\</c> 下：<c>v0001.json</c>… 和 <c>versions.json</c>（版本号、时间、保存人、说明）。
/// 每次保存后调用 <see cref="RecordVersion"/> 记一版；内容和上一版一样时不重复记。</item>
/// <item>删除只删文件，历史版本留着，可以从历史版本恢复；改名时历史版本跟着走。</item>
/// <item>只按 JSON 文本操作，不需要节点目录，插件没加载的流程也能管理。</item>
/// </list>
/// 不加锁：同一时间只应有一个界面在改同一个库。
/// </remarks>
public sealed class WorkflowFlowLibrary
{
    private const string Extension = ".json";
    private const string IndexFile = "versions.json";
    private static readonly JsonSerializerOptions IndexOptions = new() { WriteIndented = true };
    private readonly Func<DateTime> _utcNow;

    /// <summary>创建流程库。</summary>
    /// <param name="directory">流程文件目录。</param>
    /// <param name="utcNow">取当前时间（测试用）；为空时用系统时间。</param>
    public WorkflowFlowLibrary(string directory, Func<DateTime>? utcNow = null)
    {
        if (string.IsNullOrWhiteSpace(directory))
            throw new ArgumentException("目录不能为空。", nameof(directory));
        Directory = Path.GetFullPath(directory);
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
    }

    /// <summary>流程文件目录。</summary>
    public string Directory { get; }

    /// <summary>历史版本目录：<c>.history\</c>。</summary>
    public string HistoryDirectory => Path.Combine(Directory, ".history");

    /// <summary>名字对应的文件路径（不管存不存在）。</summary>
    /// <param name="name">库里的名字。</param>
    public string PathOf(string name) => Path.Combine(Directory, ValidateName(name) + Extension);

    /// <summary>文件路径对应的库里的名字；不在本库目录下时为空。</summary>
    /// <param name="filePath">文件路径。</param>
    public string? NameOf(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return null;
        var full = Path.GetFullPath(filePath);
        return string.Equals(Path.GetDirectoryName(full), Directory, StringComparison.OrdinalIgnoreCase)
               && string.Equals(Path.GetExtension(full), Extension, StringComparison.OrdinalIgnoreCase)
            ? Path.GetFileNameWithoutExtension(full)
            : null;
    }

    /// <summary>列出流程，按名字排序。</summary>
    /// <param name="includeDeleted">是否带上已删除（只剩历史版本）的流程。</param>
    public IReadOnlyList<WorkflowLibraryEntry> List(bool includeDeleted = false)
    {
        var entries = new List<WorkflowLibraryEntry>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (System.IO.Directory.Exists(Directory))
        {
            foreach (var file in System.IO.Directory.GetFiles(Directory, "*" + Extension))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                names.Add(name);
                entries.Add(Describe(name, file));
            }
        }
        if (includeDeleted && System.IO.Directory.Exists(HistoryDirectory))
        {
            foreach (var folder in System.IO.Directory.GetDirectories(HistoryDirectory))
            {
                var name = Path.GetFileName(folder);
                if (names.Contains(name))
                    continue;
                var versions = GetVersions(name);
                if (versions.Count == 0)
                    continue;
                var last = versions[versions.Count - 1];
                var (flowName, nodes, connections, error) = Summarize(last.FilePath);
                entries.Add(new WorkflowLibraryEntry(name, null, flowName, nodes, connections, last.SavedUtc, last.Version, last.SavedBy, error, true));
            }
        }
        return entries.OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    /// <summary>不重名的新名字：<paramref name="baseName"/>，有了就加 2、3…（已删除流程的名字也避开）。</summary>
    /// <param name="baseName">基础名字。</param>
    public string UniqueName(string baseName)
    {
        ValidateName(baseName);
        var name = baseName;
        for (var number = 2; Exists(name); number++)
            name = baseName + number.ToString(CultureInfo.InvariantCulture);
        return name;
    }

    /// <summary>名字是否已被占用（有文件，或有历史版本）。</summary>
    /// <param name="name">库里的名字。</param>
    public bool Exists(string name) =>
        File.Exists(PathOf(name)) || System.IO.Directory.Exists(Path.Combine(HistoryDirectory, ValidateName(name)));

    /// <summary>记一个版本：把当前文件复制成新版本。内容和上一版一样时不记，返回上一版。</summary>
    /// <param name="name">库里的名字。</param>
    /// <param name="savedBy">保存人。</param>
    /// <param name="note">说明。</param>
    /// <returns>记下（或已有）的版本。</returns>
    /// <exception cref="FileNotFoundException">文件不存在。</exception>
    public WorkflowLibraryVersion RecordVersion(string name, string? savedBy = null, string? note = null)
    {
        var path = PathOf(name);
        if (!File.Exists(path))
            throw new FileNotFoundException($"流程 {name} 不存在。", path);
        var content = File.ReadAllText(path);
        var versions = ReadIndex(name);
        if (versions.Count > 0)
        {
            var last = versions[versions.Count - 1];
            var lastPath = VersionPath(name, last.Version);
            if (File.Exists(lastPath) && File.ReadAllText(lastPath) == content)
                return ToVersion(name, last);
        }
        var record = new VersionRecord
        {
            Version = versions.Count == 0 ? 1 : versions.Max(item => item.Version) + 1,
            SavedUtc = _utcNow(),
            SavedBy = string.IsNullOrWhiteSpace(savedBy) ? null : savedBy!.Trim(),
            Note = string.IsNullOrWhiteSpace(note) ? null : note!.Trim()
        };
        var target = VersionPath(name, record.Version);
        System.IO.Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.WriteAllText(target, content);
        versions.Add(record);
        WriteIndex(name, versions);
        return ToVersion(name, record);
    }

    /// <summary>流程的历史版本，从旧到新。</summary>
    /// <param name="name">库里的名字。</param>
    public IReadOnlyList<WorkflowLibraryVersion> GetVersions(string name) =>
        ReadIndex(name).Where(record => File.Exists(VersionPath(name, record.Version))).Select(record => ToVersion(name, record)).ToArray();

    /// <summary>复制一个流程，新流程的流程名改成新名字。</summary>
    /// <param name="name">原名字。</param>
    /// <param name="newName">新名字；为空时取“原名字-副本”加编号。</param>
    /// <returns>新名字。</returns>
    public string Duplicate(string name, string? newName = null)
    {
        var source = ExistingFile(name);
        var target = string.IsNullOrWhiteSpace(newName) ? UniqueName(name + "-副本") : RequireFree(newName!);
        WriteWithFlowName(File.ReadAllText(source), PathOf(target), target);
        return target;
    }

    /// <summary>改名：文件、流程名和历史版本一起改。</summary>
    /// <param name="name">原名字。</param>
    /// <param name="newName">新名字。</param>
    public void Rename(string name, string newName)
    {
        var source = ExistingFile(name);
        if (string.Equals(name, newName, StringComparison.Ordinal))
            return;
        // 只改大小写时目标“已存在”的就是自己。
        if (!string.Equals(name, newName, StringComparison.OrdinalIgnoreCase))
            RequireFree(newName);
        var target = PathOf(newName);
        var temporary = target + ".tmp";
        WriteWithFlowName(File.ReadAllText(source), temporary, newName);
        File.Delete(source);
        File.Move(temporary, target);
        var history = Path.Combine(HistoryDirectory, name);
        if (System.IO.Directory.Exists(history))
        {
            var moved = Path.Combine(HistoryDirectory, newName);
            if (string.Equals(name, newName, StringComparison.OrdinalIgnoreCase))
            {
                var step = history + ".rename";
                System.IO.Directory.Move(history, step);
                System.IO.Directory.Move(step, moved);
            }
            else
                System.IO.Directory.Move(history, moved);
        }
    }

    /// <summary>删除：先记一版（说明“删除前”），再删文件；历史版本留着。</summary>
    /// <param name="name">库里的名字。</param>
    /// <param name="deletedBy">操作人。</param>
    public void Delete(string name, string? deletedBy = null)
    {
        var path = ExistingFile(name);
        RecordVersion(name, deletedBy, "删除前");
        File.Delete(path);
    }

    /// <summary>导入一个外部流程文件：检查是 JSON 流程，复制进库，流程名改成库里的名字。</summary>
    /// <param name="sourcePath">外部文件。</param>
    /// <param name="name">库里的名字；为空时取文件名，重名时加编号。</param>
    /// <returns>库里的名字。</returns>
    /// <exception cref="InvalidDataException">不是流程文件。</exception>
    public string Import(string sourcePath, string? name = null)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
            throw new ArgumentException("文件路径不能为空。", nameof(sourcePath));
        var content = File.ReadAllText(sourcePath);
        var target = string.IsNullOrWhiteSpace(name) ? UniqueName(Path.GetFileNameWithoutExtension(sourcePath)) : RequireFree(name!);
        System.IO.Directory.CreateDirectory(Directory);
        WriteWithFlowName(content, PathOf(target), target);
        return target;
    }

    /// <summary>导出：把流程文件复制到外部路径。</summary>
    /// <param name="name">库里的名字。</param>
    /// <param name="targetPath">目标文件。</param>
    public void Export(string name, string targetPath)
    {
        if (string.IsNullOrWhiteSpace(targetPath))
            throw new ArgumentException("文件路径不能为空。", nameof(targetPath));
        File.Copy(ExistingFile(name), targetPath, true);
    }

    /// <summary>恢复到某个版本：当前文件还在时先记一版（说明“恢复到 vN 前”），再用那个版本覆盖；已删除的流程直接恢复出文件。</summary>
    /// <param name="name">库里的名字。</param>
    /// <param name="version">版本号。</param>
    /// <param name="restoredBy">操作人。</param>
    public void Restore(string name, int version, string? restoredBy = null)
    {
        var source = VersionPath(name, version);
        if (!File.Exists(source))
            throw new FileNotFoundException($"流程 {name} 没有版本 v{version}。", source);
        var path = PathOf(name);
        if (File.Exists(path))
            RecordVersion(name, restoredBy, $"恢复到 v{version} 前");
        System.IO.Directory.CreateDirectory(Directory);
        WriteWithFlowName(File.ReadAllText(source), path, name);
        RecordVersion(name, restoredBy, $"从 v{version} 恢复");
    }

    /// <summary>读一个版本的内容。</summary>
    /// <param name="name">库里的名字。</param>
    /// <param name="version">版本号；0 表示当前文件。</param>
    public string ReadContent(string name, int version = 0)
    {
        var path = version == 0 ? ExistingFile(name) : VersionPath(name, version);
        if (!File.Exists(path))
            throw new FileNotFoundException($"流程 {name} 没有版本 v{version}。", path);
        return File.ReadAllText(path);
    }

    /// <summary>比较两份流程 JSON（左边是旧的，右边是新的）。</summary>
    /// <param name="older">旧的。</param>
    /// <param name="newer">新的。</param>
    public static WorkflowFlowComparison Compare(string older, string newer)
    {
        var left = ParseObject(older);
        var right = ParseObject(newer);
        var leftNodes = Nodes(left);
        var rightNodes = Nodes(right);
        var added = new List<string>();
        var removed = new List<string>();
        var changed = new List<string>();
        var moved = new List<string>();
        foreach (var pair in rightNodes)
        {
            if (!leftNodes.TryGetValue(pair.Key, out var before))
            {
                added.Add(Title(pair.Value, pair.Key));
                continue;
            }
            if (!SameExcept(before, pair.Value, LayoutKeys))
                changed.Add(Title(pair.Value, pair.Key));
            else if (!SameExcept(before, pair.Value, Array.Empty<string>()))
                moved.Add(Title(pair.Value, pair.Key));
        }
        foreach (var pair in leftNodes)
            if (!rightNodes.ContainsKey(pair.Key))
                removed.Add(Title(pair.Value, pair.Key));
        var leftLinks = Connections(left, leftNodes);
        var rightLinks = Connections(right, rightNodes);
        var other = new List<string>();
        if (Text(left, "Name") != Text(right, "Name"))
            other.Add($"流程名：{Text(left, "Name")} → {Text(right, "Name")}");
        if (Text(left, "EntryNodeId") != Text(right, "EntryNodeId"))
            other.Add("入口节点变了");
        if (!JsonNode.DeepEquals(Property(left, "PublicData"), Property(right, "PublicData")))
            other.Add("公共数据清单变了");
        return new WorkflowFlowComparison(added, removed, changed, moved,
            rightLinks.Except(leftLinks, StringComparer.Ordinal).ToArray(),
            leftLinks.Except(rightLinks, StringComparer.Ordinal).ToArray(),
            other);
    }

    private static readonly string[] LayoutKeys = { "X", "Y", "Width", "Height", "PortSides" };

    private WorkflowLibraryEntry Describe(string name, string file)
    {
        var versions = GetVersions(name);
        var last = versions.Count == 0 ? null : versions[versions.Count - 1];
        var (flowName, nodes, connections, error) = Summarize(file);
        return new WorkflowLibraryEntry(name, file, flowName, nodes, connections, File.GetLastWriteTimeUtc(file),
            last?.Version ?? 0, last?.SavedBy, error);
    }

    private static (string? FlowName, int Nodes, int Connections, string? Error) Summarize(string file)
    {
        try
        {
            var root = ParseObject(File.ReadAllText(file));
            return (Text(root, "Name"), (Property(root, "Nodes") as JsonArray)?.Count ?? 0, (Property(root, "Connections") as JsonArray)?.Count ?? 0, null);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            return (null, 0, 0, exception.Message);
        }
    }

    private string ExistingFile(string name)
    {
        var path = PathOf(name);
        if (!File.Exists(path))
            throw new FileNotFoundException($"流程 {name} 不存在。", path);
        return path;
    }

    private string RequireFree(string name)
    {
        if (Exists(name))
            throw new InvalidOperationException($"已经有叫 {name} 的流程（或它的历史版本）。");
        return name;
    }

    private static void WriteWithFlowName(string content, string path, string flowName)
    {
        var root = ParseObject(content);
        if (Property(root, "Nodes") is not JsonArray)
            throw new InvalidDataException("不是流程文件：没有 Nodes。");
        var key = root.Select(pair => pair.Key).FirstOrDefault(item => string.Equals(item, "Name", StringComparison.OrdinalIgnoreCase)) ?? "Name";
        root[key] = flowName;
        File.WriteAllText(path, root.ToJsonString(IndexOptions));
    }

    private static string ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("名字不能为空。", nameof(name));
        var trimmed = name.Trim();
        if (trimmed != name || trimmed.StartsWith(".", StringComparison.Ordinal) || trimmed.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || trimmed.IndexOfAny(new[] { '/', '\\', ':', '*', '?', '"', '<', '>', '|' }) >= 0)
            throw new ArgumentException($"名字 {name} 不能用：不能有前后空格、不能以点开头、不能有 \\ / : * ? \" < > |。", nameof(name));
        return trimmed;
    }

    private string VersionPath(string name, int version) =>
        Path.Combine(HistoryDirectory, ValidateName(name), "v" + version.ToString("0000", CultureInfo.InvariantCulture) + Extension);

    private List<VersionRecord> ReadIndex(string name)
    {
        var path = Path.Combine(HistoryDirectory, ValidateName(name), IndexFile);
        if (!File.Exists(path))
            return new List<VersionRecord>();
        try
        {
            return JsonSerializer.Deserialize<List<VersionRecord>>(File.ReadAllText(path)) ?? new List<VersionRecord>();
        }
        catch (JsonException)
        {
            // 索引坏了时按版本文件重建（保存人和说明丢失）。
            return System.IO.Directory.GetFiles(Path.GetDirectoryName(path)!, "v*" + Extension)
                .Select(file => (File: file, Ok: int.TryParse(Path.GetFileNameWithoutExtension(file).Substring(1), NumberStyles.None, CultureInfo.InvariantCulture, out var number), Number: number))
                .Where(item => item.Ok)
                .OrderBy(item => item.Number)
                .Select(item => new VersionRecord { Version = item.Number, SavedUtc = File.GetLastWriteTimeUtc(item.File) })
                .ToList();
        }
    }

    private void WriteIndex(string name, List<VersionRecord> versions)
    {
        var path = Path.Combine(HistoryDirectory, ValidateName(name), IndexFile);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(versions, IndexOptions));
        if (File.Exists(path))
            File.Replace(temporary, path, null);
        else
            File.Move(temporary, path);
    }

    private WorkflowLibraryVersion ToVersion(string name, VersionRecord record) =>
        new(record.Version, record.SavedUtc, record.SavedBy, record.Note, VersionPath(name, record.Version));

    private static JsonObject ParseObject(string json) =>
        JsonNode.Parse(json) as JsonObject ?? throw new InvalidDataException("不是流程文件：根不是对象。");

    private static JsonNode? Property(JsonObject node, string name)
    {
        foreach (var pair in node)
            if (string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase))
                return pair.Value;
        return null;
    }

    private static string? Text(JsonObject node, string name) =>
        Property(node, name) is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static Dictionary<string, JsonObject> Nodes(JsonObject root)
    {
        var nodes = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        if (Property(root, "Nodes") is JsonArray array)
            foreach (var item in array.OfType<JsonObject>())
                if (Text(item, "Id") is { Length: > 0 } id)
                    nodes[id] = item;
        return nodes;
    }

    private static string Title(JsonObject node, string id) => Text(node, "Title") is { Length: > 0 } title ? title : id;

    private static HashSet<string> Connections(JsonObject root, Dictionary<string, JsonObject> nodes)
    {
        var links = new HashSet<string>(StringComparer.Ordinal);
        if (Property(root, "Connections") is JsonArray array)
            foreach (var item in array.OfType<JsonObject>())
            {
                var from = Text(item, "FromNodeId") ?? string.Empty;
                var to = Text(item, "ToNodeId") ?? string.Empty;
                links.Add($"{(nodes.TryGetValue(from, out var a) ? Title(a, from) : from)}.{Text(item, "FromPort")} → {(nodes.TryGetValue(to, out var b) ? Title(b, to) : to)}");
            }
        return links;
    }

    private static bool SameExcept(JsonObject left, JsonObject right, IReadOnlyCollection<string> ignored)
    {
        bool Kept(string key) => !ignored.Contains(key, StringComparer.OrdinalIgnoreCase);
        var leftKeys = left.Where(pair => Kept(pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        var rightKeys = right.Where(pair => Kept(pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        return leftKeys.Count == rightKeys.Count
            && leftKeys.All(pair => rightKeys.TryGetValue(pair.Key, out var other) && JsonNode.DeepEquals(pair.Value, other));
    }

    private sealed class VersionRecord
    {
        public int Version { get; set; }
        public DateTime SavedUtc { get; set; }
        public string? SavedBy { get; set; }
        public string? Note { get; set; }
    }
}
