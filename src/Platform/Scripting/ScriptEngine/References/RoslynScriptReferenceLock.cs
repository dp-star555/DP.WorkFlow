using System.Xml;
using System.Xml.Linq;

namespace ScriptEngine;

/// <summary>锁定文件中的一个托管脚本引用。</summary>
public sealed record RoslynScriptReferenceLockEntry(
    string Path,
    string AssemblyName,
    string? Version,
    string Architecture,
    string Sha256,
    IReadOnlyList<string> Dependencies);

/// <summary>引用锁定验证问题。</summary>
public sealed record RoslynScriptReferenceLockIssue(
    string Code,
    string Path,
    string Message,
    bool IsError);

/// <summary>引用锁定验证结果。</summary>
public sealed record RoslynScriptReferenceLockValidation(
    IReadOnlyList<string> ResolvedPaths,
    IReadOnlyList<RoslynScriptReferenceLockIssue> Issues)
{
    /// <summary>全部文件是否存在且身份、内容和架构与锁定信息一致。</summary>
    public bool IsValid => Issues.All(item => !item.IsError);
}

/// <summary>
/// 可移植的脚本 DLL 锁定清单。记录程序集身份、SHA256、架构和直接依赖，
/// 用于部署前验证外部引用未被静默替换。
/// </summary>
public sealed class RoslynScriptReferenceLock
{
    /// <summary>锁定格式版本。</summary>
    public int FormatVersion { get; init; } = 1;

    /// <summary>按程序集名称排序的锁定引用。</summary>
    public IReadOnlyList<RoslynScriptReferenceLockEntry> References { get; init; } = Array.Empty<RoslynScriptReferenceLockEntry>();

    /// <summary>从 DLL 路径创建锁定清单；位于基准目录内的文件保存为相对路径。</summary>
    public static RoslynScriptReferenceLock Create(IEnumerable<string> paths, string baseDirectory)
    {
        if (paths is null) throw new ArgumentNullException(nameof(paths));
        var basePath = NormalizeDirectory(baseDirectory);
        var entries = paths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path =>
            {
                var info = RoslynScriptReferenceInspector.Inspect(path);
                return new RoslynScriptReferenceLockEntry(
                    MakePortablePath(path, basePath),
                    info.AssemblyName,
                    info.Version?.ToString(),
                    info.Architecture,
                    info.Sha256,
                    info.Dependencies);
            })
            .OrderBy(item => item.AssemblyName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var duplicate = entries.GroupBy(item => item.AssemblyName, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
            throw new InvalidOperationException($"不能锁定同名程序集：{duplicate.Key}。");
        return new RoslynScriptReferenceLock { References = entries };
    }

    /// <summary>验证当前磁盘文件是否仍与锁定清单一致。</summary>
    public RoslynScriptReferenceLockValidation Validate(string baseDirectory)
    {
        var basePath = NormalizeDirectory(baseDirectory);
        var paths = new List<string>();
        var issues = new List<RoslynScriptReferenceLockIssue>();
        foreach (var entry in References)
        {
            var path = ResolvePath(entry.Path, basePath);
            if (!File.Exists(path))
            {
                issues.Add(new RoslynScriptReferenceLockIssue("SRL0001", path, "锁定的脚本引用不存在。", true));
                continue;
            }
            try
            {
                var current = RoslynScriptReferenceInspector.Inspect(path);
                paths.Add(path);
                if (!string.Equals(current.AssemblyName, entry.AssemblyName, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(current.Version?.ToString(), entry.Version, StringComparison.OrdinalIgnoreCase))
                    issues.Add(new RoslynScriptReferenceLockIssue(
                        "SRL0002", path,
                        $"程序集身份已变化：锁定 {entry.AssemblyName} {entry.Version}，当前 {current.AssemblyName} {current.Version}。",
                        true));
                if (!string.Equals(current.Sha256, entry.Sha256, StringComparison.OrdinalIgnoreCase))
                    issues.Add(new RoslynScriptReferenceLockIssue("SRL0003", path, "引用文件 SHA256 与锁定值不一致。", true));
                if (!string.Equals(current.Architecture, entry.Architecture, StringComparison.OrdinalIgnoreCase))
                    issues.Add(new RoslynScriptReferenceLockIssue(
                        "SRL0004", path,
                        $"引用架构已变化：锁定 {entry.Architecture}，当前 {current.Architecture}。",
                        true));
                var missingDependencies = entry.Dependencies.Except(current.Dependencies, StringComparer.OrdinalIgnoreCase).ToArray();
                if (missingDependencies.Length > 0)
                    issues.Add(new RoslynScriptReferenceLockIssue(
                        "SRL0005", path,
                        "程序集依赖清单已变化：" + string.Join(", ", missingDependencies),
                        true));
            }
            catch (Exception exception) when (exception is IOException or BadImageFormatException)
            {
                issues.Add(new RoslynScriptReferenceLockIssue("SRL0006", path, exception.Message, true));
            }
        }
        return new RoslynScriptReferenceLockValidation(paths, issues);
    }

    /// <summary>将锁定清单保存为确定性 XML。</summary>
    public void Save(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("锁定文件路径不能为空。", nameof(path));
        var document = new XDocument(
            new XElement("ScriptReferenceLock",
                new XAttribute("Version", FormatVersion),
                References.OrderBy(item => item.AssemblyName, StringComparer.OrdinalIgnoreCase).Select(entry =>
                    new XElement("Reference",
                        new XAttribute("Path", entry.Path),
                        new XAttribute("Assembly", entry.AssemblyName),
                        new XAttribute("Version", entry.Version ?? string.Empty),
                        new XAttribute("Architecture", entry.Architecture),
                        new XAttribute("Sha256", entry.Sha256),
                        entry.Dependencies.OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
                            .Select(dependency => new XElement("Dependency", new XAttribute("Name", dependency)))))));
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath) ?? Environment.CurrentDirectory);
        using var writer = XmlWriter.Create(fullPath, new XmlWriterSettings
        {
            Encoding = new System.Text.UTF8Encoding(false),
            Indent = true,
            NewLineChars = "\n"
        });
        document.Save(writer);
    }

