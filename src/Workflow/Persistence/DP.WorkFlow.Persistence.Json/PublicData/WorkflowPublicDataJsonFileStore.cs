using System.Text.Json;
using System.Text.Json.Serialization;

namespace DP.WorkFlow.Persistence.Json;

/// <summary>
/// 把"保持"的公共数据存到一个 JSON 文件（R2-d）。每次保存整份重写：先写临时文件再替换，断电时文件要么是旧的要么是新的。
/// 文件不存在时视为没有保存过；文件损坏时 <see cref="Load"/> 抛出异常，由公共数据中心报告并改用默认值。
/// </summary>
public sealed class WorkflowPublicDataJsonFileStore : IWorkflowPublicDataPersistence
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly object _sync = new();
    private readonly string _path;
    private Dictionary<string, WorkflowPersistedPublicData>? _items;

    /// <summary>初始化文件存取。</summary>
    /// <param name="path">文件路径。</param>
    public WorkflowPublicDataJsonFileStore(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("文件路径不能为空。", nameof(path));
        _path = Path.GetFullPath(path);
    }

    /// <inheritdoc />
    public IReadOnlyList<WorkflowPersistedPublicData> Load()
    {
        lock (_sync)
            return EnsureLoadedLocked().Values.ToArray();
    }

    /// <inheritdoc />
    public void Save(WorkflowPersistedPublicData item)
    {
        Guard.NotNull(item);
        lock (_sync)
        {
            var items = EnsureLoadedLocked();
            items[item.Key] = item with { Values = item.Values.ToArray() };
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temporary = _path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(
                new FileContent { SchemaVersion = 1, Items = items.Values.OrderBy(value => value.Key, StringComparer.Ordinal).ToList() },
                Options));
            Polyfill.MoveFile(temporary, _path, true);
        }
    }

    private Dictionary<string, WorkflowPersistedPublicData> EnsureLoadedLocked()
    {
        if (_items is not null)
            return _items;
        var items = new Dictionary<string, WorkflowPersistedPublicData>(StringComparer.Ordinal);
        if (File.Exists(_path))
        {
            var content = JsonSerializer.Deserialize<FileContent>(File.ReadAllText(_path), Options)
                ?? throw new InvalidDataException($"保持数据文件 {_path} 为空。");
            if (content.SchemaVersion != 1)
                throw new InvalidDataException($"保持数据文件版本 {content.SchemaVersion} 不受支持。");
            foreach (var item in content.Items ?? new List<WorkflowPersistedPublicData>())
                if (item is not null && !string.IsNullOrWhiteSpace(item.Key))
                    items[item.Key] = item with { Values = item.Values ?? Array.Empty<string>() };
        }
        return _items = items;
    }

    private sealed class FileContent
    {
        public int SchemaVersion { get; set; }

        public List<WorkflowPersistedPublicData>? Items { get; set; }
    }
}
