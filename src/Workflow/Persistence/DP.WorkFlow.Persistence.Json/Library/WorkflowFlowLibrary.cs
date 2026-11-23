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
/// <param name="HasDraft">是否有没发布的修改（草稿和已发布的不一样，或者还从没发布过）。</param>
/// <param name="DraftModifiedUtc">草稿最后修改时间；没有草稿时为空。</param>
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
    bool IsDeleted = false,
    bool HasDraft = false,
    DateTime? DraftModifiedUtc = null)
{
    /// <summary>是否已发布（有正式文件，流程管理会加载它）。</summary>
    public bool IsPublished => FilePath is not null;
}

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
/// 流程库：一个目录里的流程文件（一个文件一个流程），每个流程的草稿，加上每个流程的发布历史。
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>已发布的流程文件放在 <see cref="Directory"/> 下，流程管理只加载这些；文件名就是库里的名字，文件里的流程名跟着改成同名，保证流程管理里不重名。</item>
/// <item>草稿放在 <c>.drafts\名字.json</c>：设计页编辑、保存的是草稿（<see cref="EnsureDraft"/>），不影响正在跑的流程，重启也还在。
/// <see cref="Publish"/> 把草稿写成正式文件并记一版，<see cref="DiscardDraft"/> 丢掉草稿回到已发布的版本。新建、复制、导入、恢复都先落成草稿。</item>
/// <item>历史版本放在 <c>.history\名字\</c> 下：<c>v0001.json</c>… 和 <c>versions.json</c>（版本号、时间、发布人、说明）。
/// 发布时记一版，删除前也记一版；内容和上一版一样时不重复记。</item>
/// <item>删除删掉正式文件和草稿，历史版本留着，可以从历史版本恢复；改名时草稿和历史版本跟着走。</item>
/// <item>只按 JSON 文本操作，不需要节点目录，插件没加载的流程也能管理。</item>
/// </list>
/// 不加锁：同一时间只应有一个界面在改同一个库。
/// </remarks>
public sealed class WorkflowFlowLibrary
{
    private const string Extension = ".json";
    private const string IndexFile = "versions.json";
    // 中文原样写出，打开文件能直接看懂，不转成 \uXXXX。
    private static readonly JsonSerializerOptions IndexOptions = new() { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
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

    /// <summary>草稿目录：<c>.drafts\</c>。</summary>
    public string DraftDirectory => Path.Combine(Directory, ".drafts");

    /// <summary>名字对应的草稿路径（不管存不存在）。</summary>
    /// <param name="name">库里的名字。</param>
    public string DraftPathOf(string name) => Path.Combine(DraftDirectory, ValidateName(name) + Extension);

    /// <summary>名字对应的文件路径（不管存不存在）。</summary>
    /// <param name="name">库里的名字。</param>
    public string PathOf(string name) => Path.Combine(Directory, ValidateName(name) + Extension);

    /// <summary>文件路径（正式文件或草稿）对应的库里的名字；不在本库目录或草稿目录下时为空。</summary>
    /// <param name="filePath">文件路径。</param>
    public string? NameOf(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return null;
        var full = Path.GetFullPath(filePath);
        var folder = Path.GetDirectoryName(full);
        return (string.Equals(folder, Directory, StringComparison.OrdinalIgnoreCase) || string.Equals(folder, DraftDirectory, StringComparison.OrdinalIgnoreCase))
               && string.Equals(Path.GetExtension(full), Extension, StringComparison.OrdinalIgnoreCase)
            ? Path.GetFileNameWithoutExtension(full)
            : null;
    }

    /// <summary>列出流程，按名字排序。只有草稿、从没发布过的流程也列出来（<see cref="WorkflowLibraryEntry.IsPublished"/> 为 false）。</summary>
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
        if (System.IO.Directory.Exists(DraftDirectory))
        {
            foreach (var draft in System.IO.Directory.GetFiles(DraftDirectory, "*" + Extension))
            {
                var name = Path.GetFileNameWithoutExtension(draft);
                if (!names.Add(name))
                    continue;
                var versions = GetVersions(name);
                var last = versions.Count == 0 ? null : versions[versions.Count - 1];
                var (flowName, nodes, connections, error) = Summarize(draft);
                var modified = File.GetLastWriteTimeUtc(draft);
                entries.Add(new WorkflowLibraryEntry(name, null, flowName, nodes, connections, modified, last?.Version ?? 0, last?.SavedBy, error,
                    false, true, modified));
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

    /// <summary>名字是否已被占用（有文件、草稿，或有历史版本）。</summary>
    /// <param name="name">库里的名字。</param>
    public bool Exists(string name) =>
        File.Exists(PathOf(name)) || File.Exists(DraftPathOf(name)) || System.IO.Directory.Exists(Path.Combine(HistoryDirectory, ValidateName(name)));

    /// <summary>取草稿路径用来编辑：没有草稿时先把已发布的文件复制一份。设计页打开、保存的都是这个文件。</summary>
    /// <param name="name">库里的名字。</param>
    /// <returns>草稿路径。</returns>
    /// <exception cref="FileNotFoundException">既没有草稿也没有已发布的文件。</exception>
    public string EnsureDraft(string name)
    {
        var draft = DraftPathOf(name);
        if (File.Exists(draft))
            return draft;
        var published = ExistingFile(name);
        System.IO.Directory.CreateDirectory(DraftDirectory);
        File.Copy(published, draft);
        return draft;
    }

    /// <summary>是否有没发布的修改：有草稿，而且和已发布的不一样（含只改了位置），或者从没发布过。格式差别不算。</summary>
    /// <param name="name">库里的名字。</param>
    public bool HasDraftChanges(string name)
    {
        var draft = DraftPathOf(name);
        if (!File.Exists(draft))
            return false;
        var published = PathOf(name);
        if (!File.Exists(published))
            return true;
        try
        {
            return !Compare(File.ReadAllText(published), File.ReadAllText(draft)).IsSame;
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException or InvalidOperationException)
        {
            return true;
        }
    }

    /// <summary>草稿和已发布的比较；从没发布过时左边当作空流程。</summary>
    /// <param name="name">库里的名字。</param>
    /// <exception cref="FileNotFoundException">没有草稿。</exception>
    public WorkflowFlowComparison CompareDraft(string name)
    {
        var draft = DraftPathOf(name);
        if (!File.Exists(draft))
            throw new FileNotFoundException($"流程 {name} 没有草稿。", draft);
        var published = PathOf(name);
        return Compare(File.Exists(published) ? File.ReadAllText(published) : "{\"Nodes\":[]}", File.ReadAllText(draft));
    }

    /// <summary>
    /// 发布：把草稿写成正式文件（流程名改成库里的名字），记一版（说明默认“发布”），删掉草稿。
    /// 草稿和已发布的一样时不改正式文件，只删草稿（版本不重复记）。
    /// 调用方负责先检查草稿能不能用（编译、设备检查），并确认没有流程在运行。
    /// </summary>
    /// <param name="name">库里的名字。</param>
    /// <param name="publishedBy">发布人。</param>
    /// <param name="note">说明。</param>
    /// <returns>发布后的版本。</returns>
    /// <exception cref="FileNotFoundException">没有草稿。</exception>
    public WorkflowLibraryVersion Publish(string name, string? publishedBy = null, string? note = null)
    {
        var draft = DraftPathOf(name);
        if (!File.Exists(draft))
            throw new FileNotFoundException($"流程 {name} 没有草稿。", draft);
        var path = PathOf(name);
        if (HasDraftChanges(name))
        {
            System.IO.Directory.CreateDirectory(Directory);
            var temporary = path + ".publish";
            WriteWithFlowName(File.ReadAllText(draft), temporary, name);
            if (File.Exists(path))
                File.Delete(path);
            File.Move(temporary, path);
        }
        var version = RecordVersion(name, publishedBy, string.IsNullOrWhiteSpace(note) ? "发布" : note);
        File.Delete(draft);
        return version;
    }

    /// <summary>放弃草稿，回到已发布的版本；没有草稿时什么都不做。从没发布过的流程放弃草稿后就没了（不进历史）。</summary>
    /// <param name="name">库里的名字。</param>
    public void DiscardDraft(string name)
    {
        var draft = DraftPathOf(name);
        if (File.Exists(draft))
            File.Delete(draft);
    }

    /// <summary>新建一个只有草稿的流程（没发布前流程管理不加载它）。</summary>
    /// <param name="name">库里的名字；必须没被占用。</param>
    /// <param name="content">流程 JSON。</param>
    /// <returns>草稿路径。</returns>
    public string CreateDraft(string name, string content)
    {
        RequireFree(name);
        System.IO.Directory.CreateDirectory(DraftDirectory);
        var draft = DraftPathOf(name);
        WriteWithFlowName(content, draft, name);
        return draft;
    }


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

    /// <summary>复制一个流程（有草稿时复制草稿）成新流程的草稿，流程名改成新名字；要发布后才会被加载。</summary>
    /// <param name="name">原名字。</param>
    /// <param name="newName">新名字；为空时取“原名字-副本”加编号。</param>
    /// <returns>新名字。</returns>
    public string Duplicate(string name, string? newName = null)
    {
        var source = WorkingFile(name);
        var target = string.IsNullOrWhiteSpace(newName) ? UniqueName(name + "-副本") : RequireFree(newName!);
        CreateDraft(target, File.ReadAllText(source));
        return target;
    }

    /// <summary>改名：正式文件、草稿、流程名和历史版本一起改。</summary>
    /// <param name="name">原名字。</param>
    /// <param name="newName">新名字。</param>
    public void Rename(string name, string newName)
    {
        WorkingFile(name);
        if (string.Equals(name, newName, StringComparison.Ordinal))
            return;
        // 只改大小写时目标“已存在”的就是自己。
        if (!string.Equals(name, newName, StringComparison.OrdinalIgnoreCase))
            RequireFree(newName);
        MoveWithFlowName(PathOf(name), PathOf(newName), newName);
        MoveWithFlowName(DraftPathOf(name), DraftPathOf(newName), newName);
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

    /// <summary>删除：已发布的先记一版（说明“删除前”）再删文件，草稿一起删；历史版本留着。</summary>
    /// <param name="name">库里的名字。</param>
    /// <param name="deletedBy">操作人。</param>
    public void Delete(string name, string? deletedBy = null)
    {
        WorkingFile(name);
        var path = PathOf(name);
        if (File.Exists(path))
        {
            RecordVersion(name, deletedBy, "删除前");
            File.Delete(path);
        }
        DiscardDraft(name);
    }

    /// <summary>导入一个外部流程文件：检查是 JSON 流程，复制进库成为草稿（发布后才加载），流程名改成库里的名字。</summary>
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
        CreateDraft(target, content);
        return target;
    }

    /// <summary>导出：把流程文件（有草稿时导出草稿）复制到外部路径。</summary>
    /// <param name="name">库里的名字。</param>
    /// <param name="targetPath">目标文件。</param>
    /// <param name="published">true 时只导出已发布的。</param>
    public void Export(string name, string targetPath, bool published = false)
    {
        if (string.IsNullOrWhiteSpace(targetPath))
            throw new ArgumentException("文件路径不能为空。", nameof(targetPath));
        File.Copy(published ? ExistingFile(name) : WorkingFile(name), targetPath, true);
    }

    /// <summary>恢复到某个版本：把那个版本写成草稿（覆盖现有草稿），发布后才生效。已删除的流程也这样恢复。</summary>
    /// <param name="name">库里的名字。</param>
    /// <param name="version">版本号。</param>
    /// <returns>草稿路径。</returns>
    public string Restore(string name, int version)
    {
        var source = VersionPath(name, version);
        if (!File.Exists(source))
            throw new FileNotFoundException($"流程 {name} 没有版本 v{version}。", source);
        System.IO.Directory.CreateDirectory(DraftDirectory);
        var draft = DraftPathOf(name);
        WriteWithFlowName(File.ReadAllText(source), draft, name);
        return draft;
    }

    /// <summary>读内容。</summary>
    /// <param name="name">库里的名字。</param>
    /// <param name="version">版本号；0 表示已发布的文件，-1 表示正在编辑的（有草稿用草稿，否则已发布的）。</param>
    public string ReadContent(string name, int version = 0)
    {
        var path = version switch { 0 => ExistingFile(name), -1 => WorkingFile(name), _ => VersionPath(name, version) };
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
            last?.Version ?? 0, last?.SavedBy, error, false, HasDraftChanges(name), DraftModified(name));
    }

    private DateTime? DraftModified(string name)
    {
        var draft = DraftPathOf(name);
        return File.Exists(draft) ? File.GetLastWriteTimeUtc(draft) : null;
    }

    // 正在编辑的那份：有草稿用草稿，否则用已发布的。
    private string WorkingFile(string name)
    {
        var draft = DraftPathOf(name);
        return File.Exists(draft) ? draft : ExistingFile(name);
    }

    private static void MoveWithFlowName(string source, string target, string flowName)
    {
        if (!File.Exists(source))
            return;
        var temporary = target + ".tmp";
        WriteWithFlowName(File.ReadAllText(source), temporary, flowName);
        File.Delete(source);
        File.Move(temporary, target);
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
