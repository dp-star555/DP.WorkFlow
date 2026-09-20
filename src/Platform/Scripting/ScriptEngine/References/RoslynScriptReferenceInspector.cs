using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;

namespace ScriptEngine;

/// <summary>不加载 DLL 即可获得的脚本引用元数据。</summary>
public sealed class RoslynScriptReferenceInfo
{
    internal RoslynScriptReferenceInfo(
        string path,
        string assemblyName,
        Version? version,
        string architecture,
        string sha256,
        IReadOnlyList<string> dependencies)
    {
        Path = path;
        AssemblyName = assemblyName;
        Version = version;
        Architecture = architecture;
        Sha256 = sha256;
        Dependencies = dependencies;
    }

    /// <summary>引用文件绝对路径。</summary>
    public string Path { get; }

    /// <summary>程序集简单名称。</summary>
    public string AssemblyName { get; }

    /// <summary>程序集版本。</summary>
    public Version? Version { get; }

    /// <summary>程序集声明的处理器架构。</summary>
    public string Architecture { get; }

    /// <summary>文件内容 SHA256。</summary>
    public string Sha256 { get; }

    /// <summary>程序集声明的直接托管依赖名称。</summary>
    public IReadOnlyList<string> Dependencies { get; }
}

/// <summary>使用 PE 元数据检查脚本 DLL，整个过程不会把目标程序集加载到宿主进程。</summary>
public static class RoslynScriptReferenceInspector
{
    /// <summary>检查一个托管 DLL。</summary>
    /// <param name="path">DLL 文件路径。</param>
    /// <returns>可供引用管理 UI 展示和锁定的元数据。</returns>
    public static RoslynScriptReferenceInfo Inspect(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("DLL 路径不能为空。", nameof(path));
        var fullPath = System.IO.Path.GetFullPath(path);
        if (!File.Exists(fullPath)) throw new FileNotFoundException($"脚本引用不存在：{fullPath}", fullPath);

        using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var peReader = new PEReader(stream, PEStreamOptions.LeaveOpen);
        if (!peReader.HasMetadata)
            throw new BadImageFormatException($"文件不是托管程序集，不能作为 C# 编译引用：{fullPath}");
        var metadata = peReader.GetMetadataReader();
        if (!metadata.IsAssembly)
            throw new BadImageFormatException($"文件不是托管程序集清单：{fullPath}");

        var definition = metadata.GetAssemblyDefinition();
        var name = metadata.GetString(definition.Name);
        var dependencies = metadata.AssemblyReferences
            .Select(handle => metadata.GetString(metadata.GetAssemblyReference(handle).Name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var architecture = GetArchitecture(peReader.PEHeaders);

        stream.Position = 0;
        using var sha256 = SHA256.Create();
        var hash = ToHex(sha256.ComputeHash(stream));
        return new RoslynScriptReferenceInfo(
            fullPath,
            name,
            definition.Version,
            architecture,
            hash,
            dependencies);
    }

    private static string GetArchitecture(PEHeaders headers)
    {
        var machine = headers.CoffHeader.Machine;
        var corFlags = headers.CorHeader?.Flags ?? 0;
        if (machine == Machine.Amd64) return "x64";
        if (machine == Machine.Arm || machine == Machine.ArmThumb2) return "ARM";
        if (machine == Machine.Arm64) return "ARM64";
        if (machine == Machine.I386 && corFlags.HasFlag(CorFlags.Requires32Bit)) return "x86";
        if (machine == Machine.I386 && corFlags.HasFlag(CorFlags.ILOnly)) return "AnyCPU";
        return machine.ToString();
    }

    private static string ToHex(byte[] bytes)
    {
        var builder = new StringBuilder(bytes.Length * 2);
        foreach (var value in bytes) builder.Append(value.ToString("X2", System.Globalization.CultureInfo.InvariantCulture));
        return builder.ToString();
    }
}
