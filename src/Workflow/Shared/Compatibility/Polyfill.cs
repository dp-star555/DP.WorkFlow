using System.IO;

namespace DP.WorkFlow;

/// <summary>
/// .NET Framework 4.8 没有的静态 API（<c>double.IsFinite</c>、<c>Math.Clamp</c>、<c>Enum.GetValues&lt;T&gt;</c> 等）。
/// 调用处统一走这里：.NET 8 上直接转给 BCL，4.8 上按相同语义实现。
/// </summary>
internal static class Polyfill
{
    /// <summary>值既不是 NaN 也不是无穷大。</summary>
    public static bool IsFinite(double value) =>
#if NETFRAMEWORK
        !double.IsNaN(value) && !double.IsInfinity(value);
#else
        double.IsFinite(value);
#endif

    /// <summary>把值限制在 [min, max] 内；min 大于 max 时抛出 <see cref="ArgumentException"/>。</summary>
    public static int Clamp(int value, int min, int max)
    {
#if NETFRAMEWORK
        if (min > max) throw new ArgumentException($"最小值 {min} 不能大于最大值 {max}。");
        return value < min ? min : value > max ? max : value;
#else
        return Math.Clamp(value, min, max);
#endif
    }

    /// <inheritdoc cref="Clamp(int, int, int)"/>
    public static long Clamp(long value, long min, long max)
    {
#if NETFRAMEWORK
        if (min > max) throw new ArgumentException($"最小值 {min} 不能大于最大值 {max}。");
        return value < min ? min : value > max ? max : value;
#else
        return Math.Clamp(value, min, max);
#endif
    }

    /// <inheritdoc cref="Clamp(int, int, int)"/>
    public static double Clamp(double value, double min, double max)
    {
#if NETFRAMEWORK
        if (min > max) throw new ArgumentException($"最小值 {min} 不能大于最大值 {max}。");
        return value < min ? min : value > max ? max : value;
#else
        return Math.Clamp(value, min, max);
#endif
    }

    /// <inheritdoc cref="Clamp(int, int, int)"/>
    public static float Clamp(float value, float min, float max)
    {
#if NETFRAMEWORK
        if (min > max) throw new ArgumentException($"最小值 {min} 不能大于最大值 {max}。");
        return value < min ? min : value > max ? max : value;
#else
        return Math.Clamp(value, min, max);
#endif
    }

    /// <inheritdoc cref="Clamp(int, int, int)"/>
    public static byte Clamp(byte value, byte min, byte max)
    {
#if NETFRAMEWORK
        if (min > max) throw new ArgumentException($"最小值 {min} 不能大于最大值 {max}。");
        return value < min ? min : value > max ? max : value;
#else
        return Math.Clamp(value, min, max);
#endif
    }

    /// <summary>按分隔符拆分，去掉每段首尾空白并丢弃空段（等同 .NET 8 的 RemoveEmptyEntries | TrimEntries）。</summary>
    public static string[] SplitTrimmed(string text, char separator) =>
#if NETFRAMEWORK
        text.Split(separator).Select(part => part.Trim()).Where(part => part.Length > 0).ToArray();
#else
        text.Split(separator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
#endif

    /// <summary>从 <see cref="System.Diagnostics.Stopwatch.GetTimestamp"/> 取得的时间戳到现在经过的时间。</summary>
    public static TimeSpan GetElapsedTime(long startingTimestamp) =>
#if NETFRAMEWORK
        TimeSpan.FromTicks((long)((System.Diagnostics.Stopwatch.GetTimestamp() - startingTimestamp)
            * ((double)TimeSpan.TicksPerSecond / System.Diagnostics.Stopwatch.Frequency)));
#else
        System.Diagnostics.Stopwatch.GetElapsedTime(startingTimestamp);
#endif

    /// <summary>已完成的 <see cref="ValueTask{TResult}"/>（等同 .NET 8 的 <c>ValueTask.FromResult</c>）。</summary>
    public static ValueTask<TResult> FromResult<TResult>(TResult result) => new(result);

    /// <summary>移动文件，目标已存在时按 <paramref name="overwrite"/> 覆盖（4.8 上用 <see cref="File.Replace(string, string, string)"/> 原子替换）。</summary>
    public static void MoveFile(string sourceFileName, string destFileName, bool overwrite)
    {
#if NETFRAMEWORK
        if (overwrite && File.Exists(destFileName)) File.Replace(sourceFileName, destFileName, null);
        else File.Move(sourceFileName, destFileName);
#else
        File.Move(sourceFileName, destFileName, overwrite);
#endif
    }

    /// <summary>值是否为枚举中定义的成员（等同 .NET 8 的泛型 <c>Enum.IsDefined</c>）。</summary>
    public static bool IsDefined<TEnum>(TEnum value) where TEnum : struct, Enum =>
#if NETFRAMEWORK
        Enum.IsDefined(typeof(TEnum), value);
#else
        Enum.IsDefined(value);
#endif

    /// <summary>ASCII 字母或数字。</summary>
    public static bool IsAsciiLetterOrDigit(char c) => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9';

    /// <summary>ASCII 十六进制数字。</summary>
    public static bool IsAsciiHexDigit(char c) => c is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F';

    /// <summary>
    /// 不回溯的正则选项：.NET 8 上为 <c>RegexOptions.NonBacktracking</c>；4.8 没有这个引擎，返回 <see cref="System.Text.RegularExpressions.RegexOptions.None"/>，
    /// 调用方须同时给出匹配超时，防止灾难性回溯。
    /// </summary>
    public static System.Text.RegularExpressions.RegexOptions NonBacktracking =>
#if NETFRAMEWORK
        System.Text.RegularExpressions.RegexOptions.None;
#else
        System.Text.RegularExpressions.RegexOptions.NonBacktracking;
#endif

    /// <summary>以 <paramref name="basePath"/> 为基准取得完整路径（等同 .NET 8 的 <c>Path.GetFullPath(path, basePath)</c>）。</summary>
    public static string GetFullPath(string path, string basePath) =>
#if NETFRAMEWORK
        Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(basePath, path));
#else
        Path.GetFullPath(path, basePath);
#endif