    /// <summary>从 XML 读取引用锁定清单。</summary>
    public static RoslynScriptReferenceLock Load(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("锁定文件路径不能为空。", nameof(path));
        using var reader = XmlReader.Create(Path.GetFullPath(path), new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null
        });
        var root = XDocument.Load(reader).Root
                   ?? throw new FormatException("脚本引用锁定文件没有根元素。");
        if (!string.Equals(root.Name.LocalName, "ScriptReferenceLock", StringComparison.Ordinal))
            throw new FormatException("不是有效的脚本引用锁定文件。");
        var version = (int?)root.Attribute("Version") ?? 0;
        if (version != 1) throw new NotSupportedException($"不支持脚本引用锁定格式版本 {version}。");
        var entries = root.Elements("Reference").Select(element => new RoslynScriptReferenceLockEntry(
            RequiredAttribute(element, "Path"),
            RequiredAttribute(element, "Assembly"),
            (string?)element.Attribute("Version"),
            RequiredAttribute(element, "Architecture"),
            RequiredAttribute(element, "Sha256"),
            element.Elements("Dependency")
                .Select(dependency => RequiredAttribute(dependency, "Name"))
                .ToArray())).ToArray();
        return new RoslynScriptReferenceLock { FormatVersion = version, References = entries };
    }

    private static string RequiredAttribute(XElement element, string name) =>
        (string?)element.Attribute(name) is { Length: > 0 } value
            ? value
            : throw new FormatException($"锁定文件元素 {element.Name} 缺少属性 {name}。");

    private static string NormalizeDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("基准目录不能为空。", nameof(path));
        return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
               + Path.DirectorySeparatorChar;
    }

    private static string MakePortablePath(string path, string baseDirectory)
    {
        var fullPath = Path.GetFullPath(path);
        if (!fullPath.StartsWith(baseDirectory, StringComparison.OrdinalIgnoreCase)) return fullPath;
        return fullPath.Substring(baseDirectory.Length).Replace(Path.DirectorySeparatorChar, '/');
    }

    private static string ResolvePath(string path, string baseDirectory) => Path.GetFullPath(
        Path.IsPathRooted(path)
            ? path
            : Path.Combine(baseDirectory, path.Replace('/', Path.DirectorySeparatorChar)));
}