    /// <summary>路径是否以目录分隔符结尾。</summary>
    public static bool EndsInDirectorySeparator(string path) =>
        path.Length > 0 && (path[path.Length - 1] == Path.DirectorySeparatorChar || path[path.Length - 1] == Path.AltDirectorySeparatorChar);

    /// <summary>从 <paramref name="relativeTo"/> 到 <paramref name="path"/> 的相对路径（等同 .NET 8 的 <c>Path.GetRelativePath</c>）。</summary>
    public static string GetRelativePath(string relativeTo, string path)
    {
#if NETFRAMEWORK
        var from = Path.GetFullPath(relativeTo);
        var to = Path.GetFullPath(path);
        if (!EndsInDirectorySeparator(from)) from += Path.DirectorySeparatorChar;
        var fromUri = new Uri(from);
        var toUri = new Uri(to);
        if (!string.Equals(fromUri.Scheme, toUri.Scheme, StringComparison.OrdinalIgnoreCase)) return to;
        var relative = Uri.UnescapeDataString(fromUri.MakeRelativeUri(toUri).ToString()).Replace('/', Path.DirectorySeparatorChar);
        return relative.Length == 0 ? "." : relative;
#else
        return Path.GetRelativePath(relativeTo, path);
#endif
    }

    /// <summary>大写十六进制字符串（等同 .NET 8 的 <c>Convert.ToHexString</c>）。</summary>
    public static string ToHexString(byte[] bytes) =>
#if NETFRAMEWORK
        BitConverter.ToString(bytes).Replace("-", string.Empty);
#else
        Convert.ToHexString(bytes);
#endif

    /// <summary>SHA-256 摘要（等同 .NET 8 的 <c>SHA256.HashData</c>）。</summary>
    public static byte[] Sha256(byte[] bytes)
    {
#if NETFRAMEWORK
        using var sha = System.Security.Cryptography.SHA256.Create();
        return sha.ComputeHash(bytes);
#else
        return System.Security.Cryptography.SHA256.HashData(bytes);
#endif
    }

    /// <summary>异步写入全部字节（4.8 没有 <c>File.WriteAllBytesAsync</c>）。</summary>
    public static async Task WriteAllBytesAsync(string path, byte[] bytes, CancellationToken cancellationToken = default)
    {
#if NETFRAMEWORK
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read, 4096, useAsync: true);
        await stream.WriteAsync(bytes, 0, bytes.Length, cancellationToken).ConfigureAwait(false);
#else
        await File.WriteAllBytesAsync(path, bytes, cancellationToken).ConfigureAwait(false);
#endif
    }

    /// <summary>异步读取全部字节（4.8 没有 <c>File.ReadAllBytesAsync</c>）。</summary>
    public static async Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken = default)
    {
#if NETFRAMEWORK
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
        var buffer = new byte[stream.Length];
        var read = 0;
        while (read < buffer.Length)
        {
            var count = await stream.ReadAsync(buffer, read, buffer.Length - read, cancellationToken).ConfigureAwait(false);
            if (count == 0) break;
            read += count;
        }
        return read == buffer.Length ? buffer : buffer.Take(read).ToArray();
#else
        return await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
#endif
    }

    /// <summary>异步读取全部文本（UTF-8，4.8 没有 <c>File.ReadAllTextAsync</c>）。</summary>
    public static async Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken = default)
    {
#if NETFRAMEWORK
        var bytes = await ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(new MemoryStream(bytes), System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
#else
        return await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
#endif
    }

    /// <summary>异步写入全部文本（UTF-8 无 BOM，4.8 没有 <c>File.WriteAllTextAsync</c>）。</summary>
    public static Task WriteAllTextAsync(string path, string contents, CancellationToken cancellationToken = default) =>
#if NETFRAMEWORK
        WriteAllBytesAsync(path, new System.Text.UTF8Encoding(false).GetBytes(contents), cancellationToken);
#else
        File.WriteAllTextAsync(path, contents, cancellationToken);
#endif

    /// <summary>去掉路径末尾的一个目录分隔符（根目录除外）。</summary>
    public static string TrimEndingDirectorySeparator(string path) =>
#if NETFRAMEWORK
        EndsInDirectorySeparator(path) && path.Length > Path.GetPathRoot(path)?.Length ? path.Substring(0, path.Length - 1) : path;
#else
        Path.TrimEndingDirectorySeparator(path);
#endif

    /// <summary>按名称解析枚举值（等同 .NET 8 的泛型 <c>Enum.Parse</c>）。</summary>
    public static TEnum ParseEnum<TEnum>(string value, bool ignoreCase = false) where TEnum : struct, Enum =>
#if NETFRAMEWORK
        (TEnum)Enum.Parse(typeof(TEnum), value, ignoreCase);
#else
        Enum.Parse<TEnum>(value, ignoreCase);
#endif

    /// <summary>枚举的全部值。</summary>
    public static TEnum[] GetEnumValues<TEnum>() where TEnum : struct, Enum =>
#if NETFRAMEWORK
        (TEnum[])Enum.GetValues(typeof(TEnum));
#else
        Enum.GetValues<TEnum>();
#endif
}
